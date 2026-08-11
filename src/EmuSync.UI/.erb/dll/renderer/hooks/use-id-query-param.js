"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
const react_router_dom_1 = require("react-router-dom");
function useIdParam() {
    const location = (0, react_router_dom_1.useLocation)();
    const query = new URLSearchParams(location.search);
    const id = query.get("id"); // string | null
    return id;
}
exports.default = useIdParam;
//# sourceMappingURL=use-id-query-param.js.map