"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
const jsx_runtime_1 = require("react/jsx-runtime");
const material_1 = require("@mui/material");
function ButtonSkeleton({ width }) {
    return (0, jsx_runtime_1.jsx)(material_1.Skeleton, { variant: "rounded", width: width, height: 37, sx: {
            borderRadius: 50
        } });
}
exports.default = ButtonSkeleton;
//# sourceMappingURL=ButtonSkeleton.js.map