using Maki.Core.Entities;
using Maki.Core.Parsing;
using Maki.Data;
using Microsoft.EntityFrameworkCore;

namespace Maki.Api.Services;

public static class ChapterPartCoverageService
{
    /// <summary>
    /// A chapter row that belongs to a split (<see cref="ChapterPartCoverage.Splits"/>), with what the
    /// counts need. <paramref name="Covered"/>: the library already has these pages in the other form,
    /// and this row was never had nor read, so it adds nothing to the series.
    /// </summary>
    public sealed record SplitRow(
        int Id, int SeriesId, int Whole, bool IsPart, bool Covered, bool Wanted, bool HasFile, bool Removed, bool Read)
    {
        public bool Kept => Wanted || HasFile || Removed || Read;
    }

    /// <summary>
    /// Stops wanting the parts of a chapter the library already has whole, and a whole chapter it
    /// already has in parts. A row can still be downloaded by hand: picking it ignores the Wanted switch.
    /// </summary>
    public static async Task<int> UnwantCoveredAsync(MakiDbContext db, int seriesId, CancellationToken ct)
    {
        var rows = await db.Chapters.AsNoTracking()
            .Where(c => c.SeriesId == seriesId && c.Number != null)
            .Select(c => new { c.Id, c.Number, c.Language, Had = c.ChapterFileId != null || c.FileRemovedAt != null, c.Wanted })
            .ToListAsync(ct);
        var covered = ChapterPartCoverage.Covered(
            ChapterPartCoverage.Splits([.. rows.Select(r => new ChapterPartCoverage.Row(r.Id, r.Number, r.Language, r.Had))]));
        var unwant = rows.Where(r => r.Wanted && covered.Contains(r.Id)).Select(r => r.Id).ToList();
        return unwant.Count == 0
            ? 0
            : await db.Chapters.Where(c => unwant.Contains(c.Id)).ExecuteUpdateAsync(u => u.SetProperty(c => c.Wanted, false), ct);
    }

    /// <summary>
    /// Every row of <paramref name="chapters"/> that belongs to a split, by series. Only series with a
    /// run of parts load their whole chapters, so a library without split sources costs one query over
    /// its decimal chapters.
    /// </summary>
    public static async Task<Dictionary<int, List<SplitRow>>> SplitRowsAsync(
        IQueryable<Chapter> chapters, IQueryable<ChapterProgress> reads, CancellationToken ct)
    {
        var decimals = (await chapters
                .Where(c => c.Number != null && c.Number != (decimal)(long)c.Number)
                .Select(c => new Loaded(
                    c.Id, c.SeriesId, c.Number!.Value, c.Language, c.Wanted, c.ChapterFileId != null,
                    c.FileRemovedAt != null, reads.Any(p => p.ChapterId == c.Id)))
                .ToListAsync(ct))
            .Where(c => ChapterPartCoverage.MayBePart(c.Number))
            .ToList();
        var candidates = decimals
            .GroupBy(c => c.SeriesId)
            .Where(g => ChapterPartCoverage.Splits([.. g.Select(Row)]).Count > 0)
            .Select(g => g.Key)
            .ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        var wholes = await chapters
            .Where(c => candidates.Contains(c.SeriesId) && c.Number != null && c.Number == (decimal)(long)c.Number)
            .Select(c => new Loaded(
                c.Id, c.SeriesId, c.Number!.Value, c.Language, c.Wanted, c.ChapterFileId != null,
                c.FileRemovedAt != null, reads.Any(p => p.ChapterId == c.Id)))
            .ToListAsync(ct);

        var result = new Dictionary<int, List<SplitRow>>();
        foreach (var series in decimals.Concat(wholes).GroupBy(c => c.SeriesId).Where(g => candidates.Contains(g.Key)))
        {
            var byId = series.ToDictionary(c => c.Id);
            var splits = ChapterPartCoverage.Splits([.. series.Select(Row)]);
            var covered = ChapterPartCoverage.Covered(splits);
            result[series.Key] =
            [
                .. splits.SelectMany(s => s.WholeRows.Select(r => (s.Whole, r, IsPart: false))
                        .Concat(s.PartRows.Select(r => (s.Whole, r, IsPart: true))))
                    .Select(x =>
                    {
                        var c = byId[x.r.Id];
                        return new SplitRow(
                            c.Id, c.SeriesId, x.Whole, x.IsPart, covered.Contains(c.Id) && !c.Read,
                            c.Wanted, c.HasFile, c.Removed, c.Read);
                    }),
            ];
        }

        return result;
    }

    private sealed record Loaded(
        int Id, int SeriesId, decimal Number, string Language, bool Wanted, bool HasFile, bool Removed, bool Read);

    private static ChapterPartCoverage.Row Row(Loaded c) => new(c.Id, c.Number, c.Language, c.HasFile || c.Removed);
}
