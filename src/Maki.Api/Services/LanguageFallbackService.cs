using System.Text.Json;
using Maki.Core.Configuration;
using Maki.Core.Entities;
using Maki.Core.Inbox;
using Maki.Core.Notifications;
using Maki.Core.Quality;
using Maki.Data;
using Microsoft.EntityFrameworkCore;

namespace Maki.Api.Services;

public enum LanguageFallbackOutcome
{
    /// <summary>The series now uses the fallback language (or, for a revert, the primary one again).</summary>
    Applied,

    /// <summary>A guard stopped it, or the rule did not call for it. Nothing changed.</summary>
    Skipped,

    /// <summary>The wanted language has no source for this series. Nothing changed.</summary>
    NoSource,

    /// <summary>The unit of work failed and was rolled back. Nothing changed.</summary>
    Failed
}

/// <param name="Why">A short code for a skip or failure (see <see cref="LanguageFallbackService"/>), for the log and the API.</param>
public sealed record LanguageFallbackResult(
    LanguageFallbackOutcome Outcome, IReadOnlyList<string> Mapped, string? Why = null)
{
    public static LanguageFallbackResult Skip(string why) => new(LanguageFallbackOutcome.Skipped, [], why);
}

/// <summary>What a series remembers about its fallback: why it happened and where it came from.</summary>
public sealed record LanguageFallbackRecord(string Reason, int FromRootFolderId, string Language);

