using Maki.Core.Parsing;

namespace Maki.Core.Tests;

public class ReleaseLanguageTests
{
    [Theory]
    [InlineData("Paradise Kiss - 20th Anniversary Edition (2019) (Digital) (1r0n)", "en")]
    [InlineData("Kimi no Yoru ni Fureru | Touch Within the Abyss (2025) (Digital) (1r0n)", "en")]
    [InlineData("Sakura wa Watashi wo Sukisugiru / Sakura's Dedication (2022-2024) (Digital) (LuCaZ)", "en")]
    [InlineData("The Witch and the Mercenary 001-015 (2024) (Digital) (danke-Empire)", "en")]
    [InlineData("ぼくの地球を守って 第01-21巻 [Boku no Chikyuu wo Mamotte vol 01-21]", "ja")]
    [InlineData("魔女と傭兵 第01巻", "ja")]
    [InlineData("Boku no Chikyuu wo Mamotte vol 01-21 [Raw]", "ja")]
    [InlineData("Boku no Chikyuu wo Mamotte v01 (JP)", "ja")]
    [InlineData("Boku no Chikyuu wo Mamotte v01 [Japanese]", "ja")]
    [InlineData("Some Title v01 (English)", "en")]
    [InlineData("Some Title v01 [ENG]", "en")]
    [InlineData("나 혼자만 레벨업 v01", "ko")]
    public void Reads_the_language_a_release_is_written_in(string title, string expected) =>
        Assert.Equal(expected, ReleaseLanguage.Detect(title));

    [Theory]
    [InlineData("魔女 傭兵 01巻")]
    [InlineData("Some Title v01 (Japanese-English)")]
    [InlineData("[Japanese] Some Title v01 [English]")]
    [InlineData("")]
    [InlineData(null)]
    public void Says_nothing_when_the_title_does_not(string? title) => Assert.Null(ReleaseLanguage.Detect(title));

    [Theory]
    [InlineData("Japanese Folktales v01 (Digital)")]
    [InlineData("Raw Hero v02 (Digital) (Group)")]
    [InlineData("Draw Me Close v01 (Digital)")]
    public void A_word_in_the_series_title_is_not_a_language_tag(string title) =>
        Assert.Equal("en", ReleaseLanguage.Detect(title));

    [Theory]
    [InlineData("ja", "Paradise Kiss (2019) (Digital) (1r0n)", true)]
    [InlineData("ja-JP", "Paradise Kiss (2019) (Digital) (1r0n)", true)]
    [InlineData("ja", "ぼくの地球を守って 第01巻", false)]
    [InlineData("en", "ぼくの地球を守って 第01巻", true)]
    [InlineData("en", "Paradise Kiss (2019) (Digital) (1r0n)", false)]
    [InlineData("fr", "Paradise Kiss (2019) (Digital) (1r0n)", false)]
    [InlineData("ja", "魔女 傭兵 01巻", false)]
    [InlineData("ja", "나 혼자만 레벨업 v01", false)]
    [InlineData(null, "Paradise Kiss (2019) (Digital) (1r0n)", false)]
    public void A_release_in_the_other_of_japanese_and_english_is_a_mismatch(string? wanted, string title, bool expected) =>
        Assert.Equal(expected, ReleaseLanguage.Mismatches(wanted, title));
}
