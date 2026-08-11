# Diagnosis: One Game / Entry, Multiple Directories

This report maps every layer of the EmuSync codebase to the proposed multi-directory feature, showing what is currently implemented, what would need to change, and where the seams (and risks) live. Findings are based on a full read of the source under `src/`.

## TL;DR

A single, single-line decision — `GameEntity.SyncSourceIdLocations: Dictionary<string, string>` — propagates through every layer of the system and is the single biggest blocker. Almost everything else in the codebase assumes exactly one folder path per `(game, syncSource)` pair. There is no partial support for multiple directories anywhere; the Ludusavi importer is the only layer that already carries a `List<string>` of paths.

The feature therefore touches **all six projects**: Domain, Services.Managers, Services.LudusaviImporter, Services.Storage, Agent (API/DTO/mapping), and UI (renderer + preload). It also changes the on-disk archive shape (one zip per game) and the in-cloud metadata format (`GameMetaData` uses a single-letter key for the path map). Glob/filter support is brand-new — there is currently **zero** file-pattern filtering anywhere in the codebase.

## 1. Data Model — `EmuSync.Domain`

### Current State

`GameEntity.SyncSourceIdLocations` is a flat map keyed by sync source id:
```csharp
public Dictionary<string, string>? SyncSourceIdLocations { get; set; }
```
Value is one folder path string per device. Used by:

- `GameSyncManager` (`GetSyncType`, `SyncGameAsync`, `ForceDownloadGameAsync`, `ForceUploadGameAsync`, `RestoreFromBackup`) — all start with `game.SyncSourceIdLocations?.TryGetValue(syncSourceId, out folderPath)`, throw `ArgumentNullException("No sync location has been set")` if missing.
- `GameManager.UpdateAsync` — calls `TrimPath(x.Value)` on each value.
- `GameManager.BulkUpsertAsync` — `GameBulkUpsert.Path` is a single string.
- `LocalGameSaveBackupService.CreateBackupAsync(GameEntity, string path, ...)` — single string path parameter.
- `DirectoryScanResult` (Domain/Results) — produced by a single `ILocalDataAccessor.ScanDirectory(string? path)` call.

### Touch Points for the Feature

- **Shape change**: `Dictionary<string, string>` → `Dictionary<string, List<GamePathEntry>>` (or similar). A `GamePathEntry` record would carry `{ Path, IncludeFilters, ExcludeFilters, Enabled }`.
- **Migration path**: existing entries have exactly one entry per key; on first load they must be re-hydrated as `[{ Path = existing, IncludeFilters = [], ExcludeFilters = [], Enabled = true }]`.
- **`GameEntity.LatestWriteTimeUtc` / `StorageBytes`**: currently aggregated over one path. With many paths, these become `max(LatestWriteTimeUtc)` and `sum(StorageBytes)` across the enabled child directories of the source.
- **`DirectoryScanResult`** is single-directory by design. Either:
  - the sync code calls `ScanDirectory` per child and aggregates, or
  - the helper is extended with a `ScanDirectories(IEnumerable<string> paths)` overload.
- **`DomainConstants`**: no per-path constants exist; nothing to change here.

## 2. Managers — `EmuSync.Services.Managers`

### Current State

Every manager reads the path map as a single string per sync source:

| Manager | File | Single-path usage |
|---|---|---|
| `GameManager` | `GameManager.cs` | `TrimPath`, `SyncSourceIdLocations[localSyncSource.Id] = TrimPath(upsert.Path)` (BulkUpsert) |
| `GameSyncManager` | `GameSyncManager.cs` | `GetSyncType`, `DownloadGameFilesAsync(path, …)`, `UploadGameFilesAsync(…, path, …)`, `RestoreFromBackup(…, folderPath, …)`, `ForceDownloadGameAsync`, `ForceUploadGameAsync` |
| `SyncSourceManager` | `SyncSourceManager.cs` | Does not touch per-game paths |

The sync flow that would need to fan out across multiple directories:

