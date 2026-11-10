using Discord;
using Discord.WebSocket;
using DiscordBirge.Data;
using DiscordBirge.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace DiscordBirge.Services;

public class OrderChannelService
{
    private readonly DiscordSocketClient _client;
    private readonly DbService _db;
    private readonly OpenDotaService _dota;
    private readonly ulong _guildId;
    private readonly ulong _ordersChannelId;

    public OrderChannelService(DiscordSocketClient client, DbService db, OpenDotaService dota, IConfiguration config)
    {
        _client = client;
        _db = db;
        _dota = dota;
        _guildId = ulong.Parse(config["Discord:GuildId"]!);
        _ordersChannelId = ulong.Parse(config["Discord:OrdersChannelId"]!);
    }

    public async Task CreateOrderThreadsAsync(Order order)
    {
        var guild = _client.GetGuild(_guildId);
        var ordersChannel = guild?.GetTextChannel(_ordersChannelId);
        if (ordersChannel == null) return;

        var client = guild!.GetUser(order.ClientId);
        var booster = guild.GetUser(order.BoosterId);

        ulong? clientThreadId = null;
        ulong? boosterThreadId = null;

        if (client != null)
        {
            try
            {
                var thread = await ordersChannel.CreateThreadAsync(
                    name: $"заказ-{order.Id}-клиент",
                    type: ThreadType.PrivateThread,
                    autoArchiveDuration: ThreadArchiveDuration.OneDay,
                    invitable: false);
                await thread.AddUserAsync(client);
                await thread.JoinAsync();
                clientThreadId = thread.Id;

                await thread.SendMessageAsync(
                    embed: await BuildClientEmbedAsync(order),
                    components: BuildClientButtons());
            }
            catch (Exception ex) { Console.WriteLine($"[THREAD CLIENT ERROR] {ex.Message}"); }
        }

        if (booster != null)
        {
            try
            {
                var thread = await ordersChannel.CreateThreadAsync(
                    name: $"заказ-{order.Id}-бустер",
                    type: ThreadType.PrivateThread,
                    autoArchiveDuration: ThreadArchiveDuration.OneDay,
                    invitable: false);
                await thread.AddUserAsync(booster);
                await thread.JoinAsync();
                boosterThreadId = thread.Id;

                await thread.SendMessageAsync(
                    embed: await BuildBoosterEmbedAsync(order),
                    components: BuildBoosterButtons(order));
            }
            catch (Exception ex) { Console.WriteLine($"[THREAD BOOSTER ERROR] {ex.Message}"); }
        }

        if (clientThreadId.HasValue || boosterThreadId.HasValue)
        {
            await _db.UpdateOrderThreadsAsync(order.Id, clientThreadId, boosterThreadId);
            Console.WriteLine($"[THREADS] Order #{order.Id}: client={clientThreadId}, booster={boosterThreadId}");
        }
    }

    public async Task UpdateThreadsProgressAsync(Order order)
    {
        if (order.ClientThreadId.HasValue)
        {
            var thread = _client.GetChannel(order.ClientThreadId.Value) as IThreadChannel;
            if (thread != null)
                await UpdateLastBotMessageAsync(thread, await BuildClientEmbedAsync(order), BuildClientButtons());
        }

        if (order.BoosterThreadId.HasValue)
        {
            var thread = _client.GetChannel(order.BoosterThreadId.Value) as IThreadChannel;
            if (thread != null)
                await UpdateLastBotMessageAsync(thread, await BuildBoosterEmbedAsync(order), BuildBoosterButtons(order));
        }
    }

