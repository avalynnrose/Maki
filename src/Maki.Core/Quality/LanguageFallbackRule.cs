namespace Maki.Core.Quality;

public enum LanguageFallbackReason
{
    /// <summary>No source in the primary language carries the series.</summary>
    NoSource,

    /// <summary>Every primary-language source was measured and serves previews or placeholders.</summary>
    PreviewsOnly
}

/// <summary>
/// Decides whether a series with no files should give up on the primary language. Pure: it is
/// handed the page counts the source scout measured and touches nothing else.
/// </summary>
public static class LanguageFallbackRule
{
    /// <summary>A source needs this many measured chapters before it can be called unusable, so one that merely failed to answer never counts.</summary>
    public const int MinMeasuredSamples = 2;

    public readonly record struct MappingSamples(int MappingId, IReadOnlyList<int> PageCounts);

    /// <summary>
    /// True when at least <see cref="MinMeasuredSamples"/> chapters were measured and the median is
    /// under <see cref="SourcePageCounts.FixedMaxPages"/> pages, which also covers files of one to
    /// three pages.
    /// </summary>
    public static bool ServesPreviewsOnly(IReadOnlyList<int> pageCounts)
    {
        if (pageCounts.Count < MinMeasuredSamples)
        {
            return false;
        }

        var sorted = pageCounts.Order().ToList();
        var median = sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;
        return median < SourcePageCounts.FixedMaxPages;
    }

    /// <summary>
    /// The reason to fall back, or null to keep the primary language. One usable or unmeasured
    /// source is enough to keep it: ranking among sources is the source order's job.
    /// </summary>
    public static LanguageFallbackReason? Evaluate(IReadOnlyCollection<MappingSamples> primaryMappings)
    {
        if (primaryMappings.Count == 0)
        {
            return LanguageFallbackReason.NoSource;
        }

        return primaryMappings.All(m => ServesPreviewsOnly(m.PageCounts))
            ? LanguageFallbackReason.PreviewsOnly
            : null;
    }
}
