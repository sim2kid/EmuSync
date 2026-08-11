"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.completeSharedFolderSetup = exports.getMicrosoftAuthUrl = exports.getGoogleAuthUrl = exports.getDropboxAuthUrl = void 0;
const api_helper_1 = require("@/renderer/api/api-helper");
const controller = "Auth";
async function getDropboxAuthUrl() {
    const path = `${controller}/Dropbox/AuthUrl`;
    return await (0, api_helper_1.get)({
        path
    });
}
exports.getDropboxAuthUrl = getDropboxAuthUrl;
async function getGoogleAuthUrl() {
    const path = `${controller}/Google/AuthUrl`;
    return await (0, api_helper_1.get)({
        path
    });
}
exports.getGoogleAuthUrl = getGoogleAuthUrl;
async function getMicrosoftAuthUrl() {
    const path = `${controller}/Microsoft/AuthUrl`;
    return await (0, api_helper_1.get)({
        path
    });
}
exports.getMicrosoftAuthUrl = getMicrosoftAuthUrl;
async function completeSharedFolderSetup(body) {
    const path = `${controller}/SharedFolder/AuthFinish`;
    await (0, api_helper_1.postWithNoResponse)({
        path,
        body
    });
}
exports.completeSharedFolderSetup = completeSharedFolderSetup;
//# sourceMappingURL=auth-api.js.map