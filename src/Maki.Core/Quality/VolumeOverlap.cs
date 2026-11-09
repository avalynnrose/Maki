using Maki.Core.Images;

namespace Maki.Core.Quality;

/// <summary>
/// Whether a chapter file is only a piece of a volume file in the same series: nearly every page of
/// the chapter appears in the volume (compared by perceptual hash, so a rescaled or re-encoded copy
/// still matches). Chapter rows carry no volume when they come from a scraping source, so the page
/// content is the one evidence that holds for every series.
/// </summary>
public static class VolumeOverlap
{
    /// <summary>Hash distance at or under which two pages are the same page.</summary>
    public const int MaxDistance = 6;

    /// <summary>Share of the chapter's pages that must be found in the volume.</summary>
    public const double MinShare = 0.9;

    /// <summary>A chapter with fewer comparable pages than this is too small to judge by.</summary>
    public const int MinPages = 8;

    /// <summary>
    /// A file named like a volume with fewer pages than this is not trusted as one: some sources label
    /// a 30-page chapter "10巻". A real volume runs well past it.
    /// </summary>
    public const int MinVolumePages = 100;

    public static double Share(IReadOnlyList<ulong> chapter, IReadOnlyList<ulong> volume)
    {
        if (chapter.Count == 0)
        {
            return 0;
        }

        var found = chapter.Count(c => volume.Any(v => PerceptualHash.Distance(c, v) <= MaxDistance));
        return (double)found / chapter.Count;
    }

    public static bool IsCovered(IReadOnlyList<ulong> chapter, IReadOnlyList<ulong> volume) =>
        chapter.Count >= MinPages && volume.Count >= MinVolumePages && chapter.Count < volume.Count
        && Share(chapter, volume) >= MinShare;
}