    private async Task<Embed> BuildClientEmbedAsync(Order order)
    {
        var player = await _dota.GetPlayerInfoAsync(order.DotaAccountId);
        var wl = await _dota.GetWinLossAsync(order.DotaAccountId);
        var heroNames = await _dota.GetHeroNamesAsync();
        var matches = JsonSerializer.Deserialize<List<OrderMatch>>(order.MatchesJson ?? "[]") ?? new();

        var statusEmoji = order.Status switch
        {
            OrderStatus.Paid => "🟡",
            OrderStatus.InProgress => "🟢",
            OrderStatus.Completed => "✅",
            OrderStatus.Cancelled => "❌",
            _ => "⚪"
        };

        var embed = new EmbedBuilder()
            .WithTitle($"💼 Заказ #{order.Id} | {order.Service.Name}")
            .WithColor(order.Status == OrderStatus.Completed ? Color.Green : Color.Blue)
            .AddField("Статус", $"{statusEmoji} {GetStatusText(order)}", true)
            .AddField("Цена", $"{order.Price} ₽", true)
            .AddField("\u200B", "\u200B", true);

        string progressLine = order.Service.Type switch
        {
            ServiceType.Boost or ServiceType.Calibration =>
                $"**MMR:** {order.CurrentMmr} → **{order.TargetMmr}** (цель +{order.TargetMmrDelta})\n" +
                $"**Сыграно:** бустером {order.BoosterGamesPlayed}, владельцем {order.OwnerGamesPlayed}",
            ServiceType.LP =>
                $"**Игр сыграно:** {order.TotalGamesPlayed} / {order.TargetGames}\n" +
                $"**Бустером:** {order.BoosterGamesPlayed}, **вами:** {order.OwnerGamesPlayed}",
            ServiceType.Behavior =>
                $"**Игр сыграно:** {order.TotalGamesPlayed}\n" +
                $"**Бустером:** {order.BoosterGamesPlayed}, **вами:** {order.OwnerGamesPlayed}",
            _ => "—"
        };
        embed.AddField("📊 Прогресс", progressLine, false);

        if (matches.Count > 0)
        {
            var lastMatches = matches.OrderByDescending(m => m.StartTime).Take(10)
                .Select(m => FormatMatchLine(m, heroNames)).ToList();
            embed.AddField($"📋 Матчи (всего {matches.Count})", string.Join("\n", lastMatches), false);
        }
        else
        {
            embed.AddField("📋 Матчи", "*Пока нет сыгранных матчей*", false);
        }

        if (player?.Profile != null)
        {
            string accountInfo = $"**Никнейм:** {player.Profile.PersonaName ?? "скрыт"}\n" +
                                 $"**Ранг:** {DotaRankHelper.GetRankName(player.RankTier, player.LeaderboardRank)}\n";
            if (wl != null && wl.Win + wl.Lose > 0)
            {
                int total = wl.Win + wl.Lose;
                double winrate = wl.Win * 100.0 / total;
                accountInfo += $"**Винрейт:** {winrate:F1}% ({wl.Win}W / {wl.Lose}L)";
            }
            embed.AddField("🎮 Ваш аккаунт", accountInfo, false);
        }

        embed.WithFooter($"Создан: {order.CreatedAt:dd.MM.yyyy HH:mm} UTC");
        if (!string.IsNullOrEmpty(player?.Profile?.AvatarFull))
            embed.WithThumbnailUrl(player.Profile.AvatarFull);

        return embed.Build();
    }

