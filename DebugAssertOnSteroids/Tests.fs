namespace global

open System
open System.Diagnostics
open Xunit
open Xunit.Abstractions
open FSharp.Diagnostics
open System.Linq

module MyFunc = 
    let even x = x % 2 = 0

type Tests(output: ITestOutputHelper) = 

    do
        //(Trace.Listeners.[0] :?> DefaultTraceListener).AssertUiEnabled <- true
        Trace.Listeners.Clear()
        Trace.Listeners.Add <| {
            new DefaultTraceListener(AssertUiEnabled = false) with
                member __.Fail( message) = 
                    raise <| Exception(message)
        } |> ignore

    let extractCondition (s: string) = 
        let s = s.Split('\n').[0]
        assert s.StartsWith("Assertion ")
        assert s.EndsWith(" failed")
        s.Substring("Assertion ".Length, s.Length - " failed".Length - "Assertion ".Length)

    [<Fact>]
    member __.TrueLiteral() =
        Debug.Assert(true)

    [<Fact>]
    member __.FalseLiteral() =
        let err = Assert.Throws<Exception>( fun() ->  Debug.Assert(false))
        Assert.Equal("false", extractCondition err.Message)

    [<Fact>]
    member __.FalseVar() =
        let isTrue = false
        let err =  Assert.Throws<Exception>( fun() ->  Debug.Assert(isTrue))
        Assert.Equal("isTrue", extractCondition err.Message)

    [<Fact>]
    member __.FalseLiteralEquality() =
        let err =  Assert.Throws<Exception>( fun() ->  Debug.Assert(42 = 0))
        Assert.Equal("42 = 0", extractCondition err.Message)

    [<Fact>]
    member __.FalseLiteralLessMore() =
        Assert.Equal(
            "42 < 0", 
            let err = Assert.Throws<Exception>( fun() ->  Debug.Assert(42 < 0)) in extractCondition err.Message
        )
        Assert.Equal(
            "42 > 100", 
            let err = Assert.Throws<Exception>( fun() ->  Debug.Assert(42 > 100)) in extractCondition err.Message
        )
        Assert.Equal(
            "42 <= 0", 
            let err = Assert.Throws<Exception>( fun() ->  Debug.Assert(42 <= 0)) in extractCondition err.Message
        )
        Assert.Equal(
            "42 >= 100", 
            let err = Assert.Throws<Exception>( fun() ->  Debug.Assert(42 >= 100)) in extractCondition err.Message
        )

    [<Fact>]
    member __.And() =
        let x = true
        let y = false

        Assert.Equal(
            "x && y", 
            let err = Assert.Throws<Exception>( fun() ->  Debug.Assert(x && y)) in extractCondition err.Message
        )

        Assert.Equal(
            "true && false", 
            let err = Assert.Throws<Exception>( fun() ->  Debug.Assert(true && false)) in extractCondition err.Message
        )

        Assert.Equal(
            "x && y && false", 
            let err = Assert.Throws<Exception>( fun() ->  Debug.Assert(x && y && false)) in extractCondition err.Message
        )

    [<Fact>]
    member __.Not() =
        let x = true
        let y = true

        Assert.Equal(
            "x && not y", 
            let err = Assert.Throws<Exception>( fun() ->  Debug.Assert(x && not y)) in extractCondition err.Message
        )

        Assert.Equal(
            "true && not true", 
            let err = Assert.Throws<Exception>( fun() ->  Debug.Assert(true && not true)) in extractCondition err.Message
        )

    [<Fact>]
    member __.CustomFunctions() =
        Assert.Equal(
            "MyFunc.even 3", 
            let err = Assert.Throws<Exception>( fun() ->  Debug.Assert(MyFunc.even 3)) in extractCondition err.Message
        )

    [<Fact>]
    member __.StaticMethods() =
        let xs  = [| 1..3 |]
        Assert.Equal(
            "Enumerable.Count(xs) > 3", 
            let err = Assert.Throws<Exception>( fun() ->  Debug.Assert(xs.Count() > 3)) in extractCondition err.Message
        )

    [<Fact>]
    member __.InstanceProps() =
        let s  = "Hello"
        Assert.Equal(
            "s.Length = 2", 
            let err = Assert.Throws<Exception>( fun() ->  Debug.Assert(s.Length = 2)) in extractCondition err.Message
        )

    [<Fact>]
    member __.Collections() =
        let xs = [ 1 .. 3 ]
        let ys = [ 6 .. 7 ]
        Assert.Equal(
            "xs = ys", 
            let err = Assert.Throws<Exception>( fun() ->  Debug.Assert((xs = ys))) in extractCondition err.Message
        )

    //[<Fact>]
    //member __.Or() =
    //    let x = false
    //    let y = false

    //    Assert.Equal(
    //        "x || y", 
    //        let err = Assert.Throws<Exception>( fun() ->  Debug.Assert(x || y)) in extractCondition err.Message
    //    )

    //    Assert.Equal(
    //        "false || false", 
    //        let err = Assert.Throws<Exception>( fun() ->  Debug.Assert(false || false)) in extractCondition err.Message
    //    )

    //    Assert.Equal(
    //        "x || y || false", 
    //        let err = Assert.Throws<Exception>( fun() ->  Debug.Assert(x || y || false)) in extractCondition err.Message
    //    )
