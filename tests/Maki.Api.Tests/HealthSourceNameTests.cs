using Maki.Api.Services;
using Maki.Core.Entities;
using Maki.Core.Sources;
using Maki.Metadata.MangaBaka;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maki.Api.Tests;

public sealed class HealthSourceNameTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly string _tmp = Directory.CreateTempSubdirectory("maki-health-name").FullName;

    public void Dispose()
    {
        _db.Dispose();
        Directory.Delete(_tmp, true);
    }

    [Fact]
    public async Task A_failing_mapping_is_named_by_its_sources_display_name()
    {
        using (var seed = _db.NewContext())
        {
            var root = new RootFolder { Path = _tmp };
            seed.RootFolders.Add(root);
            seed.SaveChanges();
            var series = new Series { Title = "S", SortTitle = "s", FolderName = "S", RootFolderId = root.Id };
            series.SourceMappings.Add(new SourceMapping
            {
                SourceName = "suwayomi-1234", SourceSeriesId = "s", Enabled = true, LastError = "No chapters found"
            });
            seed.Series.Add(series);
            seed.SaveChanges();
        }

        var registry = new SourceRegistry([new FakeSource { Name = "suwayomi-1234", DisplayName = "Rawuwu (ja)" }]);
        var dump = new MangaBakaDumpService(null!,
            new MangaBakaDumpOptions(Path.Combine(_tmp, "mangabaka.db"), _tmp), new FakeAppSettings(),
            new MangaBakaDumpStatus(), NullLogger<MangaBakaDumpService>.Instance);
        var health = new HealthCheckService(_db.NewContext(), new SourceAvailability(new FakeAppSettings(), registry), registry, dump);

        var issue = Assert.Single(await health.GetIssuesAsync(), i => i.MessageKey == "health.issue.mappingFailing");

        Assert.Contains("Rawuwu (ja)", System.Text.Json.JsonSerializer.Serialize(issue.Params));
    }
}
