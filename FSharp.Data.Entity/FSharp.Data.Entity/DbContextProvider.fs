namespace FSharp.Data.Entity

open System
open System.Reflection
open System.IO
open System.Data
open System.Data.SqlClient
open System.Collections.Generic

open Microsoft.Data.Entity

open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations

open ProviderImplementation.ProvidedTypes

open Inflector

[<TypeProvider>]
type public DbContextProvider(config: TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.LoadFrom( config.RuntimeAssembly)
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "DbContext", Some typeof<obj>, HideObjectMethods = true, IsErased = false)

    let tempAssembly = 
        let name = Path.ChangeExtension( Path.GetRandomFileName(), "dll")
        let fullPath = Path.Combine(config.TemporaryFolder, name)
        ProvidedAssembly fullPath

    do 
        tempAssembly.AddTypes [ providerType ]

    do 
        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("ConnectionString", typeof<string>) 
            ],             
            instantiationFunction = (fun typeName args ->   
                this.CreateDbContextType(typeName, unbox args.[0])
            )        
        )

        this.AddNamespace( nameSpace, [ providerType ])

    //helpers
    let (?) (row: SqlDataReader) (name: string) = unbox row.[name]

    static let typeMappings = Dictionary()

    static let loadTypeMappings(conn: SqlConnection) = 
        lock typeMappings <| fun () ->
            for x in conn.GetSchema("DataTypes").Rows do
                let typeName = string x.["TypeName"]
                let sqlEngineTypeName, clrTypeName = 
                    match typeName.Split([|','|], 2) with
                    | [| "Microsoft.SqlServer.Types.SqlHierarchyId"; _ |] ->  "hierarchyid", typeName
                    | [| "Microsoft.SqlServer.Types.SqlGeometry"; _ |] -> "geometry", typeName
                    | [| "Microsoft.SqlServer.Types.SqlGeography"; _ |] -> "geography", typeName
                    | [| "tinyint" |] -> typeName, typeof<byte>.FullName
                    | _ -> typeName, string x.["DataType"]
                typeMappings.Add(sqlEngineTypeName, Type.GetType( clrTypeName, throwOnError = true))

    override __.ResolveAssembly args =
        let missing = AssemblyName(args.Name)

        config.ReferencedAssemblies
        |> Seq.tryPick(fun assemblyFile ->
            let reference = Assembly.ReflectionOnlyLoadFrom( assemblyFile).GetName()
            if AssemblyName.ReferenceMatchesDefinition(reference, missing)
            then assemblyFile |> Assembly.LoadFrom |> Some
            else None
        )
        |> defaultArg <| base.ResolveAssembly( args)

    member internal this.CreateDbContextType( typeName, connectionString) = 
        let dbContextType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<DbContext>, HideObjectMethods = true, IsErased = false)
        tempAssembly.AddTypes [ dbContextType ]

        do 
            dbContextType.AddMembersDelayed <| fun() ->
                [
                    let parameterlessCtor = ProvidedConstructor([], IsImplicitCtor = true)
                    parameterlessCtor.BaseConstructorCall <- fun args -> 
                        let baseCtor = typeof<DbContext>.GetConstructor(BindingFlags.Instance ||| BindingFlags.NonPublic, null, [||], null) 
                        baseCtor, args
                    yield parameterlessCtor

                    for baseCtor in typeof<DbContext>.GetConstructors() do
                        let paremeters = [ for p in baseCtor.GetParameters() -> ProvidedParameter(p.Name, p.ParameterType) ]
                        let ctor = ProvidedConstructor (paremeters, InvokeCode = fun _ -> <@@ () @@>)
                        ctor.BaseConstructorCall <- fun args -> baseCtor, args
                        yield ctor
                ]

        let tables = this.GetTables(connectionString)

        dbContextType.AddMembersDelayed <| fun() ->
            [
                let entities = this.GetEntities(tables, connectionString)
                for e in entities do
                    yield e :> MemberInfo
                    let ``type`` = ProvidedTypeBuilder.MakeGenericType( typedefof<_ DbSet>, [ e ])
                    let name = e.Name.Pluralize()
                    let field = ProvidedField(name, ``type``)
                    yield field :> _
                    let prop = ProvidedProperty(name, ``type``)
                    prop.GetterCode <- fun args -> Expr.FieldGet(args.[0], field)
                    prop.SetterCode <- fun args -> Expr.FieldSet(args.[0], field, args.[1])
                    yield prop :> _

                tempAssembly.AddTypes entities
            ]

        do 
            let name = "OnConfiguring"
            let field = ProvidedField(name.Camelize(), typeof<DbContextOptionsBuilder -> unit>)
            dbContextType.AddMember field

            let property = ProvidedProperty(name, field.FieldType)
            property.SetterCode <- fun args -> Expr.FieldSet(args.[0], field, args.[1])
            dbContextType.AddMember property

            let vTableHandle = typeof<DbContext>.GetMethod(name, BindingFlags.Instance ||| BindingFlags.NonPublic)
            let impl = ProvidedMethod(vTableHandle.Name, [ ProvidedParameter("optionsBuilder", typeof<DbContextOptionsBuilder>) ], typeof<Void>)
            impl.SetMethodAttrs(vTableHandle.Attributes ||| MethodAttributes.Virtual)
            dbContextType.AddMember impl
            impl.InvokeCode <- fun args -> 
                <@@ 
                    let configuring = %%Expr.FieldGet(args.Head, field)
                    let optionsBuilder: DbContextOptionsBuilder = %%args.[1]
                    optionsBuilder.UseSqlServer(connectionString: string) |> ignore
                    if box configuring <> null
                    then configuring optionsBuilder
                @@>
            dbContextType.DefineMethodOverride(impl, vTableHandle)

        do 
            let name = "OnModelCreating"
            let field = ProvidedField(name.Camelize(), typeof<ModelBuilder -> unit>)
            dbContextType.AddMember field

            let property = ProvidedProperty(name, field.FieldType)
            property.SetterCode <- fun args -> Expr.FieldSet(args.[0], field, args.[1])
            dbContextType.AddMember property

            let vTableHandle = typeof<DbContext>.GetMethod(name, BindingFlags.Instance ||| BindingFlags.NonPublic)
            let impl = ProvidedMethod(vTableHandle.Name, [ ProvidedParameter("modelBuilder", typeof<ModelBuilder>) ], typeof<Void>)
            impl.SetMethodAttrs(vTableHandle.Attributes ||| MethodAttributes.Virtual)
            dbContextType.AddMember impl
            impl.InvokeCode <- fun args -> 
                <@@ 
                    let modelBuilder: ModelBuilder = %%args.[1]
                    let this: DbContext = %%Expr.Coerce(args.[0], typeof<DbContext>)
                    for entity in this.GetType().GetNestedTypes() do
                        let twoPartTableName = entity.FullName.Split('+') |> Array.last
                        let schema, tableName = 
                            let xs = twoPartTableName.Split([|'.'|], 2) in 
                            xs.[0], xs.[1]
                        modelBuilder.Entity(entity.FullName).ToTable(tableName, schema) |> ignore

                    let modelCreating = %%Expr.FieldGet(args.Head, field)
                    if box modelCreating <> null
                    then modelCreating modelBuilder
                @@>
            dbContextType.DefineMethodOverride(impl, vTableHandle)

        dbContextType

    member internal this.GetTables(connectionString) = [
        use conn = new SqlConnection(connectionString)
        conn.Open()
        let query = "
            SELECT TABLE_SCHEMA, TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
        "
        use cmd = new SqlCommand(query, conn)
        use cursor = cmd.ExecuteReader()
        while cursor.Read() do
            yield cursor ? TABLE_SCHEMA, cursor ? TABLE_NAME
    ]

    member internal this.GetEntities(tables, connectionString) = [
        use conn = new SqlConnection(connectionString)
        conn.Open()

        if typeMappings.Count = 0
        then loadTypeMappings conn

        for schema, name in tables do   

            let tableType = ProvidedTypeDefinition(className = sprintf "%s.%s" schema name , baseType = None, IsErased = false)

            do 
                let ctor = ProvidedConstructor([], InvokeCode = fun _ -> <@@ () @@>)
                ctor.BaseConstructorCall <- fun args -> typeof<obj>.GetConstructor([||]), args
                tableType.AddMember ctor

            do 
                tableType.AddMembersDelayed <| fun() -> 
                    [
                        use conn = new SqlConnection(connectionString)
                        conn.Open()
                        let query = 
                            sprintf "
                                SELECT COLUMN_NAME, COLUMN_DEFAULT, IS_NULLABLE, DATA_TYPE
                                FROM INFORMATION_SCHEMA.COLUMNS
                                WHERE TABLE_SCHEMA = '%s' AND TABLE_NAME = '%s'
                            " schema name
                        use cmd = new SqlCommand(query, conn)
                        use cursor = cmd.ExecuteReader()
                        while cursor.Read() do
                            let colName = cursor ? COLUMN_NAME
                            let dataType = typeMappings.[cursor ? DATA_TYPE]
                            let isNullable = cursor ? IS_NULLABLE = "YES"
                            let clrType = 
                                if isNullable && dataType.IsValueType
                                then ProvidedTypeBuilder.MakeGenericType(typedefof<_ Nullable>, [ dataType ])
                                else dataType
                            
                            let backingField = ProvidedField(colName, clrType)
                            yield backingField :> MemberInfo
                        
                            let property = ProvidedProperty(colName, clrType)
                            property.GetterCode <- fun args -> Expr.FieldGet( args.[0], backingField)
                            property.SetterCode <- fun args -> Expr.FieldSet( args.[0], backingField, args.[1])
                            yield upcast property
                    ]

            yield tableType
    ]
        

[<assembly:TypeProviderAssembly()>]
do()

