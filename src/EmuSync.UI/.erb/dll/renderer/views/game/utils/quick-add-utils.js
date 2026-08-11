"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.filePathIsUnchanged = exports.convertToRequestBody = exports.getDefaultValues = void 0;
const path_utils_1 = require("@/renderer/utils/path-utils");
function getDefaultValues() {
    return {
        games: []
    };
}
exports.getDefaultValues = getDefaultValues;
function convertToRequestBody(form, isWindows) {
    const output = {
        games: form.games.map(game => {
            const gameExists = game.existingGame !== null;
            return {
                existingGameId: game.existingGame?.id ?? null,
                path: (0, path_utils_1.normalisePathDelims)(game.path, isWindows),
                gameName: gameExists ? null : game.name,
                autoSync: game.autoSync,
                maximumLocalGameBackups: game.maxLocalBackups?.toString() === "" ? null : game.maxLocalBackups
            };
        })
    };
    return output;
}
exports.convertToRequestBody = convertToRequestBody;
function filePathIsUnchanged(existingGame, gamePath, syncSource) {
    return existingGame?.syncSourceIdLocations?.[syncSource.id] === gamePath;
}
exports.filePathIsUnchanged = filePathIsUnchanged;
//# sourceMappingURL=quick-add-utils.js.map