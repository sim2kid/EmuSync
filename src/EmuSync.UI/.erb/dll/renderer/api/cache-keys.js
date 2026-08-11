"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.buildCacheKey = exports.cacheKeys = void 0;
exports.cacheKeys = {
    localSyncSource: "localSyncSource",
    allSyncSources: "allSyncSources",
    game: (id) => {
        return buildCacheKey("game", id);
    },
    gameSyncStatus: (id) => {
        return buildCacheKey("gameSyncStatus", id);
    },
    gameBackups: (id) => {
        return buildCacheKey("gameBackups", id);
    },
    gameLocalSyncLogsList: "gameLocalSyncLogs",
    gameLocalSyncLogs: (id) => {
        return buildCacheKey("gameLocalSyncLogs", id);
    },
    gameSyncProgres: "gameSyncProgres",
    gameList: "gameList",
    gameSuggestionList: "gameSuggestionList",
    healthCheck: "healthCheck",
    latestRelease: "latestRelease",
    agentSystemInfo: "agentSystemInfo",
    changeLog: "changeLog",
    news: "news",
    nextAutoSyncTime: "nextAutoSyncTime",
    gameScanDetails: "gameScanDetails",
};
function buildCacheKey(key, additionalPart) {
    return `${key}-${additionalPart}`;
}
exports.buildCacheKey = buildCacheKey;
//# sourceMappingURL=cache-keys.js.map