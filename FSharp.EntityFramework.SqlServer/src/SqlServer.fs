[<AutoOpen>]
module FSharp.Data.Entity.Internals.InformationSchema

open System
open System.Data.SqlClient
open System.Data
open Microsoft.FSharp.Quotations
open Microsoft.Data.Entity
open ProviderImplementation.ProvidedTypes

type internal ForeignKey = {
    Name: string
    //Ordinal: int
    Column: string
    ParentTableSchema: string
    ParentTable: string
    ParentTableColumn: string
}

type internal IInformationSchema = 
    abstract GetTables: unit -> string[] 
    abstract GetColumns: tableName: string -> (string * Type)[]
    abstract GetForeignKeys : tableName: string -> (string * string)[]
    abstract ModelConfiguration: Expr<string[] * ModelBuilder -> unit>

//type Column = {
//    Name: string
//    DbTypeName: Type
//    IsNullable: bool
//    IsIdentity: bool
//    IsReadOnly: bool
//    IsPartOfPrimaryKey: bool
//    DefaultValue: obj option
//}



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

let primaryKeysConfiguration (primaryKeyColumns: (string * string)[]) (entityTypeNames: string[], modelBuilder: ModelBuilder) = 
    let pkByTable = Map.ofArray primaryKeyColumns
    for name in entityTypeNames do
        let e = modelBuilder.Entity(name)
        let relational = e.Metadata.Relational()
        sprintf "%s.%s" relational.Schema relational.TableName
        |> pkByTable.TryFind 
        |> Option.iter (fun pkColumns ->
            e.HasKey( propertyNames = pkColumns.Split '\t') |> ignore
        )

let internal getSqlServerSchema connectionString = 
    
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
                    | [| "Microsoft.SqlServer.Types.SqlHierarchyId"; _ |] -> "hierarchyid", typeName
                    | [| "Microsoft.SqlServer.Types.SqlGeometry"; _ |] -> "geometry", typeName
                    | [| "Microsoft.SqlServer.Types.SqlGeography"; _ |] -> "geography", typeName
                    | [| "tinyint" |] -> typeName, typeof<byte>.FullName
                    | _ -> typeName, string x.["DataType"]
                yield sqlEngineTypeName, clrTypeName
        |]

    let getAllColumns() = 
        let getColumnsQuery = "
            SELECT 
	            schemas.name + '.' + tables.name AS table_name
	            ,columns.name AS column_name
                ,is_nullable
	            ,is_identity
	            ,is_computed
	            ,max_length
	            ,default_constraint = ISNULL( OBJECT_DEFINITION(columns.default_object_id), '')
	            ,is_part_of_primary_key = CASE WHEN index_columns.object_id IS NULL THEN 0 ELSE 1 END
            FROM
	            sys.schemas  
	            JOIN sys.tables ON schemas.schema_id = tables.schema_id
	            JOIN sys.columns ON columns.object_id = tables.object_id
	            LEFT JOIN sys.indexes ON 
		            tables.object_id = indexes.object_id 
		            AND indexes.is_primary_key = 1
	            LEFT JOIN sys.index_columns ON 
		            index_columns.object_id = tables.object_id 
		            AND index_columns.index_id = indexes.index_id 
		            AND columns.column_id = index_columns.column_id
        " 
        use conn = openConnection()
        conn.Execute( getColumnsQuery)
        |> Seq.map( fun x ->
            x ? table_name, 
            x ? column_name, 
            x ? is_part_of_primary_key = 1, 
            (x ? is_nullable, x ? is_identity, x ? max_length, x ? is_computed, x ? default_constraint)
        )
        |> Seq.toArray

    let getAllForeignKeys() =
        ()

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
            let columns = getAllColumns() |> Seq.toArray

            let primaryKeysByTable = 
                let elements =  
                    query {
                        for (tableName: string), columnName, isPartOfPrimaryKey, _ in columns do
                        where isPartOfPrimaryKey
                        groupValBy columnName tableName into g
                        let table = Expr.Value( g.Key)
                        let pkColumns = Expr.Value( String.concat "\t" g)
                        select (Expr.NewTuple [ table; pkColumns ])
                    }
                    |> Seq.toList

                Expr.NewArray(typeof<string * string>, elements)

            <@ 
                fun (entityNames, modelBuilder) ->
                    tablesConfiguration(entityNames, modelBuilder)
                    primaryKeysConfiguration %%primaryKeysByTable (entityNames, modelBuilder)
            @>

        member __.GetForeignKeys( tableName) = 
            let query = 
                "
                    SELECT 
	                    FK.name 
	                    ,OBJECT_SCHEMA_NAME(Parent.object_id) + '.'	+ OBJECT_NAME(Parent.object_id) 
                    FROM sys.foreign_keys AS FK
	                    JOIN sys.foreign_key_columns AS FKC ON FK.object_id = FKC.constraint_object_id
	                    JOIN sys.columns AS Parent ON 
                            FKC.referenced_column_id = Parent.column_id
		                    AND FKC.referenced_object_id = Parent.object_id
                    WHERE 
                        FK.parent_object_id = OBJECT_ID( @tableName)
                "
            use conn = openConnection()
            conn.Execute( query, [ "@tableName", tableName ])
            |> Seq.map(fun x -> x.GetString(0), x.GetString(1))
            |> Seq.distinct
            |> Seq.toArray
    }