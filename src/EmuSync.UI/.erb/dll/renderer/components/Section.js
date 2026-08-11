"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
const jsx_runtime_1 = require("react/jsx-runtime");
const VerticalStack_1 = __importDefault(require("@/renderer/components/stacks/VerticalStack"));
const material_1 = require("@mui/material");
function Section({ children }) {
    return (0, jsx_runtime_1.jsx)(material_1.Paper, { elevation: 2, sx: {
            p: 2
        }, children: (0, jsx_runtime_1.jsx)(VerticalStack_1.default, { children: children }) });
}
exports.default = Section;
//# sourceMappingURL=Section.js.map