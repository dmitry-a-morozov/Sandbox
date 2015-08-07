module Basic

open Xunit
open FSharp.Data.Entity

type AdventureWorks = DbContext<"Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True">

[<Fact>]
let getShifts() = 
    let db = new AdventureWorks()
    //let x = new AdventureWorks.``HumanResources.Shift``()
    //printfn "%A" x
    ()

