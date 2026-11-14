using Maki.Api.Services;
using Maki.Core.Entities;

namespace Maki.Api.Tests;

public sealed class ChapterPartCoverageServiceTests : IDisposable
{
    private const int Owner = 1;
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private int Seed(params (decimal Number, bool File, bool Wanted)[] rows)
    {
        var seriesId = _db.SeedSeries();
        using var seed = _db.NewContext();
        var file = new ChapterFile { SeriesId = seriesId, RelativePath = "S/c.cbz", Size = 1 };
        seed.ChapterFiles.Add(file);
        seed.SaveChanges();
        foreach (var (number, withFile, wanted) in rows)
        {
            seed.Chapters.Add(new Chapter
            {
                SeriesId = seriesId, Number = number, NumberRaw = number.ToString(), Language = "ja", Wanted = wanted,
                ChapterFileId = withFile ? file.Id : null,
            });
        }

        seed.SaveChanges();
        return seriesId;
    }

    [Fact]
    public async Task Covered_rows_stop_being_wanted_and_everything_else_stays()
    {
        var seriesId = Seed(
            (4m, true, true), (4.1m, false, true), (4.2m, false, true), (5m, false, true), (3.5m, false, true),
            (6m, false, true), (6.1m, true, true), (6.2m, true, true),
            (2m, true, true), (2.5m, false, true), (2.51m, false, true), (2.52m, false, true));

        using var db = _db.NewContext();
        var changed = await ChapterPartCoverageService.UnwantCoveredAsync(db, seriesId, CancellationToken.None);

        Assert.Equal(3, changed);
        using var check = _db.NewContext();
        var wanted = check.Chapters.Where(c => c.SeriesId == seriesId && c.Wanted).Select(c => c.Number).ToList()
            .OrderBy(n => n).ToList();
        Assert.Equal([2m, 2.5m, 2.51m, 2.52m, 3.5m, 4m, 5m, 6.1m, 6.2m], wanted);
    }

    [Fact]
    public async Task A_chapter_held_in_parts_is_not_missing_and_not_skipped()
    {
        // Chapter 2 is on disk as 2.1 and 2.2 only; part coverage switched the whole 2 off.
        var seriesId = Seed((1m, true, true), (2m, false, false), (2.1m, true, true), (2.2m, true, true), (3.5m, true, true));

        using var scoped = _db.NewContext(Owner);
        var counts = (await LibraryListRows.ChapterTalliesAsync(
            scoped.Chapters.Where(c => c.SeriesId == seriesId), ReadCounts.Read(scoped), CancellationToken.None))[seriesId];
        var totals = LibraryListRows.ProgressTotals(counts, null);

        Assert.Equal((4, 4, 0), (totals.Total, totals.Have, totals.Skipped));
        Assert.Equal((4, 1, 1, 0), (counts.Known, counts.Specials, counts.SpecialsHad, counts.SwitchedOff));
        Assert.Equal((2.0, 2.0, 3.5), (counts.HighestMain, counts.HighestKeptMain, counts.HighestSpecial));
    }

    [Fact]
    public async Task Main_only_counts_a_chapter_held_in_parts_toward_the_series_length()
    {
        var seriesId = Seed((1m, true, true), (2m, false, false), (2.1m, true, true), (2.2m, true, true));
        using (var db = _db.NewContext())
        {
            foreach (var chapter in db.Chapters.Where(c => c.SeriesId == seriesId && c.ChapterFileId != null).ToList())
            {
                db.ChapterProgress.Add(new ChapterProgress
                {
                    UserId = Owner, SeriesId = seriesId, ChapterId = chapter.Id, Completed = true,
                    StartedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                });
            }

            db.SaveChanges();
        }

        using var scoped = _db.NewContext(Owner);
        var series = scoped.Series.Single(s => s.Id == seriesId);
        series.MonitorNewItems = NewChapterMonitorMode.MainOnly;
        var counts = (await LibraryListRows.ChapterTalliesAsync(
            scoped.Chapters.Where(c => c.SeriesId == seriesId), ReadCounts.Read(scoped), CancellationToken.None))[seriesId];
        var length = LibraryListRows.SeriesLength(series, counts, read: 3, readSpecials: 0, unmonitorSpecials: false);

        Assert.Equal((3, 3, 3), (length.Total, length.Have, length.Read));
    }
}
