
type MyClass(name: string) = 
    
    let mutable age = 0

    member this.Name with get() = name
    member this.age with get() = age and set value = age <- value

[<EntryPoint>]
let main argv = 
    printfn "%A" argv
    0 // return an integer exit code
