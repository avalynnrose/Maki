namespace Maki.Core.Parsing;

/// <summary>
/// A source that splits each chapter into parts lists them as 4.1, 4.2, 4.3. Whole and parts are the
/// same pages, so once the library has one form the other needs no download: parts of a chapter
/// that has a file, or a whole chapter whose parts all have files. A lone 3.5 is an extra, not a
/// part, and is left alone.
/// </summary>
public static class ChapterPartCoverage
{
    public readonly record struct Row(int Id, decimal? Number, string? Language, bool HasFile);

    /// <summary>One chapter split into parts: its whole row, if any, and the part rows .1 to .k.</summary>
    public sealed record Split(int Whole, string Language, Row? WholeRow, IReadOnlyList<Row> Parts);

    private static string Key(string? language) => (language ?? string.Empty).ToUpperInvariant();

    /// <summary>
    /// Every chapter whose parts form a run starting at .1 with at least two rows, so 4.1 and 4.2
    /// count and 3.5 does not. Languages are kept apart.
    /// </summary>
    public static IReadOnlyList<Split> Splits(IReadOnlyCollection<Row> rows)
    {
        var wholes = rows
            .Where(r => r.Number is { } n && n > 0 && n == decimal.Truncate(n))
            .GroupBy(r => (Key(r.Language), (int)r.Number!.Value))
            .ToDictionary(g => g.Key, g => g.First());

        var splits = new List<Split>();
        var groups = rows
            .Where(r => r.Number is { } n && n > 0 && n != decimal.Truncate(n) && n * 10 == decimal.Truncate(n * 10))
            .GroupBy(r => (Language: Key(r.Language), Whole: (int)decimal.Truncate(r.Number!.Value)));
        foreach (var group in groups)
        {
            var byTenth = group.GroupBy(r => (int)((r.Number!.Value - group.Key.Whole) * 10)).ToDictionary(g => g.Key, g => g.First());
            var run = 0;
            while (byTenth.ContainsKey(run + 1))
            {
                run++;
            }

            if (run < 2)
            {
                continue;
            }

            Row? whole = wholes.TryGetValue((group.Key.Language, group.Key.Whole), out var w) ? w : null;
            splits.Add(new Split(group.Key.Whole, group.Key.Language, whole, [.. Enumerable.Range(1, run).Select(t => byTenth[t])]));
        }

        return splits;
    }

    /// <summary>The part rows without a file whose whole chapter has one.</summary>
    public static IReadOnlyList<int> CoveredParts(IReadOnlyCollection<Row> rows) =>
        [.. Splits(rows).Where(s => s.WholeRow is { HasFile: true }).SelectMany(s => s.Parts).Where(p => !p.HasFile).Select(p => p.Id)];

    /// <summary>The whole rows without a file whose parts all have one.</summary>
    public static IReadOnlyList<int> CoveredWholes(IReadOnlyCollection<Row> rows) =>
        [.. Splits(rows).Where(s => s.WholeRow is { HasFile: false } && s.Parts.All(p => p.HasFile)).Select(s => s.WholeRow!.Value.Id)];
}
