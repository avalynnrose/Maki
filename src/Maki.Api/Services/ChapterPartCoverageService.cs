using Maki.Core.Parsing;
using Maki.Data;
using Microsoft.EntityFrameworkCore;

namespace Maki.Api.Services;

public static class ChapterPartCoverageService
{
    /// <summary>
    /// Stops wanting the parts of a chapter the library already has whole, and a whole chapter the
    /// library already has in parts. A row can still be downloaded by hand: picking it ignores the
    /// Wanted switch.
    /// </summary>
    public static async Task<int> UnwantCoveredPartsAsync(MakiDbContext db, int seriesId, CancellationToken ct)
    {
        var rows = await db.Chapters.AsNoTracking()
            .Where(c => c.SeriesId == seriesId && c.Number != null)
            .Select(c => new { c.Id, c.Number, c.Language, HasFile = c.ChapterFileId != null, c.Wanted })
            .ToListAsync(ct);
        var all = rows.Select(r => new ChapterPartCoverage.Row(r.Id, r.Number, r.Language, r.HasFile)).ToList();
        var covered = ChapterPartCoverage.CoveredParts(all).Concat(ChapterPartCoverage.CoveredWholes(all)).ToHashSet();
        var unwant = rows.Where(r => r.Wanted && covered.Contains(r.Id)).Select(r => r.Id).ToList();
        return unwant.Count == 0
            ? 0
            : await db.Chapters.Where(c => unwant.Contains(c.Id)).ExecuteUpdateAsync(u => u.SetProperty(c => c.Wanted, false), ct);
    }
}
