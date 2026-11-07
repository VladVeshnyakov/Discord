using System.Text.Json;

namespace DiscordBirge.Services;

public class OpenDotaService
{
    private readonly HttpClient _http;
    private readonly Dictionary<long, (DotaPlayerDto Data, DateTime CachedAt)> _playerCache = new();
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(30);
    private Dictionary<int, string>? _heroNames;
    private readonly SemaphoreSlim _heroLock = new(1, 1);

    public OpenDotaService(HttpClient http)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://api.opendota.com/api/");
    }

    public async Task<Dictionary<int, string>> GetHeroNamesAsync()
    {
        if (_heroNames != null) return _heroNames;
        await _heroLock.WaitAsync();
        try
        {
            if (_heroNames != null) return _heroNames;
            var response = await _http.GetAsync("constants/heroes");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var heroes = JsonSerializer.Deserialize<Dictionary<string, DotaHeroDto>>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _heroNames = heroes?.ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value.LocalizedName) ?? new();
            return _heroNames;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OPENDOTA HEROES ERROR] {ex.Message}");
            return new Dictionary<int, string>();
        }
        finally { _heroLock.Release(); }
    }

    public async Task<List<DotaMatchDto>> GetRecentMatchesAsync(long accountId)
    {
        try
        {
            var response = await _http.GetAsync($"players/{accountId}/matches?limit=20");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var matches = JsonSerializer.Deserialize<List<DotaMatchDto>>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return matches ?? new List<DotaMatchDto>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OPENDOTA MATCHES ERROR] {ex.Message}");
            return new List<DotaMatchDto>();
        }
    }

    public async Task<DotaMatchDetailsDto?> GetMatchDetailsAsync(long matchId)
    {
        try
        {
            var response = await _http.GetAsync($"matches/{matchId}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<DotaMatchDetailsDto>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OPENDOTA MATCH DETAILS ERROR] {ex.Message}");
            return null;
        }
    }

    public async Task<DotaPlayerDto?> GetPlayerInfoAsync(long accountId)
    {
        if (_playerCache.TryGetValue(accountId, out var cached) &&
            DateTime.UtcNow - cached.CachedAt < _cacheDuration)
            return cached.Data;

        try
        {
            var response = await _http.GetAsync($"players/{accountId}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var player = JsonSerializer.Deserialize<DotaPlayerDto>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (player?.Profile != null) _playerCache[accountId] = (player, DateTime.UtcNow);
            return player;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OPENDOTA PLAYER ERROR] {ex.Message}");
            return null;
        }
    }

    public async Task<DotaWlDto?> GetWinLossAsync(long accountId)
    {
        try
        {
            var response = await _http.GetAsync($"players/{accountId}/wl");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<DotaWlDto>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OPENDOTA WL ERROR] {ex.Message}");
            return null;
        }
    }
}

// === DTOs ===
public class DotaMatchDto
{
    public long MatchId { get; set; }
    public int PlayerSlot { get; set; }
    public bool RadiantWin { get; set; }
    public long StartTime { get; set; }
    public int HeroId { get; set; }
}

public class DotaMatchDetailsDto
{
    public long MatchId { get; set; }
    public int? AvgMmr { get; set; }
    public bool RadiantWin { get; set; }
}

public class DotaHeroDto { public string LocalizedName { get; set; } = ""; }

public class DotaPlayerDto
{
    public DotaProfileDto? Profile { get; set; }
    public int? RankTier { get; set; }
    public int? LeaderboardRank { get; set; }
    public decimal? ComputedMmr { get; set; }
    public MmrEstimate? MmrEstimate { get; set; }
}

public class DotaProfileDto
{
    public long AccountId { get; set; }
    public string? PersonaName { get; set; }
    public string? AvatarFull { get; set; }
}

public class MmrEstimate { public int? Estimate { get; set; } }
public class DotaWlDto { public int Win { get; set; } public int Lose { get; set; } }