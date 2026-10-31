using Maki.Core.Configuration;
using Maki.Core.Entities;
using Maki.Core.Indexers;
using Maki.Data;
using Microsoft.EntityFrameworkCore;

namespace Maki.Api.Services;

/// <summary>
/// With "volume first" on, a series gets a look at volume releases before its sources are asked for
/// chapters. Automatic source downloads wait while the search is still due, while a release is
/// waiting for approval, and while one is downloading, so the same chapters are not fetched twice and
/// then replaced. Manual downloads are never held. Each wait is bounded, so a missing indexer or an
/// unanswered proposal cannot hold a series for ever.
/// </summary>
public static class VolumeFirstGate
{
    /// <summary>How long after a series is added its volume search may still be pending.</summary>
    public static readonly TimeSpan SearchWindow = TimeSpan.FromHours(24);

    /// <summary>How long an unanswered proposal holds the series.</summary>
    public static readonly TimeSpan ProposalHold = TimeSpan.FromDays(7);

    public static async Task<bool> HoldsAsync(
        MakiDbContext db, IAppSettings settings, DateTime nowUtc, int seriesId, CancellationToken ct)
    {
        if (LanguageFallbackHold.Holds(seriesId, nowUtc))
        {
            return true;
        }

        if (await settings.GetAsync(SettingKeys.DownloadsVolumeFirst, ct) != "true")
        {
            return false;
        }

        if (await db.TorrentProposals.AnyAsync(p => p.SeriesId == seriesId && p.Status == TorrentProposalStatus.Pending &&
                                                    p.CreatedAtUtc > nowUtc - ProposalHold, ct))
        {
            return true;
        }

        if (await db.DownloadQueue.AnyAsync(q => q.SeriesId == seriesId && q.Protocol == AcquisitionProtocol.Torrent &&
                                                 q.Status != QueueStatus.Completed && q.Status != QueueStatus.Failed &&
                                                 q.Status != QueueStatus.Cancelled, ct))
        {
            return true;
        }

        var series = await db.Series.AsNoTracking()
            .Where(s => s.Id == seriesId)
            .Select(s => new { s.Added, s.LastVolumeSearchUtc })
            .FirstOrDefaultAsync(ct);
        return series is { LastVolumeSearchUtc: null } && series.Added > nowUtc - SearchWindow &&
               (await IndexerSettingsStore.LoadAsync(settings, ct)).IsConfigured;
    }
}
