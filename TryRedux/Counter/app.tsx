/// <reference path="typings/main.d.ts" />

import * as React from 'react'
import * as ReactDOM from 'react-dom'
import { createStore } from 'redux'
import Counter from 'Counter'
import counter from 'reducers'

const store = createStore(counter)
var rootEl = document.getElementById('root')

function render() {
    ReactDOM.render(
        <Counter
            value={store.getState() }
            onIncrement={() => store.dispatch({ type: 'INCREMENT' }) }
            onDecrement={() => store.dispatch({ type: 'DECREMENT' }) }
            />,
        rootEl
    )
}

render()
store.subscribe(render)