```
GetSyncType → ScanDirectory(path)              // currently 1 call per game
   ↓
DetermineSyncType(scanResult)                  // compares LatestWriteTimeUtc vs GameEntity.LatestWriteTimeUtc
   ↓
RequiresUpload → UploadGameFilesAsync(…, path, scanResult)
   ├─ ZipHelper.CreateZipFromFolder(path, tempZipPath, …)
   ├─ storageProvider.UpsertZipDataAsync("game-{id}.zip", fileStream, …)
   └─ GameManager.UpdateMetaDataAsync(game, LatestWriteTimeUtc, StorageBytes)

RequiresDownload → DownloadGameFilesAsync(path, game, …)
   ├─ storageProvider.GetZipFileAsync("game-{id}.zip", tempZipPath, …)
   ├─ LocalGameSaveBackupService.CreateBackupAsync(game, path, …)
   └─ ZipHelper.ExtractToDirectory(fileStream, path, …)   // DELETES path recursively first
```

### Touch Points for the Feature

- **`GameManager.UpdateAsync`** — must accept a richer `SyncSourceIdLocations` and persist a list per key (with sanitisation per child path). The bug currently visible (line `foundEntity.SyncSourceIdLocations = null;` followed by `if (entity.SyncSourceIdLocations != null)` reassignment) needs cleaning up while the field type is changing.
- **`GameManager.BulkUpsertAsync`** — `GameBulkUpsert` record must grow to either carry multiple paths or be replaced with a separate list of child entries.
- **`GameSyncManager.GetSyncType`** — needs an aggregated scan result across enabled child directories, and the returned `GetSyncTypeResult` needs to expose either a list of folder paths or a list of per-child scan results so that the upload/download paths can iterate them. (`GetSyncTypeResult.FolderPath: string` becomes a list.)
- **`GameSyncManager.UploadGameFilesAsync` and `DownloadGameFilesAsync`** — must operate per child directory. Decisions:
  - One zip per game still, or one zip per child? The current `StorageConstants.FileName_GameZip = "game-{0}.zip"` produces one zip per game; this can remain (children are zipped together) **or** change to `game-{id}-{childIndex}.zip`. The change request says "they don't have to be zipped together" — the smaller-blast-radius option is to keep one zip per game and concatenate child trees into it.
  - Local backup: today `LocalGameSaveBackupService.CreateBackupAsync(game, path)` zips one folder. With multiple children, either one combined backup zip or one backup per child.
  - Per-child failure must be reported (today the whole sync throws or succeeds).
- **`GameSyncManager.DetermineSyncType`** — decision must use the **max** of all child `LatestWriteTimeUtc` for "requires upload" and the **min** if comparing to cloud metadata; or compare against the stored `GameEntity.LatestWriteTimeUtc` which now represents the last known aggregate.
- **`GameSyncManager.RestoreFromBackup`** — `RestoreBackupAsync` takes an output directory; with multiple children, this either needs to receive a list of target paths or the restore must extract into one combined temp then copy out.

### Risk Hot Spots

- **`ZipHelper.ExtractToDirectory`** in `EmuSync.Domain/Helpers/ZipHelper.cs` does `Directory.Delete(cleanOutputDirectory, recursive: true)` before extracting. If we keep one combined zip per game, the temp staging area must be a separate scratch directory so we don't accidentally `rm -rf` one of the user's source folders. If we keep per-child zips, each child's target folder is wiped before extract — same behaviour, but now multiplied across children.
- **`GameManager.DeleteAsync`** deletes exactly one remote zip (`game-{id}.zip`). With per-child zips this would loop. With a single combined zip it stays as is, but the zip contents now need to record which file came from which child path (to write them back to the right place on download).
- **`BaseManager._lock`** (`GameManager._lock`) — a single `SemaphoreSlim` guards the whole `game-list.json`. Multi-child writes will increase the time the lock is held; probably still fine, but worth noting.

## 3. Storage — `EmuSync.Services.Storage`

### Current State

`IStorageProvider` has six methods, none of which enumerate local files:

```csharp
Task<TData?> GetJsonFileAsync<TData>(string fileName, CancellationToken);
Task GetZipFileAsync(string fileName, string writeToPath, Action<double>? onProgress, CancellationToken);
Task DeleteFileAsync(string fileName, CancellationToken);
Task UpsertJsonDataAsync(string fileName, object data, Action<double>? onProgress, CancellationToken);
Task UpsertZipDataAsync(string fileName, Stream stream, Action<double>? onProgress, CancellationToken);
void RemoveRelatedFiles();
```

All four implementations (Dropbox, OneDrive, Google Drive, SharedFolder) address files by single name. The remote data shape uses short JSON keys:

- `StorageConstants.FileName_GameZip = "game-{0}.zip"` — one zip per game.
- `GameMetaData` has property `"sl"` (sync source id locations) which is currently a dictionary of `string → string`. Changing the value type breaks the JSON wire format silently.

### Touch Points for the Feature

- **No listing API** — the providers do not currently support listing or fetching multiple files in one call. This is fine because the existing model has only a handful of named files per game. Multi-child syncing doesn't strictly need a new storage method (each child is processed sequentially with the existing `UpsertZipDataAsync` / `GetZipFileAsync`), but if we adopt a per-child zip model (`game-{id}-{childIndex}.zip`), the storage layer doesn't need changes — only the file-name conventions in `StorageConstants` (or wherever the name is built in `GameManager` / `GameSyncManager`).
- **`GameMetaData.FromGame`** must learn to serialise/deserialise `Dictionary<string, List<GamePathEntry>>` (or a new field if we add a child-directories section). The short key `"sl"` either expands to a richer value or a sibling key (e.g. `"sl"` plus `"slv"` for child metadata, `"slf"` for filters) is introduced.
- **Backward-compat read** — the metadata reader must still understand the old `{ syncSourceId: string }` shape and migrate it on load. This is the on-disk/on-cloud migration point.
- **Migration is one-way** — once an entry has been saved with the new shape, older agents must not corrupt it. Either bump the metadata version, or use tolerant reads (`JsonSerializerOptions` with permissive settings) plus a graceful "I don't understand this" path.
- **`RemoveRelatedFiles`** is unaffected; per-game file names are unchanged for the single-zip model.

## 4. Ludusavi Importer — `EmuSync.Services.LudusaviImporter`

### Current State

This is the one project that already thinks in terms of **multiple** paths per game:

- `GameDefinition.Files` is `Dictionary<string, FileEntry>` — many save-file entries per game, each tagged `save` / `config` with `When` constraints.
- `LudusaviManifestScanner.GetFileLocations` filters to entries tagged `save` and yields multiple paths.
- `LudusaviPathMap.GetOtherKnownLocations` adds emulator-specific paths (SKIDROW, EMPRESS, CreamAPI, SmartSteamEmu, Goldberg).
- `GetMostCommonFolder` collapses to a single best path; if it can't, it falls back to returning the full list.
- `FoundGame { Name, SuggestedFolderPaths: List<string> }` — already a list.
- `GameSuggestionDto { Name, SuggestedFolderPaths: List<string> }` — already a list.

But the moment it hands off to the rest of the app, the list is collapsed to a single string:

- `GameController.GetSuggestions` returns `GameSuggestionDto[]`.
- The UI's `GameSuggestionAutocomplete` (in `components/inputs/GameSuggestionAutocomplete.tsx` and consumed by `GameForm.tsx` and `GameQuickAddScreen.tsx`) writes the **single** chosen suggestion into `syncSourceIdLocations[localSyncSource.id]`.
- `QuickAddGameDto.Path: string` and `GameBulkUpsert.Path: string` flatten the list back down at the API boundary.

### Touch Points for the Feature

- The Ludusavi-side data already supports multiple paths; **the bottleneck is downstream**, in the DTOs and the `QuickAddGameDto` / `GameBulkUpsert` flow.
- If we want a "Quick add multiple directories for one game" experience, `QuickAddGameDto` must accept either:
  - `Path: string` plus optional `AdditionalPaths: List<string>`, or
  - `Paths: List<string>` (a breaking change), or
  - `PathEntries: List<{ Path, IncludeFilters?, ExcludeFilters? }>`.