/// <summary>
/// The optional instance-wide fallback language. A series with no files whose primary-language
/// sources are missing or only serve previews gets its mappings swapped for ones in the fallback
/// language and moves to the fallback root folder. See docs/design-english-fallback.md.
/// <para>
/// Nothing is removed until the fallback language has at least one source for the series: the old
/// mappings are staged for deletion, the new ones are searched, and a single save commits both. A
/// search that finds nothing discards the staged deletions. A series is "in fallback" when its root
/// folder is the fallback root folder; a mapping's language filter cannot tell, because a source
/// without a filter stores nothing for either language.
/// </para>
/// <para>
/// Works in its own scope, so the caller's DbContext and the entities it tracks are left alone.
/// Holds the series lock for the whole swap and therefore never calls <see cref="SourceMappingRemovalService"/>
/// or the move action, which take it themselves. Safe because the guards require a series with no files.
/// </para>
/// </summary>
public class LanguageFallbackService(
    IServiceScopeFactory scopes,
    IAppSettings settings,
    SourceAvailability availability,
    Maki.Core.Sources.SourceRegistry registry,
    InboxService inbox,
    ILogger<LanguageFallbackService> logger)
{
    internal const string Reverted = "reverted";

    public sealed record Config(string Language, RootFolder RootFolder);

    /// <summary>The fallback settings, or null while the feature is off or its root folder is missing.</summary>
    public async Task<Config?> LoadConfigAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        return await LoadConfigAsync(scope.ServiceProvider.GetRequiredService<MakiDbContext>(), ct);
    }

    private async Task<Config?> LoadConfigAsync(MakiDbContext db, CancellationToken ct)
    {
        var language = (await settings.GetAsync(SettingKeys.SourceFallbackLanguage, ct))?.Trim();
        if (string.IsNullOrEmpty(language) ||
            !int.TryParse(await settings.GetAsync(SettingKeys.SourceFallbackRootFolderId, ct), out var rootFolderId))
        {
            return null;
        }

        var root = await db.RootFolders.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rootFolderId, ct);
        return root is null || !Directory.Exists(root.Path) ? null : new Config(language, root);
    }

    public async Task<LanguageFallbackRecord?> GetRecordAsync(int seriesId, CancellationToken ct)
    {
        var raw = await settings.GetAsync(SettingKeys.SourceFallbackSeriesPrefix + seriesId, ct);
        if (string.IsNullOrWhiteSpace(raw) || raw == Reverted)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LanguageFallbackRecord>(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Rule A: the primary language has no source for a freshly added series.</summary>
    public Task<LanguageFallbackResult> FallBackAsync(int seriesId, LanguageFallbackReason reason, CancellationToken ct) =>
        SwapAsync(seriesId, reason, ct);

    /// <summary>
    /// Rule B: runs when a scout finishes, whether the automatic one or one somebody started by hand.
    /// Sources that were not measured twice are not judged, so a source that merely failed to answer
    /// keeps the primary language.
    /// </summary>
    public async Task<LanguageFallbackResult> EvaluateAfterScoutAsync(int seriesId, CancellationToken ct)
    {
        if (await LoadConfigAsync(ct) is null)
        {
            return LanguageFallbackResult.Skip("off");
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakiDbContext>();
        var disabled = await availability.DisabledAsync(ct);
        var mappings = (await db.SourceMappings.AsNoTracking()
                .Where(m => m.SeriesId == seriesId && m.Enabled)
                .ToListAsync(ct))
            .Where(m => !disabled.Contains(m.SourceName) && registry.Find(m.SourceName) is not null)
            .ToList();
        if (mappings.Count == 0)
        {
            return LanguageFallbackResult.Skip("noMappings");
        }

        var ids = mappings.Select(m => m.Id).ToList();
        var samples = (await db.SourceQualitySamples.AsNoTracking()
                .Where(s => s.SeriesId == seriesId && s.Origin == SourceQualityOrigin.Probe && ids.Contains(s.SourceMappingId))
                .Select(s => new { s.SourceMappingId, s.PageCount })
                .ToListAsync(ct))
            .ToLookup(s => s.SourceMappingId, s => s.PageCount);

        var reason = LanguageFallbackRule.Evaluate(
            [.. mappings.Select(m => new LanguageFallbackRule.MappingSamples(m.Id, [.. samples[m.Id]]))]);
        return reason is null
            ? LanguageFallbackResult.Skip("usableSource")
            : await SwapAsync(seriesId, reason.Value, ct);
    }

    /// <summary>"Try the primary language again": undoes a fallback while the series still has no files.</summary>
    public async Task<LanguageFallbackResult> RevertAsync(int seriesId, CancellationToken ct)
    {
        var record = await GetRecordAsync(seriesId, ct);
        if (record is null)
        {
            return LanguageFallbackResult.Skip("notInFallback");
        }

        RootFolder? origin;
        using (var scope = scopes.CreateScope())
        {
            origin = await scope.ServiceProvider.GetRequiredService<MakiDbContext>()
                .RootFolders.AsNoTracking().FirstOrDefaultAsync(r => r.Id == record.FromRootFolderId, ct);
        }

        if (origin is null)
        {
            return LanguageFallbackResult.Skip("originalRootFolderMissing");
        }

        var primary = await SourceLanguagePreference.LoadAsync(settings, ct);
        var result = await RunSwapAsync(seriesId, origin, primary, null, ct);
        if (result.Outcome == LanguageFallbackOutcome.Applied)
        {
            // Remembered so the next scout does not walk the series straight back.
            await settings.SetAsync(SettingKeys.SourceFallbackSeriesPrefix + seriesId, Reverted, ct);
        }

        return result;
    }

    private async Task<LanguageFallbackResult> SwapAsync(int seriesId, LanguageFallbackReason reason, CancellationToken ct)
    {
        var config = await LoadConfigAsync(ct);
        if (config is null)
        {
            return LanguageFallbackResult.Skip("off");
        }

        if (await settings.GetAsync(SettingKeys.SourceFallbackSeriesPrefix + seriesId, ct) == Reverted)
        {
            return LanguageFallbackResult.Skip("reverted");
        }

        return await RunSwapAsync(
            seriesId, config.RootFolder, SourceLanguagePreference.ForLanguage(config.Language), (reason, config.Language), ct);
    }

    /// <param name="fallback">Set when swapping to the fallback language; null for a revert.</param>
    private async Task<LanguageFallbackResult> RunSwapAsync(
        int seriesId, RootFolder destination, SourceLanguagePreference languages,
        (LanguageFallbackReason Reason, string Language)? fallback, CancellationToken ct)
    {
        List<string> mapped;
        Series series;
        string oldFolder;
        int oldRootFolderId;
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<MakiDbContext>();
        var matcher = services.GetRequiredService<SourceMatchService>();
        using (await SeriesLocks.SeriesAsync(seriesId, ct))
        {
            var loaded = await db.Series.Include(s => s.RootFolder).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
            if (loaded is null)
            {
                return LanguageFallbackResult.Skip("seriesGone");
            }

            series = loaded;
            oldRootFolderId = series.RootFolderId;
            if (fallback is not null && series.RootFolderId == destination.Id)
            {
                return LanguageFallbackResult.Skip("alreadyInFallback");
            }

            if (await GuardAsync(db, series, ct) is { } blocked)
            {
                return LanguageFallbackResult.Skip(blocked);
            }

            var existing = await db.SourceMappings.Where(m => m.SeriesId == seriesId).ToListAsync(ct);
            if (existing.Any(m => m.Origin == SourceMappingOrigin.Manual))
            {
                return LanguageFallbackResult.Skip("manualMapping");
            }

            var chapters = await db.Chapters.Where(c => c.SeriesId == seriesId).ToListAsync(ct);
            var others = await SeriesCreationService.SeriesFoldersInRootAsync(db, destination.Id, seriesId, ct);
            oldFolder = Path.Combine(series.RootFolder?.Path ?? string.Empty, series.FolderName);

            try
            {
                await CancelQueuedWorkAsync(db, services, seriesId, ct);

                // Staged only. AutoMatchAsync searches first and saves last, so the deletions, the
                // move and the new mappings land in one SaveChanges, and none of it if nothing matches.
                db.Chapters.RemoveRange(chapters);
                db.SourceMappings.RemoveRange(existing);
                series.RootFolderId = destination.Id;
                series.RootFolder = destination;
                series.FolderName = SeriesCreationService.FreeFolderName(
                    series.FolderName, series.MangaBakaId,
                    name => !others.Contains(name) && !SeriesCreationService.HoldsComics(destination.Path, name));

                mapped = await matcher.AutoMatchAsync(
                    series, ct, null,
                    new AutoMatchSwap(languages, existing.Select(m => m.SourceName).ToHashSet(StringComparer.Ordinal)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Switching the language of series {Id} failed; nothing was changed", seriesId);
                db.ChangeTracker.Clear();
                return new LanguageFallbackResult(LanguageFallbackOutcome.Failed, [], "failed");
            }

            if (mapped.Count == 0)
            {
                db.ChangeTracker.Clear();
                logger.LogInformation("No source carries series {Id} in the wanted language; left as it was", seriesId);
                return new LanguageFallbackResult(LanguageFallbackOutcome.NoSource, [], "noSource");
            }

            var key = SettingKeys.SourceFallbackSeriesPrefix + seriesId;
            await settings.SetAsync(key, fallback is { } f
                ? JsonSerializer.Serialize(new LanguageFallbackRecord(
                    f.Reason == LanguageFallbackReason.NoSource ? "noSource" : "previews", oldRootFolderId, f.Language))
                : null, ct);

            TidyFolders(oldFolder, Path.Combine(destination.Path, series.FolderName));
        }

        try
        {
            if (services.GetService<ChapterSyncService>() is { } sync)
            {
                await sync.SyncSeriesAsync(seriesId, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The language switch of series {Id} was saved but its first chapter sync failed", seriesId);
        }

        Announce(series, fallback, mapped);
        return new LanguageFallbackResult(LanguageFallbackOutcome.Applied, mapped);
    }

    /// <summary>Why this series must not be switched, or null. Re-checked under the series lock.</summary>
    private static async Task<string?> GuardAsync(MakiDbContext db, Series series, CancellationToken ct)
    {
        if (await db.ChapterFiles.AnyAsync(f => f.SeriesId == series.Id, ct))
        {
            return "hasFiles";
        }

        if (await db.ChapterProgress.IgnoreQueryFilters().AnyAsync(p => p.SeriesId == series.Id, ct) ||
            await db.ReaderBookmarks.IgnoreQueryFilters().AnyAsync(b => b.SeriesId == series.Id, ct))
        {
            return "hasReadingState";
        }

        return await SeriesLocks.InFlight(db.DownloadQueue).AnyAsync(q => q.SeriesId == series.Id, ct)
            ? "downloadInFlight"
            : null;
    }

    private static async Task CancelQueuedWorkAsync(
        MakiDbContext db, IServiceProvider services, int seriesId, CancellationToken ct)
    {
        var queue = services.GetService<DownloadQueueService>();
        var batches = services.GetService<DownloadBatchNotifier>();
        var active = await db.DownloadQueue
            .Where(q => q.SeriesId == seriesId && q.Status != QueueStatus.Completed && q.Status != QueueStatus.Cancelled)
            .ToListAsync(ct);
        foreach (var item in active)
        {
            queue?.CancelWork(item.Id);
            if (batches is not null)
            {
                await batches.DiscardAsync(item.SeriesId, item.Id);
            }

            db.DownloadQueue.Remove(item);
        }
    }

    /// <summary>
    /// The series has no files, so only its empty folder follows it: the new one is created and the
    /// old one is removed if it is empty. Never recursive, so a folder with anything in it stays.
    /// </summary>
    private void TidyFolders(string oldFolder, string newFolder)
    {
        try
        {
            Directory.CreateDirectory(newFolder);
            if (!string.Equals(Path.GetFullPath(oldFolder), Path.GetFullPath(newFolder), StringComparison.Ordinal) &&
                Directory.Exists(oldFolder) && !Directory.EnumerateFileSystemEntries(oldFolder).Any())
            {
                Directory.Delete(oldFolder, recursive: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not tidy the folders after a language switch ({Old} to {New})", oldFolder, newFolder);
        }
    }

    private void Announce(Series series, (LanguageFallbackReason Reason, string Language)? fallback, List<string> mapped)
    {
        var names = string.Join(", ", mapped.Select(name => registry.Find(name)?.DisplayName ?? name));
        var message = fallback is { } f
            ? new InboxMessage(
                Key: f.Reason == LanguageFallbackReason.NoSource
                    ? "inbox.languageFallback.noSource"
                    : "inbox.languageFallback.previews",
                Params: InboxMessage.Args(new { language = f.Language, sources = names }),
                Level: NotificationLevel.Warning,
                SeriesId: series.Id,
                Url: $"/series/{series.Id}")
            : new InboxMessage(
                Key: "inbox.languageFallback.reverted",
                Params: InboxMessage.Args(new { sources = names }),
                Level: NotificationLevel.Info,
                SeriesId: series.Id,
                Url: $"/series/{series.Id}");
        inbox.Raise(InboxEventType.HealthIssue, message, InboxAudience.Admins);
    }
}
