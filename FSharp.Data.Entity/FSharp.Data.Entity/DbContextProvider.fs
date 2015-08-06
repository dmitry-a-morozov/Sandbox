namespace FSharp.Data.Entity

open System.Reflection
open System.IO
open Microsoft.Data.Entity
open Microsoft.FSharp.Core.CompilerServices
open ProviderImplementation.ProvidedTypes

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

    do tempAssembly.AddTypes [ providerType ]

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
    
    member internal this.CreateRootType( typeName, connectionString) = 
        let rootType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<DbContext>, HideObjectMethods = true, IsErased = false)
        
        let ctor = ProvidedConstructor([])
        ctor.BaseConstructorCall <-
            let baseCtor = typeof<DbContext>.GetConstructor(BindingFlags.NonPublic, null, [||], null)
            fun _ -> baseCtor, []
        rootType.AddMember ctor

        rootType.AddMembersDelayed <| this.GetEntities

        rootType

    member internal this.GetEntities() = 
        []

[<assembly:TypeProviderAssembly()>]
do()