- `GameMapping.ToUpsert()` (`EmuSync.Agent/Mapping/GameMapping.cs`) must translate accordingly.
- The Quick-Add UI row (`views/game/components/QuickAddGame.tsx`) needs a UI affordance to add more paths — a sub-list editor with one `PickDirectoryButton` per row, plus the include/exclude filter inputs.

## 5. Agent — `EmuSync.Agent`

### Current State

Every Game-related DTO uses `Dictionary<string, string>` for paths:

| DTO | File | Path field |
|---|---|---|
| `IGameDto` | `Dto/Game/IGameDto.cs` | `SyncSourceIdLocations: Dictionary<string, string>?` |
| `CreateGameDto` | `Dto/Game/CreateGameDto.cs` | same |
| `UpdateGameDto` | `Dto/Game/UpdateGameDto.cs` | same |
| `GameDto` | `Dto/Game/GameDto.cs` | same |
| `GameSummaryDto` | `Dto/Game/GameSummaryDto.cs` | same |
| `QuickAddGameDto` | `Dto/Game/QuickAddRequestBodyDto.cs` | `Path: string` (single) |
| `GameBackupManifestDto` | `Dto/Game/GameBackupManifestDto.cs` | no path |
| `GameSuggestionDto` | `Dto/Game/GameSuggestionDto.cs` | `SuggestedFolderPaths: List<string>` (already a list) |
| `GameSyncStatusDto` | `Dto/GameSync/GameSyncStatusDto.cs` | `localFolderPathIsUnset` / `localFolderPathExists` — single local folder |
| `SyncProgressDto` | `Dto/GameSync/SyncProgressDto.cs` | none |

