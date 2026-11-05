using Discord;
using Discord.WebSocket;
using DiscordBirge.Data;
using DiscordBirge.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace DiscordBirge.Services;

public class OrderChannelService
{
    private readonly DiscordSocketClient _client;
    private readonly DbService _db;
    private readonly ulong _guildId;
    private readonly ulong _ordersChannelId;

    public OrderChannelService(DiscordSocketClient client, DbService db, IConfiguration config)
    {
        _client = client;
        _db = db;
        _guildId = ulong.Parse(config["Discord:GuildId"]!);
        _ordersChannelId = ulong.Parse(config["Discord:OrdersChannelId"]!);
    }

    public async Task CreateOrderThreadsAsync(Order order)
    {
        var guild = _client.GetGuild(_guildId);
        var ordersChannel = guild?.GetTextChannel(_ordersChannelId);
        if (ordersChannel == null) return;

        var client = guild.GetUser(order.ClientId);
        var booster = guild.GetUser(order.BoosterId);

        ulong? clientThreadId = null;
        ulong? boosterThreadId = null;

        // 1. Ветка клиента
        if (client != null)
        {
            try
            {
                var thread = await ordersChannel.CreateThreadAsync(
                    name: $"заказ-{order.Id}-клиент",
                    type: ThreadType.PrivateThread,
                    autoArchiveDuration: ThreadArchiveDuration.OneDay,
                    invitable: false
                );
                await thread.AddUserAsync(client);
                await thread.JoinAsync();
                clientThreadId = thread.Id;
                
                await thread.SendMessageAsync(
                    embed: BuildProgressEmbed(order, "Ожидает начала работы"),
                    components: BuildClientButtons(order)
                );
            }
            catch (Exception ex) { Console.WriteLine($"[THREAD ERROR] Client: {ex.Message}"); }
        }

        // 2. Ветка бустера
        if (booster != null)
        {
            try
            {
                var thread = await ordersChannel.CreateThreadAsync(
                    name: $"заказ-{order.Id}-бустер",
                    type: ThreadType.PrivateThread,
                    autoArchiveDuration: ThreadArchiveDuration.OneDay,
                    invitable: false
                );
                await thread.AddUserAsync(booster);
                await thread.JoinAsync();
                boosterThreadId = thread.Id;

                await thread.SendMessageAsync(
                    embed: BuildProgressEmbed(order, "Ожидает начала работы"),
                    components: BuildBoosterButtons(order)
                );
            }
            catch (Exception ex) { Console.WriteLine($"[THREAD ERROR] Booster: {ex.Message}"); }
        }

        if (clientThreadId.HasValue || boosterThreadId.HasValue)
        {
            await _db.UpdateOrderThreadsAsync(order.Id, clientThreadId, boosterThreadId);
            Console.WriteLine($"[THREADS] Order #{order.Id}: client={clientThreadId}, booster={boosterThreadId}");
        }
    }

    public async Task UpdateThreadsProgressAsync(Order order)
    {
        var status = GetStatusText(order);
        var embed = BuildProgressEmbed(order, status);

        if (order.ClientThreadId.HasValue)
        {
            var thread = _client.GetChannel(order.ClientThreadId.Value) as IThreadChannel;
            if (thread != null)
                await UpdateLastBotMessageAsync(thread, embed, BuildClientButtons(order));
        }

        if (order.BoosterThreadId.HasValue)
        {
            var thread = _client.GetChannel(order.BoosterThreadId.Value) as IThreadChannel;
            if (thread != null)
                await UpdateLastBotMessageAsync(thread, embed, BuildBoosterButtons(order));
        }
    }

    private async Task UpdateLastBotMessageAsync(IThreadChannel thread, Embed embed, MessageComponent components)
    {
        try
        {
            var messages = await thread.GetMessagesAsync(5).FlattenAsync();
            var botMessage = messages.FirstOrDefault(m => m.Author.Id == _client.CurrentUser.Id) as IUserMessage;
            
            if (botMessage != null)
            {
                await botMessage.ModifyAsync(m => 
                { 
                    m.Embed = embed; 
                    m.Components = components; 
                });
            }
            else
            {
                await thread.SendMessageAsync(embed: embed, components: components);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[THREAD UPDATE ERROR] {ex.Message}");
        }
    }

    private Embed BuildProgressEmbed(Order order, string statusText)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"💼 Заказ #{order.Id} | {order.Service.Name}")
            .WithDescription($"**Клиент:** <@{order.ClientId}>\n**Бустер:** <@{order.BoosterId}>")
            .AddField("Статус", statusText, true)
            .WithColor(order.Status == OrderStatus.Completed ? Color.Green : Color.Blue)
            .WithFooter($"Цена: {order.Price} ₽");

        if (order.Service.Type == ServiceType.Boost || order.Service.Type == ServiceType.Calibration)
            embed.AddField("MMR", $"{order.CurrentMmr} / {order.TargetMmr}", true);
        else if (order.Service.Type == ServiceType.LP)
            embed.AddField("Игры LP", $"{order.TotalGamesPlayed} / {order.TargetGames}", true);
        else if (order.Service.Type == ServiceType.Behavior)
            embed.AddField("Сыграно игр", order.TotalGamesPlayed.ToString(), true);

        if (order.TotalGamesPlayed > 0)
        {
            embed.AddField("Статистика", 
                $"Бустер: {order.BoosterGamesPlayed} игр\nВладелец: {order.OwnerGamesPlayed} игр", 
                false);
        }

        return embed.Build();
    }

    private MessageComponent BuildClientButtons(Order order)
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

    private string GetStatusText(Order order)
    {
        return order.Status switch
        {
            OrderStatus.Paid => "🟡 Ожидает начала работы",
            OrderStatus.InProgress => order.IsShiftActive ? "🟢 Бустер работает" : "🟡 В работе (смена не активна)",
            OrderStatus.Completed => "✅ Заказ выполнен",
            OrderStatus.Cancelled => "❌ Отменен",
            _ => "Неизвестно"
        };
    }
}