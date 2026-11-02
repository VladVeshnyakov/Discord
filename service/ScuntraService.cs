using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace DiscordBirge.Services;

public class ScuntraService
{
    private readonly HttpClient _http;
    private readonly string _accountId;
    private readonly string _accessToken;

    public ScuntraService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _accountId = config["Scuntra:AccountId"]!;
        _accessToken = config["Scuntra:AccessToken"]!; 
    }

    public async Task<string?> CreateInvoiceAsync(string coin, decimal amount, string orderId, string orderDesc, string webhookUrl)
    {
        var requestUrl = $"https://scuntra.com/api/v2/accounts/{_accountId}/invoices";

        // Конвертация в минимальные единицы (для BTC это 8 знаков)
        long amountInSmallestUnit = (long)(amount * 100_000_000); 

        var payload = new Dictionary<string, object>
        {
            { "currency", coin }, // Будем передавать "btc"
            { "amount", amountInSmallestUnit.ToString() },
            { "callback-url", webhookUrl },
            { "user-data", new { order_id = orderId } }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Headers.Add("Authorization", $"Bearer {_accessToken}"); 
        request.Headers.Add("Idempotency-Key", $"order_{orderId}_{Guid.NewGuid():N}");
        request.Content = JsonContent.Create(payload);

        try
        {
            var response = await _http.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[SCUNTRA ERROR] Status: {response.StatusCode}");
                Console.WriteLine($"[SCUNTRA ERROR] Body: {responseBody}");
                return null;
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Ищем ссылку на оплату
            string? payUrl = null;
            if (root.TryGetProperty("invoice-url", out var urlProp)) payUrl = urlProp.GetString(); // Для Scuntra это поле
            else if (root.TryGetProperty("url", out var urlProp2)) payUrl = urlProp2.GetString();

            return payUrl;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SCUNTRA EXCEPTION] {ex.Message}");
            return null;
        }
    }
}
