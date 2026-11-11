using Discord;
using Discord.WebSocket;
using DiscordBirge.Actions;
using DiscordBirge.Data;
using DiscordBirge.Helpers;
using DiscordBirge.Models;
using DiscordBirge.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
            var bepusdt = scope.ServiceProvider.GetRequiredService<BepusdtService>();
            var orderChannels = scope.ServiceProvider.GetRequiredService<OrderChannelService>();
            var info = scope.ServiceProvider.GetRequiredService<InfoMessageService>();
            var actions = new UserActions(db, bepusdt, orderChannels, info);

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

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task ProcessOrderAsync(
        Order order, DbService db, OpenDotaService dota, UserActions actions, CancellationToken ct)
    {
        if (order.Shifts is null || order.Shifts.Count == 0)
        {
            _logger.LogInformation("Заказ #{Id}: смен ещё не было, пропуск", order.Id);
            return;
        }

        if (order.DotaAccountId == 0)
        {
            _logger.LogWarning("Заказ #{Id}: не указан BoosterAccountId", order.Id);
            return;
        }

        var boostStart = order.Shifts.Min(s => s.StartUtc);
        var matches = await dota.GetRecentMatchesAsync(order.DotaAccountId);
        if (matches.Count == 0) return;

        var now = DateTime.UtcNow;

        var allWithTime = matches
            .Select(m => (Match: m, TimeUtc: FromUnix(m.StartTime)))
            .Where(x => x.TimeUtc >= boostStart)
            .ToList();

        if (allWithTime.Count == 0) return;

        var boosterMatches = allWithTime
            .Where(x => IsDuringAnyShift(x.TimeUtc, order.Shifts, now))
            .Select(x => x.Match)
            .ToList();

        var boosterIds = boosterMatches.Select(m => m.MatchId).ToHashSet();
        var clientMatches = allWithTime
            .Where(x => !boosterIds.Contains(x.Match.MatchId))
            .Select(x => x.Match)
            .ToList();

        var boosterWins = boosterMatches.Count(m => IsWin(m));

        var progress = new OrderProgressInfo
        {
            OrderId = order.Id,
            BoosterGames = boosterMatches.Count,
            BoosterWins = boosterWins,
            BoosterLosses = boosterMatches.Count - boosterWins,
            ClientGames = clientMatches.Count,
            TotalGamesSinceBoost = allWithTime.Count
        };

        // Обновляем поля в Order, чтобы UI (_ShowMyServicesAsync_) показывал актуальное
        order.BoosterGamesPlayed = progress.BoosterGames;
        order.OwnerGamesPlayed = progress.ClientGames;
        if (order.Service?.Type is ServiceType.LP or ServiceType.Behavior)
            

        await db.UpdateOrderAsync(order);

        // Обновляем сообщение с прогрессом в обеих ветках
        await SendProgressToThreadAsync(order.ClientThreadId, order, progress, actions);
        await SendProgressToThreadAsync(order.BoosterThreadId, order, progress, actions);
    }

    private async Task SendProgressToThreadAsync(
        ulong? threadId, Order order, OrderProgressInfo progress, UserActions actions)
    {
        if (threadId is null or 0) return;

        if (_client.GetChannel(threadId.Value) is not IMessageChannel channel)
        {
            _logger.LogWarning("Заказ #{Id}: ветка {ThreadId} не найдена в кэше", order.Id, threadId);
            return;
        }

        await actions.UpdateProgressAsync(channel, order, progress);
    }

    private static DateTime FromUnix(long unix) =>
        DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;

    private static bool IsDuringAnyShift(DateTime timeUtc, List<Shift> shifts, DateTime nowUtc)
    {
        foreach (var s in shifts)
        {
            var end = s.EndUtc ?? nowUtc;
            if (timeUtc >= s.StartUtc && timeUtc <= end) return true;
        }
        return false;
    }

    private static bool IsWin(DotaMatchDto m) =>
        m.PlayerSlot < 128 ? m.RadiantWin : !m.RadiantWin;
}