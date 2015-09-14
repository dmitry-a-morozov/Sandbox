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

    member internal this.CreateRootType( typeName, nestedTypeNames: string) = 
        let root = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true, IsErased = false)

        tempAssembly.AddTypes [ root ]

        root.AddMembersDelayed <| fun() ->
            let nestedTypes = 
                nestedTypeNames.Split(',')
                |> Array.map (fun typename -> 
                    let t = ProvidedTypeDefinition(typename, Some typeof<obj>, IsErased = false)
                    t.SetAttributes(root.Attributes ||| TypeAttributes.Abstract &&& ~~~TypeAttributes.Sealed)

                    t.AddMember <| ProvidedConstructor( [ ProvidedParameter("name",  typeof<int>) ], IsImplicitCtor = true)

                    t.AddMember <| ProvidedProperty("Greeeting", typeof<string>, GetterCode = fun args -> <@@ sprintf "Hello, I'm %s." %Expr.GlobalVar<int>("name") @@> )
                    
                    do
                        let parameters = [ ProvidedParameter("id", typeof<int>) ]
                        let m = ProvidedMethod("GetData", parameters, typeof<Async<seq<string>>>)
                        m.InvokeCode <- fun args -> <@@  raise( System.NotImplementedException()) @@>
                        m.SetMethodAttrs(m.Attributes ||| MethodAttributes.Abstract ||| MethodAttributes.Virtual)
                        root.AddMember m

                    t
                )
                |> Array.toList

            tempAssembly.AddTypes nestedTypes

            nestedTypes

        root

[<assembly:TypeProviderAssembly()>]
do()

