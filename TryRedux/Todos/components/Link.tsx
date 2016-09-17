import * as React from 'react'

export default class Link extends React.Component<{active: any, children: any, onClick: any}, {}> {
    render() {
        if (this.props.active) {
            return <span>{this.props.children}</span>
        }

        return (
            <a href="#"
                onClick={e => {
                    e.preventDefault()
                    this.props.onClick()
                } }
                >
          {this.props.children}
                </a>
        )
    }
}

