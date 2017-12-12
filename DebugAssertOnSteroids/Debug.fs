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
        
        let rec prettyPrintExpr e = 
            match e with 
            | SpecificCall <@ (=) @> (None, _, [ lhs;  rhs ]) ->
                sprintf "%s = %s" (prettyPrintExpr lhs) (prettyPrintExpr rhs)
            | SpecificCall <@ (<) @> (None, _, [ lhs;  rhs ]) ->
                sprintf "%s < %s" (prettyPrintExpr lhs) (prettyPrintExpr rhs)
            | SpecificCall <@ (>) @> (None, _, [ lhs;  rhs ]) ->
                sprintf "%s > %s" (prettyPrintExpr lhs) (prettyPrintExpr rhs)
            | SpecificCall <@ (<>) @> (None, _, [ lhs;  rhs ]) ->
                sprintf "%s <> %s" (prettyPrintExpr lhs) (prettyPrintExpr rhs)
            | SpecificCall <@ (>=) @> (None, _, [ lhs;  rhs ]) ->
                sprintf "%s >= %s" (prettyPrintExpr lhs) (prettyPrintExpr rhs)
            | SpecificCall <@ (<=) @> (None, _, [ lhs;  rhs ]) ->
                sprintf "%s <= %s" (prettyPrintExpr lhs) (prettyPrintExpr rhs)
            | SpecificCall <@ not @> (None, _, [ x ]) ->
                sprintf "not %s" (prettyPrintExpr x) 
            | Call(None, method, xs) ->
                let args = xs |> List.map prettyPrintExpr |> String.concat","
                let invocation = 
                    if Char.IsLower(method.Name.[0]) && xs.Length = 1
                    then sprintf "%s %s" method.Name (prettyPrintExpr xs.Head)
                    else sprintf "%s(%s)" method.Name args
                sprintf "%s.%s" method.DeclaringType.Name invocation
            | AndAlso (lhs, rhs) ->
                sprintf "%s && %s" (prettyPrintExpr lhs) (prettyPrintExpr rhs)
            | OrElse (lhs, rhs) ->
                sprintf "%s || %s" (prettyPrintExpr lhs) (prettyPrintExpr rhs)
            | ValueWithName(:? bool as x, _, name) -> name
            | Value(x, _) ->  sprintf "%A" x 
            | _ -> "Unknown" 


        if not (LeafExpressionConverter.EvaluateQuotation(condition) :?> _)
        then 
            let expr = prettyPrintExpr condition
            let location = sprintf "\nat %s:line %i" filePath.Value line.Value

            let condition = LeafExpressionConverter.EvaluateQuotation(condition) |> unbox

            let message = sprintf "Assertion %s failed%s" expr location
            System.Diagnostics.Debug.Assert( condition, message)

   //at <StartupCode$DebugAssertOnSteroids>.$Tests.-cctor@14.Fail(String message) in C:\Users\dmorozov\Documents\GitHub\Sandbox\DebugAssertOnSteroids\Tests.fs:line 16
   //at System.Diagnostics.TraceInternal.Fail(String message)
   //at System.Diagnostics.Debug.Assert(Boolean condition, String message)
   //at FSharp.Diagnostics.Debug.Assert(FSharpExpr`1 condition, FSharpOption`1 filePath, FSharpOption`1 line) in C:\Users\dmorozov\Documents\GitHub\Sandbox\DebugAssertOnSteroids\Debug.fs:line 24
   //at <StartupCode$DebugAssertOnSteroids>.$Tests.err@25.Invoke() in C:\Users\dmorozov\Documents\GitHub\Sandbox\DebugAssertOnSteroids\Tests.fs:line 25
   //at Xunit.Assert.RecordException(Action testCode) in C:\Dev\xunit\xunit\src\xunit.assert\Asserts\Record.cs:line 27            