using Maki.Api.Services;
using Maki.Core.Entities;
using Maki.Core.Sources;

namespace Maki.Api.Tests;

/// <summary>
/// A source whose earlier chapters on a series were all preview length is not downloaded from again for a
/// chapter of that length: the next source is tried, and with none left the row fails and is not retried
/// automatically. A download the user pinned stays as asked.
/// </summary>
[Collection(ConfigDirCollection.Name)]
public class ChapterDownloadProcessorPreviewGuardTests : IDisposable
{
    private readonly UpgradeWorld _world = new();
    private readonly int _chapterId;

    public ChapterDownloadProcessorPreviewGuardTests()
    {
        _world.Seed();
        Directory.CreateDirectory(_world.Library);
        using (var db = _world.Db.NewContext())
        {
            // The source that serves previews is tried first.
            db.SourceMappings.Single(m => m.Id == _world.OfficialMappingId).Priority = 1;
            db.SourceMappings.Single(m => m.Id == _world.AggMappingId).Priority = 2;
            db.SaveChanges();
        }

        (_chapterId, _) = _world.Chapter(1, withFile: false);
        var earlier = Enumerable.Range(2, 3).Select(n => _world.Chapter(n, withFile: false).ChapterId).ToList();
        using (var db = _world.Db.NewContext())
        {
            // Three chapters of one short length from the official source, with a long copy of each elsewhere.
            foreach (var chapter in earlier)
            {
                foreach (var (mapping, pages) in new[] { (_world.OfficialMappingId, 10), (_world.AggMappingId, 40) })
                {
                    db.SourceQualitySamples.Add(new SourceQualitySample
                    {
                        SourceMappingId = mapping, SeriesId = _world.SeriesId, ChapterId = chapter, PageCount = pages,
                        MedianWidth = 800, MedianHeight = 1200, SizeBytes = 1_000_000, ImageFormat = "jpg", MeasuredAtUtc = DateTime.UtcNow
                    });
                }
            }

            db.SaveChanges();
        }

        _world.OfficialListing = id => [new SourceChapter(UpgradeWorld.Official, id, "o1", "1", 1m, null, null, "en", null)];
        _world.OfficialPages = UpgradeWorld.InlinePages(10, 800);
    }

    public void Dispose() => _world.Dispose();

    private int Enqueue(Action<DownloadQueueItem>? configure = null)
    {
        using var db = _world.Db.NewContext();
        var item = new DownloadQueueItem
        {
            SeriesId = _world.SeriesId, ChapterId = _chapterId, Protocol = AcquisitionProtocol.Scraper,
            Status = QueueStatus.Queued, QueuedAt = DateTime.UtcNow
        };
        configure?.Invoke(item);
        db.DownloadQueue.Add(item);
        db.SaveChanges();
        return item.Id;
    }

    private void ServeFromTheAggregator()
    {
        _world.AggListing = id => [new SourceChapter(UpgradeWorld.Agg, id, "a1", "1", 1m, null, null, "en", null)];
        _world.AggPages = UpgradeWorld.InlinePages(40, 800);
    }

    [Fact]
    public async Task A_preview_length_chapter_from_a_source_that_only_served_previews_moves_to_the_next_source()
    {
        ServeFromTheAggregator();
        var id = Enqueue();

        await _world.ProcessAsync(id);

        using var check = _world.Db.NewContext();
        var row = check.DownloadQueue.Single(q => q.Id == id);
        Assert.True(row.Status == QueueStatus.Completed, $"{row.Status} {row.ErrorKey} {row.ErrorMessage}");
        Assert.Equal(_world.AggMappingId, row.SourceMappingId);
    }

    [Fact]
    public async Task With_no_other_source_it_fails_and_the_automatic_retry_leaves_it_alone()
    {
        using (var db = _world.Db.NewContext())
        {
            db.SourceMappings.Single(m => m.Id == _world.AggMappingId).Enabled = false;
            db.SaveChanges();
        }

        var id = Enqueue();

        await _world.ProcessAsync(id);

        using var check = _world.Db.NewContext();
        var row = check.DownloadQueue.Single(q => q.Id == id);
        Assert.Equal(QueueStatus.Failed, row.Status);
        Assert.Equal(DownloadFailureReason.ServesPreviews, row.ErrorKey);
        Assert.Null(row.NextAttempt);
        Assert.Contains(DownloadFailureReason.ServesPreviews, DownloadQueueService.ManualRetryOnlyErrorKeys);
    }

    [Fact]
    public async Task A_download_pinned_to_the_source_is_not_refused()
    {
        ServeFromTheAggregator();
        var id = Enqueue(item => item.PreferredMappingId = _world.OfficialMappingId);

        await _world.ProcessAsync(id);

        using var check = _world.Db.NewContext();
        var row = check.DownloadQueue.Single(q => q.Id == id);
        Assert.NotEqual(DownloadFailureReason.ServesPreviews, row.ErrorKey);
        Assert.Equal(_world.OfficialMappingId, row.SourceMappingId);
    }
}
