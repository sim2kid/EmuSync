"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.quickAddGames = exports.deleteGame = exports.updateGame = exports.clearGameCache = exports.createGame = exports.getGameById = exports.getGameBackups = exports.getGameSuggestionsList = exports.getGameList = void 0;
const api_helper_1 = require("@/renderer/api/api-helper");
const controller = "Game";
async function getGameList() {
    const path = `${controller}`;
    return await (0, api_helper_1.get)({
        path
    });
}
exports.getGameList = getGameList;
async function getGameSuggestionsList() {
    const path = `${controller}/Suggestions`;
    return await (0, api_helper_1.get)({
        path
    });
}
exports.getGameSuggestionsList = getGameSuggestionsList;
async function getGameBackups(id) {
    const path = `${controller}/${id}/Backups`;
    return await (0, api_helper_1.get)({
        path
    });
}
exports.getGameBackups = getGameBackups;
async function getGameById(id) {
    const path = `${controller}/${id}`;
    return await (0, api_helper_1.get)({
        path
    });
}
exports.getGameById = getGameById;
async function createGame(body) {
    const path = `${controller}`;
    return await (0, api_helper_1.post)({
        path,
        body
    });
}
exports.createGame = createGame;
async function clearGameCache() {
    const path = `${controller}/ClearCache`;
    return await (0, api_helper_1.postWithNoResponse)({
        path,
    });
}
exports.clearGameCache = clearGameCache;
async function updateGame(body) {
    const path = `${controller}/${body.id}`;
    await (0, api_helper_1.put)({
        path,
        body
    });
}
exports.updateGame = updateGame;
async function deleteGame(id) {
    const path = `${controller}/${id}`;
    await (0, api_helper_1.remove)({
        path
    });
}
exports.deleteGame = deleteGame;
async function quickAddGames(body) {
    const path = `${controller}/QuickAdd`;
    return await (0, api_helper_1.postWithNoResponse)({
        path,
        body
    });
}
exports.quickAddGames = quickAddGames;
//# sourceMappingURL=game-api.js.map