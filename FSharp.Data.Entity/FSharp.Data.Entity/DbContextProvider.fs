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

    let getAutoProperty(name: string, clrType) = 
        let backingField = ProvidedField(name.Camelize(), clrType)
        let property = ProvidedProperty(name, clrType)
        property.GetterCode <- fun args -> Expr.FieldGet( args.[0], backingField)
        property.SetterCode <- fun args -> Expr.FieldSet( args.[0], backingField, args.[1])
        [ backingField :> MemberInfo; property :> _ ]

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

        let sqlServerSchema = DesignTime.Schemas.getSqlServerSchema connectionString

        this.AddEntityTypesAndDataSets(dbContextType, sqlServerSchema)

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

                        RelationalEntityTypeBuilderExtensions.ToTable(
                            modelBuilder.Entity(entity),
                            tableName, 
                            schema
                        )
                        |> ignore

                    let modelCreating = %%Expr.FieldGet(args.Head, field)
                    if box modelCreating <> null
                    then modelCreating modelBuilder
                @@>
            dbContextType.DefineMethodOverride(impl, vTableHandle)

        dbContextType

    member internal this.AddEntityTypesAndDataSets(dbConTextType: ProvidedTypeDefinition, schema: DesignTime.Schemas.IInformationSchema) = 
        dbConTextType.AddMembersDelayed <| fun () ->
            let entityTypes = [

                for tableName in schema.GetTables() do   

                    let tableType = ProvidedTypeDefinition(tableName , baseType = None, IsErased = false)

                    do 
                        let ctor = ProvidedConstructor([], InvokeCode = fun _ -> <@@ () @@>)
                        ctor.BaseConstructorCall <- fun args -> typeof<obj>.GetConstructor([||]), args
                        tableType.AddMember ctor

                    do 
                        tableType.AddMembersDelayed <| fun() -> 
                            [
                                for c in schema.GetColumns(tableName) do
                                    let clrType = 
                                        if c.IsNullable && c.Type.IsValueType
                                        then ProvidedTypeBuilder.MakeGenericType(typedefof<_ Nullable>, [ c.Type ])
                                        else c.Type

                                    yield! getAutoProperty(c.Name, clrType)
        
//                                for fk in schema.GetForeignKeys(tableName) do
//                                    let parent: ProvidedTypeDefinition = downcast dbConTextType.GetNestedType( tableName) 
//                                    yield! getAutoProperty(parent.Name, parent)
//                                    
//                                    parent.AddMembersDelayed <| fun () -> 
//                                        let collectionType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ List>, clr
//                                    let backingField = ProvidedField(fk.Name.Camelize(), parent)
//                                    let prop = ProvidedProperty(fk.Name, parent)
//                                    yield upcast backingField
//                                    yield upcast property
//                                    parent.AddMemberDelayed <| fun() -> 

                            ]

                    yield tableType
            ]
            
            tempAssembly.AddTypes entityTypes

            let props = [
                for e in entityTypes do
                    let field, prop = this.GetDbSetPropAndField(e)
                    yield field :> MemberInfo
                    yield prop :> _
            ]

            [ for x in entityTypes -> x :> MemberInfo ] @ props

    member internal this.GetDbSetPropAndField(entityType: ProvidedTypeDefinition) = 
        let ``type`` = ProvidedTypeBuilder.MakeGenericType( typedefof<_ DbSet>, [ entityType ])
        let name = entityType.Name.Pluralize()
        let field = ProvidedField(name, ``type``)
        let prop = ProvidedProperty(name, ``type``)
        prop.GetterCode <- fun args -> Expr.FieldGet(args.[0], field)
        prop.SetterCode <- fun args -> Expr.FieldSet(args.[0], field, args.[1])
        field, prop

        

[<assembly:TypeProviderAssembly()>]
do()

