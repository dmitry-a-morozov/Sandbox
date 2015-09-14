#r @"bin\Debug\NonSealedGenTypes.dll"

type Root = NonSealedGenTypes.RootProvider<"One,Two">

type MyOne() =
    inherit Root.One("one")

    override this.GetData(id) = 
        async.Return(sprintf "Greeting: %s. Id - %i" this.Greeeting id)

let myone = MyOne()
myone.Greeeting
myone.GetData(42) |> Async.RunSynchronously

let getNew name = {
    new Root.Two(name) with
        member this.GetData(id) = 
            async.Return(sprintf "Special message from Data for %s. Id - %i" this.Greeeting id)
}

let two = getNew "two"
two.Greeeting
two.GetData -1 |> Async.RunSynchronously

