using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace DiscordBirge.Services;

public class ScuntraService
{
    private readonly HttpClient _http;
    private readonly string _accountId;
    private readonly string _apiHost;

    public ScuntraService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _accountId = config["Scuntra:AccountId"]!;
        _apiHost = config["Scuntra:ApiHost"] ?? "https://scuntra.com";
    }

    /// <summary>
    /// Создает инвойс и возвращает ссылку на оплату (URL)
    /// </summary>
    public async Task<string?> CreateInvoiceAsync(string coin, decimal amount, string orderId, string orderDesc, string webhookUrl)
    {
        var requestUrl = $"{_apiHost}/api/v2/accounts/{_accountId}/invoices";

        // Формируем тело запроса согласно документации
        var payload = new
        {
            coin = coin, // Например "USDT_TRC20" или "BTC"
            amount = amount,
            lifetime = 3600, // Время жизни инвойса (1 час)
            callback_url = webhookUrl, // Куда Scuntra пришлет вебхук
            // Некоторые поля могут быть опциональны, но лучше передать:
            description = orderDesc,
            order_id = orderId,
            store_name = "Birzha Bot"
        };

        // Idempotency Key (защита от дублей при повторных запросах)
        var idempotencyKey = $"order_{orderId}_{coin}_{amount}";
        _http.DefaultRequestHeaders.Remove("Idempotency-Key");
        _http.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);

        try
        {
            var response = await _http.PostAsJsonAsync(requestUrl, payload);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            
            // Scuntra обычно возвращает объект с полем url (или hosted_url, смотри ответ в тесте)
            // Предполагаем, что там есть поле "url" или "hosted_url"
            // Если в документации поле называется иначе, поправь тут.
            if (doc.RootElement.TryGetProperty("url", out var urlProp) || 
                doc.RootElement.TryGetProperty("hosted_url", out urlProp))
            {
                return urlProp.GetString();
            }
            
            Console.WriteLine($"Scuntra Response: {content}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка создания инвойса Scuntra: {ex.Message}");
            return null;
        }
    }
}