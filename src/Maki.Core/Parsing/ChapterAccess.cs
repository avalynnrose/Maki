using System.Text.RegularExpressions;

namespace Maki.Core.Parsing;

public enum ChapterAccess
{
    Free,
    Locked,
    Preview
}

public static partial class ChapterAccessReader
{
    [GeneratedRegex(@"^\s*[(（]\s*[0-9０-９]+\s*[)）]\s*$")]
    private static partial Regex BareNumberInBrackets();

    // "【期間限定 無料お試し版 閲覧期限2026年11月4日】": a free trial of a volume that expires on the date given.
    private static bool IsTrial(string label) =>
        label.Contains("期間限定") || label.Contains("無料お試し") || label.Contains("閲覧期限");

    /// <summary>
    /// What a Mihon extension's chapter label says about reading it: a lock or yen mark in front means
    /// it needs a purchase or login, and "(Preview)" means a few sample pages. A label that is only a
    /// number in brackets, "(1)", is how these sources list a free sample volume, and a time-limited
    /// trial ("期間限定 無料お試し版 閲覧期限...") is a sample that expires. None of them can be
    /// downloaded as the chapter, so listing them only adds chapters that can never be fetched.
    /// </summary>
    public static ChapterAccess Of(string? label)
    {
        if (string.IsNullOrEmpty(label))
        {
            return ChapterAccess.Free;
        }

        if (label.Contains("(Preview)", StringComparison.OrdinalIgnoreCase) || IsTrial(label) || BareNumberInBrackets().IsMatch(label))
        {
            return ChapterAccess.Preview;
        }

        return label.Contains("\U0001F512") || label.Contains("\U0001F4B4") ? ChapterAccess.Locked : ChapterAccess.Free;
    }
}
