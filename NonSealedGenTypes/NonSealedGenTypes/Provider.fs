namespace NonSealedGenTypes

open System.Reflection
open System.IO
open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations

open ProviderImplementation.ProvidedTypes

[<TypeProvider>]
type public NonSealedTypesProvider(config: TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.LoadFrom( config.RuntimeAssembly)
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "RootProvider", Some typeof<obj>, HideObjectMethods = true, IsErased = false)

    let tempAssembly = 
        ProvidedAssembly( assemblyFileName = Path.ChangeExtension( Path.GetTempFileName(), "dll"))

    do 
        tempAssembly.AddTypes [ providerType ]

    do 
        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("NestedTypeNames", typeof<string>) 
            ],             
            instantiationFunction = (fun typeName args ->   
                this.CreateRootType(typeName, unbox args.[0])
            )        
        )

        this.AddNamespace( nameSpace, [ providerType ])

    member internal this.CreateRootType( typeName, nestedTypeNames) = 
        let root = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true, IsErased = false)

        do
            let ctor = ProvidedConstructor( [ ProvidedParameter("x",  typeof<int>) ], IsImplicitCtor = true)
            let baseCtor = typeof<obj>.GetConstructors().[0]
            let ps = baseCtor.GetParameters()
            ctor.BaseConstructorCall <- fun args -> baseCtor, [ args.Head ]
            root.AddMember ctor

        root.AddMember <| ProvidedProperty("X", typeof<int>, GetterCode = fun args -> <@@ Expr.GlobalVar<int>("x") @@> )
        tempAssembly.AddTypes [ root ]

        root.SetAttributes( root.Attributes &&& ~~~TypeAttributes.Sealed)

        root.AddMembersDelayed <| fun() ->
            [
            ]

        root

[<assembly:TypeProviderAssembly()>]
do()

