using System.Text.RegularExpressions;

namespace Maki.Core.Parsing;

/// <summary>
/// The language a torrent release is written in, read from its title, so a Japanese series is not
/// offered an English digital release and the other way round. Japanese script or a Japanese tag says
/// Japanese; an English tag says English; a title in plain Latin letters with nothing to say otherwise
/// is taken as English, which is what a Western digital release is. Titles that are only Han
/// characters (Chinese or Japanese, no kana) and ones in Hangul say nothing Maki can use here.
/// </summary>
public static partial class ReleaseLanguage
{
    public const string Japanese = "ja";
    public const string English = "en";

    [GeneratedRegex(@"[぀-ヿｦ-ﾟ]")]
    private static partial Regex Kana();

    [GeneratedRegex(@"[一-鿿㐀-䶿]")]
    private static partial Regex Han();

    [GeneratedRegex(@"[가-힯ᄀ-ᇿ]")]
    private static partial Regex Hangul();

    // "[Japanese]", "(JP)", "[ja]", "(Raw)", "日本語": the release says it is Japanese. Only a bracketed tag
    // counts, so a title that merely contains the word ("Japanese Folktales", "Raw Hero") is not read as one.
    [GeneratedRegex(@"[\[(]\s*(?:japanese|jp|jpn|ja|raws?)\s*[\])]|日本語", RegexOptions.IgnoreCase)]
    private static partial Regex JapaneseTag();

    [GeneratedRegex(@"[\[(]\s*(?:english|eng|en)\s*[\])]", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishTag();

    // "(Japanese-English)", "[JP/EN]": a bilingual release, which is neither.
    [GeneratedRegex(@"[\[(][^\[\]()]*\b(?:japanese|jpn|jp|ja)\b[^\[\]()]*\b(?:english|eng|en)\b[^\[\]()]*[\])]|[\[(][^\[\]()]*\b(?:english|eng|en)\b[^\[\]()]*\b(?:japanese|jpn|jp|ja)\b[^\[\]()]*[\])]", RegexOptions.IgnoreCase)]
    private static partial Regex BilingualTag();

    /// <summary>"ja", "en" or "ko", or null when the title does not say.</summary>
    public static string? Detect(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        if (Kana().IsMatch(title))
        {
            return Japanese;
        }

        if (Hangul().IsMatch(title))
        {
            return "ko";
        }

        if (BilingualTag().IsMatch(title))
        {
            return null;
        }

        var japanese = JapaneseTag().IsMatch(title);
        var english = EnglishTag().IsMatch(title);
        if (japanese != english)
        {
            return japanese ? Japanese : English;
        }

        if (japanese)
        {
            return null;
        }

        return Han().IsMatch(title) ? null : English;
    }

    /// <summary>
    /// True when the series wants Japanese or English and the release is clearly the other one. A series
    /// in any other language is not judged: a plain Latin title is read as English, which would refuse
    /// every release for it.
    /// </summary>
    public static bool Mismatches(string? wantedLanguage, string? releaseTitle)
    {
        var wanted = Base(wantedLanguage);
        if (wanted is not (Japanese or English))
        {
            return false;
        }

        var release = Detect(releaseTitle);
        return release is Japanese or English && release != wanted;
    }

    private static string? Base(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Split('-', '_')[0].ToLowerInvariant();
}
