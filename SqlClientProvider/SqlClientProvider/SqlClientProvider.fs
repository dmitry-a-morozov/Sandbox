﻿namespace FSharp.Data

open Microsoft.FSharp.Core.CompilerServices
open System
open System.Data
open System.Data.SqlClient
open System.Reflection
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection

open ProviderImplementation.ProvidedTypes

[<TypeProvider>]
type public SqlClientProvider(config : TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.GetExecutingAssembly()
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "SqlClient", Some typeof<obj>, HideObjectMethods = true)
    let sqlEngineTypeToClrMap = ref Map.empty

    do 
        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("CommandText", typeof<string>) 
                ProvidedStaticParameter("ConnectionString", typeof<string>) 
                ProvidedStaticParameter("SingleRow", typeof<bool>, false) 
            ],             
            instantiationFunction = this.CreateType
        )
        this.AddNamespace(nameSpace, [ providerType ])

    member internal this.CreateType typeName parameters = 
        let commandText : string = unbox parameters.[0] 
        let connectionString : string = unbox parameters.[1] 
        let singleRow : bool = unbox parameters.[2] 

        this.CheckMinimalVersion connectionString
        this.LoadDataTypesMap connectionString

        let commandType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true)

        let parameters = this.ExtractParameters(connectionString, commandText)

        ProvidedConstructor(
            parameters = [],
            InvokeCode = fun _ -> 
                <@@ 
                    let this = new SqlCommand(commandText, new SqlConnection(connectionString)) 
                    for x in parameters do
                        let xs = x.Split(',') 
                        let paramName, sqlEngineTypeName = xs.[0], xs.[2]
                        let sqlEngineTypeNameWithoutSize = 
                            let openParentPos = sqlEngineTypeName.IndexOf('(')
                            if openParentPos = -1 then sqlEngineTypeName else sqlEngineTypeName.Substring(0, openParentPos)
                        let dbType = Enum.Parse(typeof<SqlDbType>, sqlEngineTypeNameWithoutSize, ignoreCase = true) |> unbox
                        this.Parameters.Add(paramName, dbType) |> ignore
                    this
                @@>
        ) 
        |> commandType.AddMember 

        commandType.AddMembersDelayed <| fun() -> 
            parameters
            |> List.map (fun x -> 
                let paramName, clrTypeName = let xs = x.Split(',') in xs.[0], xs.[1]
                assert (paramName.StartsWith "@")

                let prop = ProvidedProperty(propertyName = paramName.Substring 1, propertyType = Type.GetType clrTypeName)
                prop.GetterCode <- fun args -> 
                    <@@ 
                        let sqlCommand : SqlCommand = %%Expr.Coerce(args.[0], typeof<SqlCommand>)
                        sqlCommand.Parameters.[paramName].Value
                    @@>

                prop.SetterCode <- fun args -> 
                    <@@ 
                        let sqlCommand : SqlCommand = %%Expr.Coerce(args.[0], typeof<SqlCommand>)
                        sqlCommand.Parameters.[paramName].Value <- %%Expr.Coerce(args.[1], typeof<obj>)
                    @@>

                prop
            )

        use conn = new SqlConnection(connectionString)
        use cmd = new SqlCommand("sys.sp_describe_first_result_set", conn, CommandType = CommandType.StoredProcedure)
        cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore
        conn.Open()
        use reader = cmd.ExecuteReader()
        if not reader.HasRows
        then 
            this.AddExecuteNonQuery commandType
        else
            this.AddExecuteReader(reader, commandType, singleRow)
        commandType

    member this.ExtractParameters(connectionString, commandText) : string list =  
        [
            use conn = new SqlConnection(connectionString)
            conn.Open()

            use cmd = new SqlCommand("sys.sp_describe_undeclared_parameters", conn, CommandType = CommandType.StoredProcedure)
            cmd.Parameters.AddWithValue("@tsql", commandText) |> ignore
            use reader = cmd.ExecuteReader()
            while(reader.Read()) do
                let paramName = string reader.["name"]
                let clrTypeName = this.MapSqlEngineTypeToClr(sqlEngineTypeId = unbox<int> reader.["suggested_system_type_id"], detailedMessage = " Parameter name:" + paramName)
                let dbTypeName = string reader.["suggested_system_type_name"]
                yield sprintf "%s,%s,%s" paramName clrTypeName dbTypeName
        ]

    member __.CheckMinimalVersion connectionString = 
        use conn = new SqlConnection(connectionString)
        conn.Open()
        let majorVersion = conn.ServerVersion.Split('.').[0]
        if int majorVersion < 11 then failwithf "Minimal supported major version is 11. Currently used: %s" conn.ServerVersion

    member __.LoadDataTypesMap connectionString = 
        if sqlEngineTypeToClrMap.Value.IsEmpty
        then
            use conn = new SqlConnection(connectionString)
            conn.Open()
            sqlEngineTypeToClrMap := query {
                let getSysTypes = new SqlCommand("SELECT * FROM sys.types", conn)
                for x in conn.GetSchema("DataTypes").AsEnumerable() do
                join y in (getSysTypes.ExecuteReader(CommandBehavior.CloseConnection) |> Seq.cast<IDataRecord>) on 
                    (x.Field("TypeName") = string y.["name"])
                let system_type_id = y.["system_type_id"] |> unbox<byte> |> int 
                select(system_type_id, x.Field<string>("DataType"))
            }
            |> Map.ofSeq

    member __.MapSqlEngineTypeToClr(sqlEngineTypeId, detailedMessage) = 
        match !sqlEngineTypeToClrMap |> Map.tryFind sqlEngineTypeId with
        | Some clrType ->  clrType
        | None -> failwithf "Cannot map sql engine type %i to CLR type. %s" sqlEngineTypeId detailedMessage

    member internal this.AddExecuteNonQuery commandType = 
        let execute = ProvidedMethod("Execute", [], typeof<Async<unit>>)
        execute.InvokeCode <- fun args ->
            <@@
                async {
                    let sqlCommand = %%Expr.Coerce(args.[0], typeof<SqlCommand>) : SqlCommand
                    do! sqlCommand.Connection.OpenAsync()  |> Async.AwaitIAsyncResult |> Async.Ignore
                    return! sqlCommand.ExecuteNonQueryAsync() |> Async.AwaitTask |> Async.Ignore 
                }
            @@>
        commandType.AddMember execute

    static member internal GetRows<'Row>(cmd, rowMapper : Expr, singleRow) = 
        let commandBehavior = if singleRow then CommandBehavior.SingleRow else CommandBehavior.Default 
        //Combine with CommandBehavior.SingleResult for single result set ???
        let getSeqAsyncCode = 
            <@ 
                async {
                    let sqlCommand : SqlCommand = %%Expr.Coerce(cmd, typeof<SqlCommand>)
                    let! token = Async.CancellationToken
                    do! sqlCommand.Connection.OpenAsync(token) |> Async.AwaitIAsyncResult |> Async.Ignore
                    let! reader = 
                        try 
                            sqlCommand.ExecuteReaderAsync(commandBehavior, token) |> Async.AwaitTask
                        with _ ->
                            sqlCommand.Connection.Close()
                            reraise()
                    return 
                        seq {
                            try 
                                while(not token.IsCancellationRequested && reader.Read()) do
                                    let row = Array.zeroCreate reader.FieldCount
                                    reader.GetValues row |> ignore
                                    yield (%%rowMapper : obj[] -> 'Row)  row 
                            finally 
                                sqlCommand.Connection.Close()
                        } |> Seq.cache
                }
            @>

        if singleRow
        then 
            <@@ 
                async { 
                    let! xs = %getSeqAsyncCode
                    return Seq.exactlyOne xs
                }
            @@>
        else
            upcast getSeqAsyncCode

    static member internal GetValues0<'Row>(cmd, singleRow) = 
        SqlClientProvider.GetRows<'Row>(cmd, <@ fun (values : obj[]) -> unbox<'Row> values.[0] @>, singleRow)

    member internal this.AddExecuteReader(columnInfoReader, commandType, singleRow) = 
        let columns = 
            columnInfoReader 
            |> Seq.cast<IDataRecord> 
            |> Seq.map (fun x -> 
                let columnName = string x.["name"]
                columnName, 
                this.MapSqlEngineTypeToClr(sqlEngineTypeId = unbox x.["system_type_id"], detailedMessage = " Column name:" + columnName),
                unbox<int> x.["column_ordinal"]
            ) 
            |> Seq.toList

        if columns.Length = 1
        then
            let _, itemTypeName, _ = columns.Head

            let itemType = Type.GetType itemTypeName
            let returnType = 
                let asyncSpecialization = if singleRow then itemType else typedefof<_ seq>.MakeGenericType itemType 
                typedefof<_ Async>.MakeGenericType asyncSpecialization

            let execute = ProvidedMethod("Execute", [], returnType)

            execute.InvokeCode <- fun args -> 
                let impl = this.GetType().GetMethod("GetValues0", BindingFlags.NonPublic ||| BindingFlags.Static).MakeGenericMethod([| itemType |])
                impl.Invoke(null, [| args.[0]; singleRow |]) |> unbox
            commandType.AddMember execute

        else 
            let syncReturnType, executeMethodBody = 
                let tupleType = columns |> List.map (fun(_, typeName, _) -> Type.GetType typeName) |> List.toArray |> FSharpType.MakeTupleType
                let rowMapper = 
                    let values = Var("values", typeof<obj[]>)
                    let getTupleType = Expr.Call(typeof<Type>.GetMethod("GetType", [| typeof<string>|]), [ Expr.Value tupleType.AssemblyQualifiedName ])
                    Expr.Lambda(values, Expr.Coerce(Expr.Call(typeof<FSharpValue>.GetMethod("MakeTuple"), [Expr.Var values; getTupleType]), tupleType))
                let getExecuteBody(args : Expr list) = 
                    let impl = this.GetType().GetMethod("GetRows", BindingFlags.NonPublic ||| BindingFlags.Static).MakeGenericMethod([| tupleType |])
                    impl.Invoke(null, [| args.[0]; rowMapper; singleRow |]) |> unbox

                let resultType = if singleRow then tupleType else typedefof<_ seq>.MakeGenericType(tupleType)
                resultType, getExecuteBody
                    
            commandType.AddMember <| ProvidedMethod("Execute", [], typedefof<_ Async>.MakeGenericType syncReturnType, InvokeCode = executeMethodBody)

[<assembly:TypeProviderAssembly>]
do()