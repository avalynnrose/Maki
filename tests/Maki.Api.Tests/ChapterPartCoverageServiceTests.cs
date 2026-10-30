using Maki.Api.Services;
using Maki.Core.Entities;

namespace Maki.Api.Tests;

public sealed class ChapterPartCoverageServiceTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Covered_parts_stop_being_wanted_and_everything_else_stays()
    {
        int seriesId;
        using (var seed = _db.NewContext())
        {
            var root = new RootFolder { Path = Path.GetTempPath() };
            seed.RootFolders.Add(root);
            seed.SaveChanges();
            var series = new Series { Title = "S", SortTitle = "s", FolderName = "S", RootFolderId = root.Id };
            seed.Series.Add(series);
            seed.SaveChanges();
            seriesId = series.Id;
            var file = new ChapterFile { SeriesId = series.Id, RelativePath = "S/4.cbz", Size = 1 };
            seed.ChapterFiles.Add(file);
            seed.SaveChanges();
            foreach (var (number, withFile) in new[]
                     {
                         (4m, true), (4.1m, false), (4.2m, false), (5m, false), (3.5m, false),
                         (6m, false), (6.1m, true), (6.2m, true)
                     })
            {
                seed.Chapters.Add(new Chapter
                {
                    SeriesId = series.Id, Number = number, NumberRaw = number.ToString(), Language = "ja", Wanted = true,
                    ChapterFileId = withFile ? file.Id : null
                });
            }

            seed.SaveChanges();
        }

        using var db = _db.NewContext();
        var changed = await ChapterPartCoverageService.UnwantCoveredPartsAsync(db, seriesId, CancellationToken.None);

        Assert.Equal(3, changed);
        using var check = _db.NewContext();
        var wanted = check.Chapters.Where(c => c.SeriesId == seriesId && c.Wanted).Select(c => c.Number).ToList()
            .OrderBy(n => n).ToList();
        Assert.Equal([3.5m, 4m, 5m, 6.1m, 6.2m], wanted);
    }
}
