#r @"bin\Debug\SqlFileTypeProvider.dll"
#r @"packages\FSharp.Data.SqlClient.1.8.2\lib\net40\FSharp.Data.SqlClient.dll"

open FSharp.IO
open FSharp.Data
[<Literal>]
let sourceDirectory = __SOURCE_DIRECTORY__

type X = FileReader<"tets">

type Get42Query = FileReader<"Get42.sql">
Get42Query.Text

[<Literal>]
let connection = "server=.;trusted_connection=true"

do 
    use cmd = new SqlCommandProvider< Get42Query.Text, ConnectionStringOrName = connection, SingleRow = true>(connection)
    cmd.Execute() |> printfn "%A"

