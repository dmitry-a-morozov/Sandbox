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

open FSharp.Data.Entity.SqlServer

[<AutoOpen; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ProvidedTypes = 
    let inline addCustomAttribute<'T, ^P when 'T :> Attribute and ^P : (member AddCustomAttribute : System.Reflection.CustomAttributeData -> unit)> (provided: ^P, ctorArgs: obj list, namedArgs: list<string * obj>) = 
        let attrData = { 
            new CustomAttributeData() with
                member __.Constructor = typeof<'T>.GetConstructor [| for value in ctorArgs -> value.GetType() |]
                member __.ConstructorArguments = upcast [| for value in ctorArgs -> CustomAttributeTypedArgument value |]
                member __.NamedArguments = 
                    upcast [| 
                        for propName, value in namedArgs do 
                            let property = typeof<'T>.GetProperty propName
                            yield CustomAttributeNamedArgument(property, value) 
                    |] 
        }
        (^P : (member AddCustomAttribute : System.Reflection.CustomAttributeData -> unit) (provided, attrData))

[<TypeProvider>]
type public SqlServerDbContextTypeProvider(config: TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.LoadFrom( config.RuntimeAssembly)
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "SqlServer", Some typeof<obj>, HideObjectMethods = true, IsErased = false)

    let getProvidedAssembly() = 
        let assemblyFileName = Path.ChangeExtension( Path.GetTempFileName(), "dll")
        ProvidedAssembly( assemblyFileName)

    let addToProvidedTempAssembly types = 
        getProvidedAssembly().AddTypes types

    let getAutoProperty(name: string, clrType) = 
        let backingField = ProvidedField(name.Camelize(), clrType)
        let property = ProvidedProperty(name, clrType)
        property.GetterCode <- fun args -> Expr.FieldGet( args.[0], backingField)
        property.SetterCode <- fun args -> Expr.FieldSet( args.[0], backingField, args.[1])
        property, backingField 

    let getAutoPropertyAsList(name, clrType): MemberInfo list = 
        let p, f = getAutoProperty(name, clrType)
        [ p; f ]

    do
        addToProvidedTempAssembly [ providerType ]

    do 
        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("ConnectionString", typeof<string>) 
                ProvidedStaticParameter("Pluralize", typeof<bool>, false) 
                ProvidedStaticParameter("SuppressForeignKeyProperties", typeof<bool>, false) 
            ],             
            instantiationFunction = (fun typeName args ->   
                this.CreateDbContextType(typeName, unbox args.[0], unbox args.[1], unbox args.[2])
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

    member internal this.CreateDbContextType( typeName, connectionString, pluralize, suppressForeignKeyProperties) = 
        let dbContextType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<DbContext>, HideObjectMethods = true, IsErased = false)

        do
            addToProvidedTempAssembly [ dbContextType ]

        do 
            dbContextType.AddMembersDelayed <| fun() ->
                [
                    let parameterlessCtor = ProvidedConstructor([], InvokeCode = fun _ -> <@@ () @@>)
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

        //use conn = new SqlConnection( connectionString)

        this.AddEntityTypesAndDataSets(dbContextType, connectionString, pluralize, suppressForeignKeyProperties)

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
                use conn = new SqlConnection( connectionString)
                conn.Open()
                
                let pks = 
                    let elements = [ 
                        for pk in conn.GetAllPrimaryKeys() do 
                            let table = Expr.Value( pk.Table.TwoPartName)
                            let columns = Expr.NewArray( typeof<string>, [ for col in pk.Columns -> Expr.Value( col) ])
                            yield Expr.NewTuple [ table; columns ] 
                    ]
                    Expr.NewArray(typeof<string * string[]>, elements)
                    
                let defaultConfiguration = 
                    <@ 
                        fun (entityNames, modelBuilder) ->
                            Runtime.primaryKeysConfiguration %%pks (entityNames, modelBuilder)
                    @>

                <@@ 
                    let modelBuilder: ModelBuilder = %%args.[1]
                    let dbContext: DbContext = %%Expr.Coerce(args.[0], typeof<DbContext>)
                    let entityTypes = 
                        dbContext.GetType().GetNestedTypes() |> Array.filter (fun t -> t.IsDefined(typeof<TableAttribute>))
                    %defaultConfiguration <| (entityTypes, modelBuilder)
                    let modelCreating = %%Expr.FieldGet(args.[0], field)
                    if box modelCreating <> null
                    then 
                        modelCreating modelBuilder
                @@>
            dbContextType.DefineMethodOverride(impl, vTableHandle)


        dbContextType

    member internal this.AddEntityTypesAndDataSets(dbConTextType: ProvidedTypeDefinition, connectionString, pluralize, suppressForeignKeyProperties) = 
        dbConTextType.AddMembersDelayed <| fun () ->
            
            use conn = new SqlConnection( connectionString)
            conn.Open()

            let entityTypes = [

                for table in conn.GetTables() do   
                   
                    let twoPartTableName = table.TwoPartName
                    let tableType = ProvidedTypeDefinition( twoPartTableName, baseType = Some typeof<obj>, IsErased = false)
                    addCustomAttribute<TableAttribute, _>(tableType, [ table.Name ], [ "Schema", box table.Schema ])

                    do //Tables
                        let ctor = ProvidedConstructor([], InvokeCode = fun _ -> <@@ () @@>)
                        ctor.BaseConstructorCall <- fun args -> typeof<obj>.GetConstructor([||]), args
                        tableType.AddMember ctor

                    do 
                        tableType.AddMembersDelayed <| fun() -> 
                            [
                                use conn = new SqlConnection( connectionString)
                                conn.Open()
                                
                                let unsupported = set [ "hierarchyid"; "sql_variant"; "geography"; "geometry" ]

                                for col in conn.GetColumns( table) do
                                    if not(unsupported.Contains col.DataType)
                                    then 
                                        let prop, field  = getAutoProperty( col.Name, col.ClrType)
                                        if col.DataType = "timestamp"
                                        then 
                                            addCustomAttribute<DatabaseGeneratedAttribute, _>(prop, [ DatabaseGeneratedOption.Computed ], [])

                                        yield prop :> MemberInfo
                                        yield upcast field

//
//                                if not suppressForeignKeyProperties
//                                then 
//                                    for fk in conn.GetForeignKeys( table) do   
//                                        let parent: ProvidedTypeDefinition = downcast dbConTextType.GetNestedType( fk.Parent.TwoPartName) 
//                                        let prop, field = ProvidedProperty.Auto( fk.Name, parent)
//                                        let columns = fk.Columns |> String.concat ","
//                                        addCustomAttribute<ForeignKeyAttribute, _>(prop, [ columns ], [])
//                                        addCustomAttribute<InversePropertyAttribute, _>(prop, [ table.Name ], [])
//                                        
//                                        yield prop :> _
//                                        yield field :> _
//                                    
//                                        parent.AddMembersDelayed <| fun () -> 
//                                            let collectionType = ProvidedTypeBuilder.MakeGenericType(typedefof<_ List>, [ tableType ])
//                                            let prop, field = ProvidedProperty.Auto( table.Name, collectionType)
//                                            [ prop :> MemberInfo; field :> _ ]
                            ]

                    yield tableType
            ]
            
            do  
                addToProvidedTempAssembly entityTypes 

            let props = [
                for e in entityTypes do
                    let name = if pluralize then e.Name.Pluralize() else e.Name
                    let t = ProvidedTypeBuilder.MakeGenericType( typedefof<_ DbSet>, [ e ])
                    yield! getAutoPropertyAsList( name, t)
            ]

            [ for x in entityTypes -> x :> MemberInfo ] @ props

[<assembly:TypeProviderAssembly()>]
do()

