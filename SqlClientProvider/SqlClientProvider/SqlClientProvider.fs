namespace FSharp.Data

open Microsoft.FSharp.Core.CompilerServices
open System
open System.Data
open System.Data.SqlClient
open System.Reflection
open Microsoft.FSharp.Quotations

open ProviderImplementation.ProvidedTypes

[<TypeProvider>]
type public SqlClientTypeProvider(config : TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.GetExecutingAssembly()
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "SqlClient", Some typeof<obj>)

    do 
        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("CommandText", typeof<string>) 
                ProvidedStaticParameter("ConnectionString", typeof<string>) 
            ],             
            instantiationFunction = this.CreateType
        )
        this.AddNamespace(nameSpace, [ providerType ])

    member internal this.CreateType typeName parameters = 
        let commandText : string = unbox parameters.[0] 
        let connectionString : string = unbox parameters.[1] 

        let commandType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true)

        let parameters = this.ExtractParameters(connectionString, commandText)
             
        commandType.AddMembers [
            yield this.Ctor(connectionString, commandText, parameters) :> MemberInfo
            yield! this.GeneratePropsForParams parameters 
            yield this.GenerateExecuteMethod(connectionString, commandText)
        ]

        commandType

    member internal this.ExtractParameters(connectionString, commandText) : string list = 
        [
            use conn = new SqlConnection(connectionString)
            conn.Open()
            use cmd = new SqlCommand("sys.sp_describe_undeclared_parameters", conn, CommandType = CommandType.StoredProcedure)
            cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore
            use reader = cmd.ExecuteReader()
            while(reader.Read()) do
                let paramName = string reader.["name"]
                let dbTypeName = string reader.["suggested_system_type_name"]
                let clrTypeName = reader.["suggested_system_type_id"] |> unbox |> this.MapSqlEngineTypeIdToClr 
                yield sprintf "%s,%s,%s" paramName clrTypeName dbTypeName
        ]

    member internal this.Ctor(connectionString, commandText, parameters) = 
        ProvidedConstructor(
            parameters = [],
            InvokeCode = fun _ -> 
                <@@ 
                    let this = new SqlCommand(commandText, new SqlConnection(connectionString)) 
                    for x in parameters do
                        let paramName, sqlEngineTypeName = let xs = x.Split(',') in xs.[0], xs.[2]
                        let sqlEngineTypeNameWithoutSize = 
                            let openParentPos = sqlEngineTypeName.IndexOf('(')
                            if openParentPos = -1 then sqlEngineTypeName else sqlEngineTypeName.Substring(0, openParentPos)
                        let dbType = Enum.Parse(typeof<SqlDbType>, sqlEngineTypeNameWithoutSize, ignoreCase = true) |> unbox
                        this.Parameters.Add(paramName, dbType) |> ignore
                    this
                @@>
        ) 

    member internal this.GeneratePropsForParams parameters = 
        
        parameters
        |> List.map (fun x -> 
            let paramName, clrTypeName = let xs = x.Split(',') in xs.[0], xs.[1]
            assert (paramName.StartsWith "@")

            let prop = ProvidedProperty(propertyName = paramName.Substring 1, propertyType = Type.GetType clrTypeName)
            prop.GetterCode <- fun args -> 
                <@@ 
                    let sqlCommand : SqlCommand = unbox %%args.[0]
                    sqlCommand.Parameters.[paramName].Value
                @@>

            prop.SetterCode <- fun _ -> <@@ raise <| NotImplementedException() @@>

            prop.SetterCode <- fun args -> 
                <@@ 
                    let sqlCommand : SqlCommand = unbox %%args.[0]
                    sqlCommand.Parameters.[paramName].Value <- %%Expr.Coerce(args.[1], typeof<obj>)
                @@>

            upcast prop
        )

    member internal this.GenerateExecuteMethod(connectionString, commandText) = 
        let execute = ProvidedMethod("Execute", [], typeof<Async<seq<Map<string, obj>>>>)
        execute.InvokeCode <- fun args -> 
            <@@ 
                async {
                    let sqlCommand : SqlCommand = %%Expr.Coerce(args.[0], typeof<SqlCommand>)
                    sqlCommand.Connection.Open() 
                    let! reader = sqlCommand.ExecuteReaderAsync() |> Async.AwaitTask
                    return seq {
                        while(reader.Read()) do
                            yield seq {
                                for i = 0 to reader.FieldCount - 1 do
                                    yield reader.GetName i, reader.GetValue i
                            } |> Map.ofSeq

                        sqlCommand.Connection.Close()
                    } |> Seq.cache
                }
            @@>

        upcast execute

    member internal this.MapSqlEngineTypeIdToClr(typeNameId : int) = 
        let clrType = 
            match typeNameId with
            | 127 ->    typeof<int64>   //bigint
            | 173 ->    typeof<byte[]>  //binary
            | 104 ->    typeof<bool>    //bit
            | 175 ->    typeof<string>  //char
            | 40 ->     typeof<DateTime>  //date
            | 61 ->     typeof<DateTime>  //datetime
            | 42 ->     typeof<DateTime>  //datetime2
            | 43 ->     typeof<DateTimeOffset>  //datetimeoffset
            | 106 ->    typeof<decimal>  //decimal
            | 62 ->     typeof<float>  //float
            | 34 ->     typeof<byte[]>  //image
            | 56 ->     typeof<int>  //int
            | 60 ->     typeof<decimal>  //money
            | 239 ->    typeof<string>  //nchar
            | 99 ->     typeof<string>  //ntext
            | 108 ->    typeof<int64>  //numeric
            | 231 ->    typeof<string>  //nvarchar
            | 59 ->     typeof<single>  //real
            | 58 ->     typeof<DateTime>  //smalldatetime
            | 52 ->     typeof<int16>  //smallint
            | 122 ->    typeof<decimal>  //smallmoney
            | 98 ->     typeof<obj>  //sql_variant
            | 35 ->     typeof<string>  //text
            | 41 ->     typeof<TimeSpan>  //time
            | 48 ->     typeof<byte>  //tinyint
            | 36 ->     typeof<Guid>  //uniqueidentifier
            | 165 ->    typeof<byte[]>  //varbinary
            | 167 ->    typeof<string>  //varchar
            | _ -> failwithf "Unsupported sql engine type %i" typeNameId       
        clrType.FullName

[<assembly:TypeProviderAssembly>]
do()
