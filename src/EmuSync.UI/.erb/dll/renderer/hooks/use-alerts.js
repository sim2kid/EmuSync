"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
const notistack_1 = require("notistack");
//custom hook to make alerts easier
function useAlerts() {
    const { enqueueSnackbar, closeSnackbar } = (0, notistack_1.useSnackbar)();
    const functionExports = {
        errorAlert: (msg) => {
            enqueueSnackbar(msg, { variant: "error" });
        },
        successAlert: (msg) => {
            enqueueSnackbar(msg, { variant: "success" });
        },
        warningAlert: (msg) => {
            enqueueSnackbar(msg, { variant: "warning" });
        },
        infoAlert: (msg) => {
            enqueueSnackbar(msg, { variant: "info" });
        },
    };
    return functionExports;
}
exports.default = useAlerts;
//# sourceMappingURL=use-alerts.js.map