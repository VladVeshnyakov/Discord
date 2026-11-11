namespace DiscordBirge.Models;

public class OrderProgressInfo
{
    public int OrderId { get; set; }
    public int BoosterGames { get; set; }
    public int BoosterWins { get; set; }
    public int BoosterLosses { get; set; }
    public int ClientGames { get; set; }
    public int TotalGamesSinceBoost { get; set; }

    public double BoosterWinrate =>
        BoosterGames > 0 ? (double)BoosterWins / BoosterGames * 100.0 : 0.0;
}