using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace DiscordBirge.Services;

public class BepusdtService
{
    private readonly HttpClient _http;
    private readonly string _apiToken;
    private readonly string _apiUrl;

    public BepusdtService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiToken = config["Epusdt:ApiToken"]!;
        // URL, где запущен BEpusdt (на сервере это IP + порт)
        _apiUrl = config["Epusdt:ApiUrl"] ?? "http://127.0.0.1:8080"; 
    }

    public async Task<string?> CreateInvoiceAsync(string orderId, decimal amount, string orderDesc, string webhookUrl, string redirectUrl)
    {
        var requestUrl = $"{_apiUrl}/api/v1/order/create-transaction";

        var parameters = new Dictionary<string, string>
        {
            { "order_id", orderId },
            { "amount", amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) },
            { "fiat", "USD" },           // Считаем, что сумма передается в USD
            { "trade_type", "usdt.trc20" }, // Сеть TRC-20
            { "name", orderDesc },
            { "notify_url", webhookUrl },
            { "redirect_url", redirectUrl }
        };

        // Генерируем подпись MD5
        parameters["signature"] = GenerateSignature(parameters, _apiToken);

        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Content = JsonContent.Create(parameters);

        try
        {
            var response = await _http.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[BEPUSDT ERROR] Status: {response.StatusCode}");
                Console.WriteLine($"[BEPUSDT ERROR] Body: {responseBody}");
                return null;
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("status_code", out var statusCode) && statusCode.GetInt32() == 200)
            {
                if (root.TryGetProperty("data", out var data) && data.TryGetProperty("payment_url", out var url))
                {
                    return url.GetString();
                }
            }

            Console.WriteLine($"[BEPUSDT ERROR] Ответ не содержит ссылку: {responseBody}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BEPUSDT EXCEPTION] {ex.Message}");
            return null;
        }
    }

    private string GenerateSignature(Dictionary<string, string> parameters, string token)
    {
        var filtered = parameters
            .Where(kv => kv.Key != "signature" && !string.IsNullOrEmpty(kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal);

        var sb = new StringBuilder();
        foreach (var kv in filtered)
        {
            sb.Append($"{kv.Key}={kv.Value}&");
        }
        sb.Append(token);

        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hashBytes).ToLower();
    }
}