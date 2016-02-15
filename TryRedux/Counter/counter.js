/// <reference path="typings/main.d.ts" />
var __extends = (this && this.__extends) || function (d, b) {
    for (var p in b) if (b.hasOwnProperty(p)) d[p] = b[p];
    function __() { this.constructor = d; }
    d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
};
(function (factory) {
    if (typeof module === 'object' && typeof module.exports === 'object') {
        var v = factory(require, exports); if (v !== undefined) module.exports = v;
    }
    else if (typeof define === 'function' && define.amd) {
        define(["require", "exports", 'react'], factory);
    }
})(function (require, exports) {
    var React = require('react');
    var Counter = (function (_super) {
        __extends(Counter, _super);
        function Counter(props) {
            _super.call(this, props);
            this.incrementAsync = this.incrementAsync.bind(this);
            this.incrementIfOdd = this.incrementIfOdd.bind(this);
        }
        Counter.prototype.incrementIfOdd = function () {
            if (this.props.value % 2 !== 0) {
                this.props.onIncrement();
            }
        };
        Counter.prototype.incrementAsync = function () {
            setTimeout(this.props.onIncrement, 1000);
        };
        Counter.prototype.render = function () {
            var _a = this.props, value = _a.value, onIncrement = _a.onIncrement, onDecrement = _a.onDecrement;
            return (React.createElement("p", null, "Clicked: ", this.props.value, " times", ' ', React.createElement("button", {"onClick": this.props.onIncrement}, "+"), ' ', React.createElement("button", {"onClick": this.props.onDecrement}, "-"), ' ', React.createElement("button", {"onClick": this.incrementIfOdd}, "Increment if odd"), ' ', React.createElement("button", {"onClick": this.incrementAsync}, "Increment async")));
        };
        return Counter;
    })(React.Component);
    Object.defineProperty(exports, "__esModule", { value: true });
    exports.default = Counter;
});
//# sourceMappingURL=Counter.js.map