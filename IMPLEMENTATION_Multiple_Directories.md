# Implementation Plan: One Game / Entry, Multiple Directories

This document accompanies `CHANGE_Multiple_Directories.md` (requirements & acceptance criteria) and `FIX_Multiple_Directories.md` (design). It covers everything required to ship the feature as a complete, operable, maintainable change — testing, observability, security, performance, failure modes, configuration, documentation, rollout, and the pre-merge gate.

## 0. Definition of Done

The feature is **not** done when the code compiles. It is done when **every** item below is satisfied:

- [ ] All acceptance criteria in `CHANGE_Multiple_Directories.md` are met.
- [ ] Design in `FIX_Multiple_Directories.md` is fully implemented (or deviations are documented and approved).
- [ ] Unit tests pass and meaningfully verify behavior (not just coverage).
- [ ] Integration tests cover cross-component interactions (Domain ↔ Managers ↔ Storage, Agent DTOs ↔ Domain, UI form ↔ Agent API).
- [ ] End-to-end / functional tests cover the user-visible workflow (create multi-child game, sync, restore from backup, edit filters, observe drill-down status).
- [ ] Existing tests continue to pass — no regressions in areas touched by the change.
- [ ] Technical, API, configuration, and user-facing documentation are updated.
- [ ] Logging, metrics, and error reporting make failures diagnosable without a developer reproducing them locally.
- [ ] Security review (path traversal, arbitrary file access, glob injection) is complete.
- [ ] Performance review (large directory walks, fan-out concurrency, zip streaming) is complete.
- [ ] Backwards compatibility, data migration, and archive migration are validated end-to-end against legacy fixtures.
- [ ] Rollout strategy (feature flag, staged deployment, monitoring, rollback procedure) is in place.
- [ ] Pre-merge gate passes (CI green, reviewer sign-off, release notes drafted).

---

## 1. Requirements & Acceptance Criteria

Source of truth: `CHANGE_Multiple_Directories.md` ("Goals", "Proposed Behavior", "Acceptance Criteria"). A short summary cross-referenced here so reviewers don't have to flip files.

### Functional Acceptance

1. A user can add a new game entry with multiple child directories through the GUI.
2. A user can convert / migrate an existing single-directory entry into a multi-child entry without losing data.
3. An entry can be expanded in the GUI to show, edit, add, remove, reorder (v1: add/remove sequence), and enable/disable its child directories.
4. Per-child include and exclude glob filters work for both upload (local → remote) and download (remote → local) operations.
5. Filters use glob syntax compatible with `.gitignore` / FreeFileSync.
6. Bulk operations (check, sync, upload, download) process all enabled children and report per-child results that roll up into a single entry-level status.
7. The feature does not require any change to the Ludusavi Manifest format itself.
8. Existing entries and save data remain functional across the upgrade.

### Non-Functional Acceptance

9. No child-directory limit (configurable limit explicitly out of scope per `FIX_Multiple_Directories.md` §1).
10. Overlapping / nested child paths allowed; processed in declared order with overlap dedup.
11. Filters are symmetric across upload and download.
12. Per-child failures surface as a single entry-level warning with drill-down.
13. Strict aggregation of `GameSyncStatusDto.localFolderPathExists` / `localFolderPathIsUnset`.

### Migration Acceptance

14. Legacy `Dictionary<string, string>` cloud metadata is migrated in-memory on read with no user-visible notice.
15. Cloud metadata is rewritten lazily on first save (create / update / metadata update) — no startup-time rewrite.
16. Legacy HTTP DTOs (`SyncSourceIdLocations`) continue to be honoured for one release, in parallel with `SyncSourceIdLocationsV2`.
17. Legacy game zips (no `<childIndex>/` prefix, no `manifest.json`) download correctly without data loss.

---

## 2. Architecture & Design Fit

Source of truth: `FIX_Multiple_Directories.md` §2–§9. Single-paragraph summary:

- A `GamePathEntry` record carries `Path`, `IncludeFilters`, `ExcludeFilters`, `Enabled`.
- `GameEntity.SyncSourceIdLocations` becomes `Dictionary<string, List<GamePathEntry>>`.
- One combined zip per game; entries prefixed with `<childIndex>/`; `manifest.json` at zip root describes children.
- `ZipHelper` is split: destructive variant (used only on a temp staging directory) vs non-destructive per-entry routing.
- `ILocalDataAccessor.ScanDirectoriesAsync` aggregates per-child scan results.
- New `PathFilter` helper using `Microsoft.Extensions.FileSystemGlobbing.Matcher` evaluates includes/excludes symmetrically on upload and download.
- Cloud metadata and HTTP DTOs both use parallel-field approach (`sl` + `sl2` in cloud JSON; `SyncSourceIdLocations` + `SyncSourceIdLocationsV2` in HTTP DTOs).

