using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Discord.WebSocket;
using DiscordBirge.Data;
using DiscordBirge.Models;
using Microsoft.Extensions.Configuration;
using System.Linq; // Для FirstOrDefault

[ApiController]
[Route("api/webhooks/scuntra")] // <-- ИСПРАВЛЕНО: Точный маршрут, как в Scuntra
public class ScuntraWebhookController : ControllerBase
{
    private readonly DbService _db;
    private readonly DiscordSocketClient _client;
    private readonly string _secret;
    private readonly ulong _guildId;
    private readonly ulong _clientRoleId;

    public ScuntraWebhookController(DbService db, DiscordSocketClient client, IConfiguration config)
    {
        _db = db;
        _client = client;
        _secret = config["Scuntra:WebhookSecret"]!;
        
        // Берем ID из конфига, а не хардкодим
        _guildId = ulong.Parse(config["Discord:GuildId"]!);
        _clientRoleId = ulong.Parse(config["Discord:ClientRoleId"]!);
    }

    [HttpPost]
    public async Task<IActionResult> HandleWebhook()
    {
        // 1. Читаем тело запроса
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        // 2. Проверяем подпись (Scuntra-Signature)
        if (!Request.Headers.TryGetValue("Scuntra-Signature", out var sigHeader)) // <-- ИСПРАВЛЕНО: имя заголовка
            return Unauthorized("No signature header");

        // Формат заголовка: t=<unix-ts>,n=<hex-nonce>,v1=<hex-sha256>
        var parts = sigHeader.ToString().Split(',');
        string timestamp = "", nonce = "", signature = "";

        foreach (var part in parts)
        {
            if (part.StartsWith("t=")) timestamp = part.Substring(2);
            else if (part.StartsWith("n=")) nonce = part.Substring(2);
            else if (part.StartsWith("v1=")) signature = part.Substring(3);
        }

        if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(signature))
            return Unauthorized("Invalid signature format");

        // 3. Проверяем HMAC: hash(secret, t.n.body)
        string payloadToSign = $"{timestamp}.{nonce}.{body}"; // <-- ИСПРАВЛЕНО: правильная строка для хеша
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secret));
        var computedHash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadToSign))).ToLower();

        if (computedHash != signature.ToLower())
        {
            Console.WriteLine($"[WEBHOOK] Signature mismatch! Expected {computedHash}, got {signature}");
            return Unauthorized("Invalid signature");
        }

        // 4. Парсим JSON
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        
        string eventType = root.GetProperty("event").GetString() ?? "";
        string status = root.GetProperty("status").GetString() ?? "";
        
        // Ищем order_id (он лежит в user-data, если мы его туда передали при создании)
        string orderIdStr = "";
        if (root.TryGetProperty("user-data", out var userData) && userData.TryGetProperty("order_id", out var orderIdProp))
        {
            orderIdStr = orderIdProp.GetString() ?? "";
        }
        else if (root.TryGetProperty("order_id", out var rootOrderId))
        {
             orderIdStr = rootOrderId.GetString() ?? "";
        }

        Console.WriteLine($"[WEBHOOK] Event: {eventType}, Order: {orderIdStr}, Status: {status}");

        if (eventType == "invoice.paid" && status == "paid")
        {
            var order = await _db.GetOrderByCustomIdAsync(orderIdStr);
            if (order != null && order.Status != OrderStatus.Paid)
            {
                // Обновляем статус в БД
                await _db.UpdateOrderStatusAsync(order.Id, OrderStatus.Paid);
                Console.WriteLine($"[WEBHOOK] Order {order.Id} marked as Paid.");

                // 5. Выдаем роль клиенту в Discord
                var guild = _client.GetGuild(_guildId);
		var user = guild?.GetUser(order.ClientId); // <-- Используем синхронный метод GetUser
                
                if (user != null)
                {
                    var role = guild.GetRole(_clientRoleId);
                    if (role != null) 
                    {
                        await user.AddRoleAsync(role);
                        Console.WriteLine($"[WEBHOOK] Role given to {user.Username}");
                    }
                }
            }
        }

        // Scuntra строго требует вернуть текст *ok*
        return Content("*ok*", "text/plain"); // <-- ИСПРАВЛЕНО: возвращаем *ok*
    }
}
