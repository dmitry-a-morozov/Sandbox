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

[<TypeProvider>]
type public DbContextProvider(config: TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.LoadFrom( config.RuntimeAssembly)
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "DbContext", Some typeof<obj>, HideObjectMethods = true, IsErased = false)

    let (?) (row: DataRow) (name: string) = row.Field name

    let tempAssembly = 
        let name = Path.ChangeExtension( Path.GetRandomFileName(), "dll")
        let fullPath = Path.Combine(config.TemporaryFolder, name)
        ProvidedAssembly fullPath

    do 
        tempAssembly.AddTypes [ providerType ]

//    let addGeneratedTypes types =  
//        let name = Path.ChangeExtension( Path.GetRandomFileName(), "dll")
//        let fullPath = Path.Combine(config.TemporaryFolder, name)
//        let tempAssembly = ProvidedAssembly fullPath
//        tempAssembly.AddTypes types

//    do 
//        addGeneratedTypes [ providerType ]

    do 
        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("ConnectionString", typeof<string>) 
            ],             
            instantiationFunction = (fun typeName args ->   
                this.CreateRootType(typeName, unbox args.[0])
            )        
        )

        this.AddNamespace( nameSpace, [ providerType ])
    
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

    member internal this.CreateRootType( typeName, connectionString) = 
        let rootType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<DbContext>, HideObjectMethods = true, IsErased = false)
        //let rootType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = None, HideObjectMethods = true, IsErased = false)
        tempAssembly.AddTypes [ rootType ]

        do
            let ctor = ProvidedConstructor([], IsImplicitCtor = true)
            let baseCtor = typeof<DbContext>.GetConstructor(BindingFlags.Instance ||| BindingFlags.NonPublic, null, [||], null)
            ctor.BaseConstructorCall <- fun args -> baseCtor, args
            rootType.AddMember ctor

//        rootType.AddMembersDelayed <| fun() ->
//            let xs = this.GetEntities( connectionString)
//            tempAssembly.AddTypes xs
//            xs

        rootType

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
                        | _ -> typeName, string x.["DataType"]
            ] 

        for row in conn.GetSchema("Tables", restrictionValues = [| null; null; null; "BASE TABLE" |]).Rows do   

            let schema, name = row ? table_schema, row ? table_name
            let tableType = ProvidedTypeDefinition(className = sprintf "%s.%s" schema name , baseType = None, IsErased = false)

            do 
                let ctor = ProvidedConstructor []
                ctor.BaseConstructorCall <- fun _ -> typeof<obj>.GetConstructor([||]), []
                //ctor.InvokeCode <- fun _ -> <@@ () @@>
                ctor.IsImplicitCtor <- true
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

                            let backingField = ProvidedField(colName, dataType)
                            yield backingField :> MemberInfo
                        
                            let property = ProvidedProperty(colName, dataType)
                            property.GetterCode <- fun args -> Expr.FieldGet( args.[0], backingField)
                            yield upcast property
                    ]

            yield tableType
    ]
        

[<assembly:TypeProviderAssembly()>]
do()

