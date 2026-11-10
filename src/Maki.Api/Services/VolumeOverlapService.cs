using System.IO.Compression;
using Maki.Core.Entities;
using Maki.Core.Images;
using Maki.Core.Parsing;
using Maki.Core.Paths;
using Maki.Core.Quality;
using Maki.Core.Reading;
using Maki.Data;
using Microsoft.EntityFrameworkCore;

namespace Maki.Api.Services;

/// <summary>
/// Finds chapter files that sit beside a volume file holding the same pages and recycles them. A volume
/// that arrives from a source or a library scan never displaces the chapters it contains (only a
/// torrent import trashes what it supersedes), and chapter rows from a scraping source carry no volume,
/// so the series ends up with both. The pages decide: when nearly every page of a chapter file is in
/// a volume file of the same series, the chapters it backed are handed to the volume and the chapter
/// file goes to the recycle bin, where it can be restored. Nothing else is touched.
/// </summary>
public class VolumeOverlapService(
    MakiDbContext db,
    ChapterFileDeletion deletion,
    ILogger<VolumeOverlapService> logger)
{
    /// <summary>Pages larger than this (a stitched strip) are not compared.</summary>
    private const int MaxStripRatio = 8;

    public sealed record Covered(string ChapterFile, string VolumeFile, double Share, int Chapters);

    /// <summary>Every series that has at least one volume file and one other file.</summary>
    public async Task<List<int>> CandidateSeriesAsync(CancellationToken ct)
    {
        var rows = await db.ChapterFiles.AsNoTracking()
            .Select(f => new { f.SeriesId, f.RelativePath })
            .ToListAsync(ct);
        return [.. rows.GroupBy(r => r.SeriesId)
            .Where(g => g.Any(r => IsVolume(r.RelativePath)) && g.Any(r => !IsVolume(r.RelativePath)))
            .Select(g => g.Key)];
    }

    /// <summary>
    /// A volume by its name, including a source's "(Volume) title（２）" label. A file like that is never
    /// treated as a chapter, so a second copy of a volume is left alone.
    /// </summary>
    private static bool IsVolume(string relativePath) =>
        ReleaseNameParser.ParseFileName(relativePath) is { IsVolume: true }
        || relativePath.Contains("(Volume)", StringComparison.OrdinalIgnoreCase);

    /// <param name="apply">False only reports what would be recycled.</param>
    public async Task<List<Covered>> ReconcileAsync(int seriesId, bool apply, CancellationToken ct)
    {
        var result = new List<Covered>();
        using var seriesLock = await SeriesLocks.SeriesAsync(seriesId, ct);
        if (await SeriesLocks.InFlight(db.DownloadQueue).AnyAsync(q => q.SeriesId == seriesId, ct))
        {
            return result;
        }

        var series = await db.Series.Include(s => s.RootFolder).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series?.RootFolder is null)
        {
            return result;
        }

        var files = await db.ChapterFiles.Where(f => f.SeriesId == seriesId).ToListAsync(ct);
        var volumes = files.Where(f => IsVolume(f.RelativePath)).ToList();
        var others = files.Where(f => !IsVolume(f.RelativePath)).ToList();
        if (volumes.Count == 0 || others.Count == 0)
        {
            return result;
        }

        var volumePages = new Dictionary<int, List<ulong>>();
        foreach (var other in others)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            if (LibraryPaths.Resolve(series.RootFolder.Path, other.RelativePath) is not { } otherPath || !File.Exists(otherPath))
            {
                continue;
            }

            var pages = HashPages(otherPath, ct);
            if (pages.Count < VolumeOverlap.MinPages)
            {
                continue;
            }

            foreach (var volume in volumes)
            {
                if (!volumePages.TryGetValue(volume.Id, out var hashes))
                {
                    hashes = LibraryPaths.Resolve(series.RootFolder.Path, volume.RelativePath) is { } vp && File.Exists(vp)
                        ? HashPages(vp, ct)
                        : [];
                    volumePages[volume.Id] = hashes;
                }

                var share = VolumeOverlap.Share(pages, hashes);
                if (!VolumeOverlap.IsCovered(pages, hashes))
                {
                    continue;
                }

                var backed = await db.Chapters.Where(c => c.ChapterFileId == other.Id).ToListAsync(ct);
                var volumeNumber = ReleaseNameParser.ParseFileName(volume.RelativePath) is { Volume: { } v, VolumeEnd: null } ? v : (int?)null;
                var volumeLanguage = await db.Chapters.Where(c => c.ChapterFileId == volume.Id).Select(c => c.Language).FirstOrDefaultAsync(ct);
                if (volumeLanguage is not null && backed.Any(c => c.Language != volumeLanguage))
                {
                    continue; // a different language is not the same book
                }

                result.Add(new Covered(other.RelativePath, volume.RelativePath, share, backed.Count));
                if (!apply)
                {
                    break;
                }

                if (deletion.Bin.Refusal(series.RootFolder.Path, [otherPath]) is { } refusal)
                {
                    logger.LogWarning("Not recycling {File}: {Refusal}", other.RelativePath, refusal);
                    break;
                }

                foreach (var chapter in backed)
                {
                    chapter.ChapterFileId = volume.Id;
                    chapter.FileRemovedAt = null;
                    chapter.Volume ??= volumeNumber;
                }

                await db.SaveChangesAsync(ct);
                var outcome = await deletion.DeleteAsync(series, [other], ct, RecycleReason.DeleteFile);
                logger.LogInformation(
                    "Recycled {File}: {Share:P0} of its pages are in {Volume}; {Chapters} chapter(s) now read from the volume ({Deleted} moved, {Failed} failed)",
                    other.RelativePath, share, volume.RelativePath, backed.Count, outcome.Deleted, outcome.Failed);
                break;
            }
        }

        return result;
    }

    private static List<ulong> HashPages(string cbzPath, CancellationToken ct)
    {
        var hashes = new List<ulong>();
        try
        {
            using var archive = ZipFile.OpenRead(cbzPath);
            foreach (var name in CbzReader.PageNames(archive))
            {
                ct.ThrowIfCancellationRequested();
                using var entry = CbzReader.ReadEntry(archive, name);
                if (entry is null)
                {
                    continue;
                }

                var bytes = entry.ToArray();
                if (ImageCodec.TryIdentify(bytes) is not { } header || header.Height > MaxStripRatio * header.Width)
                {
                    continue;
                }

                hashes.Add(PerceptualHash.OfBytes(bytes));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or NetVips.VipsException)
        {
            return [];
        }

        return hashes;
    }
}
