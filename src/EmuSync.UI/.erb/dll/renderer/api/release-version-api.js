"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.getLatestReleaseVersion = void 0;
async function getLatestReleaseVersion() {
    const url = "https://api.github.com/repos/emu-sync/EmuSync/releases/latest";
    const response = await fetch(url);
    if (!response.ok) {
        return "";
    }
    const json = await response.json();
    return json.tag_name.replace("v", "");
}
exports.getLatestReleaseVersion = getLatestReleaseVersion;
//# sourceMappingURL=release-version-api.js.map