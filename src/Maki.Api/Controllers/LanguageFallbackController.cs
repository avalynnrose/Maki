using Maki.Api.Auth;
using Maki.Api.Localization;
using Maki.Api.Services;
using Maki.Core.Configuration;
using Maki.Core.Sources;
using Maki.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Maki.Api.Controllers;

/// <summary>The optional fallback language: its two settings, and the per-series badge and revert.</summary>
[ApiController]
[Route("api/v1")]
public class LanguageFallbackController(
    ILocalizer localizer,
    MakiDbContext db,
    IAppSettings settings,
    SourceRegistry sourceRegistry,
    SourceAvailability sourceAvailability,
    LanguageFallbackService fallback) : ControllerBase
{
    /// <param name="Problem">Why the saved settings do not work right now ("rootFolderNotSet", "rootFolderMissing", "rootFolderUnreachable"), or null.</param>
    public record FallbackSettings(string Language, int? RootFolderId, string? Problem = null);

    /// <param name="Reason">"noSource" or "previews".</param>
    public record SeriesFallback(bool Active, string? Reason, string? Language);

    [Authorize(Policy = Policies.Admin)]
    [HttpGet("settings/sources/fallback")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var language = (await settings.GetAsync(SettingKeys.SourceFallbackLanguage, ct))?.Trim() ?? string.Empty;
        int? rootFolderId = int.TryParse(await settings.GetAsync(SettingKeys.SourceFallbackRootFolderId, ct), out var id)
            ? id
            : null;

        string? problem = null;
        if (language.Length > 0)
        {
            var root = rootFolderId is { } rid ? await db.RootFolders.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rid, ct) : null;
            problem = rootFolderId is null ? "rootFolderNotSet"
                : root is null ? "rootFolderMissing"
                : !Directory.Exists(root.Path) ? "rootFolderUnreachable"
                : null;
        }

        return Ok(new FallbackSettings(language, rootFolderId, problem));
    }

    [Authorize(Policy = Policies.Admin)]
    [HttpPut("settings/sources/fallback")]
    public async Task<IActionResult> SetSettings([FromBody] FallbackSettings request, CancellationToken ct)
    {
        var language = request.Language?.Trim() ?? string.Empty;
        if (language.Length == 0)
        {
            await settings.SetAsync(SettingKeys.SourceFallbackLanguage, string.Empty, ct);
            if (request.RootFolderId is not null)
            {
                await settings.SetAsync(SettingKeys.SourceFallbackRootFolderId, request.RootFolderId.ToString(), ct);
            }

            return await GetSettings(ct);
        }

        var disabledSources = await sourceAvailability.DisabledAsync(ct);
        var offered = SourceLanguagePreference.Offered([.. sourceRegistry.All.Where(s =>
            s is not Maki.Sources.Suwayomi.SuwayomiSource || !disabledSources.Contains(s.Name, StringComparer.OrdinalIgnoreCase))]);
        if (offered.FirstOrDefault(code => code.Equals(language, StringComparison.OrdinalIgnoreCase)) is not { } known)
        {
            return this.Fail(localizer, "error.settings.unknownSourceLanguage", new { codes = language });
        }

        var primary = await SourceLanguagePreference.LoadAsync(settings, ct);
        if (primary.EnabledInOrder.Contains(known, StringComparer.OrdinalIgnoreCase))
        {
            return this.Fail(localizer, "error.settings.fallbackLanguageIsPrimary", new { code = known });
        }

        if (request.RootFolderId is not { } rootFolderId)
        {
            return this.Fail(localizer, "error.settings.fallbackRootFolderRequired");
        }

        var root = await db.RootFolders.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rootFolderId, ct);
        if (root is null)
        {
            return this.Fail(localizer, "error.settings.fallbackRootFolderNotFound");
        }

        if (!Directory.Exists(root.Path))
        {
            return this.Fail(localizer, "error.settings.fallbackRootFolderUnreachable", new { path = root.Path });
        }

        await settings.SetAsync(SettingKeys.SourceFallbackLanguage, known, ct);
        await settings.SetAsync(SettingKeys.SourceFallbackRootFolderId, rootFolderId.ToString(), ct);
        return await GetSettings(ct);
    }

    [Authorize]
    [HttpGet("series/{id:int}/language-fallback")]
    public async Task<IActionResult> GetForSeries(int id, CancellationToken ct)
    {
        // Through db.Series, whose query filter hides series in root folders the caller holds no grant for.
        if (!await db.Series.AnyAsync(s => s.Id == id, ct))
        {
            return NotFound();
        }

        var record = await fallback.GetActiveRecordAsync(id, ct);
        return Ok(record is null
            ? new SeriesFallback(false, null, null)
            : new SeriesFallback(true, record.Reason, record.Language));
    }

    /// <summary>Puts a series back on the primary language while it has no files.</summary>
    [Authorize(Policy = Policies.Admin)]
    [HttpPost("series/{id:int}/language-fallback/revert")]
    public async Task<IActionResult> Revert(int id, CancellationToken ct)
    {
        if (!await db.Series.AnyAsync(s => s.Id == id, ct))
        {
            return NotFound();
        }

        var result = await fallback.RevertAsync(id, ct);
        if (result.Outcome == LanguageFallbackOutcome.Applied)
        {
            return await GetForSeries(id, ct);
        }

        var key = result.Why switch
        {
            "notInFallback" => "error.languageFallback.notInFallback",
            "originalRootFolderMissing" => "error.languageFallback.originalRootFolderMissing",
            "hasFiles" => "error.languageFallback.hasFiles",
            "hasReadingState" => "error.languageFallback.hasReadingState",
            "downloadInFlight" => "error.languageFallback.downloadInFlight",
            "manualMapping" => "error.languageFallback.manualMapping",
            "noSource" => "error.languageFallback.noSource",
            _ => "error.languageFallback.failed"
        };
        return this.Fail(localizer, key);
    }
}
