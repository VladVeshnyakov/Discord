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
                var orderChannels = scope.ServiceProvider.GetRequiredService<OrderChannelService>(); // <-- получаем здесь
                
                var activeOrders = await db.Orders
                    .Include(o => o.Service)
                    .Where(o => o.Status == OrderStatus.InProgress)
                    .ToListAsync(stoppingToken);

                foreach (var order in activeOrders)
                {
                    await ProcessOrderMatchesAsync(db, orderChannels, order); // <-- передаем параметром
                }
            }
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task ProcessOrderMatchesAsync(AppDbContext db, OrderChannelService orderChannels, Order order)
    {
        var matches = await _dota.GetRecentMatchesAsync(order.DotaAccountId);
        
        var newMatches = matches
            .Where(m => order.LastProcessedMatchId == null || m.MatchId > order.LastProcessedMatchId)
            .OrderBy(m => m.StartTime)
            .ToList();

        if (!newMatches.Any()) return;

        bool orderCompleted = false;

        foreach (var match in newMatches)
        {
            DateTime matchTime = DateTimeOffset.FromUnixTimeSeconds(match.StartTime).UtcDateTime;
            bool isBoosterPlayed = false;

            if (order.IsShiftActive && order.ShiftStartedAt.HasValue)
            {
                if (matchTime >= order.ShiftStartedAt.Value.AddMinutes(-5))
                {
                    isBoosterPlayed = true;
                }
            }

            if (isBoosterPlayed) order.BoosterGamesPlayed++;
            else order.OwnerGamesPlayed++;

            order.LastProcessedMatchId = match.MatchId;

            if (order.Service.Type == ServiceType.Boost || order.Service.Type == ServiceType.Calibration)
            {
                bool isRadiant = match.PlayerSlot < 128;
                bool isWin = (isRadiant && match.RadiantWin) || (!isRadiant && !match.RadiantWin);
                int mmrDelta = isWin ? 25 : -25; 

                order.CurrentMmr += mmrDelta;
                if (isBoosterPlayed) order.BoosterMmrChange += mmrDelta;
                else order.OwnerMmrChange += mmrDelta;

                if (order.IsShiftActive && order.CurrentMmr >= order.TargetMmr)
                {
                    order.Status = OrderStatus.Completed;
                    order.IsShiftActive = false;
                    orderCompleted = true;
                    Console.WriteLine($"[TRACKER] Заказ {order.Id} (MMR) выполнен! Выплата: {order.BoosterTotalPayout:F2}р");
                    break;
                }
            }
            else if (order.Service.Type == ServiceType.LP)
            {
                if (order.TotalGamesPlayed >= order.TargetGames)
                {
                    order.Status = OrderStatus.Completed;
                    order.IsShiftActive = false;
                    orderCompleted = true;
                    Console.WriteLine($"[TRACKER] Заказ {order.Id} (LP) выполнен! Игр: {order.TotalGamesPlayed}. Выплата: {order.BoosterTotalPayout:F2}р");
                    break;
                }
            }
            else if (order.Service.Type == ServiceType.Behavior)
            {
                Console.WriteLine($"[TRACKER] Заказ {order.Id} (Порядочность). Сыграна игра. Всего: {order.TotalGamesPlayed}");
            }
        }

        await db.SaveChangesAsync();

        // Обновляем панели в каналах после сохранения данных
        await orderChannels.UpdateChannelsProgressAsync(order);

        // Если заказ завершен, можно опционально удалить каналы через минуту или оставить для истории
        // if (orderCompleted) await orderChannels.DeleteOrderChannelsAsync(order);
    }
}