    private async Task<Embed> BuildBoosterEmbedAsync(Order order)
    {
        var player = await _dota.GetPlayerInfoAsync(order.DotaAccountId);
        var wl = await _dota.GetWinLossAsync(order.DotaAccountId);
        Console.WriteLine($"win {wl.Win} ;ose {wl.Lose}");
        var heroNames = await _dota.GetHeroNamesAsync();
        var matches = JsonSerializer.Deserialize<List<OrderMatch>>(order.MatchesJson ?? "[]") ?? new();

        var embed = new EmbedBuilder()
            .WithTitle($"🎯 Заказ #{order.Id} | {order.Service.Name}")
            .WithColor(order.IsShiftActive ? Color.Green : Color.Orange)
            .AddField("Смена", order.IsShiftActive ? "🟢 Активна" : "🔴 Не начата", true)
            .AddField("Цена", $"{order.Price} ₽", true)
            .AddField("К выплате", $"{order.BoosterTotalPayout:F2} ₽", true);

        if (player?.Profile != null)
        {
            string profile = $"**Никнейм:** {player.Profile.PersonaName ?? "скрыт"}\n" +
                             $"**Ранг:** {DotaRankHelper.GetRankName(player.RankTier, player.LeaderboardRank)}\n";

            int? mmr = player.MmrEstimate?.Estimate ?? (int?)(player.ComputedMmr);
            if (mmr != null)
                profile += $"**MMR:** ~{mmr}\n";
            if (wl != null && wl.Win + wl.Lose > 0)
            {
                int total = wl.Win + wl.Lose;
                double winrate = wl.Win * 100.0 / total;
                profile += $"**Винрейт:** {winrate:F1}% ({wl.Win}W / {wl.Lose}L)";
            }
            embed.AddField("🎮 Аккаунт", profile, false);
        }

        string progress = order.Service.Type switch
        {
            ServiceType.Boost or ServiceType.Calibration =>
                $"**Старт:** {order.StartMmr} MMR → **Сейчас:** {order.CurrentMmr} MMR\n" +
                $"**Цель:** {order.TargetMmr} MMR (+{order.TargetMmrDelta})",
            ServiceType.LP =>
                $"**Игр сыграно:** {order.TotalGamesPlayed} / {order.TargetGames}\n" +
                $"**Вами:** {order.BoosterGamesPlayed} | **Владельцем:** {order.OwnerGamesPlayed}",
            ServiceType.Behavior =>
                $"**Игр сыграно:** {order.TotalGamesPlayed}",
            _ => "—"
        };
        embed.AddField("📊 Прогресс", progress, false);

        if (matches.Count > 0)
        {
            var lastMatches = matches.OrderByDescending(m => m.StartTime).Take(10)
                .Select(m => FormatMatchLine(m, heroNames)).ToList();
            embed.AddField($"📋 Матчи (всего {matches.Count})", string.Join("\n", lastMatches), false);
        }
        else
        {
            embed.AddField("📋 Матчи", "*Пока нет сыгранных матчей*", false);
        }

        embed.WithFooter($"Клиент: {order.ClientId} | Создан: {order.CreatedAt:dd.MM.yyyy HH:mm} UTC");
        if (!string.IsNullOrEmpty(player?.Profile?.AvatarFull))
            embed.WithThumbnailUrl(player.Profile.AvatarFull);

        return embed.Build();
    }

    private string FormatMatchLine(OrderMatch m, Dictionary<int, string> heroNames)
    {
        string result = m.IsWin ? "✅ Win" : "❌ Lose";
        string who = m.IsBooster ? "🎯 Бустер" : "👤 Владелец";
        string hero = heroNames.TryGetValue(m.HeroId, out var name) ? name : $"hero#{m.HeroId}";
        string mmr = m.MmrDelta != 0 ? $" `{(m.MmrDelta > 0 ? "+" : "")}{m.MmrDelta} MMR`" : "";
        return $"{result} • **{hero}** • {who}{mmr}";
    }

    private MessageComponent BuildClientButtons()
    {
        return new ComponentBuilder().Build();
    }

    private MessageComponent BuildBoosterButtons(Order order)
    {
        var builder = new ComponentBuilder();
        if (order.Status == OrderStatus.Completed) return builder.Build();

        if (order.IsShiftActive)
            builder.WithButton("🔴 Закончить смену", $"end_shift_{order.Id}", ButtonStyle.Danger);
        else
            builder.WithButton("🟢 Начать смену", $"start_shift_{order.Id}", ButtonStyle.Success);

        return builder.Build();
    }

    private async Task UpdateLastBotMessageAsync(IThreadChannel thread, Embed embed, MessageComponent components)
    {
        try
        {
            var messages = await thread.GetMessagesAsync(5).FlattenAsync();
            var botMessage = messages.FirstOrDefault(m => m.Author.Id == _client.CurrentUser.Id) as IUserMessage;

            if (botMessage != null)
                await botMessage.ModifyAsync(m => { m.Embed = embed; m.Components = components; });
            else
                await thread.SendMessageAsync(embed: embed, components: components);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[THREAD UPDATE ERROR] {ex.Message}");
        }
    }

    private string GetStatusText(Order order)
    {
        return order.Status switch
        {
            OrderStatus.Paid => "Ожидает начала работы",
            OrderStatus.InProgress => order.IsShiftActive ? "Бустер работает" : "В работе (смена не активна)",
            OrderStatus.Completed => "Заказ выполнен",
            OrderStatus.Cancelled => "Отменен",
            _ => "Неизвестно"
        };
    }
}