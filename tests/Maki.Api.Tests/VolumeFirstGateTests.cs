using Maki.Api.Services;
using Maki.Core.Configuration;
using Maki.Core.Entities;

namespace Maki.Api.Tests;

public sealed class VolumeFirstGateTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = new();
    private readonly FakeAppSettings _settings = new();

    public VolumeFirstGateTests()
    {
        _settings.Set(SettingKeys.DownloadsVolumeFirst, "true");
        _settings.Set(SettingKeys.ProwlarrUrl, "http://prowlarr.test");
        _settings.Set(SettingKeys.ProwlarrApiKey, "key");
    }

    public void Dispose() => _db.Dispose();

    private int Series(TimeSpan age, DateTime? searched = null)
    {
        using var db = _db.NewContext();
        var root = db.RootFolders.FirstOrDefault() ?? db.RootFolders.Add(new RootFolder { Path = Path.GetTempPath() }).Entity;
        db.SaveChanges();
        var series = new Series
        {
            Title = "S", SortTitle = "s", FolderName = "S", RootFolderId = root.Id, Added = Now - age,
            LastVolumeSearchUtc = searched
        };
        db.Series.Add(series);
        db.SaveChanges();
        return series.Id;
    }

    private async Task<bool> HoldsAsync(int seriesId)
    {
        using var db = _db.NewContext();
        return await VolumeFirstGate.HoldsAsync(db, _settings, Now, seriesId, CancellationToken.None);
    }

    private void Proposal(int seriesId, TimeSpan age, TorrentProposalStatus status = TorrentProposalStatus.Pending)
    {
        using var db = _db.NewContext();
        db.TorrentProposals.Add(new TorrentProposal
        {
            SeriesId = seriesId, ReleaseGuid = Guid.NewGuid().ToString(), Status = status, CreatedAtUtc = Now - age
        });
        db.SaveChanges();
    }

    private void TorrentItem(int seriesId, QueueStatus status)
    {
        using var db = _db.NewContext();
        db.DownloadQueue.Add(new DownloadQueueItem
        {
            SeriesId = seriesId, Protocol = AcquisitionProtocol.Torrent, Status = status, QueuedAt = Now
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Nothing_is_held_when_the_setting_is_off()
    {
        _settings.Set(SettingKeys.DownloadsVolumeFirst, "false");
        var id = Series(TimeSpan.FromHours(1));
        Proposal(id, TimeSpan.FromHours(1));

        Assert.False(await HoldsAsync(id));
    }

    [Fact]
    public async Task A_new_series_is_held_until_its_volume_search_has_run()
    {
        var id = Series(TimeSpan.FromHours(2));

        Assert.True(await HoldsAsync(id));

        using var db = _db.NewContext();
        db.Series.Single(s => s.Id == id).LastVolumeSearchUtc = Now;
        db.SaveChanges();
        Assert.False(await HoldsAsync(id));
    }

    [Fact]
    public async Task Without_an_indexer_there_is_nothing_to_wait_for()
    {
        _settings.Set(SettingKeys.ProwlarrUrl, "");
        var id = Series(TimeSpan.FromHours(2));

        Assert.False(await HoldsAsync(id));
    }

    [Fact]
    public async Task A_series_added_long_ago_is_not_held_for_a_search_that_never_ran()
    {
        var id = Series(TimeSpan.FromDays(3));

        Assert.False(await HoldsAsync(id));
    }

    [Fact]
    public async Task A_pending_proposal_holds_the_series_for_a_week_and_no_longer()
    {
        var recent = Series(TimeSpan.FromDays(30), searched: Now);
        Proposal(recent, TimeSpan.FromDays(6));
        var old = Series(TimeSpan.FromDays(30), searched: Now);
        Proposal(old, TimeSpan.FromDays(8));

        Assert.True(await HoldsAsync(recent));
        Assert.False(await HoldsAsync(old));
    }

    [Fact]
    public async Task An_answered_proposal_holds_nothing()
    {
        var id = Series(TimeSpan.FromDays(30), searched: Now);
        Proposal(id, TimeSpan.FromHours(1), TorrentProposalStatus.Dismissed);

        Assert.False(await HoldsAsync(id));
    }

    [Theory]
    [InlineData(QueueStatus.Queued, true)]
    [InlineData(QueueStatus.Downloading, true)]
    [InlineData(QueueStatus.Importing, true)]
    [InlineData(QueueStatus.Completed, false)]
    [InlineData(QueueStatus.Failed, false)]
    [InlineData(QueueStatus.Cancelled, false)]
    public async Task A_release_still_downloading_holds_the_series(QueueStatus status, bool holds)
    {
        var id = Series(TimeSpan.FromDays(30), searched: Now);
        TorrentItem(id, status);

        Assert.Equal(holds, await HoldsAsync(id));
    }
}
