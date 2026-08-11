"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.getLocalSyncLogsForGame = exports.getAllLocalSyncLogs = void 0;
const api_helper_1 = require("@/renderer/api/api-helper");
const controller = "LocalSyncLog";
async function getAllLocalSyncLogs() {
    const path = `${controller}`;
    return await (0, api_helper_1.get)({
        path
    });
}
exports.getAllLocalSyncLogs = getAllLocalSyncLogs;
async function getLocalSyncLogsForGame(gameId) {
    const path = `${controller}/Game/${gameId}`;
    return await (0, api_helper_1.get)({
        path
    });
}
exports.getLocalSyncLogsForGame = getLocalSyncLogsForGame;
//# sourceMappingURL=local-sync-log-api.js.map