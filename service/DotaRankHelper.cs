using DiscordBirge.Models;

namespace DiscordBirge.Services;

public static class DotaRankHelper
{
    private static readonly string[] Medals =
    {
        "Неизвестно", "Herald", "Guardian", "Crusader", "Archon",
        "Legend", "Ancient", "Divine", "Immortal"
    };

    public static string GetRankName(int? rankTier, int? leaderboardRank)
    {
        if (!rankTier.HasValue || rankTier.Value == 0)
            return "Не откалиброван";

        int medal = rankTier.Value / 10;
        int star = rankTier.Value % 10;

        if (medal >= 8)
            return leaderboardRank.HasValue ? $"Immortal #{leaderboardRank}" : "Immortal";

        if (medal < 1 || medal > 8) return "Неизвестно";

        return $"{Medals[medal]} {star}";
    }

    /// <summary>
    /// Проверяет, подходит ли сыгранный матч под условия конкретной услуги.
    /// </summary>
    public static bool IsMatchAllowed(ServiceType type, int gameMode, int lobbyType)
    {
        return type switch
        {
            //только Single Draft (код 4)
            ServiceType.LP => gameMode == 4,

            // только Рейтинг (lobby_type == 7)
            ServiceType.Boost => lobbyType == 7,
            ServiceType.Calibration => lobbyType == 7,

            // Behavior (Порядочность) - подходит любой режим (и обычные, и турбо)
            ServiceType.Behavior => true,

            _ => true
        };
    }
}