using Discord.WebSocket;
using DiscordBirge.Data;
using DiscordBirge.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Discord;
using DiscordBirge.Helpers;
using DiscordBirge.Services;

namespace DiscordBirge.Workers;

public class MatchTrackerService : BackgroundService
{
    private readonly ILogger<MatchTrackerService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DiscordSocketClient _client;

    public MatchTrackerService(
        ILogger<MatchTrackerService> logger,
        IServiceScopeFactory scopeFactory,
        DiscordSocketClient client)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _client = client;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbService>();
            var dota = scope.ServiceProvider.GetRequiredService<OpenDotaService>();
            
            // Получаем UserActions
            var bepusdt = scope.ServiceProvider.GetRequiredService<BepusdtService>();
            var orderChannels = scope.ServiceProvider.GetRequiredService<OrderChannelService>();
            var info = scope.ServiceProvider.GetRequiredService<InfoMessageService>();
            var actions = new DiscordBirge.Actions.UserActions(db, bepusdt, orderChannels, info, dota);

            List<Order> activeOrders;
            try
            {
                activeOrders = await db.GetActiveOrdersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось получить активные заказы");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }

            foreach (var order in activeOrders)
            {
                if (stoppingToken.IsCancellationRequested) break;
                try
                {
                    await ProcessOrderAsync(order, db, dota, actions, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка обработки заказа #{Id}", order.Id);
                }
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task ProcessOrderAsync(Order order, DbService db, OpenDotaService dota, DiscordBirge.Actions.UserActions actions, CancellationToken ct)
    {
        if (order.Shifts == null || order.Shifts.Count == 0) return;
        if (order.DotaAccountId == 0) return;

        var boostStart = order.Shifts.Min(s => s.StartUtc);
        var matches = await dota.GetRecentMatchesAsync(order.DotaAccountId);
        if (matches.Count == 0) return;

        // 1. Обновляем текущий ранг игрока
        var playerInfo = await dota.GetPlayerInfoAsync(order.DotaAccountId);
        if (playerInfo?.RankTier != null && order.CurrentRankTier != playerInfo.RankTier.Value)
        {
            order.CurrentRankTier = playerInfo.RankTier.Value;
            await db.UpdateOrderAsync(order);
        }

        var now = DateTime.UtcNow;
        
        // Загружаем уже обработанные матчи из БД
        var storedMatches = JsonSerializer.Deserialize<List<OrderMatch>>(order.MatchesJson ?? "[]") ?? new();
        var processedIds = storedMatches.Select(m => m.MatchId).ToHashSet();

        // Находим новые матчи
        var allWithTime = matches
            .Select(m => (Match: m, TimeUtc: FromUnix(m.StartTime)))
            .Where(x => x.TimeUtc >= boostStart && !processedIds.Contains(x.Match.MatchId))
            .OrderBy(x => x.TimeUtc)
            .ToList();

        if (allWithTime.Count == 0) return;

        bool orderCompleted = false;

        foreach (var item in allWithTime)
        {
            var m = item.Match;
            bool isBooster = IsDuringAnyShift(item.TimeUtc, order.Shifts, now);
            bool isRadiant = m.PlayerSlot < 128;
            bool isWin = (isRadiant && m.RadiantWin) || (!isRadiant && !m.RadiantWin);

            if (isBooster) order.BoosterGamesPlayed++;
            else order.OwnerGamesPlayed++;

            // Добавляем в историю
            storedMatches.Add(new OrderMatch
            {
                MatchId = m.MatchId,
                StartTime = m.StartTime,
                IsWin = isWin,
                IsBooster = isBooster,
                HeroId = m.HeroId,
                Kills = m.Kills,
                Deaths = m.Deaths,
                Assists = m.Assists,
                Duration = m.Duration
            });

            // Проверка завершения (Если есть конкретный ранг или количество игр)
            if (order.Service?.Type == ServiceType.Boost || order.Service?.Type == ServiceType.Calibration)
            {
                if (order.TargetRankTier > 0 && order.CurrentRankTier >= order.TargetRankTier)
                {
                    order.Status = OrderStatus.Completed;
                    order.IsShiftActive = false;
                    orderCompleted = true;
                }
            }
            else if (order.Service?.Type == ServiceType.LP || order.Service?.Type == ServiceType.Behavior)
            {
                if (order.TotalGamesPlayed >= order.TargetGames)
                {
                    order.Status = OrderStatus.Completed;
                    order.IsShiftActive = false;
                    orderCompleted = true;
                }
            }
        }
        
        if (storedMatches.Count > 15)
            storedMatches = storedMatches.OrderByDescending(m => m.StartTime).Take(15).ToList();

        order.MatchesJson = JsonSerializer.Serialize(storedMatches);
        await db.UpdateOrderAsync(order);
        
        await SendProgressToThreadAsync(order.ClientThreadId, order, actions);
        await SendProgressToThreadAsync(order.BoosterThreadId, order, actions);

        if (orderCompleted)
            Console.WriteLine($"[TRACKER] Заказ {order.Id} ВЫПОЛНЕН!");
    }

    private async Task SendProgressToThreadAsync(ulong? threadId, Order order, DiscordBirge.Actions.UserActions actions)
    {
        if (threadId is null or 0) return;
        if (_client.GetChannel(threadId.Value) is not IMessageChannel channel) return;
        
        await actions.UpdateProgressAsync(channel, order, page: 0);
    }

    private static DateTime FromUnix(long unix) => DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;

    private static bool IsDuringAnyShift(DateTime timeUtc, List<Shift> shifts, DateTime nowUtc)
    {
        foreach (var s in shifts)
        {
            var end = s.EndUtc ?? nowUtc;
            if (timeUtc >= s.StartUtc && timeUtc <= end) return true;
        }
        return false;
    }
}