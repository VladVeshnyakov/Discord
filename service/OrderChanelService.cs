using Discord;
using Discord.WebSocket;
using DiscordBirge.Data;
using DiscordBirge.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DiscordBirge.Services;

public class OrderChannelService
{
    private readonly DiscordSocketClient _client;
    private readonly DbService _db;
    private readonly ulong _guildId;

    public OrderChannelService(DiscordSocketClient client, DbService db, IConfiguration config)
    {
        _client = client;
        _db = db;
        _guildId = ulong.Parse(config["Discord:GuildId"]!);
    }

    // ==========================================
    // СОЗДАНИЕ КАНАЛОВ
    // ==========================================

    public async Task CreateOrderChannelsAsync(Order order)
    {
        var guild = _client.GetGuild(_guildId);
        if (guild == null) return;

        var client = guild.GetUser(order.ClientId);
        var booster = guild.GetUser(order.BoosterId);

        ulong clientChannelId = 0;
        ulong boosterChannelId = 0;

        // Канал для клиента
        if (client != null)
        {
            var channel = await CreatePrivateChannelAsync(
                guild, 
                new[] { client.Id, _client.CurrentUser.Id }, 
                $"order-{order.Id}-client"
            );
            
            if (channel != null)
            {
                clientChannelId = channel.Id;
                await channel.SendMessageAsync(
                    embed: BuildProgressEmbed(order, "Ожидает начала работы"),
                    components: BuildClientButtons(order)
                );
            }
        }

        // Канал для бустера
        if (booster != null)
        {
            var channel = await CreatePrivateChannelAsync(
                guild, 
                new[] { booster.Id, client?.Id ?? 0, _client.CurrentUser.Id }.Where(id => id != 0).ToArray(),
                $"order-{order.Id}-booster"
            );
            
            if (channel != null)
            {
                boosterChannelId = channel.Id;
                await channel.SendMessageAsync(
                    embed: BuildProgressEmbed(order, "Ожидает начала работы"),
                    components: BuildBoosterButtons(order)
                );
            }
        }

        // Сохраняем ID каналов в БД
        if (clientChannelId != 0 || boosterChannelId != 0)
        {
            await _db.UpdateOrderChannelsAsync(order.Id, clientChannelId, boosterChannelId);
            Console.WriteLine($"[CHANNELS] Order #{order.Id}: client={clientChannelId}, booster={boosterChannelId}");
        }
    }

    // ==========================================
    // ОБНОВЛЕНИЕ ПРОГРЕССА В КАНАЛАХ
    // ==========================================

    public async Task UpdateChannelsProgressAsync(Order order)
    {
        var status = GetStatusText(order);
        var embed = BuildProgressEmbed(order, status);

        if (order.ClientChannelId.HasValue)
        {
            var clientChannel = _client.GetChannel(order.ClientChannelId.Value) as IMessageChannel;
            if (clientChannel != null)
                await UpdateLastBotMessageAsync(clientChannel, embed, BuildClientButtons(order));
        }

        if (order.BoosterChannelId.HasValue)
        {
            var boosterChannel = _client.GetChannel(order.BoosterChannelId.Value) as IMessageChannel;
            if (boosterChannel != null)
                await UpdateLastBotMessageAsync(boosterChannel, embed, BuildBoosterButtons(order));
        }
    }

    // ==========================================
    // УДАЛЕНИЕ КАНАЛОВ
    // ==========================================

    public async Task DeleteOrderChannelsAsync(Order order)
    {
        if (order.ClientChannelId.HasValue)
        {
            var ch = _client.GetChannel(order.ClientChannelId.Value) as IGuildChannel;
            if (ch != null) await ch.DeleteAsync();
        }
        if (order.BoosterChannelId.HasValue)
        {
            var ch = _client.GetChannel(order.BoosterChannelId.Value) as IGuildChannel;
            if (ch != null) await ch.DeleteAsync();
        }
    }

    // ==========================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ==========================================

    private async Task<ITextChannel?> CreatePrivateChannelAsync(IGuild guild, ulong[] allowedUserIds, string channelName)
    {
        try
        {
            var overwrites = new List<Overwrite>
            {
                // 1. Запрещаем всем (роль @everyone) видеть канал
                new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role, 
                    new OverwritePermissions(viewChannel: PermValue.Deny))
            };

            // 2. Разрешаем доступ клиенту и бустеру (тем, кто передан в allowedUserIds)
            foreach (var userId in allowedUserIds.Distinct())
            {
                overwrites.Add(new Overwrite(userId, PermissionTarget.User, 
                    new OverwritePermissions(
                        viewChannel: PermValue.Allow,
                        sendMessages: PermValue.Allow,
                        readMessageHistory: PermValue.Allow,
                        attachFiles: PermValue.Allow
                    )));
            }

            // 3. Разрешаем доступ самому боту (используем его ID напрямую)
            overwrites.Add(new Overwrite(_client.CurrentUser.Id, PermissionTarget.User, 
                new OverwritePermissions(
                    viewChannel: PermValue.Allow,
                    sendMessages: PermValue.Allow,
                    readMessageHistory: PermValue.Allow,
                    manageChannel: PermValue.Allow,
                    manageMessages: PermValue.Allow
                )));

            // 4. Создаем канал с этими правами
            return await guild.CreateTextChannelAsync(channelName, x =>
            {
                x.PermissionOverwrites = overwrites;
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CHANNEL ERROR] {ex.Message}");
            return null;
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
        {
            embed.AddField("MMR", $"{order.CurrentMmr} / {order.TargetMmr}", true);
        }
        else if (order.Service.Type == ServiceType.LP)
        {
            embed.AddField("Игры LP", $"{order.TotalGamesPlayed} / {order.TargetGames}", true);
        }
        else if (order.Service.Type == ServiceType.Behavior)
        {
            embed.AddField("Сыграно игр", order.TotalGamesPlayed.ToString(), true);
        }

        if (order.TotalGamesPlayed > 0)
        {
            embed.AddField("Статистика", 
                $"Бустер: {order.BoosterGamesPlayed} игр\nВладелец: {order.OwnerGamesPlayed} игр", 
                false);
        }

        return embed.Build();
    }

    private MessageComponent BuildBoosterButtons(Order order)
    {
        var builder = new ComponentBuilder();
        if (order.Status == OrderStatus.Completed)
            return builder.Build();

        if (order.IsShiftActive)
        {
            builder.WithButton("🔴 Закончить смену", $"end_shift_{order.Id}", ButtonStyle.Danger);
        }
        else
        {
            builder.WithButton("🟢 Начать смену", $"start_shift_{order.Id}", ButtonStyle.Success);
        }
        return builder.Build();
    }

    private MessageComponent BuildClientButtons(Order order)
    {
        var builder = new ComponentBuilder();
        // Пока что у клиента только информационные кнопки
        // Можно добавить "Связаться с поддержкой", но это отдельная фича
        return builder.Build();
    }

    private async Task UpdateLastBotMessageAsync(IMessageChannel channel, Embed embed, MessageComponent components)
    {
        try
        {
            var messages = await channel.GetMessagesAsync(5).FlattenAsync();
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
                await channel.SendMessageAsync(embed: embed, components: components);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CHANNEL UPDATE ERROR] {ex.Message}");
        }
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