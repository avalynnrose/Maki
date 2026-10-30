using Maki.Core.Parsing;

namespace Maki.Core.Tests;

public class ChapterCollectionTests
{
    private static (decimal, bool)[] Files(params decimal[] numbers) => [.. numbers.Select(n => (n, true))];

    [Fact]
    public void A_complete_series_has_nothing_missing()
    {
        var (collected, missing) = ChapterCollection.Collected(Files(1, 2, 3, 4, 5), 5);

        Assert.Equal(5, collected);
        Assert.Empty(missing);
    }

    [Fact]
    public void Missing_chapters_are_listed_in_order()
    {
        var (collected, missing) = ChapterCollection.Collected(Files(1, 2, 4, 5, 7), 7);

        Assert.Equal(5, collected);
        Assert.Equal([3, 6], missing);
    }

    [Fact]
    public void Parts_extras_and_chapters_past_the_total_do_not_count()
    {
        var (collected, missing) = ChapterCollection.Collected(Files(1, 2.5m, 3.1m, 3, 20251030m, 9), 4);

        Assert.Equal(2, collected);
        Assert.Equal([2, 4], missing);
    }

    [Fact]
    public void A_chapter_counts_once_however_many_rows_have_a_file()
    {
        var (collected, _) = ChapterCollection.Collected(Files(4, 4, 4), 4);

        Assert.Equal(1, collected);
    }

    [Fact]
    public void The_missing_list_is_capped()
    {
        var (_, missing) = ChapterCollection.Collected([], 500);

        Assert.Equal(ChapterCollection.MaxMissingListed, missing.Count);
    }

    [Fact]
    public void A_chapter_held_in_parts_counts_as_collected()
    {
        var rows = new (decimal, bool)[] { (1, false), (1.1m, true), (1.2m, true), (1.3m, true), (2, true) };

        var (collected, missing) = ChapterCollection.Collected(rows, 2);

        Assert.Equal(2, collected);
        Assert.Empty(missing);
    }

    [Fact]
    public void A_chapter_with_a_part_still_missing_is_not_collected()
    {
        var rows = new (decimal, bool)[] { (1, false), (1.1m, true), (1.2m, false) };

        var (collected, missing) = ChapterCollection.Collected(rows, 1);

        Assert.Equal(0, collected);
        Assert.Equal([1], missing);
    }
}
