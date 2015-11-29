module Jamie

open System
open Xunit
open FSharp.Data.Entity
open Microsoft.Data.Entity
open System.Linq
open Linq.NullableOperators

[<Fact>]
let query1() = 
    
    let db = AdventureWorks.db
    
    let xs =
        query {
            for soh in db.``Sales.SalesOrderHeaders`` do
            where (soh.OrderDate > new DateTime(2013, 5, 1))
            groupJoin sod in db.``Sales.SalesOrderDetails`` on (soh.SalesOrderID = sod.SalesOrderID) into xs
            for x in xs do
            groupJoin p in db.``Production.Products`` on (x.ProductID = p.ProductID) into ys
            for y in ys do
            where (query {
                for subcategory in [| 1; 2; 3 |] do
                exists(y.ProductSubcategoryID ?= subcategory)
            })
            select (x.SalesOrderID, xs.Count())
        }

    Assert.Equal(21680, xs.Count())

[<Fact>]
let query2() = 
    
    let db = AdventureWorks.db
    
    let xs =
        query {
            for soh in db.``Sales.SalesOrderHeaders`` do
            where (soh.OrderDate > new DateTime(2013, 5, 1))
            groupJoin sod in db.``Sales.SalesOrderDetails`` on (soh.SalesOrderID = sod.SalesOrderID) into xs
            for x in xs do
            groupJoin p in db.``Production.Products`` on (x.ProductID = p.ProductID) into ys
            for y in ys do
            where ([| 1; 2; 3 |].Any(fun x -> x =? y.ProductSubcategoryID))
            select (x.SalesOrderID, xs.Count())
        }

    Assert.Equal(21680, xs.Count())
