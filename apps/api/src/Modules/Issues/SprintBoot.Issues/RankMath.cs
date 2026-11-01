namespace SprintBoot.Issues;

/// <summary>
/// Fractional ranks: a new rank fits between two neighbours as a string, so a move
/// writes one row. Compared ordinally, the column uses the C collation.
/// </summary>
public static class RankMath
{
    public const int RebalanceLength = 64;

    private const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";

    public static string Between(string? low, string? high)
    {
        low ??= string.Empty;
        if (high is not null && string.CompareOrdinal(low, high) >= 0)
        {
            throw new ArgumentException($"Rank '{low}' must sort before '{high}'.");
        }

        var result = new System.Text.StringBuilder();
        var bounded = high is not null;
        for (var index = 0; ; index++)
        {
            var lowDigit = index < low.Length ? Digits.IndexOf(low[index]) : 0;
            var highDigit = bounded && index < high!.Length ? Digits.IndexOf(high[index]) : Digits.Length;

            if (highDigit - lowDigit > 1)
            {
                result.Append(Digits[(lowDigit + highDigit) / 2]);
                return result.ToString();
            }

            result.Append(Digits[lowDigit]);
            if (highDigit != lowDigit)
            {
                bounded = false;
            }
        }
    }

    /// <summary>Evenly spaced ranks for a whole list, used to restore gaps.</summary>
    public static List<string> Sequence(int count)
    {
        var ranks = new List<string>(count);
        var previous = (string?)null;
        for (var index = 0; index < count; index++)
        {
            previous = Between(previous, null);
            ranks.Add(previous);
        }

        return ranks;
    }
}
