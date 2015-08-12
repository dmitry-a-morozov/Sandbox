namespace FSharp.Data.Entity

open System
open System.Reflection
open System.IO
open System.Data
open System.Data.SqlClient

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
    let (?) (row: DataRow) (name: string) = row.Field name

    let camelCase (s: string) = sprintf "%c%s" (Char.ToLower s.[0]) (s.Substring(1))
    
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
                    let modelCreating = %%Expr.FieldGet(args.Head, field)
                    if box modelCreating <> null
                    then 
                        let modelBuilder: ModelBuilder = %%args.[1]
                        modelCreating modelBuilder
                @@>
            dbContextType.DefineMethodOverride(impl, vTableHandle)

        dbContextType.AddMembersDelayed <| fun() ->
            [
                let entities = this.GetEntities( connectionString)
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

        dbContextType

    member internal this.GetEntities(connectionString: string) = [
        use conn = new SqlConnection(connectionString)
        conn.Open()

        let types = 
            dict [
                for x in conn.GetSchema("DataTypes").Rows do
                    let typeName = string x.["TypeName"]
                    yield
                        match typeName.Split([|','|], 2) with
                        | [| "Microsoft.SqlServer.Types.SqlHierarchyId"; _ |] -> "hierarchyid", typeName
                        | [| "Microsoft.SqlServer.Types.SqlGeometry"; _ |] -> "geometry", typeName
                        | [| "Microsoft.SqlServer.Types.SqlGeography"; _ |] -> "geography", typeName
                        | _ -> 
                            let datatype = if typeName = "tinyint" then typeof<byte>.FullName else x.Field( "DataType")
                            typeName, datatype
            ] 

        for row in conn.GetSchema("Tables", restrictionValues = [| null; null; null; "BASE TABLE" |]).Rows do   

            let schema, name = row ? table_schema, row ? table_name
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
                        let columns = conn.GetSchema("Columns", restrictionValues = [| null; schema; name; null |]).Rows
                        for c in columns do
                            let colName = c ? column_name
                            let dataType = 
                                let typeName = c ? data_type
                                Type.GetType(types.[typeName], throwOnError = true)
                            let isNullable = c ? is_nullable
                            let clrType = 
                                if c ? is_nullable = "YES" && dataType.IsValueType
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

