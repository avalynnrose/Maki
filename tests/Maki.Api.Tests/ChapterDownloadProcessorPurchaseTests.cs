using Maki.Api.Services;
using Maki.Core.Entities;
using Maki.Core.Sources;

namespace Maki.Api.Tests;

/// <summary>A source that wants a purchase for a chapter is not retried: the download moves on, or stops.</summary>
[Collection(ConfigDirCollection.Name)]
public class ChapterDownloadProcessorPurchaseTests : IDisposable
{
    private readonly UpgradeWorld _world = new();
    private readonly int _chapterId;
    private int _pageCalls;

    public ChapterDownloadProcessorPurchaseTests()
    {
        _world.Seed();
        Directory.CreateDirectory(_world.Library);
        (_chapterId, _) = _world.Chapter(1, withFile: false);
        _world.OfficialListing = id => [new SourceChapter(UpgradeWorld.Official, id, "o1", "1", 1m, null, null, "en", null)];
        _world.OfficialPages = _ =>
        {
            Interlocked.Increment(ref _pageCalls);
            throw new ChapterRequiresPurchaseException("Log in via WebView and purchase this chapter to read.");
        };
    }

    public void Dispose() => _world.Dispose();

    [Fact]
    public async Task With_no_other_source_the_download_fails_at_once_and_is_not_retried()
    {
        using (var db = _world.Db.NewContext())
        {
            db.SourceMappings.Single(m => m.Id == _world.AggMappingId).Enabled = false;
            db.SaveChanges();
        }

        int id;
        using (var db = _world.Db.NewContext())
        {
            var item = new DownloadQueueItem
            {
                SeriesId = _world.SeriesId, ChapterId = _chapterId, Protocol = AcquisitionProtocol.Scraper,
                Status = QueueStatus.Queued, QueuedAt = DateTime.UtcNow
            };
            db.DownloadQueue.Add(item);
            db.SaveChanges();
            id = item.Id;
        }

        await _world.ProcessAsync(id);

        using var check = _world.Db.NewContext();
        var row = check.DownloadQueue.Single(q => q.Id == id);
        Assert.Equal(QueueStatus.Failed, row.Status);
        Assert.Equal(DownloadFailureReason.SourceError, row.ErrorKey);
        Assert.Contains("purchase", row.ErrorMessage);
        Assert.Null(row.NextAttempt);
        Assert.Equal(1, _pageCalls);
    }
}
