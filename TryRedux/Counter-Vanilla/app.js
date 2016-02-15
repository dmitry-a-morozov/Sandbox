/// <reference path="typings/main.d.ts" />
(function (factory) {
    if (typeof module === 'object' && typeof module.exports === 'object') {
        var v = factory(require, exports); if (v !== undefined) module.exports = v;
    }
    else if (typeof define === 'function' && define.amd) {
        define(["require", "exports", 'react', 'react-dom', 'redux'], factory);
    }
})(function (require, exports) {
    var React = require('react');
    var ReactDOM = require('react-dom');
    var Redux = require('redux');
    //import { Counter } from 'counter'
    function counter(state, action) {
        if (typeof state === 'undefined') {
            return 0;
        }
        switch (action.type) {
            case 'INCREMENT':
                return state + 1;
            case 'DECREMENT':
                return state - 1;
            default:
                return state;
        }
    }
    var store = Redux.createStore(counter);
    var valueEl = document.getElementById('value');
    function render() {
        ReactDOM.render(React.DOM.text(42), valueEl);
        //valueEl.innerHTML = store.getState().toString()
    }
    render();
    store.subscribe(render);
    document.getElementById('increment')
        .addEventListener('click', function () {
        store.dispatch({ type: 'INCREMENT' });
    });
    document.getElementById('decrement')
        .addEventListener('click', function () {
        store.dispatch({ type: 'DECREMENT' });
    });
    document.getElementById('incrementIfOdd')
        .addEventListener('click', function () {
        if (store.getState() % 2 !== 0) {
            store.dispatch({ type: 'INCREMENT' });
        }
    });
    document.getElementById('incrementAsync')
        .addEventListener('click', function () {
        setTimeout(function () {
            store.dispatch({ type: 'INCREMENT' });
        }, 1000);
    });
});
//# sourceMappingURL=app.js.map