# English as a fallback language

Design note, written 2026-10-30. Nothing here is implemented. Written for review before any code.

## The problem

Maki matches a new series against every enabled source that publishes one of the enabled languages. With only Japanese enabled, a series whose Japanese sources are missing or serve nothing useful ends up with no real chapters:

- **No Japanese source carries it.** The series has no source mapping at all.
- **Japanese sources exist but serve previews or placeholders.** The source scout measures them at add time and finds 7 to 12 page samples, or one page files. A download then fills the library with files nobody wants, and the fork's preview rule only reacts after several bad samples.

A user who normally reads Japanese raws would sometimes accept an English release for exactly those series, kept apart from the Japanese library. Enabling English globally does not give that: Maki would map English sources for every new series and, because chapters are tracked per language (`(Number, Language)`), would probably download both versions of every chapter.

## What it should do

An optional, instance-wide fallback, off by default:

1. A new series is added and matched as today, with the normal language preference.
2. Maki decides the Japanese side is unusable (rules below).
3. Only then does it match sources that publish the fallback language, map them with that language as the mapping's `LanguageFilter`, and move the series to a separate root folder.
4. A series with any usable primary-language source never touches the fallback language.

Settings (admin, instance-wide, under Sources):

| Key | Meaning |
|---|---|
| `sources.fallback.language` | Language code, for example `en`. Empty means the feature is off. |
| `sources.fallback.rootfolderid` | Root folder that holds fallback series. Required when the language is set. |

No new column is needed. A series is "in fallback" when its root folder is the fallback root folder and its mappings carry the fallback language. That keeps the migration history unchanged, so an older image can still open the database (a rollback stays possible).

## When the primary language counts as unusable

Evaluated once per series, only while it has **no chapter file at all**, and only if the series is not already in the fallback root folder.

- **Rule A, no primary mapping.** `SourceMatchService.AutoMatchAsync` mapped nothing and `SeedFromCrossRefsAsync` added nothing. Decided immediately in `SourceMatchWorkerHostedService`, before the chapter sync and the scout.
- **Rule B, nothing usable after the scout.** The scout (`SourceScoutService`, three chapters per source) finished and, for every primary-language mapping, the measured samples are previews or placeholders: median page count under the preview threshold (15, the same figure the fork's source reliability rule uses) or all samples one to three pages, or no sample could be measured. At least two measured samples are required before a source counts as unusable, so a source that merely failed to answer is not enough.

A series with one usable source is left alone, even if nine others are previews. Ranking that source is the existing source order's job.

## Hook points

| Where | Change |
|---|---|
| `SourceLanguagePreference` | Add `ForLanguage(code)`, a preference whose only enabled language is the fallback language. `SeedFilter` and `Rank` already work from a preference value, so nothing else changes. |
| `SourceMatchService.AutoMatchAsync` | Take an optional language preference and use it instead of the instance one. Today `SearchSourcesAsync` loads it itself via `SourceLanguagePreference.LoadAsync`. |
| `SourceMatchWorkerHostedService` (after `AutoMatchAsync`, line around 216) | Rule A: when nothing was mapped and the feature is on, call the new service. |
| `SourceScoutService` (end of `ScoutAsync`) | Rule B: when a scout finishes for a series, ask the new service to evaluate it. Needs a completion callback, because the scout runs detached. It only runs when `sources.scoutonmatch` is on; Rule B is skipped when that is off, Rule A still works. |
| New `LanguageFallbackService` | `TryFallbackAsync(seriesId)`: checks the guards, removes the primary-language mappings with cleanup (`SourceMappingRemovalService`, so their chapter rows go too), runs `AutoMatchAsync` with the fallback preference, runs the chapter sync, moves the series, records the reason. |
| Move | The move logic in `SeriesController.Move` is inside the action. Extract it into a service both call. For a series with no files it only repoints `RootFolderId` and creates or moves the empty folder (the folder name is already unique per root, `SeriesCreationService.SeriesFoldersInRootAsync`). |
| Series page | A badge "Fallback language: en" with the reason ("no Japanese source", "Japanese sources only serve previews"), and an admin action "Try the primary language again". |
| Inbox | One inbox entry per fallback, so it never happens silently. |
| Frontend settings | One card next to the language order: on/off, language, root folder. All text through `t`, translated catalogs updated, no raw literals. |

## Guards

- Never when the series already has a chapter file. A series with existing files keeps its current state; a manual "try fallback" for it is out of scope here.
- Never when `sources.fallback.rootfolderid` is unset, missing or unreachable. The feature stays off and says why on the settings card.
- Never twice: a series already in the fallback root folder is skipped, so there is no loop between languages.
- A fallback that finds no source in the fallback language leaves the series untouched (primary mappings are only removed once at least one fallback mapping was created, in one transaction).
- Serialised per series with the existing series lock (`SeriesLocks`), the same lock the move uses.

## Revert

"Try the primary language again" (admin only) removes the fallback mappings with cleanup, re-runs the normal match, and moves the series back, again only while it has no files. It is the escape hatch for a false positive.

## Rollout

- No database migration, so the schema stays at the current head.
- Branch off `dev`; the live deployment runs an older release plus the fork's changes, so the change is cherry-picked onto the deploy branch for a live build, as before.
- Test instance first (separate config volume, separate library volumes), then the owner decides about the live deploy. A database backup comes first, and the previous container stays parked for rollback.
- Needs the fallback root folder created and mounted into the container (container recreation) before the setting can be turned on.

## Tests

- Decision rule: no mapping; all mappings previews; one usable mapping among several previews (no fallback); series with files (no fallback); feature off; fallback root folder missing; two measured samples versus one.
- `AutoMatchAsync` with a language override maps only sources publishing that language and seeds `LanguageFilter` with it.
- Fallback service: mappings removed only after a fallback mapping exists; rollback of the transaction on failure; the move is skipped when a download is in flight; second call is a no-op.
- Revert path.
- Frontend: settings card validation (language set without a root folder is rejected) and the badge.

## Risks

- **False positives.** A source that is briefly down during the scout could look unusable. Mitigation: two measured samples per source, scout must have measured something, and the revert action.
- **Quality of the fallback sources.** English sources differ in quality and rate limits. The feature uses the sources already enabled in Manage sources and their existing ranking; there is no separate list.
- **Naming and ordering.** English files carry the ` [en]` suffix already and live in their own root folder, so nothing mixes with the primary library.
- **Existing series.** Series that already hold files, including preview files, are out of scope until the owner decides how such files should be handled.

## Questions for the owner

1. Is the preview threshold of 15 pages and the "one to three pages are placeholders" rule right for your sources?
2. Should the fallback use every enabled source that publishes the language, or a separate short list?
3. Should a series that holds only preview files (no real chapter) be allowed to fall back, with those files moved to the recycle bin by the owner first?
4. Which accounts may run "Try the primary language again"? Admin only is proposed.
