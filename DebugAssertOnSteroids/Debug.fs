module FSharp.Diagnostics 

open System.Diagnostics
open FSharp.Quotations
open System.Runtime.CompilerServices
open Patterns
open DerivedPatterns
open System
open FSharp.Linq.RuntimeHelpers

type Debug =

    [<ConditionalAttribute("DEBUG")>]
    static member Assert([<ReflectedDefinition>] condition: Expr<bool>, [<CallerFilePath>]?filePath: string, [<CallerLineNumber>]?line: int) : unit =
        
        let rec prettyPrinty e = 
            match e with 
            | SpecificCall <@ (=) @> (None, _, [ lhs;  rhs ]) ->
                sprintf "%s = %s" (prettyPrinty lhs) (prettyPrinty rhs)
            | SpecificCall <@ (<) @> (None, _, [ lhs;  rhs ]) ->
                sprintf "%s < %s" (prettyPrinty lhs) (prettyPrinty rhs)
            | SpecificCall <@ (>) @> (None, _, [ lhs;  rhs ]) ->
                sprintf "%s > %s" (prettyPrinty lhs) (prettyPrinty rhs)
            | SpecificCall <@ (<>) @> (None, _, [ lhs;  rhs ]) ->
                sprintf "%s <> %s" (prettyPrinty lhs) (prettyPrinty rhs)
            | SpecificCall <@ (>=) @> (None, _, [ lhs;  rhs ]) ->
                sprintf "%s >= %s" (prettyPrinty lhs) (prettyPrinty rhs)
            | SpecificCall <@ (<=) @> (None, _, [ lhs;  rhs ]) ->
                sprintf "%s <= %s" (prettyPrinty lhs) (prettyPrinty rhs)
            | SpecificCall <@ not @> (None, _, [ x ]) ->
                sprintf "not %s" (prettyPrinty x) 
            | PropertyGet( Some( self), prop, args) -> 
                sprintf "%s.%s" (prettyPrinty self) prop.Name
            | Call(None, method, xs) ->
                let args = xs |> List.map prettyPrinty |> String.concat","
                let invocation = 
                    if Char.IsLower(method.Name.[0]) && xs.Length = 1
                    then sprintf "%s %s" method.Name (prettyPrinty xs.Head)
                    else sprintf "%s(%s)" method.Name args
                sprintf "%s.%s" method.DeclaringType.Name invocation
            | OrElse (lhs, rhs) ->
                sprintf "%s || %s" (prettyPrinty lhs) (prettyPrinty rhs)
            | AndAlso (lhs, rhs) ->
                sprintf "%s && %s" (prettyPrinty lhs) (prettyPrinty rhs)
            | ValueWithName(_, _, name) -> 
                name
            | Value(x, _) -> 
                sprintf "%A" x 
            | _ -> "" 

        let evalCondition = LeafExpressionConverter.EvaluateQuotation(condition) :?> _
        if not evalCondition
        then 
            let expr = prettyPrinty condition
            let location = sprintf "\nat %s:line %i" filePath.Value line.Value
            let message = sprintf "Assertion (%s) failed%s" expr location
            System.Diagnostics.Debug.Assert( evalCondition, message)
