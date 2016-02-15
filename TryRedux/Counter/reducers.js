(function (factory) {
    if (typeof module === 'object' && typeof module.exports === 'object') {
        var v = factory(require, exports); if (v !== undefined) module.exports = v;
    }
    else if (typeof define === 'function' && define.amd) {
        define(["require", "exports"], factory);
    }
})(function (require, exports) {
    function counter(state, action) {
        if (state === void 0) { state = 0; }
        switch (action.type) {
            case 'INCREMENT':
                return state + 1;
            case 'DECREMENT':
                return state - 1;
            default:
                return state;
        }
    }
    Object.defineProperty(exports, "__esModule", { value: true });
    exports.default = counter;
});
//# sourceMappingURL=reducers.js.map