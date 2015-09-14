#r @"bin\Debug\NonSealedGenTypes.dll"

type Root = NonSealedGenTypes.RootProvider<"One,Two">

type MyRoot(x) =
    inherit Root(x)

    override __.GetData(id) = 
        async.Return(upcast [ "Hello"; "world" ])

type MyOne() =
    inherit Root.One()


let root = MyRoot(112)
root.X
root.GetData(42) |> Async.RunSynchronously

let myone = MyOne()
myone.Greeeting