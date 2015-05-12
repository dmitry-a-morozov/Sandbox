namespace TPCacheMisses

open ProviderImplementation.ProvidedTypes
open Microsoft.FSharp.Core.CompilerServices
open System.Reflection
open System
open System.Dynamic
open System.Collections.Generic
open Microsoft.FSharp.Quotations

[<TypeProvider>]
type InstantiationFunctionProvider (config : TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces ()

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.LoadFrom( config.RuntimeAssembly)
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "InstantiationFunction", Some typeof<obj>, HideObjectMethods = true)
    
    let instantiationFunctionInvocations = Dictionary()

    do 
        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("PropertyName", typeof<string>) 
                ProvidedStaticParameter("PropertyType", typeof<string>, "System.Int32") 
            ],             
            instantiationFunction = (fun typeName [| :? string as propertyName; :? string as propertyTypeName |] ->


                let counter = 
                    match instantiationFunctionInvocations.TryGetValue typeName with
                    | true, x -> x 
                    | false, _ -> 
                        let x = ref 0 
                        instantiationFunctionInvocations.Add(typeName, x)
                        x

                incr counter
                System.Diagnostics.Debug.Assert(!counter <= 1, sprintf "instantiationFunction invoked times: [%i].\nTypename: [%s]" !counter typeName)
                
                let rootType = ProvidedTypeDefinition(assembly, nameSpace, typeName, Some typeof<ExpandoObject>, HideObjectMethods = true)
                let propertyType = Type.GetType propertyTypeName
                let ctor = ProvidedConstructor [ ProvidedParameter("PropertyValue", propertyType) ]
                ctor.InvokeCode <- fun [ propertyValue ] -> 
                    <@@ 
                        let self: IDictionary<string, obj> = upcast ExpandoObject()
                        self.Add(propertyName, %%Expr.Coerce(propertyValue, typeof<obj>))
                        self
                    @@>
                rootType.AddMember ctor
                let property = ProvidedProperty(propertyName, propertyType)
                property.GetterCode <- fun [ self ] -> 
                    <@@ 
                        let dict : IDictionary<_, _> = upcast (%%self: ExpandoObject)
                        dict.[propertyName] 
                    @@>
                rootType.AddMember property 

//                let invocationTrace = 
//                    String.concat "\n" [
//                        for KeyValue(typeName, timeStamps) in instantiationFunctionInvocations -> 
//                            timeStamps
//                            |> Seq.map (fun x -> x.ToString("HH:mm:ss.f"))
//                            |> String.concat "," 
//                            |> sprintf "Typename: %s. Timestamps: %s" typeName 
//                    ]
//
//                rootType.AddMember <| ProvidedProperty("InvocationsTrace", typeof<string>, GetterCode = fun _ -> <@@ invocationTrace @@>)

                rootType
            ) 
            
        )

    do
        this.AddNamespace(nameSpace, [ providerType ])

[<assembly:TypeProviderAssembly>]
do ()

