namespace ExnInDelayedCtorErasedTypes

open ProviderImplementation.ProvidedTypes
open Microsoft.FSharp.Core.CompilerServices

[<TypeProvider>]
type MyProvider(config: TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces ()

    let nameSpace = this.GetType().Namespace
    let assembly = System.Reflection.Assembly.LoadFrom( config.RuntimeAssembly)
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "MyType", None)

    do 
        providerType.AddMembersDelayed <| fun() -> [ failwith "Kaboom !!!" ]

        //exception within add single member delayed add also causes same obscure error message:  
        //No constructors are available for the type ...

        //providerType.AddMemberDelayed <| fun() -> failwith "Kaboom !!!"

        this.AddNamespace(nameSpace, [ providerType ])

[<assembly:TypeProviderAssembly>]
do ()