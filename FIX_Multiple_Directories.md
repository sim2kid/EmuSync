# Fix: One Game / Entry, Multiple Directories

This document proposes a concrete design for the multi-directory feature based on the answers to the open questions in `CHANGE_Multiple_Directories.md`. It assumes the diagnosis in `DIAGNOSIS_Multiple_Directories.md` and lists the exact changes per file, the data-shape decisions, the sync algorithm, and the migration strategy.

## 1. Design Decisions (Resolved from Open Questions)

| Question | Decision |
|---|---|
| Limit on child directories per entry | **No limit.** The UI grows as the user adds children. |
| Overlapping / nested child paths | **Allowed.** Children are processed in declared order. A file that was already included by an earlier child is skipped by later children. The combined archive tracks each entry's source-child so the downloader writes it back to the correct location. |
| Filters on download vs. upload | **Both, symmetric.** Includes and excludes are applied at pack time (upload) and at extract time (download). |
| Per-child failures in the UI | **Single entry-level warning with drill-down.** The entry's `GameSyncStatus` rolls up child statuses; if any child failed, the entry shows a warning and the user can drill in to see per-child detail. |
| Sync mode toggle | **None.** There is one sync method, designed for many folders. A single-folder entry is the degenerate case (one child). |
| Archive shape | **One combined zip per game** with provenance metadata. (See §4.) |
| API/JSON contract compatibility | **Parallel DTO field.** Cloud metadata and the HTTP `IGameDto` both carry a legacy field and a new `V2` field side by side for one release. New readers prefer `V2`, fall back to migrating the legacy field. After the rollout, the legacy field is dropped. (See §3 and §7.) |
| Filter editor UI | **Plain textarea, one glob per line** per list (include / exclude). |
| Quick Add with multi-path Ludusavi suggestion | **Preserve today's single-path flow.** Quick Add picks one path as the primary child of a new or existing entry; Ludusavi's additional suggested paths are not auto-added. May be revisited once Ludusavi behaviour is better understood. |
| Legacy migration UX | **Silent in-place migration.** No UI notice; a legacy entry becomes a one-child multi-child entry on load. |
| `GameSyncStatusDto.localFolderPathExists` / `localFolderPathIsUnset` aggregation | **Strict.** `unset` iff every enabled child is unset; `exists` iff every enabled child exists on disk. Drill-down shows per-child state. |
| When the legacy → new cloud shape is persisted | **Lazy on first save.** In-memory migration on read; new shape is written the next time the entry is saved (create / update / metadata update). |

## 2. Data Model

### 2.1 New Type — `GamePathEntry`

Introduce a record in `EmuSync.Domain/Objects/GamePathEntry.cs`:

```csharp
public record GamePathEntry
{
    public string Path { get; set; }                      // absolute path; trimmed and slash-normalised
    public List<string> IncludeFilters { get; set; } = []; // glob patterns; empty = include everything
    public List<string> ExcludeFilters { get; set; } = []; // glob patterns; applied after includes
    public bool Enabled { get; set; } = true;              // false = skip during sync but keep config
}
```

### 2.2 Updated `GameEntity`

`EmuSync.Domain/Entities/GameEntity.cs`:

```csharp
// Before:
public Dictionary<string, string>? SyncSourceIdLocations { get; set; }

// After:
public Dictionary<string, List<GamePathEntry>>? SyncSourceIdLocations { get; set; }
```

`LatestWriteTimeUtc` and `StorageBytes` continue to live on `GameEntity` and now represent the **aggregate across enabled children** of the sync source the game was last synced from. (See §6 for aggregation rules.)

### 2.3 Domain Service — `ILocalDataAccessor` Additions

Add to `EmuSync.Domain/Services/Interfaces/ILocalDataAccessor.cs` (and implement in `LocalDataAccessor.cs`):

```csharp
Task<DirectoryScanResult> ScanDirectoriesAsync(
    IEnumerable<string> paths,
    CancellationToken cancellationToken = default);
```

This is a fan-out + aggregate. Implementation:

- For each path, run the existing `ScanDirectory` logic.
- Aggregate `FileCount`, `DirectoryCount`, `StorageBytes` (sums).
- Aggregate `LatestFileWriteTimeUtc` / `LatestDirectoryWriteTimeUtc` (max).
- Return a single `DirectoryScanResult` with the aggregate values plus a new `ScannedPaths: List<string>` property listing paths that were scanned.

Add `ScannedPaths: List<string>` to `EmuSync.Domain/Results/DirectoryScanResult.cs` (default `[]`).

### 2.4 Domain — Constants

No changes to `DomainConstants.cs`.

## 3. Cloud / On-Disk Metadata Format

