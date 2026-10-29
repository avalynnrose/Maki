namespace Maki.Core.Parsing;

/// <summary>
/// How much of a series the library holds, judged against the chapter total the metadata gives
/// rather than against the chapters its sources happen to list, which includes parts, extras and
/// rows no one can download.
/// </summary>
public static class ChapterCollection
{
    public const int MaxMissingListed = 30;

    /// <param name="numbersWithFile">Number of every chapter row that has a file, chapter file or volume file alike.</param>
    /// <param name="expectedTotal">The metadata's chapter count.</param>
    /// <returns>Whole chapters 1 to the total that have a file, and the whole chapters that do not (at most <see cref="MaxMissingListed"/>).</returns>
    public static (int Collected, IReadOnlyList<int> Missing) Collected(IEnumerable<decimal> numbersWithFile, int expectedTotal)
    {
        var have = numbersWithFile
            .Where(n => n >= 1 && n <= expectedTotal && n == decimal.Truncate(n))
            .Select(n => (int)n)
            .ToHashSet();
        var missing = Enumerable.Range(1, expectedTotal).Where(n => !have.Contains(n)).Take(MaxMissingListed).ToList();
        return (have.Count, missing);
    }
}
