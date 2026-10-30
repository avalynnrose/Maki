namespace Maki.Core.Parsing;

/// <summary>
/// How much of a series the library holds, judged against the chapter total the metadata gives
/// rather than against the chapters its sources happen to list, which includes parts, extras and
/// rows no one can download.
/// </summary>
public static class ChapterCollection
{
    public const int MaxMissingListed = 30;

    /// <param name="rows">Every numbered chapter row, with whether it has a file (chapter file or volume file alike).</param>
    /// <param name="expectedTotal">The metadata's chapter count.</param>
    /// <returns>
    /// Whole chapters 1 to the total that are collected, and the ones that are not (at most
    /// <see cref="MaxMissingListed"/>). A whole chapter counts when it has a file or when all of its parts do.
    /// </returns>
    public static (int Collected, IReadOnlyList<int> Missing) Collected(IReadOnlyCollection<(decimal Number, bool HasFile)> rows, int expectedTotal)
    {
        var have = rows
            .Where(r => r.HasFile && r.Number >= 1 && r.Number <= expectedTotal && r.Number == decimal.Truncate(r.Number))
            .Select(r => (int)r.Number)
            .ToHashSet();
        var parts = rows.Select((r, i) => new ChapterPartCoverage.Row(i, r.Number, null, r.HasFile)).ToList();
        foreach (var split in ChapterPartCoverage.Splits(parts).Where(s => s.Parts.All(p => p.HasFile) && s.Whole <= expectedTotal))
        {
            have.Add(split.Whole);
        }

        var missing = Enumerable.Range(1, expectedTotal).Where(n => !have.Contains(n)).Take(MaxMissingListed).ToList();
        return (have.Count, missing);
    }
}
