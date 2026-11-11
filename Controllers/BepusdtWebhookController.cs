using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Discord.WebSocket;
using DiscordBirge.Data;
using DiscordBirge.Models;
using DiscordBirge.Services;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.IO;
using Discord;

[ApiController]
[Route("api/webhooks/bepusdt")]
public class BepusdtWebhookController : ControllerBase
{
    private readonly DbService _db;
    private readonly DiscordSocketClient _client;
    private readonly OrderChannelService _orderChannels;
    private readonly string _apiToken;
    private readonly ulong _guildId;
    private readonly ulong _clientRoleId;
    IConfiguration _config;

    public BepusdtWebhookController(
        DbService db, 
        DiscordSocketClient client, 
        OrderChannelService orderChannels,
        IConfiguration config)
    {
        _config = config;
        _db = db;
        _client = client;
        _orderChannels = orderChannels;
        _apiToken = config["Epusdt:ApiToken"]!;
        _guildId = ulong.Parse(config["Discord:GuildId"]!);
        _clientRoleId = ulong.Parse(config["Discord:ClientRoleId"]!);
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
                
                // Безопасно достаем поля, конвертируя числа в строки
                notify = new BepusdtNotify
                {
                    trade_id = root.TryGetProperty("trade_id", out var trade) ? trade.ToString() : "",
                    order_id = root.TryGetProperty("order_id", out var order) ? order.ToString() : "",
                    amount = root.TryGetProperty("amount", out var amount) ? amount.ToString() : "",
                    actual_amount = root.TryGetProperty("actual_amount", out var actual) ? actual.ToString() : "",
                    token = root.TryGetProperty("token", out var token) ? token.ToString() : "",
                    block_transaction_id = root.TryGetProperty("block_transaction_id", out var block) ? block.ToString() : "",
                    signature = root.TryGetProperty("signature", out var sig) ? sig.ToString() : "",
                    status = root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Number ? status.GetInt32() : 0
                };
            }
            // Если пришел form-data (от твоего curl скрипта)
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

        if (notify.status == 2)
        {
            var order = await _db.GetOrderByCustomIdAsync(notify.order_id);
            if (order != null && order.Status != OrderStatus.Paid)
            {
                await _db.UpdateOrderStatusAsync(order.Id, OrderStatus.Paid);
                Console.WriteLine($"[BEPUSDT WEBHOOK] Order {order.Id} marked as Paid.");

                var guild = _client.GetGuild(_guildId);
                var user = guild?.GetUser(order.ClientId);

                if (user != null)
                {
                    var role = guild!.GetRole(_clientRoleId);
                    if (role != null) await user.AddRoleAsync(role);
                }

                await CreateOrderThreadsAsync(order);   // ← вот это
            }
        }

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
            .OrderBy(kv => kv.Key, System.StringComparer.Ordinal);

        var sb = new StringBuilder();
        foreach (var kv in filtered) sb.Append($"{kv.Key}={kv.Value}&");
        
        if (sb.Length > 0) sb.Length--; // Убираем последний '&'
        sb.Append(_apiToken);

        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        var computed = Convert.ToHexString(hash).ToLower();

        return computed == notify.signature.ToLower();
    }
    private async Task CreateOrderThreadsAsync(Order order)
{
    var guild = _client.GetGuild(_guildId);
    if (guild == null) return;

    var channel = guild.GetTextChannel(_guildId);
    if (channel == null)
    {
        Console.WriteLine($"[ORDER THREADS] Канал {_guildId} не найден");
        return;
    }

    // Защита от дублей при повторном вебхуке
    if (order.ClientThreadId != null && order.BoosterThreadId != null)
    {
        Console.WriteLine($"[ORDER THREADS] Заказ #{order.Id} уже имеет ветки, пропуск");
        return;
    }

    var client = guild.GetUser(order.ClientId);
    var booster = order.BoosterId != 0 ? guild.GetUser(order.BoosterId) : null;

    // ── Клиентская ветка ──
    var clientThread = await CreatePrivateThreadAsync(
        channel, $"Заказ #{order.Id} — Клиент",
        new[] { client, booster });

    // ── Бустерская ветка ──
    var boosterThread = await CreatePrivateThreadAsync(
        channel, $"Заказ #{order.Id} — Бустер",
        new[] { booster, client });

    if (clientThread == null || boosterThread == null)
    {
        Console.WriteLine($"[ORDER THREADS] Не удалось создать ветки для заказа #{order.Id}");
        return;
    }

    // Сохраняем ID в Order, чтобы MatchTrackerService писал в них
    order.ClientThreadId = clientThread.Id;
    order.BoosterThreadId = boosterThread.Id;
    await _db.UpdateOrderAsync(order);

    // Приветственные эмбеды (по одному в каждой ветке)
    await SendOrderWelcomeAsync(clientThread, order, "client");
    await SendOrderWelcomeAsync(boosterThread, order, "booster");

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

private async Task SendOrderWelcomeAsync(IThreadChannel thread, Order order, string role)
{
    var service = order.Service;

    var embed = new EmbedBuilder()
        .WithTitle($"Заказ #{order.Id}")
        .WithColor(Color.Blue)
        .WithCurrentTimestamp();

    if (service != null)
    {
        string priceLabel = service.Type == ServiceType.LP
            ? $"{service.Price} ₽ / игра"
            : $"{service.Price} ₽";

        embed.AddField("Услуга",
            $"**{service.Name}** ({service.Type})\nЦена: {priceLabel}",
            inline: false);
    }

    embed.AddField("Клиент", $"<@{order.ClientId}>", inline: true);
    embed.AddField("Бустер",
        order.BoosterId != 0 ? $"<@{order.BoosterId}>" : "не назначен",
        inline: true);

    if (role == "client")
    {
        embed.WithDescription(
            "Спасибо за оплату! Здесь вы будете видеть прогресс по заказу.\n" +
            "Бот будет обновлять это сообщение автоматически. 📊");
    }
    else
    {
        embed.WithDescription(
            "Заказ оплачен. Нажмите «Начать смену» в меню «Мои услуги», " +
            "когда приступите к работе. Бот будет обновлять прогресс автоматически. 📊");
    }

    await thread.SendMessageAsync(embed: embed.Build());
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