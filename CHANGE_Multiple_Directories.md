# Change Request: One Game / Entry, Multiple Directories

## Overview

Allow a single game entry in EmuSync to reference and track multiple source directories, rather than requiring a separate entry per directory. This reduces UI clutter and enables more flexible backup/sync setups for games and applications whose saves or configuration data are spread across multiple locations.

## Background

Today, when a title's relevant data lives in more than one folder (e.g. Steam cloud-synced saves in one directory and local-profile saves in another), the user must create multiple entries with the same name, each pointing at a single directory. This quickly becomes noisy, especially for projects like Flashpoint Ultimate where many web-runtime save folders each appear as their own top-level entry.

Users also need the ability to trim what is included per directory, so that large or machine-specific subtrees (mods, local backups, per-machine options, etc.) are excluded from sync.

## Goals

- Allow a single game entry to own and manage multiple child directories.
- Make multiple-directory management a first-class concept in the EmuSync GUI (not just an underlying file format detail).
- Allow per-directory include/exclude filtering using glob-style patterns.
- Continue to support the existing single-directory flow without breaking existing entries or save data.

## Non-Goals

- Combining the contents of multiple directories into a single archive file.
- Requiring all child directories of an entry to be checked/synced at exactly the same time.
- Replacing or rewriting the Ludusavi Manifest system; entries on the manifest continue to be the source of truth for default paths, this change only extends what an entry can do.

## Proposed Behavior

### Data Model

A game entry, in addition to its existing fields, gains an ordered list of one or more child directory records:

- `Display Name` (inherited from the parent entry).
- `Child Directories` (new): a list of child records. For backward compatibility, existing entries are migrated to contain exactly one child that matches today's single directory.
  - `Path` — absolute path to the directory on disk.
  - `Include Filters` (optional) — glob patterns; only matching files/paths are considered.
  - `Exclude Filters` (optional) — glob patterns; matching files/paths are skipped.
  - `Enabled` — per-child on/off toggle so individual child directories can be disabled without deleting them.

### GUI

- The game list/entry view shows the entry name once, with its child directories listed beneath it as expandable children (tree-style) so the user can see at a glance which directories belong to which game.
- Adding, editing, removing, reordering, and enabling/disabling a child directory is done through the entry editor.
- Each child row exposes its path and a summary of active filters; opening the row reveals the full include/exclude lists.
- Bulk operations (sync, check, upload, download) operate on all enabled child directories of an entry. Disabled children are skipped but their configuration is preserved.

### Filtering Semantics

- `Include Filters`: when non-empty, only files/paths matching at least one include pattern are eligible for sync. When empty, the child is unfiltered by include.
- `Exclude Filters`: applied after includes. Anything matching an exclude pattern is skipped, even if it matched an include.
- Pattern syntax follows familiar glob conventions (the same syntax used by `.gitignore` and FreeFileSync), supporting `*`, `?`, `**`, character classes, and directory-anchored patterns. Patterns are evaluated relative to the child directory's root.
- Filters are evaluated client-side; when uploading, only the included-and-not-excluded subset is packed/sent. When downloading, the symmetric apply ensures files excluded from upload are not overwritten on the remote.

### Sync Semantics

- Each enabled child directory is processed independently. A child directory can succeed while another fails; the entry's overall status aggregates per-child results.
- Child directories do not need to be packed into the same archive; the underlying archive/transfer format remains per-directory unless future work changes that.
- Timestamps, hashing, and conflict detection continue to work per-directory, just as they do today.

### Backward Compatibility

- Existing entries are loaded as single-child entries on first run after upgrade.
- The on-disk representation of a single-child entry should remain readable by older builds if practical; if a format change is required, it must include a one-way migration that preserves all existing data and settings.
- The Ludusavi Manifest continues to provide default paths; the multi-directory capability is additive.

## Use Cases

1. **Steam + Local Saves**: A single "Terraria" entry has one child for the Steam cloud save directory and a second child for the local profile save directory.
2. **Flashpoint Ultimate**: A single "Flashpoint" entry has one child per web runtime that stores saves, replacing a long flat list of `(Flashpoint) <runtime>` duplicates.
3. **Modded Minecraft**: A single entry for a modded instance points at the world's directory only, with include/exclude filters keeping `mods/`, `local/`, `backups/`, and `options.txt` out of sync, while still backing up per-world data and shared config that should roam.
4. **Per-Machine Adjustments**: Users with complex layouts can tune each child independently (filters, enabled state, path) without polluting other entries.

## Acceptance Criteria

- A user can add a new game entry with multiple child directories through the GUI.
- A user can convert (or migrate) an existing single-directory entry into a multi-child entry without losing data.
- An entry can be expanded in the GUI to show, edit, add, remove, reorder, and enable/disable its child directories.
- Per-child include and exclude glob filters work for both upload (local -> remote) and download (remote -> local) operations.
- Filters use glob syntax compatible with `.gitignore` / FreeFileSync.
- Bulk operations (check, sync, upload, download) process all enabled children and report per-child results that roll up into a single entry-level status.
- The feature does not require any change to the Ludusavi Manifest format itself.
- Existing entries and save data remain functional across the upgrade.

## Open Questions

- Should there be a limit on the number of child directories per entry? If so, what is the limit and is it configurable?
- Should child directories be allowed to share a path prefix, or to overlap (one being a parent of another)? If overlapping is allowed, what are the merge/priority rules?
- Should per-child filters also apply when downloading, or only on upload? (Proposed: both, for symmetry.)
- How should per-child failures surface in the UI — as a single entry-level warning with a drill-down, or as separate status rows in the entry list?
- Is there a need for a "sync children together / independently" mode at the entry level, or is independent processing always preferred?

## Related

- Feature Request: "one game / entry, multiple directories" (#20)
- Feature Request: "Please add a way to exclude specific files in a folder" (#25) — overlapping concern; the per-child exclude filter covers the use case raised there for this feature.
