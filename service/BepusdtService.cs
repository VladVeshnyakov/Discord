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
        _apiToken = config["Epusdt:ApiToken"]?.Trim().Trim('"')!;
        _apiUrl = config["Epusdt:ApiUrl"] ?? "http://127.0.0.1:8080";
    }

    public async Task<string?> CreateInvoiceAsync(string orderId, decimal amount, string orderDesc, string webhookUrl, string redirectUrl)
    {
        var requestUrl = $"{_apiUrl}/api/v1/order/create-transaction";

        // 1. JSON-запрос: отправляем все параметры, которые нужны BEpusdt
        var requestParams = new Dictionary<string, object>
        {
            { "order_id", orderId },
            { "amount", amount },
            { "notify_url", webhookUrl },
            { "redirect_url", redirectUrl },
            { "trade_type", "usdt.trc20" },
            { "name", orderDesc }
        };

        // 2. Подпись: в нее входят ВСЕ непустые параметры из запроса
        // amount форматируем как "0.##" (1.00 -> "1"), чтобы совпасть с тем,
        // как BEpusdt распарсит число из JSON
        var signParams = new Dictionary<string, string>
        {
            { "order_id", orderId },
            { "amount", amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) },
            { "notify_url", webhookUrl },
            { "redirect_url", redirectUrl },
            { "trade_type", "usdt.trc20" },
            { "name", orderDesc }
        };

        var signature = GenerateSignature(signParams, _apiToken);
        requestParams["signature"] = signature;

        // 3. Логирование: покажет итоговую строку для хеша
        var debugString = string.Join("&", signParams.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}")) + _apiToken;
        Console.WriteLine($"[BEPUSDT DEBUG] SIGN STRING: {debugString}");
        Console.WriteLine($"[BEPUSDT DEBUG] SIGNATURE: {signature}");

        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Content = JsonContent.Create(requestParams);

        try
        {
            var response = await _http.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[BEPUSDT ERROR] {responseBody}");
                return null;
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.GetProperty("status_code").GetInt32() == 200)
            {
                if (root.GetProperty("data").TryGetProperty("payment_url", out var url))
                    return url.GetString();
            }

            Console.WriteLine($"[BEPUSDT ERROR] {responseBody}");
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
            sb.Append($"{kv.Key}={kv.Value}&");
        
        sb.Length--; // убираем последний '&'
        sb.Append(token);

        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLower();
    }
}
