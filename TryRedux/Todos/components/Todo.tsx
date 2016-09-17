import * as React from 'react'

export default class Todo extends React.Component<{onClick, completed, text}, {}> {
    render() {
        return (
            <li
                onClick={this.props.onClick}
                style={{
                    textDecoration: completed ? 'line-through' : 'none'
                }}
                >
    {text}
                </li>
        )
    }

