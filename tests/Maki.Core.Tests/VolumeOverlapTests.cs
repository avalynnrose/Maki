using Maki.Core.Quality;

namespace Maki.Core.Tests;

public class VolumeOverlapTests
{
    private static ulong[] Pages(params int[] seeds) => [.. seeds.Select(s => (ulong)s * 0x9E3779B97F4A7C15UL)];

    [Fact]
    public void A_chapter_whose_pages_are_all_in_the_volume_is_covered()
    {
        var volume = Pages(Enumerable.Range(1, 120).ToArray());
        var chapter = Pages(Enumerable.Range(5, 12).ToArray());

        Assert.True(VolumeOverlap.IsCovered(chapter, volume));
    }

    [Fact]
    public void A_chapter_with_pages_the_volume_lacks_is_not_covered()
    {
        var volume = Pages(Enumerable.Range(1, 120).ToArray());
        var chapter = Pages(Enumerable.Range(115, 12).ToArray());

        Assert.False(VolumeOverlap.IsCovered(chapter, volume));
    }

    [Fact]
    public void A_nearly_identical_page_still_counts()
    {
        var volume = Pages(Enumerable.Range(1, 120).ToArray());
        var chapter = volume.Take(10).Select(h => h ^ 0b101UL).ToArray();

        Assert.True(VolumeOverlap.IsCovered(chapter, volume));
    }

    [Fact]
    public void A_chapter_too_small_to_judge_is_never_covered()
    {
        var volume = Pages(Enumerable.Range(1, 120).ToArray());

        Assert.False(VolumeOverlap.IsCovered(Pages(1, 2, 3), volume));
        Assert.False(VolumeOverlap.IsCovered([], volume));
    }

    [Fact]
    public void A_short_file_named_like_a_volume_is_not_a_volume()
    {
        var notAVolume = Pages(Enumerable.Range(1, 30).ToArray());
        var chapter = Pages(Enumerable.Range(1, 12).ToArray());

        Assert.False(VolumeOverlap.IsCovered(chapter, notAVolume));
    }

    [Fact]
    public void A_file_is_never_covered_by_a_file_with_no_more_pages()
    {
        var volume = Pages(Enumerable.Range(1, 120).ToArray());

        Assert.False(VolumeOverlap.IsCovered(volume, volume));
    }
}
