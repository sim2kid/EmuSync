"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.normalisePathDelims = void 0;
function normalisePathDelims(path, isWindows) {
    return isWindows
        ? path.replace(/\//g, "\\") //normalise → Windows
        : path.replace(/\\/g, "/"); //normalise → mac + linux
}
exports.normalisePathDelims = normalisePathDelims;
//# sourceMappingURL=path-utils.js.map