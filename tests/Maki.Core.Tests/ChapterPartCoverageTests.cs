using Maki.Core.Parsing;

namespace Maki.Core.Tests;

public class ChapterPartCoverageTests
{
    private static ChapterPartCoverage.Row R(int id, decimal number, bool file = false, string lang = "ja") =>
        new(id, number, lang, file);

    [Fact]
    public void Parts_of_a_chapter_the_library_has_whole_are_covered()
    {
        var rows = new[] { R(1, 4, file: true), R(2, 4.1m), R(3, 4.2m), R(4, 4.3m), R(5, 4.4m) };

        Assert.Equal([2, 3, 4, 5], ChapterPartCoverage.CoveredParts(rows).Order());
    }

    [Fact]
    public void Parts_are_not_covered_while_the_whole_chapter_has_no_file()
    {
        var rows = new[] { R(1, 4), R(2, 4.1m), R(3, 4.2m) };

        Assert.Empty(ChapterPartCoverage.CoveredParts(rows));
    }

    [Fact]
    public void A_lone_half_chapter_is_an_extra_and_is_left_alone()
    {
        var rows = new[] { R(1, 3, file: true), R(2, 3.5m) };

        Assert.Empty(ChapterPartCoverage.CoveredParts(rows));
    }

    [Fact]
    public void One_part_alone_is_not_a_split()
    {
        var rows = new[] { R(1, 37, file: true), R(2, 37.1m) };

        Assert.Empty(ChapterPartCoverage.CoveredParts(rows));
    }

    [Fact]
    public void A_run_that_does_not_start_at_one_is_not_parts()
    {
        var rows = new[] { R(1, 9, file: true), R(2, 9.5m), R(3, 9.6m) };

        Assert.Empty(ChapterPartCoverage.CoveredParts(rows));
    }

    [Fact]
    public void A_part_that_already_has_a_file_is_not_listed()
    {
        var rows = new[] { R(1, 4, file: true), R(2, 4.1m, file: true), R(3, 4.2m) };

        Assert.Equal([3], ChapterPartCoverage.CoveredParts(rows));
    }

    [Fact]
    public void Languages_are_judged_separately()
    {
        var rows = new[] { R(1, 4, file: true, lang: "en"), R(2, 4.1m, lang: "ja"), R(3, 4.2m, lang: "ja") };

        Assert.Empty(ChapterPartCoverage.CoveredParts(rows));
    }
}
