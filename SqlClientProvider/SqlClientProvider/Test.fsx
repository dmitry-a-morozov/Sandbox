
#r "bin\Debug\SqlClientProvider.dll"

open FSharp.Data

[<Literal>]
let connection = "Data Source=(localdb)\ProjectsV12;Initial Catalog=master;Integrated Security=True"

type GetDatabaseById = SqlClient<"SELECT name FROM sys.databases WHERE database_id = @id", connection>
let getDatabaseById = GetDatabaseById(id = 1)
getDatabaseById.Execute() |> Async.RunSynchronously |> Seq.toArray