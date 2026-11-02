using DiscordBirge.Data;
using DiscordBirge.Models;
using Microsoft.EntityFrameworkCore;

namespace DiscordBirge.Services;

public class MatchTrackerService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly OpenDotaService _dota;

    public MatchTrackerService(IServiceProvider services, OpenDotaService dota)
    {
        _services = services;
        _dota = dota;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using (var scope = _services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                // Берем только те заказы, которые в процессе
                var activeOrders = await db.Orders
                    .Where(o => o.Status == OrderStatus.InProgress)
                    .ToListAsync(stoppingToken);

                foreach (var order in activeOrders)
                {
                    await ProcessOrderMatchesAsync(db, order);
                }
            }
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task ProcessOrderMatchesAsync(AppDbContext db, Order order)
    {
        var matches = await _dota.GetRecentMatchesAsync(order.DotaAccountId);
        
        var newMatches = matches
            .Where(m => order.LastProcessedMatchId == null || m.MatchId > order.LastProcessedMatchId)
            .OrderBy(m => m.StartTime)
            .ToList();

        if (!newMatches.Any()) return;

        foreach (var match in newMatches)
        {
            bool isRadiant = match.PlayerSlot < 128;
            bool isWin = (isRadiant && match.RadiantWin) || (!isRadiant && !match.RadiantWin);

            // В реальности нужно брать точное изменение MMR (например, +28/-22). Пока берем +25/-25.
            int mmrDelta = isWin ? 25 : -25; 

            DateTime matchTime = DateTimeOffset.FromUnixTimeSeconds(match.StartTime).UtcDateTime;
            bool isBoosterPlayed = false;

            if (order.IsShiftActive && order.ShiftStartedAt.HasValue)
            {
                // Если матч был после начала смены (с погрешностью 5 минут)
                if (matchTime >= order.ShiftStartedAt.Value.AddMinutes(-5))
                {
                    isBoosterPlayed = true;
                }
            }

            if (isBoosterPlayed)
            {
                // === ИГРАЛ БУСТЕР ===
                order.BoosterGamesPlayed++;
                order.CurrentMmr += mmrDelta;
                order.BoosterMmrChange += mmrDelta;
                Console.WriteLine($"[TRACKER] Бустер сыграл ({(isWin ? "Win" : "Lose")}). MMR: {order.CurrentMmr}");
            }
            else
            {
                // === ИГРАЛ ВЛАДЕЛЕЦ ===
                order.OwnerGamesPlayed++;
                order.CurrentMmr += mmrDelta;
                order.OwnerMmrChange += mmrDelta;
                Console.WriteLine($"[TRACKER] Владелец сыграл ({(isWin ? "Win" : "Lose")}). MMR: {order.CurrentMmr}");
            }

            order.LastProcessedMatchId = match.MatchId;

            // Проверка: достигнута ли цель (только если смена активна)
            if (order.IsShiftActive && order.CurrentMmr >= order.TargetMmr)
            {
                order.Status = OrderStatus.Completed;
                order.IsShiftActive = false; // Закрываем смену
                Console.WriteLine($"[TRACKER] ЗАКАЗ {order.Id} ВЫПОЛНЕН!");
                Console.WriteLine($"[TRACKER] Всего игр: {order.TotalGamesPlayed} (Бустер: {order.BoosterGamesPlayed}, Владелец: {order.OwnerGamesPlayed})");
                Console.WriteLine($"[TRACKER] Цена за игру: {order.PricePerGame:F2} руб.");
                Console.WriteLine($"[TRACKER] К ВЫПЛАТЕ БУСТЕРУ: {order.BoosterTotalPayout:F2} руб.");
                break;
            }
        }

        await db.SaveChangesAsync();
    }
}