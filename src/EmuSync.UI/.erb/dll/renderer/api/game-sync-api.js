"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.getSyncProgress = exports.deleteBackup = exports.restoreGameFromBackup = exports.forceUploadGame = exports.forceDownloadGame = exports.syncGame = exports.getGameSyncStatus = void 0;
const api_helper_1 = require("@/renderer/api/api-helper");
const controller = "GameSync";
async function getGameSyncStatus(id) {
    const path = `${controller}/${id}`;
    return await (0, api_helper_1.get)({
        path
    });
}
exports.getGameSyncStatus = getGameSyncStatus;
async function syncGame(id) {
    const path = `${controller}/${id}`;
    await (0, api_helper_1.postWithNoResponse)({
        path
    });
}
exports.syncGame = syncGame;
async function forceDownloadGame(id) {
    const path = `${controller}/${id}/ForceDownload`;
    await (0, api_helper_1.postWithNoResponse)({
        path
    });
}
exports.forceDownloadGame = forceDownloadGame;
async function forceUploadGame(id) {
    const path = `${controller}/${id}/ForceUpload`;
    await (0, api_helper_1.postWithNoResponse)({
        path
    });
}
exports.forceUploadGame = forceUploadGame;
async function restoreGameFromBackup(id, backupId) {
    const path = `${controller}/${id}/RestoreFromBackup/${backupId}`;
    await (0, api_helper_1.postWithNoResponse)({
        path
    });
}
exports.restoreGameFromBackup = restoreGameFromBackup;
async function deleteBackup(id, backupId) {
    const path = `${controller}/${id}/Backup/${backupId}`;
    await (0, api_helper_1.remove)({
        path
    });
}
exports.deleteBackup = deleteBackup;
async function getSyncProgress(id) {
    const path = `${controller}/${id}/SyncProgress`;
    return await (0, api_helper_1.get)({
        path
    });
}
exports.getSyncProgress = getSyncProgress;
//# sourceMappingURL=game-sync-api.js.map