#load "EFDependecies.fsx"

#r @"System.Transactions.dll"
#r @"..\FSharp.Data.Entity\bin\Debug\FSharp.Data.Entity.dll"

open FSharp.Data.Entity

type AdventureWorks = DbContext<"Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True">

open Microsoft.Data.Entity

let db = 
    new AdventureWorks( 
        configuring = (fun optionsBuilder -> 
            optionsBuilder.UseSqlServer("Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True") |> ignore
        ), 
        modelCreating = (fun modelBuilder -> 
            modelBuilder.Entity<AdventureWorks.``HumanResources.Shift``>().ToTable("Shift", "HumanResources") |> ignore
        )
    )

query {
    for x in db.``HumanResources.ShiftTable`` do
    where (x.ShiftID > 1uy)
    select x.Name
}
|> Seq.iter (printfn "Shift: %s")



