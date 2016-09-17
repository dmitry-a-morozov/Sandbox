/// <reference path="typings/main.d.ts" />

import * as React from 'react'

export default class Counter extends React.Component<{ value: number, onIncrement: () => void, onDecrement: () => void }, {}> {
    constructor(props) {
        super(props)
        //this.incrementAsync = this.incrementAsync.bind(this)
        //this.incrementIfOdd = this.incrementIfOdd.bind(this)
    }

    incrementIfOdd = () => {
        if (this.props.value % 2 !== 0) {
            this.props.onIncrement()
        }
    }

    incrementAsync = () => {
        setTimeout(this.props.onIncrement, 1000)
    }

    render() {
        const { value, onIncrement, onDecrement } = this.props
        return (
            <p>
                Clicked: {this.props.value} times
                {' '}
        <button onClick={this.props.onIncrement}>
            +
            </button>
        {' '}
        <button onClick={this.props.onDecrement}>
            -
            </button>
        {' '}
        <button onClick={this.incrementIfOdd}>
            Increment if odd
            </button>
        {' '}
        <button onClick={this.incrementAsync}>
            Increment async
            </button>
                </p>
        )
    }
}

