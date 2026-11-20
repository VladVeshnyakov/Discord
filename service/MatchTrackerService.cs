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
using System.Collections.Generic;
using DiscordBirge.Actions; // Добавили для HashSet

namespace DiscordBirge.Workers;

public class MatchTrackerService : BackgroundService
{
    private readonly ILogger<MatchTrackerService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DiscordSocketClient _client;
    // Храним ID заказов, которые уже обновили интерфейс после старта
    private readonly HashSet<int> _initializedOrders = new();
    private readonly HashSet<int> _notifiedOrders = new();
    
    public MatchTrackerService(
        ILogger<MatchTrackerService> logger,
        IServiceScopeFactory scopeFactory,
        DiscordSocketClient client, 
        UserActions  userActions)
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
            var socket = scope.ServiceProvider.GetRequiredService<DiscordSocketClient>();
            var actions = new UserActions(db, bepusdt, orderChannels, info, dota, socket);
    
            // 1. Обрабатываем активные заказы (как было)
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
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); // 15 сек для экономии лимитов
            }
    
            // 2. НОВОЕ: Проверяем завершенные заказы
            // 2. Проверяем завершенные заказы
            var completedOrders = await db.GetCompletedOrdersAsync(stoppingToken);

            foreach (var order in completedOrders)
            {
                // Если мы еще не отправляли уведомление по этому заказу в текущей сессии
                if (!_notifiedOrders.Contains(order.Id))
                {
                    // ИСПОЛЬЗУЕМ actions, а не _userActions
                    await actions.SendCompletionNotificationAsync(order, db); 
                    _notifiedOrders.Add(order.Id); // Запоминаем, что отправили
                }
            }
            await Task.Delay(TimeSpan.FromMinutes(6), stoppingToken);
        }
    }
    private async Task ProcessOrderAsync(Order order, DbService db, OpenDotaService dota, DiscordBirge.Actions.UserActions actions, CancellationToken ct)
    {
        if (order.Shifts == null || order.Shifts.Count == 0) return;
        if (order.DotaAccountId == 0) return;

        bool isFirstRun = !_initializedOrders.Contains(order.Id);
        bool needsDiscordUpdate = isFirstRun;

        var boostStart = order.Shifts.Min(s => s.StartUtc);
        
        // 1. Запрашиваем ТОЛЬКО матчи (1 запрос)
        var matches = await dota.GetRecentMatchesAsync(order.DotaAccountId);
        if (matches == null) matches = new List<DotaMatchDto>();

        var now = DateTime.UtcNow;
        var storedMatches = JsonSerializer.Deserialize<List<OrderMatch>>(order.MatchesJson ?? "[]") ?? new();
        var processedIds = storedMatches.Select(m => m.MatchId).ToHashSet();

        var allWithTime = matches
            .Select(m => (Match: m, TimeUtc: FromUnix(m.StartTime)))
            .Where(x => x.TimeUtc >= boostStart && !processedIds.Contains(x.Match.MatchId))
            .OrderBy(x => x.TimeUtc)
            .ToList();

        bool matchesAdded = false;

        // 2. Обрабатываем новые матчи (если они есть)
        if (allWithTime.Count > 0)
        {
            bool orderCompleted = false;

            foreach (var item in allWithTime)
            {
                var m = item.Match;
                if (m == null) continue;

                if (order.Service != null && !DotaRankHelper.IsMatchAllowed(order.Service.Type, m.GameMode, m.LobbyType))
                {
                    continue; 
                }

                matchesAdded = true;

                bool isBooster = IsDuringAnyShift(item.TimeUtc, order.Shifts, now);
                bool isRadiant = m.PlayerSlot < 128;
                bool isWin = (isRadiant && m.RadiantWin) || (!isRadiant && !m.RadiantWin);

                if (isBooster)
                {
                    order.BoosterGamesPlayed++;
                    if (isWin) order.BoosterWin++;
                    else order.BoosterLose++;
                }
                else
                {
                    order.OwnerGamesPlayed++;
                    if (isWin) order.ClientWin++;
                    else order.ClientLose++;
                }

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
                    Duration = m.Duration,
                    GameMode = m.GameMode,
                    LobbyType = m.LobbyType
                });
            }

            if (matchesAdded)
            {
                needsDiscordUpdate = true;

                // Проверка на завершение заказа
                if (order.Service?.Type == ServiceType.Boost || order.Service?.Type == ServiceType.Calibration)
                {
                    int netWins = order.BoosterWin - order.BoosterLose;
                    if (order.TargetGames > 0 && netWins >= order.TargetGames) orderCompleted = true;
                }
                else if (order.Service?.Type == ServiceType.LP)
                {
                    int totalWins = order.BoosterWin + order.ClientWin;
                    if (order.TargetGames > 0 && totalWins >= order.TargetGames) orderCompleted = true;
                }
                else if (order.Service?.Type == ServiceType.Behavior)
                {
                    if (order.TargetGames > 0 && order.BoosterGamesPlayed >= order.TargetGames) orderCompleted = true;
                }

                if (orderCompleted)
                {
                    order.Status = OrderStatus.Completed;
                    order.IsShiftActive = false;
                }

                if (storedMatches.Count > 15)
                    storedMatches = storedMatches.OrderByDescending(m => m.StartTime).Take(15).ToList();

                order.MatchesJson = JsonSerializer.Serialize(storedMatches);
            }
        }

        // 3. Если новых матчей нет и это НЕ первый запуск — ЭКОНОМИМ ЗАПРОСЫ и выходим!
        if (!matchesAdded && !isFirstRun)
        {
            return; 
        }

        _initializedOrders.Add(order.Id);

        // 4. Запрашиваем профиль (2-й запрос) ТОЛЬКО если есть новые игры или это первый запуск
        var playerInfo = await dota.GetPlayerInfoAsync(order.DotaAccountId);
        if (playerInfo?.Profile != null && order.NameAccount != playerInfo.Profile.PersonaName)
        {
            order.NameAccount = playerInfo.Profile.PersonaName;
            needsDiscordUpdate = true;
        }

        if (playerInfo?.RankTier != null && order.CurrentRankTier != playerInfo.RankTier.Value)
        {
            order.CurrentRankTier = playerInfo.RankTier.Value;
            needsDiscordUpdate = true;
        }

        // 5. Сохраняем в БД и обновляем Discord
        await db.UpdateOrderAsync(order);

        if (needsDiscordUpdate)
        {
            await SendProgressToThreadAsync(order.ClientThreadId, order, actions);
            await SendProgressToThreadAsync(order.BoosterThreadId, order, actions);
        }
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