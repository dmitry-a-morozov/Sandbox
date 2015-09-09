namespace FSharp.Data.Entity

open System
open System.Reflection
open System.IO
open System.Data
open System.Data.SqlClient
open System.Collections.Generic

open System.ComponentModel.DataAnnotations
open System.ComponentModel.DataAnnotations.Schema
open Microsoft.Data.Entity

open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations

open ProviderImplementation.ProvidedTypes

open Inflector

open FSharp.Data.Entity.DesignTime

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
        [ property :> MemberInfo; backingField :> _ ]

    do 
        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("ConnectionString", typeof<string>) 
                ProvidedStaticParameter("Pluralize", typeof<bool>, false) 
                ProvidedStaticParameter("SuppressForeignKeyProperties", typeof<bool>, false) 
            ],             
            instantiationFunction = (fun typeName args ->   
                this.CreateDbContextType(typeName, unbox args.[0], unbox args.[1])
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

    member internal this.CreateDbContextType( typeName, connectionString, pluralize) = 
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

        let sqlServerSchema = DesignTime.SqlServer.getSqlServerSchema connectionString

        this.AddEntityTypesAndDataSets(dbContextType, sqlServerSchema, pluralize)

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
                let defaultConfiguration = sqlServerSchema.ModelConfiguration
                <@@ 
                    let modelBuilder: ModelBuilder = %%args.[1]
                    let dbContext: DbContext = %%Expr.Coerce(args.[0], typeof<DbContext>)
                    let entityTypeNames = dbContext.GetType().GetNestedTypes() |> Array.map (fun x -> x.FullName)
                    %defaultConfiguration <| (entityTypeNames, modelBuilder)
                    let modelCreating = %%Expr.FieldGet(args.[0], field)
                    if box modelCreating <> null
                    then 
                        modelCreating modelBuilder
                @@>
            dbContextType.DefineMethodOverride(impl, vTableHandle)

        dbContextType

    member internal this.AddEntityTypesAndDataSets(dbConTextType: ProvidedTypeDefinition, schema: IInformationSchema, pluralize) = 
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
                                for name, clrType in schema.GetColumns(tableName) do

                                    yield! getAutoProperty(name, clrType)

//                                for fk in schema.GetForeignKeys(tableName) do   
//                                    let parentEntityName = sprintf "%s.%s" fk.ParentTableSchema fk.ParentTable
//                                    let parent: ProvidedTypeDefinition = downcast dbConTextType.GetNestedType( parentEntityName) 
//
//                                    yield! getAutoProperty(fk.ParentTable.Pluralize(), parent)
//                                    
//                                    parent.AddMembersDelayed <| fun () -> 
//                                        let collectionType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ List>, [ tableType ])
//                                        let table = tableName.Split('.').[1]
//                                        getAutoProperty(table.Pluralize(), collectionType)
                            ]

                    yield tableType
            ]
            
            tempAssembly.AddTypes entityTypes

            let props = [
                for e in entityTypes do
                    let name = if pluralize then e.Name.Pluralize() else e.Name
                    let t = ProvidedTypeBuilder.MakeGenericType( typedefof<_ DbSet>, [ e ])
                    yield! getAutoProperty(name, t)
            ]

            [ for x in entityTypes -> x :> MemberInfo ] @ props

[<assembly:TypeProviderAssembly()>]
do()

