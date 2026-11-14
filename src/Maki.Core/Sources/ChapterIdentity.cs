using Maki.Core.Entities;

namespace Maki.Core.Sources;

/// <summary>The shared identity rule for merging a source listing into Maki chapters.</summary>
public static class ChapterIdentity
{
    public static bool Matches(Chapter chapter, SourceChapter sourceChapter)
    {
        sourceChapter = Labelled(sourceChapter);
        if (sourceChapter.Number is not null)
        {
            return chapter.Number == sourceChapter.Number &&
                   SourceLanguages.Same(chapter.Language, sourceChapter.Language) &&
                   (chapter.Volume is null || sourceChapter.Volume is null ||
                    chapter.Volume == sourceChapter.Volume);
        }

        return chapter.IsOneShot &&
               SourceLanguages.Same(chapter.Language, sourceChapter.Language) &&
               string.Equals(chapter.Title, sourceChapter.Title, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A number this large is a date or an id, not a chapter: an extension listed a magazine issue as
    /// 20251030, and the series total, which takes the highest whole chapter listed, read about 20 million.
    /// </summary>
    public const decimal MaxChapterNumber = 10000;

    /// <summary>
    /// An unnumbered chapter is told apart by its title, so one with neither number nor title takes
    /// the source's label ("Special", "Extra") as its title. Without it every such chapter of a series
    /// is the same identity and all but the first are dropped. A number of
    /// <see cref="MaxChapterNumber"/> or more is dropped first, so such a chapter is told apart the same way.
    /// </summary>
    public static SourceChapter Labelled(SourceChapter chapter)
    {
        if (chapter.Number >= MaxChapterNumber)
        {
            chapter = chapter with { Number = null };
        }

        return chapter is { Number: null, Title: null } && !string.IsNullOrWhiteSpace(chapter.NumberRaw)
            ? chapter with { Title = chapter.NumberRaw.Trim() }
            : chapter;
    }
}
