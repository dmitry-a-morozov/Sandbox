namespace global

open System
open System.Diagnostics
open Xunit
open Xunit.Abstractions
open FSharp.Diagnostics

type Tests(output: ITestOutputHelper) = 

    static do
        //(Trace.Listeners.[0] :?> DefaultTraceListener).AssertUiEnabled <- true
        if Trace.Listeners.Count = 1 && Trace.Listeners.[0].GetType() = typeof<DefaultTraceListener>
        then 
            Trace.Listeners.Clear()
            Trace.Listeners.Add <| {
                new DefaultTraceListener(AssertUiEnabled = false) with
                    member __.Fail( message) = raise <| Exception(message)
            } |> ignore

    [<Fact>]
    member __.TrueLiteral() =
        Debug.Assert(true)

    [<Fact>]
    member __.FalseLiteral() =
        let err =  Assert.Throws<Exception>( fun() ->  Debug.Assert(false))
        output.WriteLine(err.Message)

    [<Fact>]
    member __.AlwaysFalse() =
        let err =  Assert.Throws<Exception>( fun() ->  Debug.Assert(false))
        output.WriteLine(err.Message)

    [<Fact>]
    member __.FalseVar() =
        let cond = false
        let err =  Assert.Throws<Exception>( fun() ->  Debug.Assert(cond))
        output.WriteLine(err.Message)

    [<Fact>]
    member __.FalseLiteralEquality() =
        let err =  Assert.Throws<Exception>( fun() ->  Debug.Assert(42 = 0))
        output.WriteLine(err.Message)

    [<Fact>]
    member __.FalseLiteralLess() =
        let err =  Assert.Throws<Exception>( fun() ->  Debug.Assert(42 < 0))
        output.WriteLine(err.Message)

    [<Fact>]
    member __.FalseLiteralBigger() =
        let err =  Assert.Throws<Exception>( fun() ->  Debug.Assert(42 > 1000))
        output.WriteLine(err.Message)
