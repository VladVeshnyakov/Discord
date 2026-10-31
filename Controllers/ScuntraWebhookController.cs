using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Discord.WebSocket;
using DiscordBirge.Data;
using DiscordBirge.Models;
using Microsoft.Extensions.Configuration;

[ApiController]
[Route("api/scuntra")]
public class ScuntraWebhookController : ControllerBase
{
    private readonly DbService _db;
    private readonly DiscordSocketClient _client; // Чтобы выдать роль
    private readonly string _secret;

    public ScuntraWebhookController(DbService db, DiscordSocketClient client, IConfiguration config)
    {
        _db = db;
        _client = client;
        _secret = config["Scuntra:WebhookSecret"]!;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> HandleWebhook()
    {
        // 1. Читаем тело запроса
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        // 2. Проверяем подпись (HMAC-SHA256)
        // Scuntra передает подпись в заголовке (обычно X-Scuntra-Signature или похожий)
        // Смотри документацию Scuntra для точного названия заголовка.
        if (!Request.Headers.TryGetValue("X-Scuntra-Signature", out var signature))
            return Unauthorized("No signature");

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secret));
        var computedHash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLower();
        
        if (computedHash != signature.ToString().ToLower())
            return Unauthorized("Invalid signature");

        // 3. Парсим JSON
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        
        // Ищем order_id и статус
        // Поля могут называться по-разному, проверь ответ Scuntra
        string orderIdStr = root.GetProperty("order_id").GetString();
        string status = root.GetProperty("status").GetString(); // "completed" или "confirmed"

        if (status == "completed" || status == "confirmed")
        {
            var order = await _db.GetOrderByCustomIdAsync(orderIdStr);
            if (order != null && order.Status != OrderStatus.Paid)
            {
                // 4. Обновляем статус в БД
                await _db.UpdateOrderStatusAsync(order.Id, OrderStatus.Paid);

                // 5. Выдаем роль клиенту в Discord
                // (Нужно получить гильдию и пользователя)
                var guild = _client.GetGuild(1564780799419748382); // Замени на свой ID сервера
                var user = guild?.GetUser(order.ClientId);
                if (user != null)
                {
                    // Предположим, у тебя есть роль "Клиент" с ID
                    var role = guild.GetRole(1565933660291735663); 
                    if (role != null) await user.AddRoleAsync(role);
                }
                
                // 6. Отправляем сообщение в ветку заказа (если нужно)
                // Можно отправить уведомление бустеру.
            }
        }

        return Ok();
    }
}