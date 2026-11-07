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

    public static string GetMmrDisplay(int? mmrEstimate, int? rankTier)
    {
        if (mmrEstimate.HasValue) return $"~{mmrEstimate} MMR";
        if (rankTier.HasValue && rankTier.Value > 0) return GetRankName(rankTier, null);
        return "MMR неизвестен";
    }
}