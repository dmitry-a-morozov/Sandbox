import * as React from 'react'
import { connect } from 'react-redux'
import { addTodo } from '../actions/index'

class AddTodo extends React.Component<{ dispatch: any }, { input: any}> {
    constructor(props) {
        super(props)
        this.state = { input: null };
    }

    render() {
        return (
            <div>
            <input ref={ node => { this.setState({ input: node }); } } />
            <button onClick={ () => { this.props.dispatch(addTodo(input.value))   
                    input.value = ''
                }
        }>
            Add Todo
                < /button>
                < /div>
  )
    }
}

AddTodo = connect()(AddTodo)

export default AddTodo
