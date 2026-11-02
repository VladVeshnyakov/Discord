using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using Discord.WebSocket;
using DiscordBirge.Data;
using DiscordBirge.Models;
using Microsoft.Extensions.Configuration;

[ApiController]
[Route("api/webhooks/bepusdt")]
public class BepusdtWebhookController : ControllerBase
{
    private readonly DbService _db;
    private readonly DiscordSocketClient _client;
    private readonly string _apiToken;
    private readonly ulong _guildId;
    private readonly ulong _clientRoleId;

    public BepusdtWebhookController(DbService db, DiscordSocketClient client, IConfiguration config)
    {
        _db = db;
        _client = client;
        _apiToken = config["Epusdt:ApiToken"]!;
        _guildId = ulong.Parse(config["Discord:GuildId"]!);
        _clientRoleId = ulong.Parse(config["Discord:ClientRoleId"]!);
    }

    [HttpPost]
    public async Task<IActionResult> HandleWebhook([FromForm] BepusdtNotify notify)
    {
        // 1. Проверяем подпись
        if (!ValidateSignature(notify))
        {
            Console.WriteLine("[BEPUSDT WEBHOOK] Invalid signature");
            return Unauthorized();
        }

        // 2. Проверяем статус (2 = оплачено)
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
                    if (role != null)
                    {
                        await user.AddRoleAsync(role);
                        Console.WriteLine($"[BEPUSDT WEBHOOK] Role given to {user.Username}");
                    }
                }
            }
        }

        // BEpusdt требует вернуть "success"
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

        var filtered = parameters
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal);

        var sb = new StringBuilder();
        foreach (var kv in filtered)
        {
            sb.Append($"{kv.Key}={kv.Value}&");
        }
        sb.Append(_apiToken);

        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        var computed = Convert.ToHexString(hashBytes).ToLower();

        return computed == notify.signature.ToLower();
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