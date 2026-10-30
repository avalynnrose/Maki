using Maki.Core.Parsing;

namespace Maki.Core.Tests;

public class ChapterAccessReaderTests
{
    [Theory]
    [InlineData("第1話", ChapterAccess.Free)]
    [InlineData("Ch. 10 - The Fight", ChapterAccess.Free)]
    [InlineData("\U0001F193 第1話", ChapterAccess.Free)]
    [InlineData("\U0001F512 第14話(1)", ChapterAccess.Locked)]
    [InlineData("\U0001F4B4 第9話", ChapterAccess.Locked)]
    [InlineData("\U0001F512 (Preview) 1巻", ChapterAccess.Preview)]
    [InlineData("(Preview) 隣のステラ（１）", ChapterAccess.Preview)]
    [InlineData("【期間限定　無料お試し版　閲覧期限2026年11月4日】隣のステラ（１）", ChapterAccess.Preview)]
    [InlineData("【期間限定 無料お試し版】恋せよまやかし天使ども（１）", ChapterAccess.Preview)]
    [InlineData("閲覧期限2026年11月4日 第1話", ChapterAccess.Preview)]
    [InlineData("第1話 期間のお知らせ", ChapterAccess.Free)]
    [InlineData("(1)", ChapterAccess.Preview)]
    [InlineData("（１）", ChapterAccess.Preview)]
    [InlineData(" （１０） ", ChapterAccess.Preview)]
    [InlineData("隣のステラ（１）", ChapterAccess.Free)]
    [InlineData("第1話(1)", ChapterAccess.Free)]
    [InlineData(null, ChapterAccess.Free)]
    [InlineData("", ChapterAccess.Free)]
    public void Reads_the_access_a_label_announces(string? label, ChapterAccess expected) =>
        Assert.Equal(expected, ChapterAccessReader.Of(label));
}
