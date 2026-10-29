using Maki.Core.Parsing;

namespace Maki.Core.Tests;

public class ChapterCollectionTests
{
    [Fact]
    public void A_complete_series_has_nothing_missing()
    {
        var (collected, missing) = ChapterCollection.Collected([1, 2, 3, 4, 5], 5);

        Assert.Equal(5, collected);
        Assert.Empty(missing);
    }

    [Fact]
    public void Missing_chapters_are_listed_in_order()
    {
        var (collected, missing) = ChapterCollection.Collected([1, 2, 4, 5, 7], 7);

        Assert.Equal(5, collected);
        Assert.Equal([3, 6], missing);
    }

    [Fact]
    public void Parts_extras_and_chapters_past_the_total_do_not_count()
    {
        var (collected, missing) = ChapterCollection.Collected([1, 2.5m, 3.1m, 3, 20251030m, 9], 4);

        Assert.Equal(2, collected);
        Assert.Equal([2, 4], missing);
    }

    [Fact]
    public void A_chapter_counts_once_however_many_rows_have_a_file()
    {
        var (collected, _) = ChapterCollection.Collected([4, 4, 4], 4);

        Assert.Equal(1, collected);
    }

    [Fact]
    public void The_missing_list_is_capped()
    {
        var (_, missing) = ChapterCollection.Collected([], 500);

        Assert.Equal(ChapterCollection.MaxMissingListed, missing.Count);
    }
}
