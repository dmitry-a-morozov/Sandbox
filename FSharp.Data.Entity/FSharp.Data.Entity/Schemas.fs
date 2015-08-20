module internal FSharp.Data.Entity.DesignTime.Schemas

open System

type Column = {
    Name: string
    Type: Type
    IsNullable: bool
    IsIdentity: bool
    DefaultValue: obj option
}

type ForeignKey = {
    Name: string
    Column: string
    ParentTable: string
    ParentTableColumn: string
}

type IInformationSchema = 
    abstract GetTables: unit -> string[]
    abstract GetColumns: tableName: string -> Column[]
    abstract GetForeignKeys : tableName: string -> ForeignKey[]

open System.Data.SqlClient

type SqlConnection with 
    member internal this.Execute(query, ?parameters) = 
        seq {
            assert(this.State = Data.ConnectionState.Open)
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

let getSqlServerSchema connectionString = 
    
    let (?) (row: SqlDataReader) (name: string) = unbox row.[name]
    
    let openConnection() = 
        let x = new SqlConnection(connectionString)
        x.Open()
        x

    let (|TwoPartName|) (identifier: string) = 
        match identifier.Split('.') with
        | [| schema; name |] -> schema, name
        | _ -> failwithf "%s is not valid two-part name."  identifier

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

    {
        new IInformationSchema with
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

            member __.GetColumns( TwoPartName( schema, tableName)) = [|
                let query = "
                    DECLARE @tableId AS INT = (SELECT object_id(QUOTENAME(@schema) + '.' + QUOTENAME(@tableName)))
                    SELECT 
                        COLUMN_NAME, 
                        DATA_TYPE, 
                        COLUMN_DEFAULT, 
                        IS_NULLABLE, 
                        COLUMNPROPERTY(@tableId, COLUMN_NAME, 'IsIdentity') AS IS_IDENTITY
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @tableName
                " 
                use conn = openConnection()
                for x in conn.Execute(query, [ "@schema", schema; "@tableName", tableName ]) do
                    yield { 
                        Name = x ? COLUMN_NAME
                        Type = Type.GetType( typeMappings.Value.[x ? DATA_TYPE], throwOnError = true)
                        IsNullable = x ? IS_NULLABLE = "YES"
                        IsIdentity = x ? IS_IDENTITY = 1
                        DefaultValue = x.TryGetValue("COLUMN_DEFAULT")
                    }
            |]
                    
            member __.GetForeignKeys( TwoPartName( schema, tableName)) = [|
                let query = "
                    SELECT 
	                    FK.name AS Name
	                    ,Child.name AS ChildColumn
	                    ,OBJECT_NAME(Parent.object_id) AS ParentTable
	                    ,Parent.name AS ParentColumn 
                    FROM sys.foreign_keys AS FK
	                    JOIN sys.foreign_key_columns AS FKC ON FK.object_id = FKC.constraint_object_id
	                    JOIN sys.columns AS Child ON 
                            FKC.referenced_column_id = Child.column_id
		                    AND FKC.parent_object_id = Child.object_id
	                    JOIN sys.columns AS Parent ON 
                            FKC.referenced_column_id = Parent.column_id
		                    AND FKC.referenced_object_id = Parent.object_id
                    WHERE 
                        FK.parent_object_id = OBJECT_ID( QUOTENAME(@schema) + '.' + QUOTENAME(@tableName))
                " 
                use conn = openConnection()
                for x in conn.Execute(query, [ "@schema", schema; "@tableName", tableName ]) do
                    yield { 
                        Name = x ? Name
                        Column = x ? Column
                        ParentTable = x ? ParentTable
                        ParentTableColumn = x ? ParentTableColumn
                    }
            |]
    }
