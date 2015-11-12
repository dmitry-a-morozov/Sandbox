module FSharp.Data.Entity.SqlServer.Runtime

open System
open Microsoft.Data.Entity

let primaryKeysConfiguration (pks: (string * string[])[]) (entityTypes: Type[], modelBuilder: ModelBuilder) = 
    let pkByTable = Map.ofArray pks

    let xs = [
        for t in entityTypes do
            let e = modelBuilder.Entity(t)
            let relational = e.Metadata.Relational()
            yield sprintf "%s.%s" relational.Schema relational.TableName
    ]

    let diff = pks |> Array.map fst |> set |> Set.difference (set xs)

    for t in entityTypes do
        let e = modelBuilder.Entity(t)
        let relational = e.Metadata.Relational()
        let twoPartTableName = sprintf "%s.%s" relational.Schema relational.TableName

        twoPartTableName
        |> pkByTable.TryFind 
        |> Option.iter (fun pkColumns -> e.HasKey( pkColumns) |> ignore)


