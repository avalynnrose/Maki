using Maki.Core.Parsing;

namespace Maki.Core.Tests;

public class ChapterPartCoverageTests
{
    private static ChapterPartCoverage.Row R(int id, decimal number, bool had = false, string lang = "ja") =>
        new(id, number, lang, had);

    [Fact]
    public void Parts_of_a_chapter_the_library_has_whole_are_covered()
    {
        var rows = new[] { R(1, 4, had: true), R(2, 4.1m), R(3, 4.2m), R(4, 4.3m), R(5, 4.4m) };

        Assert.Equal([2, 3, 4, 5], ChapterPartCoverage.CoveredParts(rows).Order());
    }

    [Fact]
    public void Parts_are_not_covered_while_the_whole_chapter_is_not_had()
    {
        var rows = new[] { R(1, 4), R(2, 4.1m), R(3, 4.2m) };

        Assert.Empty(ChapterPartCoverage.CoveredParts(rows));
    }

    [Fact]
    public void A_lone_half_chapter_is_an_extra_and_is_left_alone()
    {
        var rows = new[] { R(1, 3, had: true), R(2, 3.5m) };

        Assert.Empty(ChapterPartCoverage.CoveredParts(rows));
    }

    [Fact]
    public void One_part_alone_is_not_a_split()
    {
        var rows = new[] { R(1, 37, had: true), R(2, 37.1m) };

        Assert.Empty(ChapterPartCoverage.CoveredParts(rows));
    }

    [Fact]
    public void A_run_that_does_not_start_at_one_is_not_parts()
    {
        var rows = new[] { R(1, 9, had: true), R(2, 9.5m), R(3, 9.6m) };

        Assert.Empty(ChapterPartCoverage.CoveredParts(rows));
    }

    [Fact]
    public void Parts_of_an_extra_in_the_hundredths_leave_the_extra_and_the_whole_chapter_alone()
    {
        // The parser puts the part in the next decimal place, so 2.5 part 1 is 2.51.
        var rows = new[] { R(1, 2, had: true), R(2, 2.5m), R(3, 2.51m), R(4, 2.52m), R(5, 3), R(6, 3.1m, had: true), R(7, 3.2m) };

        Assert.Empty(ChapterPartCoverage.CoveredParts(rows));
        Assert.Empty(ChapterPartCoverage.CoveredWholes(rows));
        Assert.DoesNotContain(ChapterPartCoverage.Splits(rows), s => s.Whole == 2);
    }

    [Fact]
    public void A_part_that_is_already_had_is_not_listed()
    {
        var rows = new[] { R(1, 4, had: true), R(2, 4.1m, had: true), R(3, 4.2m) };

        Assert.Equal([3], ChapterPartCoverage.CoveredParts(rows));
    }

    [Fact]
    public void Two_sources_listing_the_same_part_are_both_covered()
    {
        var rows = new[] { R(1, 4, had: true), R(2, 4.1m), R(3, 4.1m), R(4, 4.2m) };

        Assert.Equal([2, 3, 4], ChapterPartCoverage.CoveredParts(rows).Order());
    }

    [Fact]
    public void Languages_are_judged_separately()
    {
        var rows = new[] { R(1, 4, had: true, lang: "en"), R(2, 4.1m, lang: "ja"), R(3, 4.2m, lang: "ja") };

        Assert.Empty(ChapterPartCoverage.CoveredParts(rows));
    }

    [Fact]
    public void A_whole_chapter_whose_parts_are_all_had_is_covered()
    {
        var rows = new[] { R(1, 1), R(2, 1.1m, had: true), R(3, 1.2m, had: true), R(4, 1.3m, had: true) };

        Assert.Equal([1], ChapterPartCoverage.CoveredWholes(rows));
    }

    [Fact]
    public void A_part_had_from_either_of_two_sources_counts()
    {
        var rows = new[] { R(1, 1), R(2, 1.1m, had: true), R(3, 1.2m), R(4, 1.2m, had: true) };

        Assert.Equal([1], ChapterPartCoverage.CoveredWholes(rows));
    }

    [Fact]
    public void A_whole_chapter_with_a_part_missing_is_not_covered()
    {
        var rows = new[] { R(1, 1), R(2, 1.1m, had: true), R(3, 1.2m) };

        Assert.Empty(ChapterPartCoverage.CoveredWholes(rows));
    }

    [Fact]
    public void A_whole_chapter_that_is_had_is_not_listed_as_covered()
    {
        var rows = new[] { R(1, 1, had: true), R(2, 1.1m, had: true), R(3, 1.2m, had: true) };

        Assert.Empty(ChapterPartCoverage.CoveredWholes(rows));
    }

    [Fact]
    public void A_lone_extra_does_not_cover_its_whole_chapter()
    {
        var rows = new[] { R(1, 3), R(2, 3.5m, had: true) };

        Assert.Empty(ChapterPartCoverage.CoveredWholes(rows));
    }
}