### 3.1 `GameMetaData`

`EmuSync.Services.Storage/Objects/GameMetaData.cs` currently uses short JSON keys (`"sl"`, etc.). Change the path field:

- Key stays `"sl"` for backward compatibility, but its value type changes from `Dictionary<string, string>` to `Dictionary<string, List<GamePathEntry>>` (serialised as objects).
- On read, accept either:
  - **Legacy** value is a `Dictionary<string, string>` (old shape) — migrate each entry to `[{ Path = v, IncludeFilters = [], ExcludeFilters = [], Enabled = true }]` and remember that the entry was migrated.
  - **New** value is a `Dictionary<string, List<GamePathEntry>>` — use directly.
- `FromGame(GameEntity)` writes only the new shape.

Add a private static helper `MigrateFromLegacy(Dictionary<string, string>? legacy)` returning the new shape (or `null` when input is null).

### 3.2 On-Cloud Version Marker

Add a top-level `"v": 2` integer to `GameMetaData` so future readers can distinguish formats. Default to `1` for legacy shapes on read; `FromGame` always writes `2`. A future `v: 3` would similarly be opt-in.

## 4. Archive Shape — One Combined Zip per Game

**Decision:** Keep `StorageConstants.FileName_GameZip = "game-{0}.zip"` — one combined zip per game. Inside the zip, every entry's path is prefixed with a synthetic child-index folder so that downloaders can route the file back to its original child directory.

### 4.1 Upload-Time Layout

For a game with three children (in declared order):

```
game-{id}.zip
├── 0/                          ← child index 0
│   ├── manifest.json           ← per-zip child manifest (see §4.3)
│   ├── <relative path 1>
│   └── <relative path 2>
├── 1/                          ← child index 1
│   └── …
└── 2/                          ← child index 2
    └── …
```

Each entry's actual relative path inside the child becomes `<childIndex>/<relative path within child>`. The prefix `0/`, `1/`, `2/`, … is a single numeric directory segment.

### 4.2 Why One Combined Zip

- Smallest change to the storage layer: `UpsertZipDataAsync` / `GetZipFileAsync` / `DeleteFileAsync` remain one-call-per-game.
- The remote file count stays the same.
- Local backup (`LocalGameSaveBackupService.CreateBackupAsync`) can produce one combined zip that mirrors the game zip's structure.
- The only consumer that changes is `ZipHelper`, which learns the new prefix scheme.

### 4.3 Per-Zip Manifest

Inside each game zip, write `manifest.json` at the zip root (or at `0/manifest.json` — pick one; **zip root** is simpler). Schema:

```json
{
  "v": 1,
  "children": [
    { "index": 0, "path": "C:\\Path\\A", "includeFilters": [], "excludeFilters": [], "enabled": true },
    { "index": 1, "path": "C:\\Path\\B", "includeFilters": ["*.sav"], "excludeFilters": ["tmp/**"], "enabled": true }
  ]
}
```

`index` matches the leading directory segment on each entry. The manifest is required for download-time routing; if it is missing on download, fall back to writing everything under `0/...` and log a warning (legacy / pre-feature zip).

### 4.4 Backward-Compat Zips

A pre-feature game zip has entries at the zip root (no `0/`, `1/`, … prefix). The downloader treats the missing prefix as child index `0` for routing and writes everything to a single best-guess target (the first enabled child that exists on disk, or fails with a clear error if none). This preserves data integrity for any user who upgrades mid-sync — at worst, files land in one child folder instead of being split.

## 5. Sync Algorithm

### 5.1 `GetSyncType` — Aggregate

In `EmuSync.Services.Managers/GameSyncManager.cs::GetSyncType`:

1. Resolve the enabled children list from `game.SyncSourceIdLocations[syncSourceId]` (skip `Enabled = false`).
2. If the list is empty → `GameSyncStatus.UnsetDirectory` (same as today's "no path set").
3. Call `LocalDataAccessor.ScanDirectoriesAsync(children.Select(c => c.Path))` — produces one aggregate `DirectoryScanResult` with `ScannedPaths`.
4. Call `DetermineSyncType(game, scanResult)` exactly as today — the decision is based on the aggregate `LatestWriteTimeUtc`.
5. Return a new result shape that exposes per-child info.

### 5.2 New Result Type — `GetSyncTypeResult`

Replace `EmuSync.Services.Managers/Results/GetSyncTypeResult.cs`:

```csharp
public record GetSyncTypeResult
{
    public GameSyncStatus SyncStatus { get; set; }
    public List<ChildSyncContext> Children { get; set; } = [];
    public DirectoryScanResult DirectoryScanResult { get; set; }
    public bool NoLocalFolderPath => Children.Count == 0;
}

public record ChildSyncContext
{
    public string Path { get; set; }                  // child path
    public List<string> IncludeFilters { get; set; }
    public List<string> ExcludeFilters { get; set; }
    public bool Enabled { get; set; }
}
```

`Path` is included in the result so the upload/download code does not need to re-look-up the entity's path map.

### 5.3 Upload — One Pass, Combined Zip

In `GameSyncManager.UploadGameFilesAsync(syncSourceId, result, game, isAutoSync, scanResult, ct)`:

1. Build the per-child list from `result.Children`.
2. Walk files per child in declared order:
   - For each child, enumerate files with `Directory.EnumerateFiles(childPath, "*", SearchOption.AllDirectories)`.
   - For each file, evaluate filters (see §5.5). Skip files that don't pass.
   - **Overlap dedup:** maintain a `HashSet<string>` of relative paths already included in this upload. Skip any file whose relative path (relative to **its child**) matches one already added. Because children can have overlapping absolute paths but we key by the **child's absolute path + relative file path**, the dedup is per-source.
   - Add each surviving file as a `ZipArchiveEntry` whose `FullName` is `<childIndex>/<Path.GetRelativePath(childPath, filePath)>`.
3. Write `manifest.json` as the first entry.
4. Stream the zip to `storageProvider.UpsertZipDataAsync("game-{id}.zip", stream, …)` exactly as today.
5. After upload succeeds:
   - `game.LastSyncedFrom = syncSourceId`
   - `game.LastSyncTimeUtc = DateTime.UtcNow`
   - `game.LatestWriteTimeUtc = scanResult.LatestWriteTimeUtc` (aggregate)
   - `game.StorageBytes = scanResult.StorageBytes` (aggregate)
6. Per-child failures: catch exceptions per child; record them in a per-child error list; continue with remaining children; aggregate to one error at the end if any child failed. (See §5.7.)

### 5.4 Download — One Pass, Split Back

In `GameSyncManager.DownloadGameFilesAsync(syncSourceId, result, game, isAutoSync, ct)`:

1. Read `manifest.json` from the zip first to learn the children list.
2. Pre-download backup (combined) — `LocalGameSaveBackupService.CreateBackupAsync(game, scanResult, …)` writes a backup zip that mirrors the current state across all enabled children. The backup uses the same combined-zip layout (`0/...`, `1/...`, etc.) so a restore from backup is symmetric.
3. Download `game-{id}.zip` to a **temp staging directory** (not to any child path) — see §5.6 for staging.
4. For each entry in the zip (excluding `manifest.json`):
   - Parse the leading numeric directory segment as `childIndex`.
   - Resolve the child path via `manifest.Children[childIndex]`.
   - Apply the child's filters (post-sync check) — see §5.5. Skip entries that don't pass.
   - **Overlap dedup:** maintain a `HashSet<string>` of `<childPath>/<relative>` already written. Skip duplicates.
   - Write the file to `Path.Combine(childPath, remainingRelativePath)` under the original child path.
   - If `childIndex` references a child that no longer exists in the current config, skip and log a warning (the file is orphaned in this version of the config but kept safe in the zip).
5. Set `LastWriteTimeUtc` to the stored `game.LatestWriteTimeUtc` to preserve the "in sync" decision.
6. Aggregate per-child errors and surface as a single entry-level error.

### 5.5 Filter Evaluation

Introduce `EmuSync.Domain/Helpers/PathFilter.cs`:

```csharp
public static class PathFilter
{
    // Returns true if the relative path passes this child's filter rules.
    // Includes short-circuit: empty include list = include everything.
    // Excludes always win.
    public static bool Passes(IEnumerable<string> includeFilters, IEnumerable<string> excludeFilters, string relativePath);
}
```

Pattern syntax: `.gitignore`-compatible glob (`*`, `?`, `**`, character classes). Implementation uses `Microsoft.Extensions.FileSystemGlobbing.Matcher` (already a transitive of `Microsoft.Extensions.FileSystem` — add it explicitly to `EmuSync.Domain.csproj`).

Patterns are evaluated **relative to the child path's root**. A leading `/` anchors to the child root.

Symmetry: filters apply on both upload (skip before adding to zip) and download (skip before writing back to disk). This ensures excluded files stay excluded across syncs.

### 5.6 Download Staging — Avoid `rm -rf` of User Folders

**Critical:** `ZipHelper.ExtractToDirectory` currently does `Directory.Delete(cleanOutputDirectory, recursive: true)` before extracting. That was safe when the target was always the user's single game folder, but with many children it would silently destroy unrelated files in any child that the user happened to point at a broader directory.

Refactor `EmuSync.Domain/Helpers/ZipHelper.cs`:

- Keep a **destructive** overload `ExtractToDirectoryDestructive(Stream zipStream, string outputDirectory, …)` for the case where the caller explicitly wants wipe-then-extract (used internally only for the **temp staging dir**).
- Add a **non-destructive** method `ExtractCombinedZip(Stream zipStream, string tempStagingDir, Action<ZipArchiveEntry, string> onEntry, …)` that extracts entries one by one to the staging directory under their original `<childIndex>/<relative>` paths. The caller then routes each file to its target child on disk.
- Delete `ZipHelper.ExtractToDirectory`'s external use in `GameSyncManager.DownloadGameFilesAsync`; route everything through the non-destructive path.

`LocalGameSaveBackupService.RestoreBackupAsync` keeps the destructive behavior because the restore flow already wipes the single target path on purpose — but its `outputDirectory` parameter is replaced with a list of per-child target paths, each wiped independently inside its own root.

### 5.7 Per-Child Failure Handling

Per-child try/catch inside the upload and download loops. Each loop accumulates a list of `(childIndex, exception)`. At the end:

- If all succeeded → status `InSync`.
- If some failed and others succeeded → status `RequiresUpload` or `RequiresDownload` (whichever direction we were attempting), with the per-child error list attached to a logged warning and a `GameSyncStatusDto` field (see §7).
- If the zip operation itself failed (network error uploading the combined zip) → treat as the legacy single-error path.

`SyncTaskProcessor` and `GameSyncService` continue to call `SyncGameAsync` exactly as today; the difference is invisible at the orchestration layer.

## 6. Aggregation Rules

### 6.1 `LatestWriteTimeUtc`

`GameEntity.LatestWriteTimeUtc` is the **max** of `LatestWriteTimeUtc` across all enabled children's most recent scan. `DirectoryScanResult.LatestWriteTimeUtc` already returns the max — no change needed beyond making `ScanDirectoriesAsync` use it.

### 6.2 `StorageBytes`

Sum of all enabled children's `StorageBytes`. Same: `ScanDirectoriesAsync` returns the sum, `GameSyncManager` writes it to `game.StorageBytes`.

### 6.3 Per-Child Status (for UI)

Introduce `ChildSyncStatusDto` in `EmuSync.Agent/Dto/Game/`:

```csharp
public record ChildSyncStatusDto
{
    public string Path { get; set; }
    public bool Exists { get; set; }
    public DateTime? LatestWriteTimeUtc { get; set; }
    public List<ChildSyncErrorDto> Errors { get; set; } = [];
}

public record ChildSyncErrorDto
{
    public string Stage { get; set; }   // "scan" / "upload" / "download" / "extract"
    public string Message { get; set; }
}
```

`GameSyncStatusDto` gains `Children: List<ChildSyncStatusDto>`. The flat booleans (`localFolderPathIsUnset`, `localFolderPathExists`) remain but become **strict** aggregates:

- `localFolderPathIsUnset = Children.Count == 0 || Children.All(c => !c.Exists)` — i.e. true iff there are no enabled children at all, **or** every child is missing from disk.
- `localFolderPathExists = Children.All(c => c.Exists)` — i.e. true iff **every** enabled child exists on disk.

A mixed state (e.g. 2 of 3 exist) yields `localFolderPathIsUnset = false` and `localFolderPathExists = false`, which surfaces a warning at the entry level (driven by the alert hierarchy) and the per-child truth is visible in the drill-down.

## 7. API / DTO Changes

**Compatibility strategy (confirmed):** every Game DTO carries both the legacy `SyncSourceIdLocations: Dictionary<string, string>?` and a new `SyncSourceIdLocationsV2: Dictionary<string, List<GamePathEntryDto>>?` field for one release. On write, both are populated. On read, `V2` is preferred when present, otherwise the legacy value is migrated to `V2` in memory. After the rollout window the legacy field is removed (see §10.4).

### 7.1 `EmuSync.Agent/Dto/Game/IGameDto.cs`

Add `GamePathEntryDto`:

```csharp
public record GamePathEntryDto
{
    public string Path { get; set; }
    public List<string> IncludeFilters { get; set; } = [];
    public List<string> ExcludeFilters { get; set; } = [];
    public bool Enabled { get; set; } = true;
}
```

Add to `IGameDto`:

```csharp
public interface IGameDto
{
    string Name { get; }
    bool AutoSync { get; }
    // Legacy shape. Retained for one release; new code reads SyncSourceIdLocationsV2.
    Dictionary<string, string>? SyncSourceIdLocations { get; }
    // New shape. Preferred read path.
    Dictionary<string, List<GamePathEntryDto>>? SyncSourceIdLocationsV2 { get; }
    int? MaximumLocalGameBackups { get; }
}
```

### 7.2 `CreateGameDto`, `UpdateGameDto`, `GameDto`, `GameSummaryDto`

Each DTO gets the parallel field. Write paths populate **both** fields. Read paths consult `V2` first and fall back to `SyncSourceIdLocations`. `QuickAddGameDto` keeps a single `Path: string` for v1 (see §7.3).

### 7.3 `QuickAddGameDto` — Single Path Only (v1 Scope)

`QuickAddGameDto.Path: string` is the only path field for v1. Ludusavi suggestions that contain multiple paths are still surfaced as `GameSuggestionDto.suggestedFolderPaths: List<string>`, but the Quick Add UI picks one (today's behavior). The user's choice becomes a single-child multi-child entry on the local sync source:

```csharp
GameBulkUpsert { Path = dto.Path, GameName, AutoSync, MaximumLocalGameBackups }
```

Mapping from `QuickAddGameDto.ToUpsert()` is unchanged. The Quick Add flow does not exercise multi-child creation in v1; users who want multiple directories for a Quick-Added game add them via the Game Edit screen.

> Out of scope for v1: auto-populating Ludusavi's additional suggested paths into `AdditionalPaths`. The plumbing for additional paths stays available in the underlying `GameEntity` and `GamePathEntry` types but is not exposed through the Quick Add DTO/UI.

### 7.4 `GameDtoValidator` (FluentValidation)

Add per-child rules in `IGameDto.cs`:

- Each `GamePathEntryDto.Path` must be non-empty.
- Each child's filter lists, if non-empty, must each be a syntactically valid glob pattern (validate via the same `Matcher` instance used at runtime, catching parse exceptions).
- Total number of children is uncapped.

### 7.5 `GameSyncStatusDto`

Add `Children: List<ChildSyncStatusDto>` (see §6.3). Keep existing flat fields, recomputed as **strict** aggregates per §6.3.

### 7.6 `GameMapping`

`EmuSync.Agent/Mapping/GameMapping.cs`:

- `IGameDto.ToEntity()` — read `SyncSourceIdLocationsV2` first; if absent, read `SyncSourceIdLocations` and migrate each value to `[{ Path = v, IncludeFilters = [], ExcludeFilters = [], Enabled = true }]`. Deep-copy filter lists.
- `GameEntity.ToDto()` / `.ToSummaryDto()` — write **both** the `V2` and legacy fields (so existing clients keep working for one release).
- `QuickAddGameDto.ToUpsert()` — unchanged (single path).
- Add `GamePathEntryDto.ToDomain() / .ToDto()` helpers.

## 8. Manager Changes

### 8.1 `GameManager.UpdateAsync`

Replace `TrimPath` with `SanitisePathEntry(GamePathEntry)`:

- Trim `Path`, strip trailing `/` or `\`.
- Deduplicate filter lists (case-sensitive trim).
- Leave `Enabled` untouched.

### 8.2 `GameManager.BulkUpsertAsync`

`GameBulkUpsert` keeps the single-path shape for v1:

```csharp
public record GameBulkUpsert
{
    public string? ExistingGameId { get; set; }
    public string Path { get; set; }
    public string? GameName { get; set; }
    public bool? AutoSync { get; set; }
    public int? MaximumLocalGameBackups { get; set; }
}
```

The chosen `Path` becomes a single-child multi-child entry on the local sync source (`[{ Path, IncludeFilters = [], ExcludeFilters = [], Enabled = true }]`). Users who want more directories add them through the Game Edit screen.

> Out of scope for v1: an `AdditionalPaths` field on `GameBulkUpsert`. The mapping layer (`QuickAddGameDto.ToUpsert()`) does not change in v1.

### 8.3 `GameSyncManager` — See §5

### 8.4 `LocalGameSaveBackupService` — Combined Backup

Refactor `CreateBackupAsync(GameEntity, string path, …)` → `CreateBackupAsync(GameEntity, GetSyncTypeResult, …)`. It receives the resolved children list (paths already populated by `GetSyncType`), produces a single combined zip matching the upload layout, and stores it as a single `manifest.json` + per-child entries under `<localData>/game-backups/<gameId>/backup_<ts>.zip`. `RestoreBackupAsync(gameId, backupId, List<string> targetPaths)` restores into the supplied per-child targets.

## 9. UI Changes

### 9.1 Types — `EmuSync.UI/src/renderer/types/Game.ts`

Add:

```ts
export type GamePathEntry = {
    path: string;
    includeFilters: string[];
    excludeFilters: string[];
    enabled: boolean;
};
```

Update `Game`, `CreateGame`, `UpdateGame`, `GameSummary` to carry both the legacy and new fields:

```ts
// Legacy: retained for one release; new code reads syncSourceIdLocationsV2.
syncSourceIdLocations?: Record<string, string> | null;
// New: preferred read path.
syncSourceIdLocationsV2?: Record<string, GamePathEntry[]> | null;
```

`QuickAddGame` is **unchanged** in v1 (`path: string` only).

### 9.2 `GameForm.tsx`

Replace the "Sync locations" `Paper` body. For each device:

- Render a list editor (`useFieldArray({ name: 'syncSourceIdLocations.<syncSourceId>' })`).
- Each child row:
  - `path` text field + `PickDirectoryButton` (only for the local-device children; remote-device children are text-only).
  - `enabled` toggle.
  - Collapsible section for `includeFilters` and `excludeFilters` (each a multi-line `DefaultTextField`, one glob per line; on blur, parsed into a string array).
  - Remove button.
- "+ Add directory" button per device. Adds an empty child at the end.
- Drag-to-reorder is **out of scope for v1**; order is set by add/remove sequence.

`replacePathDelims` (in `utils/game-utils.ts`) is updated to run `normalisePathDelims` on every child's `Path`.

### 9.3 `views/game/components/QuickAddGame.tsx`

**No changes for v1.** Quick Add keeps the single `path` field with one `PickDirectoryButton`. Ludusavi suggestions are still surfaced as `GameSuggestionDto.suggestedFolderPaths: List<string>`, but the component continues to pick one path as today. Multi-child creation happens via the Game Edit screen (§9.2).

### 9.4 `views/game/components/DisplayGameSyncStatus.tsx`

Keep the existing alert hierarchy (UnsetDirectory / RequiresDownload / RequiresUpload / InSync / NeverSynced). Add a small "Details" disclosure beneath the alert that lists each child with its path and a green/red dot per status. The whole entry's status remains one chip in the game list — the drill-down is opt-in.

### 9.5 `views/game/utils/quick-add-utils.ts`

**No changes for v1.** `QuickAddGameClientModel` keeps its single `path: string` field. `convertToRequestBody` continues to emit `{ games: [{ existingGameId, gameName, path, autoSync, maximumLocalGameBackups }] }`.

## 10. Migration & Backward Compatibility

### 10.1 On-Load Migration

In `GameManager.GetAsync` / `GetListAsync`, after deserialising a `GameListFile`, run a `MigrateGame(GameEntity)` helper:

- If `SyncSourceIdLocations` is `Dictionary<string, List<GamePathEntry>>` (new shape) → no-op.
- If it is `Dictionary<string, string>` (legacy) → convert each value to `[{ Path = value, IncludeFilters = [], ExcludeFilters = [], Enabled = true }]`.
- **No user-visible notice.** The user sees a one-child multi-child entry in the Game Edit screen, which looks identical to today's one-path entry. They can add more directories from there whenever they want.

### 10.2 On-Write Migration (Lazy)

The new shape is persisted the **next time the entry is saved** — `UpdateAsync`, `BulkUpsertAsync`, or `UpdateMetaDataAsync`. There is no startup-time rewrite and no eager read-time write; a save failure leaves legacy data intact.

`GameMetaData.FromGame` always writes `v: 2` plus the new shape. Legacy readers deserialise `v` as an unknown property (ignored) and fail on the new shape because they expect `Dictionary<string, string>`.

### 10.3 Parallel Cloud-Metadata Fields

Cloud metadata uses the parallel-field approach to keep mixed-version agents working:

```json
{
  "id": "abc",
  "sl": { "<syncSourceId>": "C:\\Path\\A" },
  "sl2": { "<syncSourceId>": [ {"path": "C:\\Path\\A", "includeFilters": [], "excludeFilters": [], "enabled": true} ] },
  "v": 2
}
```

- **Legacy readers** ignore `sl2` and `v`, keep using `sl`.
- **New readers** prefer `sl2`, fall back to migrating `sl` on load.
- On first save after upgrade, the new writer populates both fields.
- After the rollout window, the legacy `sl` field is dropped (see §10.4).

### 10.4 Parallel DTO Fields (HTTP API)

The same parallel-field approach is applied to `IGameDto`, `CreateGameDto`, `UpdateGameDto`, `GameDto`, `GameSummaryDto`:

- Legacy: `SyncSourceIdLocations: Dictionary<string, string>?`
- New: `SyncSourceIdLocationsV2: Dictionary<string, List<GamePathEntryDto>>?`

Both fields are populated on write for one release. New readers prefer `V2`, fall back to `SyncSourceIdLocations`. After the rollout window (and once telemetry / support confirms most clients are upgraded), the legacy field is removed.

### 10.5 Archive Migration

Pre-feature game zips have no `<childIndex>/` prefix and no `manifest.json`. The downloader handles this as described in §4.4. No re-upload or repair step is needed; the next upload naturally produces the new layout.

## 11. Tests

### 11.1 Domain

- `EmuSync.Domain.Tests/Objects/GamePathEntryTests.cs` — record equality, defaults.
- `EmuSync.Domain.Tests/Helpers/PathFilterTests.cs` — include-only, exclude-only, both, `**`, anchored patterns, case sensitivity on Linux vs Windows, empty lists.
- `EmuSync.Domain.Tests/Services/LocalDataAccessorTests.cs` — `ScanDirectoriesAsync` aggregates correctly, handles a missing child path.

### 11.2 Managers

- `EmuSync.Services.Managers.Tests/Results/` — `GetSyncTypeResultTests`: empty children → `UnsetDirectory`; one child → mirror today; multi-child aggregation.
- New `EmuSync.Services.Managers.Tests/GameSyncManagerTests.cs`:
  - **Migration**: legacy entry auto-upgrades to multi-child on read.
  - **Upload**: multi-child zip layout (entries prefixed with `0/`, `1/`, …; manifest.json present; filters applied; overlapping paths deduped by source).
  - **Download**: extracts to staging; routes per-entry to correct child; filters skip on download.
  - **Per-child failure**: one child throws → other children still sync; entry status reflects partial failure.
  - **Filter symmetry**: same filter set on upload and download; excluded files stay excluded.
  - **Staging safety**: target child folders are never wiped recursively before extract.

### 11.3 Storage

- `EmuSync.Services.Storage.Tests/Objects/GameMetaDataTests.cs` — round-trip legacy and new shapes; `v: 2` written; legacy key migrated on read.

### 11.4 Agent

- `EmuSync.Agent.Tests/Dto/Game/` — validators reject empty paths, malformed globs.
- `EmuSync.Agent.Tests/Mapping/GameMappingTests.cs` — `ToEntity` / `ToDto` for multi-child, including legacy read.

### 11.5 UI

- Unit tests for `replacePathDelims` over a list of children.
- Component test for `GameForm.tsx`: rendering N child rows, adding/removing, toggling `enabled`, editing filter text.

## 12. Rollout Order

1. **Domain** — `GamePathEntry`, `PathFilter`, `ScanDirectoriesAsync`. No behaviour change yet.
2. **Storage metadata migration** — `GameMetaData` parallel fields (`sl` + `sl2`) + `v: 2`. Reads still work; new writes include both shapes.
3. **Manager sync algorithm** — `GameSyncManager` upload/download/backup updated, with `LocalGameSaveBackupService` combined-zip. `ZipHelper` split into destructive vs non-destructive. Internal testing with synthetic multi-child games.
4. **DTO / Mapping** — `GamePathEntryDto`, parallel `SyncSourceIdLocationsV2` field on `IGameDto` family, validators, `GameSyncStatusDto.Children`, mapping helpers. Both legacy and V2 fields populated on write.
5. **Controller** — endpoints unchanged on the surface; status DTO carries the new children list.
6. **UI** — types gain `syncSourceIdLocationsV2`, `GameForm` multi-child editor (for the Edit screen), `DisplayGameSyncStatus` drill-down. Quick Add UI is unchanged.
7. **Migration cut-over** — flip the read path to prefer `sl2` over `sl` in cloud metadata and `SyncSourceIdLocationsV2` over `SyncSourceIdLocations` in HTTP DTOs. Once a cloud `game-list.json` has been rewritten (lazy, on first save after upgrade for any entry), the legacy `sl` field is dropped from that entry's payload.
8. **Cleanup** — remove `ZipHelper`'s old destructive overload (callers all migrated); remove legacy `sl` read path from cloud metadata; remove legacy `SyncSourceIdLocations` field from `IGameDto` and its consumers.

## 13. File Change List

### New files

- `EmuSync.Domain/Objects/GamePathEntry.cs`
- `EmuSync.Domain/Helpers/PathFilter.cs`
- `EmuSync.Agent/Dto/Game/GamePathEntryDto.cs`
- `EmuSync.Agent/Dto/Game/ChildSyncErrorDto.cs`
- `EmuSync.Agent/Dto/Game/ChildSyncStatusDto.cs`
- `EmuSync.Domain.Tests/Objects/GamePathEntryTests.cs`
- `EmuSync.Domain.Tests/Helpers/PathFilterTests.cs`
- `EmuSync.Services.Managers.Tests/GameSyncManagerTests.cs`

### Modified files

**Domain:**
- `EmuSync.Domain/Entities/GameEntity.cs`
- `EmuSync.Domain/Results/DirectoryScanResult.cs`
- `EmuSync.Domain/Services/Interfaces/ILocalDataAccessor.cs`
- `EmuSync.Domain/Services/LocalDataAccessor.cs`
- `EmuSync.Domain/Helpers/ZipHelper.cs`
- `EmuSync.Domain/Services/LocalGameSaveBackupService.cs`
- `EmuSync.Domain.csproj` — add `Microsoft.Extensions.FileSystemGlobbing` package reference

**Managers:**
- `EmuSync.Services.Managers/GameManager.cs`
- `EmuSync.Services.Managers/GameSyncManager.cs`
- `EmuSync.Services.Managers/Objects/GameBulkUpsert.cs` — unchanged for v1 (kept under "modified" because it lives near the touched code)
- `EmuSync.Services.Managers/Results/GetSyncTypeResult.cs`

**Storage:**
- `EmuSync.Services.Storage/Objects/GameMetaData.cs`

**Agent:**
- `EmuSync.Agent/Dto/Game/IGameDto.cs`
- `EmuSync.Agent/Dto/Game/CreateGameDto.cs`
- `EmuSync.Agent/Dto/Game/UpdateGameDto.cs`
- `EmuSync.Agent/Dto/Game/GameDto.cs`
- `EmuSync.Agent/Dto/Game/GameSummaryDto.cs`
- `EmuSync.Agent/Dto/Game/QuickAddRequestBodyDto.cs` — unchanged for v1 (kept under "modified" because it lives near the touched code)
- `EmuSync.Agent/Dto/GameSync/GameSyncStatusDto.cs`
- `EmuSync.Agent/Mapping/GameMapping.cs`
- `EmuSync.Agent.Tests/Dto/Game/IGameDtoTests.cs` (or equivalent validator test)
- `EmuSync.Agent.Tests/Mapping/GameMappingTests.cs`

**UI:**
- `EmuSync.UI/src/renderer/types/Game.ts`
- `EmuSync.UI/src/renderer/views/game/forms/GameForm.tsx`
- `EmuSync.UI/src/renderer/views/game/components/QuickAddGame.tsx` — unchanged for v1 (kept under "modified" because it lives near the touched code)
- `EmuSync.UI/src/renderer/views/game/components/DisplayGameSyncStatus.tsx`
- `EmuSync.UI/src/renderer/views/game/utils/game-utils.ts`
- `EmuSync.UI/src/renderer/views/game/utils/quick-add-utils.ts` — unchanged for v1 (kept under "modified" because it lives near the touched code)

## 14. Open Follow-Up Items (Non-Blocking)

These items surfaced during the planning pass and are flagged here so they aren't lost. They do not block implementation of v1.

1. **Test framework conventions** — Existing test projects (`EmuSync.Domain.Tests`, `EmuSync.Services.Managers.Tests`, `EmuSync.Agent.Tests`, `EmuSync.Services.Storage.Tests`) need to be reviewed for framework (xUnit vs NUnit), mocking library (Moq vs NSubstitute), assertion style (FluentAssertions, etc.), and AAA structure before the new tests in §11 are written, so the additions match house style.
2. **UI list-editor pattern** — `GameForm.tsx`'s new child-list editor should follow an existing list-editor pattern in the codebase if one exists. The closest candidate is `QuickAddGame.tsx`'s use of `useFieldArray` for the `games` array, but that may not transfer cleanly because each child has its own nested filter fields. To be confirmed when the UI work begins.
3. **Filter UI affordance** — Beyond the plain textareas (one glob per line), consider a small "Show examples" link / popover that displays a few sample patterns (`*.sav`, `mods/**`, `options.txt`, `**/backup-*`). Deferred — easy to add later.
4. **Restore-from-backup UX** — `LocalSyncLogDataGrid` and `RestoreFromBackupModal` continue to show a single list of combined-backup zips; restore overwrites all enabled children at once. With per-child backup zips, the modal stays as-is and no UI change is needed in v1. Worth confirming during UI work that the post-restore re-upload flow still works.
5. **Include filter semantics** — Decision: empty include list = include everything; non-empty include list = include only paths that match at least one pattern. This is a positive allowlist, **not** the `.gitignore` semantics (which is exclude-only). Document this in any user-facing help text.
6. **Filter case sensitivity** — Default to per-OS behavior matching the target filesystem: case-sensitive on Linux/macOS, case-insensitive on Windows. Implementation uses `StringComparison.Ordinal` on Linux/macOS and `StringComparison.OrdinalIgnoreCase` on Windows via `PlatformHelper.GetOsPlatform()`.