Any deviation from this design discovered during implementation must be captured here with rationale and reviewer sign-off.

---

## 3. Dependencies & Affected Systems

### NuGet / Package Dependencies

| Package | Project | Reason | Action |
|---|---|---|---|
| `Microsoft.Extensions.FileSystemGlobbing` | `EmuSync.Domain` | Glob matcher for `PathFilter` | Add explicit `<PackageReference>` (currently transitive only) |
| (None new) | — | — | — |

Verify the package version is compatible with the existing `Microsoft.Extensions.*` references in `EmuSync.Services.Storage.csproj` and the .NET 10 SDK pinned by every project.

### Affected Systems (In Priority Order)

| System | Files | Risk |
|---|---|---|
| `EmuSync.Domain` | `Entities/GameEntity.cs`, `Results/DirectoryScanResult.cs`, `Services/Interfaces/ILocalDataAccessor.cs`, `Services/LocalDataAccessor.cs`, `Helpers/ZipHelper.cs`, `Services/LocalGameSaveBackupService.cs`, `Helpers/PathFilter.cs` (new), `Objects/GamePathEntry.cs` (new) | Medium — core invariants |
| `EmuSync.Services.Managers` | `GameManager.cs`, `GameSyncManager.cs`, `Results/GetSyncTypeResult.cs` | High — sync logic, destructive-vs-non-destructive extraction |
| `EmuSync.Services.Storage` | `Objects/GameMetaData.cs` | High — wire-format change |
| `EmuSync.Agent` | `Dto/Game/*`, `Dto/GameSync/GameSyncStatusDto.cs`, `Mapping/GameMapping.cs`, validators in `IGameDto.cs` | Medium — API contract |
| `EmuSync.UI` | `types/Game.ts`, `views/game/forms/GameForm.tsx`, `views/game/components/DisplayGameSyncStatus.tsx`, `views/game/utils/game-utils.ts` | Medium — list editor UX |
| `EmuSync.Services.LudusaviImporter` | (no change) | None |
| Tests | `EmuSync.Domain.Tests`, `EmuSync.Services.Managers.Tests`, `EmuSync.Agent.Tests`, `EmuSync.Services.Storage.Tests`, UI tests | Medium — fixture updates |

### External Integrations

- **Storage providers** (Dropbox, OneDrive, Google Drive, SharedFolder) — no contract changes; one combined zip per game uses the existing `UpsertZipDataAsync` / `GetZipFileAsync` / `DeleteFileAsync`.
- **Ludusavi Manifest** — no contract changes; suggestions still surface as `List<string>` but Quick Add still picks one for v1 (per `FIX_Multiple_Directories.md` §7.3).

---

## 4. Test Strategy

Tests are organised by **behavior**, not by file count. Every test must answer: "what invariant does this protect, and what would break without it?"

### 4.1 Unit Tests

**`EmuSync.Domain.Tests/Objects/GamePathEntryTests.cs` (new)**
- Default values: `IncludeFilters = []`, `ExcludeFilters = []`, `Enabled = true`.
- Record equality semantics.

**`EmuSync.Domain.Tests/Helpers/PathFilterTests.cs` (new)**
- Empty include list ⇒ everything passes (modulo excludes).
- Non-empty include list ⇒ only paths matching at least one include pass.
- Excludes always win over includes.
- Pattern syntax: `*`, `?`, `**`, character classes, leading `/` (anchored), trailing `/` (directory match).
- Case sensitivity: `Ordinal` on Linux/macOS, `OrdinalIgnoreCase` on Windows (parametrised by `OsPlatform`).
- Empty / null inputs do not throw.
- Malformed patterns throw `ArgumentException` with a message identifying the bad pattern — this is the contract used by the FluentValidation rules.

**`EmuSync.Domain.Tests/Services/LocalDataAccessorTests.cs` (extended)**
- `ScanDirectoriesAsync` aggregates `FileCount`, `DirectoryCount`, `StorageBytes` correctly across multiple paths.
- `LatestFileWriteTimeUtc` / `LatestDirectoryWriteTimeUtc` are the **max** across inputs.
- A missing child path produces a partial result without throwing — the missing path is recorded in `ScannedPaths` with `Exists = false` (or an equivalent aggregate flag if the design changes).
- Order independence: the aggregate is the same regardless of input order.

