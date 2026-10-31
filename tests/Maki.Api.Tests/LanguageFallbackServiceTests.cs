using System.Text.Json;
using Maki.Api.Services;
using Maki.Core.Configuration;
using Maki.Core.Entities;
using Maki.Core.Quality;
using Maki.Core.Sources;
using Maki.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maki.Api.Tests;

public class LanguageFallbackServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "maki-fallback-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingInbox _inbox = new();
    private readonly FakeAppSettings _settings = new();
    private int _fallbackRootId;
    private string _fallbackPath = string.Empty;

    public LanguageFallbackServiceTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static FakeSource Japanese(string name, string sourceId = "jp1", bool matches = true) => new()
    {
        Name = name,
        SupportedLanguages = ["ja"],
        Capabilities = SourceCapabilities.SupportsLanguageFilter,
        OnSearch = _ => matches ? [new SourceSeriesResult(sourceId, "Hajime no Ippo", $"https://{name}.test/{sourceId}")] : []
    };

    private static FakeSource English(string name, string sourceId = "en1", bool matches = true) => new()
    {
        Name = name,
        SupportedLanguages = ["en"],
        Capabilities = SourceCapabilities.SupportsLanguageFilter,
        OnSearch = _ => matches ? [new SourceSeriesResult(sourceId, "Hajime no Ippo", $"https://{name}.test/{sourceId}")] : []
    };

    /// <summary>A source that publishes both languages, as MangaDex does.</summary>
    private static FakeSource Both(string name) => new()
    {
        Name = name,
        SupportedLanguages = ["ja", "en"],
        Capabilities = SourceCapabilities.SupportsLanguageFilter,
        OnSearch = _ => [new SourceSeriesResult("both1", "Hajime no Ippo", $"https://{name}.test/both1")]
    };

    /// <summary>Turns the fallback on with a real folder as its root, and ja as the only primary language.</summary>
    private void EnableFallback(string language = "en")
    {
        _fallbackPath = Path.Combine(_tempRoot, "english");
        Directory.CreateDirectory(_fallbackPath);
        using var db = _db.NewContext();
        var root = new RootFolder { Path = _fallbackPath };
        db.RootFolders.Add(root);
        db.SaveChanges();
        _fallbackRootId = root.Id;
        _settings.Set(SettingKeys.SourceFallbackLanguage, language)
            .Set(SettingKeys.SourceFallbackRootFolderId, _fallbackRootId.ToString());
    }

    private LanguageFallbackService Service(params ISource[] sources)
    {
        _settings.Set(SettingKeys.SourceLanguageOrder, "ja,en").Set(SettingKeys.SourceLanguagesDisabled, "en")
            .Set(SettingKeys.SourcePriorityOrder, string.Join(",", sources.Select(s => s.Name)));
        var registry = new SourceRegistry(sources);
        var availability = new SourceAvailability(_settings, registry);
        var services = new ServiceCollection();
        services.AddScoped(_ => _db.NewContext());
        services.AddScoped(sp => new SourceMatchService(
            sp.GetRequiredService<MakiDbContext>(), registry, _settings, availability,
            new SourceExternalIdCache(TimeProvider.System), new SourceMatchSearchCache(TimeProvider.System),
            NullLogger<SourceMatchService>.Instance));
        return new LanguageFallbackService(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            _settings, availability, registry, _inbox, NullLogger<LanguageFallbackService>.Instance);
    }

    /// <summary>A series in the "/manga" root with one Japanese mapping per name and two chapters each listed by every mapping.</summary>
    private int SeedJapaneseSeries(params string[] mappedSources)
    {
        var mappings = mappedSources.Select((name, i) => new SourceMapping
        {
            SourceName = name,
            SourceSeriesId = "jp1",
            Url = $"https://{name}.test/jp1",
            LanguageFilter = "ja",
            Priority = i + 1,
            Origin = SourceMappingOrigin.TitleSearch
        }).ToArray();
        var id = _db.SeedSeries("Hajime no Ippo", mappings: mappings);

        using var db = _db.NewContext();
        var stored = db.SourceMappings.Where(m => m.SeriesId == id).ToList();
        foreach (var number in new[] { 1m, 2m })
        {
            var chapter = new Chapter { SeriesId = id, Number = number, NumberRaw = number.ToString(), Language = "ja" };
            db.Chapters.Add(chapter);
            foreach (var mapping in stored)
            {
                chapter.SourceLinks.Add(new ChapterSourceLink { SourceMapping = mapping, SourceChapterId = $"c{number}" });
            }
        }

        db.SaveChanges();
        return id;
    }

    private (List<SourceMapping> Mappings, Series Series, int Chapters) State(int seriesId)
    {
        using var db = _db.NewContext();
        return (db.SourceMappings.AsNoTracking().Where(m => m.SeriesId == seriesId).OrderBy(m => m.Priority).ToList(),
            db.Series.AsNoTracking().First(s => s.Id == seriesId),
            db.Chapters.Count(c => c.SeriesId == seriesId));
    }

    private LanguageFallbackRecord? Record(int seriesId)
    {
        var raw = _settings.GetAsync(SettingKeys.SourceFallbackSeriesPrefix + seriesId).Result;
        return raw is null or "reverted" ? null : JsonSerializer.Deserialize<LanguageFallbackRecord>(raw);
    }

    private void Probe(int seriesId, string source, params int[] pages)
    {
        using var db = _db.NewContext();
        var mappingId = db.SourceMappings.Single(m => m.SeriesId == seriesId && m.SourceName == source).Id;
        while (db.Chapters.Count(c => c.SeriesId == seriesId) < pages.Length)
        {
            var next = db.Chapters.Count(c => c.SeriesId == seriesId) + 1;
            db.Chapters.Add(new Chapter { SeriesId = seriesId, Number = next, NumberRaw = next.ToString(), Language = "ja" });
            db.SaveChanges();
        }

        var chapters = db.Chapters.Where(c => c.SeriesId == seriesId).OrderBy(c => c.Number).Select(c => c.Id).ToList();
        for (var i = 0; i < pages.Length; i++)
        {
            db.SourceQualitySamples.Add(new SourceQualitySample
            {
                SourceMappingId = mappingId,
                SeriesId = seriesId,
                ChapterId = chapters[i],
                Origin = SourceQualityOrigin.Probe,
                PageCount = pages[i],
                MeasuredAtUtc = DateTime.UtcNow
            });
        }

        db.SaveChanges();
    }

    [Fact]
    public async Task A_series_no_primary_source_carries_falls_back_to_the_english_source_and_moves()
    {
        EnableFallback();
        var seriesId = _db.SeedSeries("Hajime no Ippo");
        var originalRoot = State(seriesId).Series.RootFolderId;
        var service = Service(Japanese("raw", matches: false), English("mangaplus"));

        var result = await service.FallBackAsync(seriesId, LanguageFallbackReason.NoSource, default);

        Assert.Equal(LanguageFallbackOutcome.Applied, result.Outcome);
        var (mappings, series, _) = State(seriesId);
        var mapping = Assert.Single(mappings);
        // Plain English is stored as no filter, which is why "in fallback" is the root folder and not the filter.
        Assert.Equal(("mangaplus", null), (mapping.SourceName, mapping.LanguageFilter));
        Assert.Equal(_fallbackRootId, series.RootFolderId);
        Assert.True(Directory.Exists(Path.Combine(_fallbackPath, series.FolderName)));
        Assert.Equal(new LanguageFallbackRecord("noSource", originalRoot, "en"), Record(seriesId));
        var raised = Assert.Single(_inbox.Raised);
        Assert.Equal("inbox.languageFallback.noSource", raised.Message.Key);
    }

    [Fact]
    public async Task A_source_that_publishes_both_languages_is_remapped_not_dropped()
    {
        EnableFallback();
        var seriesId = SeedJapaneseSeries("mangadex");
        var service = Service(Both("mangadex"));

        Probe(seriesId, "mangadex", 4, 6, 5);

        var result = await service.EvaluateAfterScoutAsync(seriesId, default);

        Assert.Equal(LanguageFallbackOutcome.Applied, result.Outcome);
        var (mappings, series, chapters) = State(seriesId);
        var mapping = Assert.Single(mappings);
        Assert.Equal("mangadex", mapping.SourceName);
        Assert.Null(mapping.LanguageFilter);
        Assert.Equal(_fallbackRootId, series.RootFolderId);
        Assert.Equal(0, chapters);
        Assert.Equal("previews", Record(seriesId)!.Reason);
    }

    [Fact]
    public async Task Nothing_changes_when_the_fallback_language_has_no_source_for_the_series()
    {
        EnableFallback();
        var seriesId = SeedJapaneseSeries("raw");
        var before = State(seriesId);
        var service = Service(Japanese("raw"), English("mangaplus", matches: false));

        var result = await service.FallBackAsync(seriesId, LanguageFallbackReason.PreviewsOnly, default);

        Assert.Equal(LanguageFallbackOutcome.NoSource, result.Outcome);
        var after = State(seriesId);
        Assert.Equal(before.Mappings.Select(m => (m.SourceName, m.LanguageFilter)), after.Mappings.Select(m => (m.SourceName, m.LanguageFilter)));
        Assert.Equal(before.Series.RootFolderId, after.Series.RootFolderId);
        Assert.Equal(2, after.Chapters);
        Assert.Null(Record(seriesId));
        Assert.Empty(_inbox.Raised);
    }

    [Fact]
    public async Task A_series_with_a_file_is_never_switched()
    {
        EnableFallback();
        var seriesId = SeedJapaneseSeries("raw");
        using (var db = _db.NewContext())
        {
            db.ChapterFiles.Add(new ChapterFile { SeriesId = seriesId, RelativePath = "Hajime no Ippo/c1.cbz", SourceName = "raw" });
            db.SaveChanges();
        }

        var result = await Service(Japanese("raw"), English("mangaplus")).FallBackAsync(seriesId, LanguageFallbackReason.NoSource, default);

        Assert.Equal(("hasFiles", LanguageFallbackOutcome.Skipped), (result.Why, result.Outcome));
        Assert.Equal("raw", Assert.Single(State(seriesId).Mappings).SourceName);
    }

    [Fact]
    public async Task A_series_somebody_has_read_part_of_is_never_switched()
    {
        EnableFallback();
        var seriesId = SeedJapaneseSeries("raw");
        using (var db = _db.NewContext())
        {
            var user = _db.SeedUser("reader2");
            var chapterId = db.Chapters.First(c => c.SeriesId == seriesId).Id;
            db.ChapterProgress.Add(new ChapterProgress { UserId = user, SeriesId = seriesId, ChapterId = chapterId, PageIndex = 3 });
            db.SaveChanges();
        }

        var result = await Service(Japanese("raw"), English("mangaplus")).FallBackAsync(seriesId, LanguageFallbackReason.NoSource, default);

        Assert.Equal("hasReadingState", result.Why);
    }

    [Fact]
    public async Task A_hand_linked_source_is_left_alone()
    {
        EnableFallback();
        var seriesId = _db.SeedSeries("Hajime no Ippo", mappings: new SourceMapping
        {
            SourceName = "raw", SourceSeriesId = "jp1", Url = "https://raw.test/jp1", Origin = SourceMappingOrigin.Manual
        });

        var result = await Service(Japanese("raw"), English("mangaplus")).FallBackAsync(seriesId, LanguageFallbackReason.PreviewsOnly, default);

        Assert.Equal("manualMapping", result.Why);
    }

    [Fact]
    public async Task It_does_nothing_while_the_feature_is_off_or_its_root_folder_is_unreachable()
    {
        var seriesId = _db.SeedSeries("Hajime no Ippo");
        var service = Service(English("mangaplus"));
        Assert.Equal("off", (await service.FallBackAsync(seriesId, LanguageFallbackReason.NoSource, default)).Why);

        EnableFallback();
        Directory.Delete(_fallbackPath);
        Assert.Equal("off", (await service.FallBackAsync(seriesId, LanguageFallbackReason.NoSource, default)).Why);
        Assert.Empty(State(seriesId).Mappings);
    }

    [Fact]
    public async Task A_second_call_is_a_no_op()
    {
        EnableFallback();
        var seriesId = _db.SeedSeries("Hajime no Ippo");
        var service = Service(English("mangaplus"));
        await service.FallBackAsync(seriesId, LanguageFallbackReason.NoSource, default);

        var again = await service.FallBackAsync(seriesId, LanguageFallbackReason.NoSource, default);

        Assert.Equal("alreadyInFallback", again.Why);
        Assert.Single(State(seriesId).Mappings);
    }

    [Fact]
    public async Task A_finished_scout_keeps_the_primary_language_when_one_source_is_usable()
    {
        EnableFallback();
        var seriesId = SeedJapaneseSeries("raw", "other");
        Probe(seriesId, "raw", 5, 6);
        Probe(seriesId, "other", 28, 31);

        var result = await Service(Japanese("raw"), Japanese("other"), English("mangaplus")).EvaluateAfterScoutAsync(seriesId, default);

        Assert.Equal("usableSource", result.Why);
        Assert.Equal(2, State(seriesId).Mappings.Count);
    }

    [Fact]
    public async Task A_finished_scout_does_not_judge_a_source_it_measured_once()
    {
        EnableFallback();
        var seriesId = SeedJapaneseSeries("raw", "other");
        Probe(seriesId, "raw", 5, 6);
        Probe(seriesId, "other", 3);

        var result = await Service(Japanese("raw"), Japanese("other"), English("mangaplus")).EvaluateAfterScoutAsync(seriesId, default);

        Assert.Equal("usableSource", result.Why);
    }

    [Fact]
    public async Task A_finished_scout_switches_when_every_source_only_serves_previews()
    {
        EnableFallback();
        var seriesId = SeedJapaneseSeries("raw", "other");
        Probe(seriesId, "raw", 5, 6);
        Probe(seriesId, "other", 3, 3);

        var result = await Service(Japanese("raw"), Japanese("other"), English("mangaplus")).EvaluateAfterScoutAsync(seriesId, default);

        Assert.Equal(LanguageFallbackOutcome.Applied, result.Outcome);
        Assert.Equal(["mangaplus"], State(seriesId).Mappings.Select(m => m.SourceName));
    }

    [Fact]
    public async Task Reverting_restores_the_primary_mappings_and_root_folder_and_stops_it_happening_again()
    {
        EnableFallback();
        var seriesId = SeedJapaneseSeries("raw");
        var originalRoot = State(seriesId).Series.RootFolderId;
        Probe(seriesId, "raw", 4, 5);
        var service = Service(Japanese("raw"), English("mangaplus"));
        await service.EvaluateAfterScoutAsync(seriesId, default);
        Assert.Equal(_fallbackRootId, State(seriesId).Series.RootFolderId);

        var reverted = await service.RevertAsync(seriesId, default);

        Assert.Equal(LanguageFallbackOutcome.Applied, reverted.Outcome);
        var (mappings, series, _) = State(seriesId);
        Assert.Equal(("raw", "ja"), (Assert.Single(mappings).SourceName, mappings[0].LanguageFilter));
        Assert.Equal(originalRoot, series.RootFolderId);
        Assert.Null(await service.GetActiveRecordAsync(seriesId, default));

        Probe(seriesId, "raw", 4, 5);
        var again = await service.EvaluateAfterScoutAsync(seriesId, default);
        Assert.Equal("reverted", again.Why);
        Assert.Equal("inbox.languageFallback.reverted", _inbox.Raised.Last().Message.Key);
    }

    [Fact]
    public async Task Reverting_stays_in_the_fallback_when_the_primary_language_still_has_no_source()
    {
        EnableFallback();
        var seriesId = _db.SeedSeries("Hajime no Ippo");
        var service = Service(Japanese("raw", matches: false), English("mangaplus"));
        await service.FallBackAsync(seriesId, LanguageFallbackReason.NoSource, default);

        var reverted = await service.RevertAsync(seriesId, default);

        Assert.Equal(LanguageFallbackOutcome.NoSource, reverted.Outcome);
        Assert.Equal(_fallbackRootId, State(seriesId).Series.RootFolderId);
        Assert.Equal("mangaplus", Assert.Single(State(seriesId).Mappings).SourceName);
        Assert.NotNull(await service.GetActiveRecordAsync(seriesId, default));
    }

    [Fact]
    public async Task A_series_moved_out_of_the_fallback_folder_by_hand_is_no_longer_in_fallback()
    {
        EnableFallback();
        var seriesId = _db.SeedSeries("Hajime no Ippo");
        var originalRoot = State(seriesId).Series.RootFolderId;
        var service = Service(English("mangaplus"));
        await service.FallBackAsync(seriesId, LanguageFallbackReason.NoSource, default);
        Assert.NotNull(await service.GetActiveRecordAsync(seriesId, default));

        using (var db = _db.NewContext())
        {
            db.Series.Single(s => s.Id == seriesId).RootFolderId = originalRoot;
            db.SaveChanges();
        }

        Assert.Null(await service.GetActiveRecordAsync(seriesId, default));
        Assert.Equal("notInFallback", (await service.RevertAsync(seriesId, default)).Why);
    }

    [Fact]
    public async Task Reverting_a_series_that_never_fell_back_does_nothing()
    {
        EnableFallback();
        var seriesId = SeedJapaneseSeries("raw");

        var result = await Service(Japanese("raw")).RevertAsync(seriesId, default);

        Assert.Equal("notInFallback", result.Why);
    }
}
