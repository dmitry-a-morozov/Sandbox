module Program 

open System.Diagnostics
open FSharp.Diagnostics

let [<EntryPoint>] main _ =
    let x = "hello".Substring(0, 5)
    Debug.Assert(x.Length > 5)
    0
