namespace Maki.Core.Parsing;

/// <summary>
/// A source that splits each chapter into parts lists them as 4.1, 4.2, 4.3. Whole and parts are the
/// same pages, so once the library has one form the other needs no download: parts of a chapter
/// the library has whole, or a whole chapter whose parts it all has. A run of parts starts at .1 and
/// has at least two, so a lone 3.5 is an extra and is left alone, and so is 2.51, the first part of
/// the extra 2.5.
/// </summary>
public static class ChapterPartCoverage
{
    /// <param name="Had">The row has a file, or had one that was removed on purpose.</param>
    public readonly record struct Row(int Id, decimal? Number, string? Language, bool Had);

    /// <summary>One chapter split into parts: its whole rows, if any, and the part rows .1 to .k by part.</summary>
    public sealed record Split(int Whole, string Language, IReadOnlyList<Row> WholeRows, IReadOnlyList<IReadOnlyList<Row>> Parts)
    {
        public bool WholeHad => WholeRows.Any(r => r.Had);

        public bool EveryPartHad => Parts.All(part => part.Any(r => r.Had));

        public IEnumerable<Row> PartRows => Parts.SelectMany(part => part);
    }

    private static string Key(string? language) => (language ?? string.Empty).ToUpperInvariant();

    /// <summary>Whether a number can be a part: one decimal place, above zero.</summary>
    public static bool MayBePart(decimal number) =>
        number > 0 && number != decimal.Truncate(number) && number * 10 == decimal.Truncate(number * 10);

    /// <summary>Every chapter whose parts form a run from .1 with at least two parts. Languages are kept apart.</summary>
    public static IReadOnlyList<Split> Splits(IReadOnlyCollection<Row> rows)
    {
        var wholes = rows
            .Where(r => r.Number is { } n && n >= 0 && n == decimal.Truncate(n))
            .GroupBy(r => (Key(r.Language), (int)r.Number!.Value))
            .ToDictionary(g => g.Key, g => g.ToList());

        var splits = new List<Split>();
        var groups = rows
            .Where(r => r.Number is { } n && MayBePart(n))
            .GroupBy(r => (Language: Key(r.Language), Whole: (int)decimal.Truncate(r.Number!.Value)));
        foreach (var group in groups)
        {
            var byPart = group
                .GroupBy(r => (int)((r.Number!.Value - group.Key.Whole) * 10))
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Row>)[.. g]);
            var run = 0;
            while (byPart.ContainsKey(run + 1))
            {
                run++;
            }

            if (run < 2)
            {
                continue;
            }

            splits.Add(new Split(
                group.Key.Whole, group.Key.Language,
                wholes.TryGetValue((group.Key.Language, group.Key.Whole), out var w) ? w : [],
                [.. Enumerable.Range(1, run).Select(t => byPart[t])]));
        }

        return splits;
    }

    /// <summary>The part rows not had whose whole chapter is.</summary>
    public static IReadOnlyList<int> CoveredParts(IReadOnlyCollection<Row> rows) => CoveredParts(Splits(rows));

    /// <summary>The whole rows not had whose every part is.</summary>
    public static IReadOnlyList<int> CoveredWholes(IReadOnlyCollection<Row> rows) => CoveredWholes(Splits(rows));

    /// <summary>Rows the library already has in the other form: <see cref="CoveredParts(IReadOnlyCollection{Row})"/> and <see cref="CoveredWholes(IReadOnlyCollection{Row})"/>.</summary>
    public static IReadOnlySet<int> Covered(IReadOnlyList<Split> splits) =>
        CoveredParts(splits).Concat(CoveredWholes(splits)).ToHashSet();

    private static IReadOnlyList<int> CoveredParts(IReadOnlyList<Split> splits) =>
        [.. splits.Where(s => s.WholeHad).SelectMany(s => s.PartRows).Where(r => !r.Had).Select(r => r.Id)];

    private static IReadOnlyList<int> CoveredWholes(IReadOnlyList<Split> splits) =>
        [.. splits.Where(s => !s.WholeHad && s.EveryPartHad).SelectMany(s => s.WholeRows).Select(r => r.Id)];
}