**`EmuSync.Services.Storage.Tests/Objects/GameMetaDataTests.cs` (new)**
- Round-trip a new-shape entity (`Dictionary<string, List<GamePathEntry>>`) — both directions.
- Round-trip a legacy-shape payload (`Dictionary<string, string>`) — read path migrates in memory; write path produces new shape.
- Mixed payload: `sl` legacy + `sl2` new + `v: 2` — read prefers `sl2`; write populates both.
- Missing `v` field on legacy read is treated as `v: 1`; missing `v` field on new payload is tolerated (assume `v: 2`).

**`EmuSync.Agent.Tests/Dto/Game/IGameDtoTests.cs` (new / extended)**
- Validator rejects empty child path.
- Validator rejects malformed glob pattern in any filter list.
- Validator accepts the legacy single-path shape (still valid for one release).
- Validator accepts empty children list (renders as `UnsetDirectory`, not an error).

**`EmuSync.Agent.Tests/Mapping/GameMappingTests.cs` (extended)**
- `IGameDto.ToEntity()` — `V2` field used when present; legacy field migrated when `V2` is absent.
- `GameEntity.ToDto()` / `.ToSummaryDto()` — write **both** fields.
- `QuickAddGameDto.ToUpsert()` — single-path mapping unchanged for v1.

**UI unit tests**
- `replacePathDelims` over a `GamePathEntry[]` correctly normalises each child's `Path`.
- Filter-text parsing: blank lines ignored, comments (`#`-prefixed) ignored per `.gitignore` convention, one glob per line otherwise.

### 4.2 Integration Tests

These exercise cross-component behavior with real (or in-process fake) collaborators.

**`EmuSync.Services.Managers.Tests/GameSyncManagerTests.cs` (new)**

Uses a fake `IStorageProvider` that records calls, an in-process `ILocalDataAccessor`, and an in-memory `ILocalGameSaveBackupService`.

| Scenario | What it proves |
|---|---|
| **Legacy entry migration on read** | A legacy `Dictionary<string, string>` becomes a single-child multi-child entry after `GameManager.GetAsync`. |
| **Multi-child upload zip layout** | The zip written to the fake storage has entries prefixed with `0/`, `1/`, … and a root-level `manifest.json`. |
| **Filter evaluation on upload** | A file matched by an exclude filter is not present in the zip. |
| **Overlap dedup on upload** | When two children have overlapping relative paths, only the first child's file is included. |
| **Multi-child download routing** | A zip with `0/foo`, `1/bar` extracts `foo` to child 0's directory and `bar` to child 1's directory. |
| **Filter evaluation on download** | An entry whose path matches an exclude filter is not written to disk. |
| **Staging safety** | No call to `Directory.Delete(recursive: true)` is made against any child directory before extract — only against the temp staging dir. |
| **Per-child failure isolation** | One child throws during enumeration; other children's files still appear in the zip; the resulting status is `RequiresUpload` with the failing child's error captured in `ChildSyncStatusDto.Errors`. |
| **Combined backup zip** | `LocalGameSaveBackupService.CreateBackupAsync` produces a single combined-zip whose layout matches the upload layout. |
| **Restore from combined backup** | A combined backup restores into the supplied per-child target paths and the resulting state matches the pre-backup state. |
| **Strict aggregation** | `GameSyncStatusDto` with 2 of 3 children existing yields `localFolderPathExists = false` and `localFolderPathIsUnset = false`. |
| **Empty children list** | Returns `GameSyncStatus.UnsetDirectory` without scanning. |
| **All children disabled** | Same as empty children. |

**Storage integration (`EmuSync.Services.Storage.Tests/Objects/GameMetaDataTests.cs`)** already listed above is effectively a DTO↔wire integration test.

**UI form integration (if framework permits)**
- `GameForm.tsx` mounted with a multi-child `Game`, modified by adding/removing a child and editing filters, submits an `UpdateGame` DTO that round-trips through `GameMapping` to the same entity shape.

### 4.3 End-to-End / Functional Tests

These are scoped at the user-visible workflow. The Electron UI is hard to exercise headlessly; if the project already has Playwright / Spectron / similar, use it. If not, document a **manual E2E checklist** instead.

**Automated (if E2E framework exists)**
- Create a game with two children pointing at two temp directories on the local disk.
- Trigger sync against a fake / dev storage provider.
- Verify each child's file lands in the remote and that on a fresh download each file lands in the right local child.
- Disable a child, re-sync, verify the disabled child's files are not uploaded.
- Edit a filter, re-sync, verify excluded files are excluded on both directions.
- Restore from a backup, verify per-child state is recovered.

