public class OrderMatch
{
    public long MatchId { get; set; }
    public long StartTime { get; set; }
    public bool IsWin { get; set; }
    public bool IsBooster { get; set; }
    public int HeroId { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int Duration { get; set; }
    
    public int GameMode { get; set; }
    
    public int LobbyType { get; set; }
}