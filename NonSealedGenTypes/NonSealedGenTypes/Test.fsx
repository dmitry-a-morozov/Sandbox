#r @"bin\Debug\NonSealedGenTypes.dll"

type Root = NonSealedGenTypes.RootProvider<"One,Two">

let root = Root(42)

root.X