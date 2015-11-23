module CustomAttributeData

open Xunit
open System.ComponentModel.DataAnnotations
open System.ComponentModel.DataAnnotations.Schema
open System.Reflection
open FSharp.Quotations.Patterns

//let (|CustomAttributeData|) expr = 
//    match expr with 
//    | NewObject(ctor, args) ->  
//        CustomAttributeData(
//            AttributeType = ctor.DeclaringType,
//            Constructor = ctor,
//        )

//[<Fact>]
//let parse() = 
//    let tableName = TableAttribute("HumanResources")
//    let tableAttrExpr = tableName.
//
//
//
