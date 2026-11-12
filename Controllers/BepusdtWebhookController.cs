using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Discord.WebSocket;
using Discord;
using DiscordBirge.Data;
using DiscordBirge.Models;
using DiscordBirge.Services;
using DiscordBirge.Helpers;                 // ← для InfoMessageService
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

[ApiController]
[Route("api/webhooks/bepusdt")]
public class BepusdtWebhookController : ControllerBase
{
    private readonly DbService _db;
    private readonly DiscordSocketClient _client;
    private readonly InfoMessageService _info;    // ← добавили
    private readonly string _apiToken;
    private readonly ulong _guildId;
    private readonly ulong _clientRoleId;
    private readonly ulong _ordersChannelId;

    public BepusdtWebhookController(
        DbService db,
        DiscordSocketClient client,
        InfoMessageService info,                  // ← добавили
        IConfiguration config)
    {
        _db = db;
        _client = client;
        _info = info;
        _apiToken = config["Epusdt:ApiToken"]!;
        _guildId = ulong.Parse(config["Discord:GuildId"]!);
        _clientRoleId = ulong.Parse(config["Discord:ClientRoleId"]!);
        _ordersChannelId = ulong.Parse(config["Discord:OrdersChannelId"]!);
    }

    [HttpPost]
    public async Task<IActionResult> HandleWebhook()
    {
        BepusdtNotify? notify = null;

        try
        {
            // Если пришел JSON (от реального BEpusdt)
            if (Request.ContentType?.Contains("application/json") == true)
            {
                using var doc = await JsonDocument.ParseAsync(Request.Body);
                var root = doc.RootElement;
                
                notify = new BepusdtNotify
                {
                    trade_id = root.TryGetProperty("trade_id", out var tradeProp) ? tradeProp.ToString() : "",
                    order_id = root.TryGetProperty("order_id", out var orderProp) ? orderProp.ToString() : "", // <-- Исправили на orderProp
                    amount = root.TryGetProperty("amount", out var amountProp) ? amountProp.ToString() : "",
                    actual_amount = root.TryGetProperty("actual_amount", out var actualProp) ? actualProp.ToString() : "",
                    token = root.TryGetProperty("token", out var tokenProp) ? tokenProp.ToString() : "",
                    block_transaction_id = root.TryGetProperty("block_transaction_id", out var blockProp) ? blockProp.ToString() : "",
                    signature = root.TryGetProperty("signature", out var sigProp) ? sigProp.ToString() : "",
                    status = root.TryGetProperty("status", out var statusProp) && statusProp.ValueKind == JsonValueKind.Number 
                        ? statusProp.GetInt32() 
                        : 0
                };
            }
            else
            {
                var form = await Request.ReadFormAsync();
                notify = new BepusdtNotify
                {
                    trade_id = form["trade_id"].ToString(),
                    order_id = form["order_id"].ToString(),
                    amount = form["amount"].ToString(),
                    actual_amount = form["actual_amount"].ToString(),
                    token = form["token"].ToString(),
                    block_transaction_id = form["block_transaction_id"].ToString(),
                    signature = form["signature"].ToString(),
                    status = int.TryParse(form["status"], out var s) ? s : 0
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WEBHOOK PARSING ERROR] {ex.Message}");
            return BadRequest("Parsing error");
        }

        if (notify == null || !ValidateSignature(notify))
        {
            Console.WriteLine("[BEPUSDT WEBHOOK] Invalid signature");
            return Unauthorized("Invalid signature");
        }

        if (notify.status != 2)
            return Content("success", "text/plain");

        var order = await _db.GetOrderByCustomIdAsync(notify.order_id);

        if (order == null)
        {
            Console.WriteLine($"[BEPUSDT WEBHOOK] Заказ с order_id='{notify.order_id}' не найден");
            return Content("order_not_found", "text/plain");
        }

        if (order.Status == OrderStatus.Paid)
        {
            Console.WriteLine($"[BEPUSDT WEBHOOK] Заказ {order.Id} уже оплачен, пропуск");
            return Content("already_paid", "text/plain");
        }

        await _db.UpdateOrderStatusAsync(order.Id, OrderStatus.Paid);
        Console.WriteLine($"[BEPUSDT WEBHOOK] Заказ {order.Id} помечен как Paid");

        var guild = _client.GetGuild(_guildId);
        if (guild == null)
        {
            Console.WriteLine($"[BEPUSDT WEBHOOK] Гильдия {_guildId} не найдена — проверьте GuildId!");
            return Content("guild_not_found", "text/plain");
        }

        var user = guild.GetUser(order.ClientId);
        if (user != null)
        {
            var role = guild.GetRole(_clientRoleId);
            if (role != null) await user.AddRoleAsync(role);
        }

        await CreateOrderThreadsAsync(order);

        return Content("success", "text/plain");
    }

    private bool ValidateSignature(BepusdtNotify notify)
    {
        var parameters = new Dictionary<string, string>
        {
            { "trade_id", notify.trade_id },
            { "order_id", notify.order_id },
            { "amount", notify.amount },
            { "actual_amount", notify.actual_amount },
            { "token", notify.token },
            { "block_transaction_id", notify.block_transaction_id },
            { "status", notify.status.ToString() }
        };

        var filtered = parameters.Where(kv => !string.IsNullOrEmpty(kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal);

        var sb = new StringBuilder();
        foreach (var kv in filtered) sb.Append($"{kv.Key}={kv.Value}&");
        if (sb.Length > 0) sb.Length--;
        sb.Append(_apiToken);

        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        var computed = Convert.ToHexString(hash).ToLower();

        return computed == notify.signature.ToLower();
    }

    // ─────────────── ВЕТКИ ───────────────

    private async Task CreateOrderThreadsAsync(Order order)
    {
        var guild = _client.GetGuild(_guildId);
        if (guild == null)
        {
            Console.WriteLine($"[ORDER THREADS] Гильдия {_guildId} не найдена");
            return;
        }

        var channel = guild.GetTextChannel(_ordersChannelId);
        if (channel == null)
        {
            Console.WriteLine($"[ORDER THREADS] Канал {_ordersChannelId} не найден");
            return;
        }

        if (order.ClientThreadId != null && order.BoosterThreadId != null)
        {
            Console.WriteLine($"[ORDER THREADS] Заказ #{order.Id} уже имеет ветки, пропуск");
            return;
        }

        var client = guild.GetUser(order.ClientId);
        var booster = order.BoosterId != 0 ? guild.GetUser(order.BoosterId) : null;

        if (client == null)
            Console.WriteLine($"[ORDER THREADS] Клиент {order.ClientId} не найден на сервере");
        if (booster == null && order.BoosterId != 0)
            Console.WriteLine($"[ORDER THREADS] Бустер {order.BoosterId} не найден на сервере");

        var clientThread = await CreatePrivateThreadAsync(
            channel, $"Заказ #{order.Id} — Клиент",
            new IGuildUser?[] { client, booster });

        var boosterThread = await CreatePrivateThreadAsync(
            channel, $"Заказ #{order.Id} — Бустер",
            new IGuildUser?[] { booster, client });

        if (clientThread == null || boosterThread == null)
        {
            Console.WriteLine($"[ORDER THREADS] Не удалось создать ветки для заказа #{order.Id}");
            return;
        }

        order.ClientThreadId = clientThread.Id;
        order.BoosterThreadId = boosterThread.Id;
        await _db.UpdateOrderAsync(order);

        // Вместо welcome — стартовый прогресс через InfoMessageService (marker: "📊")
        await SendInitialProgressAsync(clientThread, order);
        await SendInitialProgressAsync(boosterThread, order, BuildBoosterButtons(order));

        Console.WriteLine($"[ORDER THREADS] Заказ #{order.Id}: client={clientThread.Id}, booster={boosterThread.Id}");
    }

    private async Task<IThreadChannel?> CreatePrivateThreadAsync(
        ITextChannel channel, string name, IEnumerable<IGuildUser?> users)
    {
        try
        {
            var thread = await channel.CreateThreadAsync(
                name: name,
                type: ThreadType.PrivateThread,
                autoArchiveDuration: ThreadArchiveDuration.OneWeek,
                invitable: false);

            foreach (var u in users)
                if (u != null) await thread.AddUserAsync(u);

            await thread.JoinAsync();
            return thread;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ORDER THREADS CREATE ERROR] {ex.Message}");
            return null;
        }
    }

    // ─────────────── СТАРТОВЫЙ ПРОГРЕСС ───────────────

    private async Task SendInitialProgressAsync(
        IThreadChannel thread, Order order, MessageComponent? components = null)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"📊 Заказ #{order.Id} | {order.Service?.Name ?? "—"}")
            .WithDescription(
                "Ожидание первого матча.\n" +
                "Бот обновит прогресс автоматически, как только появится игра.")
            .AddField("Клиент", $"<@{order.ClientId}>", inline: true)
            .AddField("Бустер",
                order.BoosterId != 0 ? $"<@{order.BoosterId}>" : "не назначен",
                inline: true)
            .AddField("Статус",
                order.IsShiftActive ? "🟢 Смена активна" : "🔴 Смена не начата",
                inline: true)
            .WithColor(Color.Blue)
            .WithFooter("Обновляется автоматически")
            .WithCurrentTimestamp()
            .Build();

        await _info.UpdateAsync(
            thread,
            text: "",
            embed: embed,
            components: components,
            marker: "📊");
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
}

public class BepusdtNotify
{
    public string trade_id { get; set; } = string.Empty;
    public string order_id { get; set; } = string.Empty;
    public string amount { get; set; } = string.Empty;
    public string actual_amount { get; set; } = string.Empty;
    public string token { get; set; } = string.Empty;
    public string block_transaction_id { get; set; } = string.Empty;
    public string signature { get; set; } = string.Empty;
    public int status { get; set; }
}