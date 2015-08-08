module Basic

open Xunit
open FSharp.Data.Entity
open Microsoft.Data.Entity
open System

type AdventureWorks = FSharp.Data.Entity.DbContext<"Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True">

[<Fact>]
let getShifts() = 
    let configuring(optionsBuilder: DbContextOptionsBuilder) =
        optionsBuilder.UseSqlServer("Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True") |> ignore

    let db = new AdventureWorks( Action<_>(configuring))

    for x in db.``HumanResources.ShiftTable`` do
        printfn "Name: %s; ID: %i" x.Name x.ShiftID

