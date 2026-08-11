"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.deleteSyncSource = exports.unlinkStorageProvider = exports.setLocalStorageProvider = exports.updateLocalSyncSource = exports.forceGameScan = exports.getGameScanDetails = exports.getNextAutoSyncTime = exports.getLocalSyncSource = exports.getSyncSourceList = void 0;
const api_helper_1 = require("@/renderer/api/api-helper");
const controller = "SyncSource";
async function getSyncSourceList() {
    const path = `${controller}`;
    return await (0, api_helper_1.get)({
        path
    });
}
exports.getSyncSourceList = getSyncSourceList;
async function getLocalSyncSource() {
    const path = `${controller}/Local`;
    return await (0, api_helper_1.get)({
        path
    });
}
exports.getLocalSyncSource = getLocalSyncSource;
async function getNextAutoSyncTime() {
    const path = `${controller}/NextAutoSyncTime`;
    return await (0, api_helper_1.get)({
        path
    });
}
exports.getNextAutoSyncTime = getNextAutoSyncTime;
async function getGameScanDetails() {
    const path = `${controller}/GameScanDetails`;
    return await (0, api_helper_1.get)({
        path
    });
}
exports.getGameScanDetails = getGameScanDetails;
async function forceGameScan() {
    const path = `${controller}/ForceGameScan`;
    return await (0, api_helper_1.postWithNoResponse)({
        path
    });
}
exports.forceGameScan = forceGameScan;
async function updateLocalSyncSource(body) {
    const path = `${controller}/Local`;
    await (0, api_helper_1.put)({
        path,
        body
    });
}
exports.updateLocalSyncSource = updateLocalSyncSource;
async function setLocalStorageProvider(body) {
    const path = `${controller}/Local/StorageProvider`;
    await (0, api_helper_1.postWithNoResponse)({
        path,
        body
    });
}
exports.setLocalStorageProvider = setLocalStorageProvider;
async function unlinkStorageProvider(force) {
    const path = `${controller}/Local/StorageProvider`;
    const query = {
        force
    };
    await (0, api_helper_1.remove)({
        path,
        query
    });
}
exports.unlinkStorageProvider = unlinkStorageProvider;
async function deleteSyncSource(id) {
    const path = `${controller}/${id}`;
    await (0, api_helper_1.remove)({
        path
    });
}
exports.deleteSyncSource = deleteSyncSource;
//# sourceMappingURL=sync-source-api.js.map