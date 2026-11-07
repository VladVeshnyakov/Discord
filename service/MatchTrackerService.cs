using DiscordBirge.Data;
using DiscordBirge.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordBirge.Services;

public class OrderMatch
{
    public long MatchId { get; set; }
    public long StartTime { get; set; }
    public bool IsWin { get; set; }
    public bool IsBooster { get; set; }
    public int HeroId { get; set; }
    public int MmrDelta { get; set; }
}

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
            try
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var orderChannels = scope.ServiceProvider.GetRequiredService<OrderChannelService>();

                var activeOrders = await db.Orders
                    .Include(o => o.Service)
                    .Where(o => o.Status == OrderStatus.InProgress || o.Status == OrderStatus.Paid)
                    .ToListAsync(stoppingToken);

                foreach (var order in activeOrders)
                {
                    await ProcessOrderMatchesAsync(db, orderChannels, order);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TRACKER LOOP ERROR] {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task ProcessOrderMatchesAsync(AppDbContext db, OrderChannelService orderChannels, Order order)
    {
        if (order.DotaAccountId == 0) return;

        var matches = await _dota.GetRecentMatchesAsync(order.DotaAccountId);
        var startTime = order.PaidAt ?? order.CreatedAt;
        var orderStartUnix = new DateTimeOffset(startTime).ToUnixTimeSeconds();

        var storedMatches = JsonSerializer.Deserialize<List<OrderMatch>>(order.MatchesJson ?? "[]") ?? new();
        var processedIds = storedMatches.Select(m => m.MatchId).ToHashSet();

        var newMatches = matches
            .Where(m => m.StartTime >= orderStartUnix && !processedIds.Contains(m.MatchId))
            .OrderBy(m => m.StartTime)
            .ToList();

        if (!newMatches.Any()) return;

        var playerInfo = await _dota.GetPlayerInfoAsync(order.DotaAccountId);
        int? playerMmr = playerInfo?.MmrEstimate?.Estimate;

        bool orderCompleted = false;

        foreach (var match in newMatches)
        {
            DateTime matchTime = DateTimeOffset.FromUnixTimeSeconds(match.StartTime).UtcDateTime;
            bool isBoosterPlayed = false;

            if (order.IsShiftActive && order.ShiftStartedAt.HasValue)
            {
                if (matchTime >= order.ShiftStartedAt.Value.AddMinutes(-5))
                    isBoosterPlayed = true;
            }

            bool isRadiant = match.PlayerSlot < 128;
            bool isWin = (isRadiant && match.RadiantWin) || (!isRadiant && !match.RadiantWin);

            var details = await _dota.GetMatchDetailsAsync(match.MatchId);
            int mmrDelta = CalculateMmrDelta(isWin, playerMmr, details?.AvgMmr);

            if (isBoosterPlayed) order.BoosterGamesPlayed++;
            else order.OwnerGamesPlayed++;

            if (order.Service.Type == ServiceType.Boost || order.Service.Type == ServiceType.Calibration)
            {
                order.CurrentMmr += mmrDelta;
                if (isBoosterPlayed) order.BoosterMmrChange += mmrDelta;
                else order.OwnerMmrChange += mmrDelta;

                if (order.IsShiftActive && order.CurrentMmr >= order.TargetMmr)
                {
                    order.Status = OrderStatus.Completed;
                    order.IsShiftActive = false;
                    orderCompleted = true;
                }
            }
            else if (order.Service.Type == ServiceType.LP)
            {
                if (order.TotalGamesPlayed >= order.TargetGames)
                {
                    order.Status = OrderStatus.Completed;
                    order.IsShiftActive = false;
                    orderCompleted = true;
                }
            }

            storedMatches.Add(new OrderMatch
            {
                MatchId = match.MatchId,
                StartTime = match.StartTime,
                IsWin = isWin,
                IsBooster = isBoosterPlayed,
                HeroId = match.HeroId,
                MmrDelta = mmrDelta
            });

            order.LastProcessedMatchId = match.MatchId;
            if (orderCompleted) break;
        }

        if (storedMatches.Count > 50)
            storedMatches = storedMatches.OrderByDescending(m => m.StartTime).Take(50)
                .OrderBy(m => m.StartTime).ToList();

        order.MatchesJson = JsonSerializer.Serialize(storedMatches);

        await db.SaveChangesAsync();
        await orderChannels.UpdateThreadsProgressAsync(order);

        if (orderCompleted)
            Console.WriteLine($"[TRACKER] Заказ {order.Id} ВЫПОЛНЕН! Выплата бустеру: {order.BoosterTotalPayout:F2}₽");
    }

    private int CalculateMmrDelta(bool isWin, int? playerMmr, int? avgMmr)
    {
        const int baseDelta = 25;

        if (!playerMmr.HasValue || !avgMmr.HasValue)
            return isWin ? baseDelta : -baseDelta;

        int diff = playerMmr.Value - avgMmr.Value;
        int adjustment = (int)(diff / 100.0 * 2);

        int delta = baseDelta - adjustment;
        delta = Math.Clamp(delta, 10, 40);

        return isWin ? delta : -delta;
    }
}