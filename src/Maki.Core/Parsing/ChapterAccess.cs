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

    /// <summary>
    /// What a Mihon extension's chapter label says about reading it: a lock or yen mark in front means
    /// it needs a purchase or login, and "(Preview)" means a few sample pages. A label that is only a
    /// number in brackets, "(1)", is how these sources list a free sample volume. None of them can be
    /// downloaded as the chapter, so listing them only adds chapters that can never be fetched.
    /// </summary>
    public static ChapterAccess Of(string? label)
    {
        if (string.IsNullOrEmpty(label))
        {
            return ChapterAccess.Free;
        }

        if (label.Contains("(Preview)", StringComparison.OrdinalIgnoreCase) || BareNumberInBrackets().IsMatch(label))
        {
            return ChapterAccess.Preview;
        }

        return label.Contains("\U0001F512") || label.Contains("\U0001F4B4") ? ChapterAccess.Locked : ChapterAccess.Free;
    }
}