Validation lives in `Dto/Game/IGameDto.cs` (`GameDtoValidator`): name ≤ 255, max backups > -1. **There is no path validation** today; `GameManager.TrimPath` is the only sanitiser (trims whitespace and trailing `/` or `\`).

Mapping (`Mapping/GameMapping.cs`):
- `IGameDto.ToEntity()` — copies `SyncSourceIdLocations` straight through (and the `Id` branch is missing for non-update DTOs).
- `QuickAddGameDto.ToUpsert()` — builds `GameBulkUpsert { Path = dto.Path, … }`.
- `GameEntity.ToDto()` / `.ToSummaryDto()` — direct passthrough.

Controllers:
- `GameController` — `GET /game`, `POST /game`, `PUT /game/{id}`, `POST /game/quickadd`. All touch `SyncSourceIdLocations` as a single string per device.
- `GameSyncController` — `GET /gamesync/{id}` returns `GameSyncStatusDto` with `localFolderPathIsUnset` / `localFolderPathExists`. With multiple children, these booleans become aggregates (e.g. `unset = all children unset`, `exists = all children exist`) and the response needs richer per-child info, or the user drills into a per-child status.

### Touch Points for the Feature

- **DTO shape** — `IGameDto`, `CreateGameDto`, `UpdateGameDto`, `GameDto`, `GameSummaryDto` must change their path field type. This is a **breaking change** to the JSON contract; agents of mixed versions will see type errors on deserialise.
- **Validation** — `GameDtoValidator` (FluentValidation, in `IGameDto.cs`) must validate each child directory (path non-empty; OS-safe chars; per-child filters well-formed if present). Reasonable to add a child-level validator and run it inside the collection rule.
- **`QuickAddGameDto`** — needs to carry multiple paths; the simplest non-breaking change is to add an optional `AdditionalPaths: List<string>?` (or `AdditionalPathEntries: List<PathEntry>?`).
- **`GameMapping`** — must handle both shapes on read (old shape → migrate) and write the new shape.
- **`GameSyncStatusDto`** — needs richer status to convey per-child states. Options:
  - Add `ChildStatuses: List<ChildSyncStatusDto>` carrying `Path`, `StatusId`, `LatestWriteTimeUtc?`.
  - Keep flat booleans but make them aggregate: `localFolderPathIsUnset` = all unset; `localFolderPathExists` = all exist.
- **Caching** — `Services/ApiCache.cs` `CacheSlot<List<GameEntity>>` and `GameController.GetList` will continue to work as long as the entity shape stays serialisable. `IGameSyncService.TryDetectGameSyncStatusesAsync` iterates all games and calls `IGameSyncManager.GetSyncType` per game — that path must remain cheap because it runs on every `GET /game`.

## 6. UI — `EmuSync.UI`

### Current State

The UI is the most visibly single-directory of all layers:

- **Types** (`src/renderer/types/`):
  - `Game.syncSourceIdLocations: Record<string, string> | null`
  - `CreateGame`, `UpdateGame` mirror the above.
  - `QuickAddGame.path: string` (single).
- **Game form** (`views/game/forms/GameForm.tsx`):
  - Maps `allSyncSources.map(src => …)` and renders one `DefaultTextField` per device, plus a `PickDirectoryButton` for the local device.
  - `replacePathDelims(allSyncSources, data)` normalises each value's slashes based on the source's `OsPlatform` (via `normalisePathDelims` in `utils/path-utils.ts`).
- **Game list** (`views/game/GameListScreen.tsx`):
  - Shows `GameSyncStatusChip`, name, `autoSync`, `StorageSizeChip`, last-uploaded device + date. **No directory column** today.
- **Sync status form** (`views/game/forms/SyncStatusForm.tsx`):
  - Buttons "Sync Now", "Upload", "Download" target the single local path.
  - `DisplayGameSyncStatus` (`views/game/components/DisplayGameSyncStatus.tsx`) shows `localFolderPathIsUnset`, `localFolderPathExists`, etc.
- **Quick add** (`views/game/GameQuickAddScreen.tsx`, `views/game/components/QuickAddGame.tsx`):
  - One `path` field per row in `QuickAddGame`, plus one `PickDirectoryButton`.
  - `views/game/utils/quick-add-utils.ts` — `QuickAddGameClientModel.path: string`, `convertToRequestBody` writes one path per `QuickAddGame`.
- **Hooks / state**:
  - `state/all-sync-sources.ts` — `allSyncSourcesAtom = atom<SyncSourceSummary[]>` — used to drive the "one row per device" mapping.
  - `state/local-sync-source.ts` — `localSyncSourceAtom`.
  - `hooks/use-sync-source-mapper.ts` — maps a sync source id to a display label.
  - `hooks/use-edit-form.ts` and `hooks/use-edit-query.ts` — generic RHF/react-query wrappers; agnostic to path shape.

### Touch Points for the Feature

- **Types** — change `syncSourceIdLocations` to `Record<syncSourceId, GamePathEntry[]>` and add `GamePathEntry { Path, IncludeFilters?, ExcludeFilters?, Enabled? }`. Update `QuickAddGame` likewise.
- **`GameForm.tsx`** — for each device row, render a list editor (add / remove / reorder / toggle enabled child). The local-device row gets a `PickDirectoryButton` per child; non-local-device rows stay text-only. This is the largest single UI change.
- **Game list** — could optionally show a count badge ("3 directories") per entry. Not strictly required.
- **Sync status form** — needs to show per-child status when there is more than one child. The simplest UX is a collapsible section per child.
- **Quick add** — `QuickAddGame.tsx` row needs to support a list of paths instead of one. `quick-add-utils.ts` builds the request body — must emit all paths.
- **Path normalisation** — `utils/path-utils.ts::normalisePathDelims` is called per child path now. Trivially extended.
- **Preload / IPC** — `PickDirectoryButton` calls `window.electron.openDirectory(defaultPath)`; no changes needed here.

## 7. Cross-Cutting Concerns

### Glob / Filter Support

**There is currently no file-pattern filtering anywhere in the codebase.** The closest things are:

- `LudusaviPathMap.ExpandRecursiveWildcardDirectories` — expands `*{WILD_CARD}*` tokens in Ludusavi path templates.
- `LudusaviPathMap.WildcardDirectory` constant.
- `LudusaviManifestScanner.AddLocationIfExists` — reduces a path whose last segment is a wildcard filename to the parent directory.

No library is referenced for glob matching today. Adding include/exclude filters means introducing one. Candidates:

- `Microsoft.Extensions.FileSystemGlobbing` (already part of the .NET ecosystem via `Microsoft.Extensions.FileSystemGlobbing` and `Matcher` in `Microsoft.Extensions.FileSystemGlobbing.Abstractions` / `Microsoft.Extensions.FileSystemGlobbing.Matcher`).
- `DotNet.Glob` (a popular standalone package).
- Roll a small in-house matcher with `*`, `?`, `**`, `[…]` support.

Filter evaluation needs a deterministic rule for **path relativity** (rooted at each child directory), **case sensitivity** (depends on OS), and **directory-vs-file** semantics. Filters should apply symmetrically on upload and download so that excluded items don't get clobbered locally after a download.

### Tests

Existing test projects:

- `EmuSync.Domain.Tests` — exercises entities, results, helpers.
- `EmuSync.Services.Managers.Tests` — exercises manager-level sync flows (`Results/` directory suggests result-shaped tests).
- `EmuSync.Services.Storage.Tests` — per-provider tests (Dropbox, OneDrive, GoogleDrive).
- `EmuSync.Agent.Tests` — DTO and mapping tests, plus service-level tests.

Every test that builds a `GameEntity` or `GameBulkUpsert` will need to be updated to the new path shape. New tests should cover:

- Migration of a single-path entry into a single-child multi-child entry on load.
- Aggregation of `LatestWriteTimeUtc` and `StorageBytes` across children.
- Per-child include/exclude filter behavior at upload (filter before adding to zip) and download (filter before extracting to disk).
- Per-child failure isolation (one child failing does not abort the others).
- Backward-compat reads of legacy cloud metadata.

### Backward Compatibility & Versioning

Two independent compat surfaces to manage:

1. **On-disk / on-cloud metadata** — old `{ syncSourceId: path }` shape vs. new `{ syncSourceId: [child…] }` shape. The reader must tolerate the old shape and migrate on first save.
2. **API JSON contract** — `IGameDto.SyncSourceIdLocations` type change is breaking. Mixed-version agents (old UI + new Agent, or new UI + old Agent) will fail JSON deserialisation. Two options:
   - Add a parallel field (e.g. `SyncSourceIdLocationsV2`) and read either, prefer the new one if present. Deprecate the old. (More code, zero breakage.)
   - Bump the API contract and require both sides upgraded together. (Cleaner, but disruptive.)

### Risks & Open Questions

1. **One zip per game vs. one zip per child** — keeping one combined zip per game is a smaller change to storage but requires tracking per-file provenance (where to write each entry back to on download). Per-child zips are simpler to reason about but multiply storage operations and increase remote-file counts.
2. **Per-child failures** — today, any sync exception bubbles up as a 500. The UI shows an error. With per-child failures, we need a per-child status aggregate.
3. **Auto-sync** — `GameSyncWorker` and `SyncTaskProcessor` operate on `GameEntity`. Auto-sync today triggers on the whole game; with multi-child it still does, but per-child status aggregation in `GameSyncStatusDto` must be consistent with the new model.
4. **Backup semantics** — `LocalGameSaveBackupService` zips the existing folder before download overwrites it. With multi-child, the backup either fans out per child or combines all children into one backup zip. The combined approach is simpler but loses per-child fidelity in the backup.
5. **Filter conflicts** — overlapping children (one path is a parent of another) must be either disallowed or given a deterministic merge rule.
6. **Migration volume** — every existing user has `Dictionary<string, string>` data. One-way migration on load is fine, but the first sync after upgrade must not corrupt legacy data — write-back must happen only after read-side migration has succeeded.
7. **UI ergonomics** — a tree-style expandable entry in the game list (per the change request) is a substantial UI redesign, not a small change. Worth scoping as its own sub-task.

## 8. Touch-Point Summary Table

| Area | File(s) | Change Type | Notes |
|---|---|---|---|
| Domain — entity | `EmuSync.Domain/Entities/GameEntity.cs` | Breaking — change `SyncSourceIdLocations` shape | Plus new `GamePathEntry` record (in `Objects/`) |
| Domain — result | `EmuSync.Domain/Results/DirectoryScanResult.cs` | Add aggregation helper or new overload | Multi-path scan |
| Managers | `EmuSync.Services.Managers/GameManager.cs` | Update + bulk-upsert to handle child lists | TrimPath per child |
| Managers | `EmuSync.Services.Managers/GameSyncManager.cs` | Sync methods fan out per child; aggregated sync type | Per-child progress, per-child errors |
| Managers | `EmuSync.Services.Managers/Objects/GameBulkUpsert.cs` | Replace `Path: string` with list + per-child filters | |
| Managers | `EmuSync.Services.Managers/Results/GetSyncTypeResult.cs` | `FolderPath` → `FolderPaths` (or per-child results) | |
| Domain — helpers | `EmuSync.Domain/Helpers/ZipHelper.cs` | Add filter-aware zip creation / extraction | Or wrap from `GameSyncManager` |
| Domain — services | `EmuSync.Domain/Services/LocalGameSaveBackupService.cs` | Backup of multiple child directories | Combined or per-child zip |
| Storage | `EmuSync.Services.Storage/Objects/GameMetaData.cs` | New shape for path map; tolerate legacy | Short-key JSON; on-cloud format change |
| Storage | `EmuSync.Services.Storage/StorageConstants.cs` | Optional: per-child zip file name template | Only if we adopt per-child zips |
| Ludusavi Importer | (no change needed) | Already supports multiple paths | Already exposes `SuggestedFolderPaths` |
| Agent — DTOs | `EmuSync.Agent/Dto/Game/{IGameDto,CreateGameDto,UpdateGameDto,GameDto,GameSummaryDto,QuickAddRequestBodyDto}.cs` | Breaking — change path shape | Plus `GamePathEntryDto` |
| Agent — DTOs | `EmuSync.Agent/Dto/GameSync/GameSyncStatusDto.cs` | Add per-child status | |
| Agent — DTOs | `EmuSync.Agent/Dto/Game/IGameDto.cs` (`GameDtoValidator`) | Validate each child path + filter syntax | |
| Agent — mapping | `EmuSync.Agent/Mapping/GameMapping.cs` | Translate between legacy and new shapes | Backward-compat read |
| Agent — controllers | `EmuSync.Agent/Controllers/GameController.cs` and `GameSyncController.cs` | Mostly unchanged; status DTO changes | |
| UI — types | `EmuSync.UI/src/renderer/types/Game.ts` | New `GamePathEntry` type; update maps | |
| UI — views | `EmuSync.UI/src/renderer/views/game/forms/GameForm.tsx` | Per-device child-directory list editor | Largest single UI change |
| UI — views | `EmuSync.UI/src/renderer/views/game/components/QuickAddGame.tsx` | Multi-path support per QuickAdd row | |
| UI — utils | `EmuSync.UI/src/renderer/views/game/utils/quick-add-utils.ts` | Build request body with all paths | |
| UI — utils | `EmuSync.UI/src/renderer/views/game/utils/game-utils.ts` | Per-child `replacePathDelims` | |
| UI — utils | `EmuSync.UI/src/renderer/utils/path-utils.ts` | Reuse as-is for each child path | No change |
| Tests | `EmuSync.Domain.Tests`, `EmuSync.Services.Managers.Tests`, `EmuSync.Agent.Tests`, `EmuSync.Services.Storage.Tests` | Update fixtures; add new tests for migration, filters, per-child failures | |
| Build | `EmuSync.UI/src/renderer/types/Game.ts`, `EmuSync.UI/src/main/preload.ts` | No IPC changes needed | Directory picker unchanged |
