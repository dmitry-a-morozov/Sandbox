module FSharp.Data.Entity.DesignTime.SqlServer

open System
open System.Data.SqlClient
open System.Data
open Microsoft.FSharp.Quotations
open Microsoft.Data.Entity
open ProviderImplementation.ProvidedTypes

type SqlConnection with 
    member internal this.Execute(query, ?parameters) = 
        seq {
            assert(this.State = ConnectionState.Open)
            use cmd = new SqlCommand(query, this)

            cmd.Parameters.AddRange [|
                let ps = defaultArg parameters []
                for name, value in ps do
                    yield SqlParameter(name, value = value) 
            |]
            
            use cursor = cmd.ExecuteReader()
            while cursor.Read() do
                yield cursor
        }

type SqlDataReader with
    member internal this.TryGetValue(name: string) = 
        let value = this.[name] 
        if Convert.IsDBNull value then None else Some(unbox<'a> value)

let tablesConfiguration (entityTypeNames: string[], modelBuilder: ModelBuilder) = 
    for name in entityTypeNames do
        let schema, table = 
            let table2PartName = name.Split('+') |> Array.last
            let xs = table2PartName.Split('.')
            xs.[0], xs.[1]        

        modelBuilder.Entity(name).ToTable(table, schema) |> ignore

let primaryKeysConfiguration (tables: string[]) (primaryKeyColumns: string[]) (entityTypeNames: string[], modelBuilder: ModelBuilder) = 
    let pkByTable = (tables, primaryKeyColumns) ||> Array.zip |> Map.ofArray
    for name in entityTypeNames do
        let e = modelBuilder.Entity(name)
        let relational = e.Metadata.Relational()
        sprintf "%s.%s" relational.Schema relational.TableName
        |> pkByTable.TryFind 
        |> Option.iter (fun pkColumns ->
            e.Key( propertyNames = pkColumns.Split '\t') |> ignore
        )

let getSqlServerSchema connectionString = 
    
    let (?) (row: SqlDataReader) (name: string) = unbox row.[name]
    
    let openConnection() = 
        let x = new SqlConnection(connectionString)
        x.Open()
        x

    let typeMappings = 
        lazy dict [|
            use conn = openConnection()
            for x in conn.GetSchema("DataTypes").Rows do
                let typeName = string x.["TypeName"]
                let sqlEngineTypeName, clrTypeName = 
                    match typeName.Split([|','|], 2) with
                    | [| "Microsoft.SqlServer.Types.SqlHierarchyId"; _ |] ->  "hierarchyid", typeName
                    | [| "Microsoft.SqlServer.Types.SqlGeometry"; _ |] -> "geometry", typeName
                    | [| "Microsoft.SqlServer.Types.SqlGeography"; _ |] -> "geography", typeName
                    | [| "tinyint" |] -> typeName, typeof<byte>.FullName
                    | _ -> typeName, string x.["DataType"]
                yield sqlEngineTypeName, clrTypeName
        |]

    { new IInformationSchema with
        member __.GetTables() = [|
            let query = "
                SELECT TABLE_SCHEMA, TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE'
            "
            use conn = openConnection()
            for x in conn.Execute(query) do
                yield sprintf "%s.%s" x ? TABLE_SCHEMA x ? TABLE_NAME
        |]

        member __.GetColumns(tableName) = [|
            let query = 
                sprintf "
                    SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE 
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA + '.' + TABLE_NAME = '%s'
                    ORDER BY ORDINAL_POSITION
                " tableName
            use conn = openConnection()
            for x in conn.Execute(query) do
                let name = x ? COLUMN_NAME
                let clrTypeIncludingNullability = 
                    let typename = x ? DATA_TYPE
                    let t = Type.GetType( typeMappings.Value.[typename], throwOnError = true)
                    if x ? IS_NULLABLE = "YES" && t.IsValueType
                    then ProvidedTypeBuilder.MakeGenericType(typedefof<_ Nullable>, [ t ])
                    else t

                yield name, clrTypeIncludingNullability
        |]            

        member __.ModelConfiguration = 
            let getColumnsQuery = "
                SELECT 
	                schemas.name + '.' + tables.name AS table_name
	                ,columns.name AS column_name
	                --,types.name AS typename
	                --,is_identity
	                --,is_readonly = CASE WHEN is_identity = 0 OR is_computed = 0 THEN 1 ELSE 0 END
	                --,columns.max_length
	                --,default_constraint = OBJECT_DEFINITION(columns.default_object_id)
	                ,is_part_of_primary_key = CASE WHEN index_columns.object_id IS NULL THEN 0 ELSE 1 END
                FROM
	                sys.schemas  
	                JOIN sys.tables ON schemas.schema_id = tables.schema_id
	                JOIN sys.columns ON columns.object_id = tables.object_id
	                --JOIN sys.types ON columns.system_type_id = types.system_type_id 
	                --	AND ((types.is_assembly_type = 1 AND columns.user_type_id = types.user_type_id) 
	                --		OR columns.system_type_id = types.user_type_id)
	                LEFT JOIN sys.indexes ON 
		                tables.object_id = indexes.object_id 
		                AND indexes.is_primary_key = 1
	                LEFT JOIN sys.index_columns ON 
		                index_columns.object_id = tables.object_id 
		                AND index_columns.index_id = indexes.index_id 
		                AND columns.column_id = index_columns.column_id
                ORDER BY 
                    tables.object_id, columns.column_id            
            " 
            use conn = openConnection()
            let columns = [|
                for x in conn.Execute( getColumnsQuery) do
                    yield x.GetString(0), x.GetString(1), x.GetInt32(2) = 1
            |]

            let tables, primaryKeyColumns = 
                query {
                    for tableName, columnName, isPartOfPrimaryKey in columns do
                    where isPartOfPrimaryKey
                    groupValBy columnName tableName into g
                    select (g.Key, String.concat "\t" g)
                    //select (g.Key, Array.ofSeq g)
                }
                |> Seq.toArray
                |> Array.unzip

            <@ 
                fun (entityNames, modelBuilder) ->
                    tablesConfiguration(entityNames, modelBuilder)
                    primaryKeysConfiguration tables primaryKeyColumns (entityNames, modelBuilder)
            @>

//            let columns = 
//                conn.Execute(query, [ "@tableName", twoPartTableName ]) 
//                |> Seq.map(fun x -> 
//                    { 
//                        Name = x ? name
//                        Type = Type.GetType( typeMappings.Value.[x ? typename], throwOnError = true)
//                        IsNullable = x ? is_nullable
//                        IsIdentity = x ? is_identity
//                        IsReadOnly = x ? is_readonly = 0 
//                        IsPartOfPrimaryKey = x ? is_part_of_primary_key = 1
//                        DefaultValue = x.TryGetValue("default_constraint")
//                    }
//                )
//                |> Seq.toArray

//            let config = 
//                let pkColumns = [| for c in columns do if c.IsPartOfPrimaryKey then yield c.Name |]
//                <@ fun(entity: EntityTypeBuilder) ->
//                    entity.Key(pkColumns) |> ignore
//                @>
//            columns, config
//
//        member __.GetForeignKeys( twoPartTableName) = [|
//            let query = "
//                SELECT 
//	                FK.name AS Name
//	                ,Child.name AS [Column]
//	                ,OBJECT_SCHEMA_NAME(Parent.object_id) AS ParentTableSchema
//	                ,OBJECT_NAME(Parent.object_id) AS ParentTable
//	                ,Parent.name AS ParentTableColumn 
//                FROM sys.foreign_keys AS FK
//	                JOIN sys.foreign_key_columns AS FKC ON FK.object_id = FKC.constraint_object_id
//	                JOIN sys.columns AS Child ON 
//                        FKC.referenced_column_id = Child.column_id
//		                AND FKC.parent_object_id = Child.object_id
//	                JOIN sys.columns AS Parent ON 
//                        FKC.referenced_column_id = Parent.column_id
//		                AND FKC.referenced_object_id = Parent.object_id
//                WHERE 
//                    FK.parent_object_id = OBJECT_ID( @tableName)
//            " 
//            use conn = openConnection()
//            for x in conn.Execute(query, [ "@tableName", twoPartTableName ]) do
//                yield { 
//                    Name = x ? Name
//                    Ordinal = x ? Ordinal
//                    Column = x ? Column
//                    ParentTableSchema = x ? ParentTableSchema
//                    ParentTable = x ? ParentTable
//                    ParentTableColumn = x ? ParentTableColumn
//                }
//        |]
    }