**Manual E2E checklist** (run on Windows + Linux + macOS before release)
- [ ] Create a new game with two children.
- [ ] Edit an existing single-child game → add a second child.
- [ ] Upgrade an existing pre-feature user (with cloud data) to the new build, perform one sync, observe that the cloud `game-list.json` carries the new `sl2` field.
- [ ] Force upload, force download, restore from backup — each path with at least two children, at least one disabled, and at least one filter set.
- [ ] Verify the drill-down in `DisplayGameSyncStatus` reflects per-child truth.
- [ ] Trigger a deliberate per-child failure (e.g. delete one child directory mid-sync) and observe the entry-level warning plus drill-down.

### 4.4 Regression Coverage

For each area touched by the change, identify the existing tests that protect it and run them as part of this PR. Specifically:

- `EmuSync.Services.Managers.Tests/Results/` — ensure `GetSyncType` results for legacy single-path games remain bit-identical to today's behaviour.
- `EmuSync.Agent.Tests/Dto/Game/` — ensure validators continue to accept the legacy single-path DTO.
- `EmuSync.Services.Storage.Tests/` — ensure provider-specific tests still pass (no contract change).

### 4.5 Test Conventions

Per `FIX_Multiple_Directories.md` §14, the existing test projects need a quick review pass before tests are written, so additions match house style:

- Test framework (xUnit vs NUnit)
- Mocking library (Moq vs NSubstitute vs hand-rolled fakes)
- Assertion style (FluentAssertions vs raw asserts)
- AAA structure
- Naming convention

Capture the conventions in a short note at the top of each new test file so reviewers don't need to ask.

---

## 5. Observability — Logging, Metrics, Error Reporting

Failures must be diagnosable from logs alone. No "works on my machine" required.

### 5.1 Logging

Structured logging via Serilog (already wired in `EmuSync.Agent`). Required log events (all with `gameId`, `syncSourceId`, `childIndex` scope properties where applicable):

| Level | Event | Properties |
|---|---|---|
| `Information` | "Game sync started" | `GameId`, `SyncSourceId`, `ChildCount`, `EnabledChildCount` |
| `Information` | "Game sync completed" | `GameId`, `SyncSourceId`, `SyncStatus`, `DurationMs`, `ChildFailures` |
| `Information` | "Legacy entry migrated" | `GameId`, `SyncSourceId`, `LegacyPathCount` |
| `Information` | "Cloud metadata shape upgraded" | `GameId`, `FromVersion`, `ToVersion` |
| `Warning` | "Per-child failure" | `GameId`, `SyncSourceId`, `ChildIndex`, `Stage` (scan/upload/download/extract), `Exception` |
| `Warning` | "Overlapping child paths detected" | `GameId`, `SyncSourceId`, `ChildIndexA`, `ChildIndexB`, `RelativePath` |
| `Warning` | "Filter excluded file" | `GameId`, `ChildIndex`, `RelativePath`, `Pattern` |
| `Warning` | "Legacy game zip lacks manifest" | `GameId`, `ZipPath`, `Action` ("treated as single child") |
| `Warning` | "Child path no longer exists on disk" | `GameId`, `ChildIndex`, `Path`, `Action` ("skipped, files preserved in zip") |
| `Error` | "Sync aborted" | `GameId`, `SyncSourceId`, `Stage`, `Exception` |
| `Debug` | "Filter evaluated" | `GameId`, `ChildIndex`, `RelativePath`, `Result` |

Use `Logger.BeginScope` or structured `LogInformation("... {Property}", value)` so the values are queryable in Serilog sinks, not interpolated into the message.

### 5.2 Metrics

If/when the project adopts a metrics pipeline, surface:

- `emusync.game.sync.duration_ms` (histogram, tagged by `direction=upload|download`, `outcome=success|partial|failure`).
- `emusync.game.children.count` (histogram, tagged by sync source).
- `emusync.game.sync.bytes_total` (counter, tagged by direction).
- `emusync.game.filter.excluded.count` (counter).
- `emusync.game.zip.entries` (histogram).

For v1, capture the same data via structured logs and document the counters so a future metrics pass can lift them. No new dependency introduced in v1.

### 5.3 Error Reporting

