"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.checkApiIsRunning = void 0;
const api_helper_1 = require("@/renderer/api/api-helper");
const controller = "System";
async function checkApiIsRunning() {
    const path = `${controller}/HealthCheck`;
    await (0, api_helper_1.postWithNoResponse)({
        path
    });
    return true;
}
exports.checkApiIsRunning = checkApiIsRunning;
//# sourceMappingURL=system-info-api.js.map