/// <reference path="typings/main.d.ts" />
(function (factory) {
    if (typeof module === 'object' && typeof module.exports === 'object') {
        var v = factory(require, exports); if (v !== undefined) module.exports = v;
    }
    else if (typeof define === 'function' && define.amd) {
        define(["require", "exports", 'react', 'react-dom', 'redux', 'Counter', 'reducers'], factory);
    }
})(function (require, exports) {
    var React = require('react');
    var ReactDOM = require('react-dom');
    var redux_1 = require('redux');
    var Counter_1 = require('Counter');
    var reducers_1 = require('reducers');
    var store = redux_1.createStore(reducers_1.default);
    var rootEl = document.getElementById('root');
    function render() {
        ReactDOM.render(React.createElement(Counter_1.default, {"value": store.getState(), "onIncrement": function () { return store.dispatch({ type: 'INCREMENT' }); }, "onDecrement": function () { return store.dispatch({ type: 'DECREMENT' }); }}), rootEl);
    }
    render();
    store.subscribe(render);
});
//# sourceMappingURL=app.js.map