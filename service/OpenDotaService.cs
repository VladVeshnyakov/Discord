using System.Text.Json;

namespace DiscordBirge.Services;

public class OpenDotaService
{
    private readonly HttpClient _http;

    public OpenDotaService(HttpClient http)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://api.opendota.com/api/");
    }

    public async Task<List<DotaMatchDto>> GetRecentMatchesAsync(long accountId)
    {
        try
        {
            var response = await _http.GetAsync($"players/{accountId}/matches?limit=10");
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            var matches = JsonSerializer.Deserialize<List<DotaMatchDto>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            return matches ?? new List<DotaMatchDto>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OPENDOTA ERROR] {ex.Message}");
            return new List<DotaMatchDto>();
        }
    }
}

public class DotaMatchDto
{
    public long MatchId { get; set; }
    public int PlayerSlot { get; set; } // 0-4 Radiant, 128-132 Dire
    public bool RadiantWin { get; set; }
    public long StartTime { get; set; } // Unix timestamp
}