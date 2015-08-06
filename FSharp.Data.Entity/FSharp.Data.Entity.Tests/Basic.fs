module Basic

open Xunit
open FSharp.Data.Entity

type AdventureWorks = DbContext<"Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True">

let db = new AdventureWorks()

[<Fact>]
let getShifts() = 
    ()

