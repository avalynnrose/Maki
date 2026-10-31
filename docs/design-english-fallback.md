# English as a fallback language

Design note, written 2026-10-30. Implemented on `feat/english-fallback` the same day, after the owner answered the questions at the end. The section "As built" lists where the code differs from the first draft below, and why.

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
- Built on the deploy branch (`deploy/0.34.1-reconcile`), so a live build is that branch plus this one.
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

All four were answered on 2026-10-30.

1. Preview thresholds: 15 pages, and one to three pages as placeholders. Accepted.
2. Fallback sources: every enabled source that publishes the language. Accepted.
3. A series holding only preview files may fall back once those files are in the recycle bin. Accepted. The code only requires that the series has no files when it is evaluated.
4. "Try the primary language again": admin only. Accepted.

## As built

Differences from the draft above, found while reading the code against it.

- **One save, not a transaction held across the search.** The draft promised one transaction. A SQLite transaction cannot stay open across a minute of network searches. Instead the old mappings and chapters are staged for deletion, `SourceMatchService.AutoMatchAsync` searches (no database write), and its single `SaveChanges` commits the deletions, the move and the new mappings together. If nothing matches, the staged deletions are discarded. The guarantee is the same: nothing is removed unless the fallback language has a source.
- **Sources that publish both languages.** A mapping is unique per `(series, source name)`, and `AutoMatchAsync` skips a source that is already mapped. MangaDex and MangaFire, mapped for Japanese, would never have received an English mapping. `AutoMatchSwap` lets the caller name the sources it is replacing, so they are searched again in the fallback language.
- **`SourceMappingRemovalService` and the move action are not used.** Both take the series lock themselves and the lock is not reentrant, and the removal service refuses to run while other mappings have no chapter snapshot. The swap holds the lock and does its own cleanup, which is safe because it requires a series with no files. The move is only a repoint of the root folder, plus creating the empty folder in the new root and removing the old one if it is empty (never recursive). `/library` and `/english` are separate mounts, so a rename between them would not work, and nothing needs one.
- **"In fallback" is the root folder.** A language filter cannot tell: plain English is stored as no filter, and so is a source without filter support. The reason and the folder the series came from are kept as a setting per series (`sources.fallback.series.{id}`), so there is still no migration and an older image can open the database.
- **Rule A needs the primary searches to have answered.** During an outage every search fails and nothing is mapped, and without a gate every new series would go to English. It reuses the match worker's failure-streak rule: every source answered, or the ones that did not have failed three matches in a row and another source answered. A series is also left alone when a source was linked by hand, when someone has reading progress, or when a download is running.
- **Rule B judges a source only after two measured samples.** Fewer than two means "not judged", which keeps the primary language. The threshold is `SourcePageCounts.FixedMaxPages` (15), with the median strictly under it.
- **The scout trigger is every finished run**, including one started by hand. That is what re-evaluates a series after the owner has binned its preview files. A run that fails or times out does not trigger it.
- **Downloads wait for the measurement.** The preview check runs when the source measurement finishes, but `SmartDownloadJob` and the monitor refresh would otherwise queue downloads first, and one file makes a series ineligible for good. While the fallback is on and a new series is being measured, `LanguageFallbackHold` keeps automatic source downloads away (through `VolumeFirstGate`). It is in memory and lapses after 25 minutes. Manual downloads are never held.
- **A revert is remembered.** After "try the primary language again" the series is marked so the next scout does not send it back.
- **Inbox.** The fallback raises an admin-only `HealthIssue` entry, which is on by default. `SourceMatchFinished` is off by default, so it would have made the switch silent.
- **Base branch.** Built on `deploy/0.34.1-reconcile`, because the scout and the preview rule are fork changes and are not in `main`.

## Testing

- Unit tests: the decision rule, the service (applied, same-name source, no source, each guard, the scout rule, revert), and the scout trigger.
- A test container on separate volumes with `/library` and `/english` as separate bind mounts: settings validation, the new-series path end to end (the series matched English sources, 281 chapters synced, the folder appeared in `/english`, the inbox entry was raised), a refused revert, and a successful revert that moved the folder back. Rule B was not run against real sites.
