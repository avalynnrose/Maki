namespace Maki.Core.Parsing;

/// <summary>
/// A source that splits each chapter into parts lists them as 4.1, 4.2, 4.3. When the whole of
/// chapter 4 is already in the library those parts are the same pages again, so they need no
/// download. A lone 3.5 is an extra, not a part, and is left alone.
/// </summary>
public static class ChapterPartCoverage
{
    public readonly record struct Row(int Id, decimal? Number, string? Language, bool HasFile);

    /// <summary>
    /// The part rows without a file whose whole chapter has one. A run of parts must start at .1 and
    /// have at least two, so 4.1 and 4.2 count and 3.5 does not.
    /// </summary>
    public static IReadOnlyList<int> CoveredParts(IReadOnlyCollection<Row> rows)
    {
        static string Key(string? language) => (language ?? string.Empty).ToUpperInvariant();

        var wholesWithFile = rows
            .Where(r => r is { HasFile: true, Number: { } n } && n == decimal.Truncate(n))
            .Select(r => (Key(r.Language), (int)r.Number!.Value))
            .ToHashSet();

        var covered = new List<int>();
        var parts = rows
            .Where(r => r.Number is { } n && n > 0 && n != decimal.Truncate(n) && n * 10 == decimal.Truncate(n * 10))
            .GroupBy(r => (Key(r.Language), Whole: (int)decimal.Truncate(r.Number!.Value)));
        foreach (var group in parts)
        {
            if (!wholesWithFile.Contains((group.Key.Item1, group.Key.Whole)))
            {
                continue;
            }

            var byTenth = group.GroupBy(r => (int)((r.Number!.Value - group.Key.Whole) * 10)).ToDictionary(g => g.Key, g => g.ToList());
            var run = 0;
            while (byTenth.ContainsKey(run + 1))
            {
                run++;
            }

            if (run < 2)
            {
                continue;
            }

            covered.AddRange(Enumerable.Range(1, run).SelectMany(t => byTenth[t]).Where(r => !r.HasFile).Select(r => r.Id));
        }

        return covered;
    }
}