Errors bubble through `HttpResponseExceptionFilter` (`EmuSync.Agent/Middleware/HttpResponseExceptionFilter.cs`) — already produces `ErrorResponseDto`. Per-child errors are surfaced as **entry-level** errors with the drill-down carrying the per-child detail (see `FIX_Multiple_Directories.md` §5.7).

For the UI:
- The drill-down panel in `DisplayGameSyncStatus` must show `ChildSyncErrorDto.Stage` and `.Message` for each failing child.
- Toast / alert severity matches the entry-level status: red for partial-failure, yellow for `RequiresUpload` / `RequiresDownload`, green for `InSync`.

---

## 6. Security

### 6.1 Path Traversal

The user-supplied child path is **not** constrained today beyond "exists". A malicious or careless user could point a child at `C:\` or `/` and trigger a recursive walk of the entire filesystem.

**Mitigations:**
- `PathFilter.Passes` and the file enumeration must run with `Path.GetFullPath` resolved and normalised to reject relative-path escapes.
- Reject (or warn) any child path whose normalised form equals a system root, the user's home root, or the `.emusync-data` directory itself.
- The temp staging directory must be unique per sync (already true via `IdHelper.Create()`) and live under `DomainConstants.LocalDataGameTempZipsFolder` — never user-controlled.

### 6.2 Glob Injection / ReDoS

A malicious glob pattern (e.g. `**(a{1,1000}){1,1000}`) could trigger pathological backtracking in the matcher.

**Mitigations:**
- Use `Microsoft.Extensions.FileSystemGlobbing.Matcher`, which is non-backtracking by design (it converts patterns to a DFA).
- Add a hard timeout per filter evaluation (e.g. 1 second) using `CancellationToken`. On timeout, log a warning and treat the file as excluded.
- Validate pattern syntax in `GameDtoValidator` so a pathological pattern is rejected at the API boundary, not at sync time.

### 6.3 Arbitrary File Access via Zip Entry Names

A corrupted or maliciously crafted zip could contain entries with `..` segments or absolute paths.

**Mitigations:**
- After extracting each entry in the non-destructive path, validate the resolved path is still under the intended child directory:
  ```csharp
  var resolved = Path.GetFullPath(Path.Combine(childPath, remainingRelativePath));
  if (!resolved.StartsWith(Path.GetFullPath(childPath) + Path.DirectorySeparatorChar))
      throw new InvalidDataException("Entry escapes child directory");
  ```
- Reject zip entries whose `FullName` is absolute or contains `..` segments.

### 6.4 Auth / Token Leakage

No new auth surface. Storage tokens are unchanged. Existing secrets-handling rules in `EmuSync.Services.Storage` apply.

### 6.5 Sensitive Content in Logs

Per-child paths and filter patterns can be sensitive (a user might consider their save locations private). Do not log full paths at `Information` level — use `Debug`. Aggregate counts are fine at `Information`.

---

## 7. Performance

### 7.1 Directory Walk Fan-Out

`ScanDirectoriesAsync` runs one `SearchDirectory` per child. For a game with N children, the wall-clock cost is roughly N × (single-child cost). With `Directory.EnumerateFiles` this is I/O-bound, not CPU-bound.

**Mitigations:**
- Run per-child scans in parallel using `Parallel.ForEachAsync` with a `MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, children.Count)`. The existing `LudusaviManifestScanner` pattern (`clamp(ProcessorCount * 3, 1, 10)`) is a reasonable upper bound — reuse it.
- Aggregate the results in a thread-safe manner (`ConcurrentBag` for the per-path scan results, then merge).

### 7.2 Zip Creation

`ZipHelper.CreateZipFromFolder` is single-threaded today. With multiple children, the file count multiplies.

**Mitigations:**
- Profile with realistic game save sizes (5–500 MB, 100–50,000 files). If single-threaded zip creation becomes a bottleneck, consider parallel entry creation via `ZipArchive.CreateEntry` + manual `Parallel.ForEach` over file reads (the central `archive` writer is not thread-safe; serialize entry creation, parallelize reads).
- Stream directly into the storage provider's `UpsertZipDataAsync` — never write the whole zip to a temp file first.

### 7.3 Filter Evaluation

For a game with 50,000 files and 10 filters, filter evaluation is 500,000 matcher calls.

**Mitigations:**
- Cache the compiled `Matcher` per child per sync (filters are unlikely to change mid-sync).
- Short-circuit on first match for include lists; short-circuit on first match for exclude lists.

### 7.4 Status Aggregation

`GameSyncService.TryDetectGameSyncStatusesAsync` runs `GetSyncType` on every game on every `GET /game`. With multi-child games, this is more expensive.

**Mitigations:**
- The existing `ApiCache` (1-minute TTL) already absorbs the worst case.
- Consider adding a per-game `LastCheckedAtUtc` and skipping games checked within the last N seconds. Document this as a future optimisation, not a v1 requirement.

### 7.5 UI Rendering

The Game List screen renders a row per game; the Game Form renders a row per device, then per child.

**Mitigations:**
- The list screen is unaffected (no directory column).
- The form uses `useFieldArray` which virtualises correctly. No additional work needed for v1.

---

## 8. Failure Modes

A catalog of what can go wrong, how the system behaves, and how the user is informed.

| Failure | Detection | Behaviour | User-visible |
|---|---|---|---|
| Child path does not exist on disk | `ScanDirectoriesAsync` per-path | Treated as missing; `localFolderPathExists` aggregate becomes false | Drill-down shows red dot for that child |
| All child paths missing | All scans report not-exists | `localFolderPathIsUnset = true` | `ErrorAlert` in `DisplayGameSyncStatus` (existing behavior) |
| Filter pattern is malformed | Validator on save, matcher on sync | Save rejected at API boundary; sync-time matcher throws are caught and logged | API returns 400; sync logs warning and treats as excluded |
| Child path overlaps another child's path | Detected at sync time (not validated) | First child "owns" the overlapping files; later children skip | Logged at `Warning` ("Overlapping child paths detected"); no UI change |
| Filter excludes everything | All files skipped | Empty zip uploaded | Logged at `Warning`; user sees `InSync` after the empty upload (existing behavior for empty folders) |
| One child throws during enumeration | Per-child try/catch in sync loop | Other children proceed; entry status becomes partial | Entry-level warning + drill-down with the failing child's error |
| Combined zip upload fails (network) | Storage provider exception | Whole sync aborts; no metadata update | Existing entry-level error path |
| Per-entry extract fails (corrupt zip, missing `manifest.json`) | Validation in `ZipHelper` non-destructive path | Treated as legacy layout (writes everything under child 0); logged at `Warning` | Sync completes with a warning |
| Legacy cloud metadata (`sl` only) | `GameMetaData.ToEntity` read path | Migrated in-memory to `sl2`; next save persists new shape | Silent (per decision) |
| Mixed-version client (old UI, new Agent) | DTO deserialisation | Old UI ignores `V2` field, uses legacy; new Agent serves both | No breakage during rollout window |
| Mixed-version client (new UI, old Agent) | DTO deserialisation | New UI reads `V2`, falls back to legacy | No breakage during rollout window |
| User disables all children | `GetSyncType` returns empty children list | Returns `UnsetDirectory` | `ErrorAlert` in `DisplayGameSyncStatus` |

---

## 9. Backwards Compatibility & Data Migration

Cross-references `FIX_Multiple_Directories.md` §10. This section restates the **operational** requirements.

### 9.1 On-Load

- All reads of `game-list.json` go through a `MigrateGame` helper. Failure to migrate (e.g. corrupt entry) is logged at `Error` and the entry is skipped — never crashes the whole list load.

### 9.2 On-Write

- Lazy on first save. A failure to write the new shape leaves the legacy shape on disk — the entry is re-tried on the next save.

### 9.3 Cut-Over

- After the rollout window (length TBD with maintainer sign-off), remove the legacy `sl` field from the cloud write path and the legacy `SyncSourceIdLocations` field from the HTTP DTO write path.
- Remove the legacy read paths only when telemetry shows near-zero legacy clients.
- This is a separate PR with its own review.

### 9.4 Archive

- Pre-feature zips are read-only at the legacy path for at least one major release. No re-upload is forced.

---

## 10. Configuration References

No new application configuration is required in v1. The following constants exist already and are reused; verify they are still appropriate:

- `DomainConstants.LocalDataFolder = ".emusync-data"`
- `DomainConstants.LocalDataGameTempZipsFolder = "temp-zips"`
- `DomainConstants.LocalDataGameBackupFolder = "game-backups"`
- `DomainConstants.LocalDataGameBackupFileNameFormat = "backup_{0}.zip"`
- `StorageConstants.FileName_GameZip = "game-{0}.zip"`

If new tunables are needed during implementation (e.g. a filter-evaluation timeout), add them to `appsettings.json` with a default value, document them in this file, and reference them by section in the README / configuration docs.

---

## 11. Documentation

### 11.1 Technical Documentation

- **README.md** — no change required; multi-directory is an additive feature.
- **CHANGELOG.md** — add an entry under the upcoming release describing:
  - New capability: multiple directories per game entry.
  - Per-child include/exclude filters.
  - Migration: legacy entries automatically upgraded on read.
- **NEWS.md** — short user-visible announcement mirroring CHANGELOG entry.
- **Code comments** — keep sparse per repo convention; meaningful XML doc comments on public surfaces of `GamePathEntry`, `PathFilter`, `GamePathEntryDto`, `ChildSyncStatusDto`.

### 11.2 API / Interface Documentation

- `EmuSync.Agent` currently has no OpenAPI / Swagger surface. If the project adopts one later, document `SyncSourceIdLocationsV2`, `GamePathEntryDto`, `ChildSyncStatusDto`, and the parallel-field strategy.
- For this PR, document the DTO changes in a comment block at the top of `IGameDto.cs` explaining the parallel-field rollout window.

### 11.3 User-Facing Documentation

- **README / FAQ section** (if the project has one) — short how-to: "To back up multiple folders for one game, edit the game and add additional directories under Sync locations."
- **In-app help** — a small "?" link next to the include/exclude textareas showing example patterns (`*.sav`, `mods/**`, `options.txt`).
- **Release notes** — see §13.

---

## 12. Rollout Strategy

The change is medium-risk: it touches wire format (cloud metadata), archive format (zip layout), HTTP DTO shape, and core sync logic. A staged rollout with monitoring and rollback is appropriate.

### 12.1 Feature Flag

Introduce a runtime flag (in `appsettings.json` and overridable via env var) so the new code path can be enabled per-installation:

```json
{
  "Features": {
    "MultiDirectoryGames": {
      "Enabled": true,
      "WriteNewMetadataShape": false,
      "WriteNewDtoShape": false
    }
  }
}
```

Flags in v1:

| Flag | Default | Purpose |
|---|---|---|
| `Enabled` | `true` | Master switch; turning off reverts to legacy single-path behaviour (read legacy, write legacy, single-zip layout). |
| `WriteNewMetadataShape` | `false` | When `false`, `GameMetaData.FromGame` writes only the legacy `sl` field, even with `Enabled = true`. Allows staged rollout where reads accept the new shape but writes haven't switched yet. |
| `WriteNewDtoShape` | `false` | Same idea for HTTP DTOs. |

### 12.2 Staged Deployment

1. **Internal dogfood** — maintainers + early testers. `Enabled = true`, `WriteNewMetadataShape = false`, `WriteNewDtoShape = false`. New reads work; nothing new is written; legacy clients unaffected.
2. **Beta channel** — opt-in users. Same flags as dogfood.
3. **Gradual production rollout** — flip `WriteNewMetadataShape = true` and `WriteNewDtoShape = true` per cohort, monitored for 1 week each.
4. **Full rollout** — both write flags on by default.
5. **Cut-over** (separate PR, ≥1 release later) — remove legacy read paths; flip default `Enabled = false` and remove the flag in a later release.

### 12.3 Monitoring

During the rollout window, dashboards / alerts on:

- `emusync.game.sync.duration_ms` p95 — should not regress meaningfully for single-child games.
- `emusync.game.sync.partial_failure.count` — track rate of partial failures.
- `emusync.game.metadata.migration.count` — track how many legacy entries have been migrated.
- `emusync.game.zip.legacy_layout.count` — count of legacy-format zips encountered on download.

(All captured via structured logs in v1; lifted to first-class metrics in a follow-up.)

### 12.4 Rollback Procedure

The feature flag's `Enabled = false` reverts the runtime to legacy single-path behaviour. Concrete steps:

1. Flip `MultiDirectoryGames.Enabled = false` in `appsettings.json` (or set the env var override).
2. Restart the Agent service.
3. Verify `GET /game` returns games with only the legacy `SyncSourceIdLocations` field populated.
4. If new-shape metadata was already written (post-gradual-rollout), keep the read path capable of reading it — but writes revert to legacy. Multi-child games already saved are read as legacy (first child only); the user sees a warning that other children are not yet visible. This is acceptable for a temporary rollback.

If a hard rollback is required (e.g. data corruption), the cloud `game-list.json` can be rewritten by an admin script that flattens every `sl2` entry back into `sl`. This is not a v1 deliverable but should be documented in an ops runbook.

---

## 13. Release Notes Draft

Draft text for CHANGELOG / NEWS / GitHub release, to be finalised at release time.

> **Multiple directories per game**
>
> A single game entry can now reference multiple source directories. Use this when a title's saves or config are spread across more than one folder (e.g. Steam cloud saves + a local profile folder) and you want them treated as one game in your library.
>
> Open a game for editing to add additional directories under **Sync locations**. Each directory can have its own include / exclude glob filters using `.gitignore`-compatible syntax (one pattern per line). Disabled directories keep their configuration but are skipped during sync.
>
> Existing entries are automatically upgraded — your current single-directory games work as before and you can add more directories whenever you want.

---

## 14. Code Review Checklist

Reviewers should confirm every item below before approving.

### Design

- [ ] `FIX_Multiple_Directories.md` is current; any in-PR deviations are noted here with rationale.
- [ ] `GamePathEntry` is a record with the documented shape.
- [ ] Combined-zip layout matches §4.1 (entries prefixed `<childIndex>/`, `manifest.json` at root).
- [ ] `ZipHelper` split into destructive (staging-only) and non-destructive (per-entry routing) variants.
- [ ] `PathFilter` uses `Microsoft.Extensions.FileSystemGlobbing.Matcher`; filters applied symmetrically on upload and download.

### Behaviour

- [ ] No child-directory limit enforced.
- [ ] Overlapping children deduped in declared order.
- [ ] Per-child failures isolated; entry status reflects partial failure.
- [ ] Strict aggregation of `localFolderPathExists` / `localFolderPathIsUnset`.
- [ ] Legacy single-path behaviour unchanged (regression check).

### Backwards Compatibility

- [ ] `sl` and `sl2` both populated on write.
- [ ] `SyncSourceIdLocations` and `SyncSourceIdLocationsV2` both populated on write.
- [ ] `GameManager.GetAsync` migrates legacy shape silently in memory.
- [ ] Lazy on first save — no startup-time rewrite.

### Tests

- [ ] Unit tests for `PathFilter`, `GamePathEntry`, `LocalDataAccessor.ScanDirectoriesAsync`, `GameMetaData` round-trip, validators, mapping.
- [ ] Integration tests for the 12 `GameSyncManager` scenarios in §4.2.
- [ ] Regression tests for legacy single-path behaviour.
- [ ] Test conventions match house style (framework / mocking / assertions documented at top of new files).

### Observability

- [ ] All log events from §5.1 emitted with structured properties.
- [ ] No full child paths logged at `Information` (use `Debug`).

### Security

- [ ] Path-traversal check on extracted entries.
- [ ] ReDoS guard via matcher timeout or pattern validation.
- [ ] Reject malformed / absolute / `..`-containing zip entries.

### Performance

- [ ] Per-child scans run in parallel with bounded concurrency.
- [ ] No full-zip temp file written; streaming into `UpsertZipDataAsync`.

### Configuration & Documentation

- [ ] No new config keys without an entry in §10 and (if user-facing) README.
- [ ] CHANGELOG / NEWS entries drafted.
- [ ] In-app help for filter syntax.

### Rollout

- [ ] Feature flag added with sensible defaults.
- [ ] Rollback procedure tested manually.

---

## 15. Pre-Merge Gate

The PR cannot be merged until **all** of the following are true:

- [ ] CI green (lint, type-check, unit tests, integration tests).
- [ ] Manual E2E checklist (§4.3) completed on at least one platform; results recorded in the PR description.
- [ ] Two approving reviews, including at least one from a maintainer with merge rights.
- [ ] CHANGELOG / NEWS entries reviewed and merged.
- [ ] Release notes draft (§13) reviewed and merged.
- [ ] Rollout runbook (§12.4) reviewed; feature flag values confirmed for the target release channel.
- [ ] No known regressions; any pre-existing test failures unrelated to the change are explicitly triaged and either fixed or out-of-scope-documented in the PR.
- [ ] Backwards compatibility verified against at least one legacy `game-list.json` fixture and one legacy zip fixture.

---

## 16. Cross-Reference Index

For convenience, the canonical sources of truth:

| Concern | Document / Section |
|---|---|
| Requirements & acceptance criteria | `CHANGE_Multiple_Directories.md` |
| Design | `FIX_Multiple_Directories.md` |
| Diagnosis of touched areas | `DIAGNOSIS_Multiple_Directories.md` |
| Implementation plan (this document) | `IMPLEMENTATION_Multiple_Directories.md` |
| Test strategy | §4 of this document; tests in §11 of `FIX_Multiple_Directories.md` |
| Rollout & migration | `FIX_Multiple_Directories.md` §10; §12 of this document |
| Open follow-ups | `FIX_Multiple_Directories.md` §14 |
