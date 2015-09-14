#r @"bin\Debug\NonSealedGenTypes.dll"

type Root = NonSealedGenTypes.RootProvider<"One,Two">

type MyOne() =
    inherit Root.One("one")

    override this.GetData(id) = 
        async.Return(sprintf "Data for %s. Id - %i" this.Greeeting id)

let myone = MyOne()
myone.Greeeting
myone.GetData(42) |> Async.RunSynchronously

let two = {
    new Root.Two("tow") with
        member this.GetData(id) = 
            async.Return(sprintf "Data for %s. Id - %i" this.Greeeting id)
}

