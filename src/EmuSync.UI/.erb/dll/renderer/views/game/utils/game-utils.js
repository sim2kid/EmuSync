"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.parseFilterText = exports.replacePathDelims = exports.determineGameSyncStatus = exports.transformCreateGame = exports.transformUpdateGame = exports.defaultCreateGame = exports.defaultUpdateGame = void 0;
const enums_1 = require("@/renderer/types/enums");
const path_utils_1 = require("@/renderer/utils/path-utils");
exports.defaultUpdateGame = {
    id: "",
    name: "",
    autoSync: false,
    syncSourceIdLocations: null,
    syncSourceIdLocationsV2: null,
    maximumLocalGameBackups: null
};
exports.defaultCreateGame = {
    name: "",
    autoSync: false,
    syncSourceIdLocations: null,
    syncSourceIdLocationsV2: null,
    maximumLocalGameBackups: null
};
function transformUpdateGame(game) {
    return {
        id: game.id,
        autoSync: game.autoSync,
        syncSourceIdLocations: game.syncSourceIdLocations,
        syncSourceIdLocationsV2: cloneLocations(game.syncSourceIdLocationsV2)
            ?? migrateLegacyLocations(game.syncSourceIdLocations),
        name: game.name,
        maximumLocalGameBackups: game.maximumLocalGameBackups
    };
}
exports.transformUpdateGame = transformUpdateGame;
function transformCreateGame() {
    return { ...exports.defaultCreateGame };
}
exports.transformCreateGame = transformCreateGame;
function determineGameSyncStatus(gameSyncStatus) {
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
    };
}
exports.determineGameSyncStatus = determineGameSyncStatus;
function replacePathDelims(syncSources, game) {
    if (!game.syncSourceIdLocationsV2)
        return game;
    const updated = {};
    for (const [id, entries] of Object.entries(game.syncSourceIdLocationsV2)) {
        const syncSource = syncSources.find(s => s.id === id);
        if (!syncSource) {
            continue;
        }
        const isWindows = syncSource.platformId === enums_1.OsPlatform.Windows;
        updated[id] = entries.map(entry => ({
            ...entry,
            path: (0, path_utils_1.normalisePathDelims)(entry.path, isWindows)
        }));
    }
    return {
        ...game,
        syncSourceIdLocationsV2: updated,
        syncSourceIdLocations: Object.fromEntries(Object.entries(updated)
            .filter(([, entries]) => entries.length > 0)
            .map(([id, entries]) => [id, entries[0].path]))
    };
}
exports.replacePathDelims = replacePathDelims;
function migrateLegacyLocations(locations) {
    if (!locations)
        return null;
    return Object.fromEntries(Object.entries(locations).map(([id, path]) => [id, [{
                path,
                includeFilters: [],
                excludeFilters: [],
                enabled: true
            }]]));
}
function cloneLocations(locations) {
    if (!locations)
        return null;
    return Object.fromEntries(Object.entries(locations).map(([id, entries]) => [id,
        entries.map(entry => ({
            ...entry,
            includeFilters: [...(entry.includeFilters ?? [])],
            excludeFilters: [...(entry.excludeFilters ?? [])]
        }))
    ]));
}
function parseFilterText(value) {
    return value.split(/\r?\n/)
        .map(line => line.trim())
        .filter(line => line.length > 0 && !line.startsWith("#"));
}
exports.parseFilterText = parseFilterText;
//# sourceMappingURL=game-utils.js.map