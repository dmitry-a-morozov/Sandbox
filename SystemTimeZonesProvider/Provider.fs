namespace System

open System.Reflection
open Microsoft.FSharp.Core.CompilerServices

open ProviderImplementation.ProvidedTypes

[<TypeProvider>]
type public SystemTimeZonesProvider(config : TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.LoadFrom( config.RuntimeAssembly)

    let t = ProvidedTypeDefinition(assembly, nameSpace, "SystemTimeZones", Some typeof<obj>, HideObjectMethods = true)

    do 
        t.AddMembers [
            for x in TimeZoneInfo.GetSystemTimeZones() do
                let id = x.Id
                yield ProvidedProperty(x.DisplayName, typeof<TimeZoneInfo>, [], IsStatic = true, GetterCode = fun _ -> <@@ TimeZoneInfo.FindSystemTimeZoneById id @@>)
        ]

        this.AddNamespace(nameSpace, [ t ])

open System.Runtime.CompilerServices

[<assembly:TypeProviderAssembly>]
[<assembly:AssemblyVersion("0.1.0.0")>]
do ()

    
