import { CreateGame, Game, GamePathEntry, GameSyncStatus, SyncSourceSummary, UpdateGame } from "@/renderer/types";
import { OsPlatform } from "@/renderer/types/enums";
import { normalisePathDelims } from "@/renderer/utils/path-utils";

export const defaultUpdateGame: UpdateGame = {
    id: "",
    name: "",
    autoSync: false,
    syncSourceIdLocations: null,
    syncSourceIdLocationsV2: null,
    maximumLocalGameBackups: null
};

export const defaultCreateGame: CreateGame = {
    name: "",
    autoSync: false,
    syncSourceIdLocations: null,
    syncSourceIdLocationsV2: null,
    maximumLocalGameBackups: null
};

export function transformUpdateGame(game: Game): UpdateGame {
    return {
        id: game.id,
        autoSync: game.autoSync,
        syncSourceIdLocations: game.syncSourceIdLocations,
        syncSourceIdLocationsV2: cloneLocations(game.syncSourceIdLocationsV2)
            ?? migrateLegacyLocations(game.syncSourceIdLocations),
        name: game.name,
        maximumLocalGameBackups: game.maximumLocalGameBackups
    }
}

export function transformCreateGame(): CreateGame {
    return { ...defaultCreateGame }
}

export function determineGameSyncStatus(gameSyncStatus: GameSyncStatus) {

    const neverSynced = !(gameSyncStatus.lastSyncedFrom);
    const { requiresDownload, requiresUpload } = gameSyncStatus;
    const isUpToDate = !neverSynced && !requiresDownload && !requiresUpload;
    const localPathIsUnset = gameSyncStatus.localFolderPathIsUnset;
    const localPathExists = gameSyncStatus.localFolderPathExists;

    return {
        neverSynced,
        requiresDownload,
        requiresUpload,
        isUpToDate,
        localPathIsUnset,
        localPathExists
    }

}

export function replacePathDelims(syncSources: SyncSourceSummary[], game: UpdateGame | CreateGame) {
    if (!game.syncSourceIdLocationsV2) return game;

    const updated: Record<string, GamePathEntry[]> = {};

    for (const [id, entries] of Object.entries(game.syncSourceIdLocationsV2)) {

        const syncSource = syncSources.find(s => s.id === id);

        if (!syncSource) {
            continue;
        }

        const isWindows = syncSource.platformId === OsPlatform.Windows;
        updated[id] = entries.map(entry => ({
            ...entry,
            path: normalisePathDelims(entry.path, isWindows)
        }));
    }

    return {
        ...game,
        syncSourceIdLocationsV2: updated,
        syncSourceIdLocations: Object.fromEntries(
            Object.entries(updated)
                .filter(([, entries]) => entries.length > 0)
                .map(([id, entries]) => [id, entries[0].path])
        )
    };
}

function migrateLegacyLocations(locations?: Record<string, string> | null) {
    if (!locations) return null;
    return Object.fromEntries(Object.entries(locations).map(([id, path]) => [id, [{
        path,
        includeFilters: [],
        excludeFilters: [],
        enabled: true
    }]]));
}

function cloneLocations(locations?: Record<string, GamePathEntry[]> | null) {
    if (!locations) return null;
    return Object.fromEntries(Object.entries(locations).map(([id, entries]) => [id,
        entries.map(entry => ({
            ...entry,
            includeFilters: [...(entry.includeFilters ?? [])],
            excludeFilters: [...(entry.excludeFilters ?? [])]
        }))
    ]));
}

export function parseFilterText(value: string) {
    return value.split(/\r?\n/)
        .map(line => line.trim())
        .filter(line => line.length > 0 && !line.startsWith("#"));
}
