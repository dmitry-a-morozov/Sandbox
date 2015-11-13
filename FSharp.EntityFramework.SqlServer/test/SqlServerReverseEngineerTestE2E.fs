﻿module SqlServerReverseEngineerTestE2E

open System
open Microsoft.SqlServer.Types
open Xunit
open FSharp.Data.Entity
open Microsoft.Data.Entity.Metadata

type DB = SqlServer<"Data Source=.;Initial Catalog=SqlServerReverseEngineerTestE2E;Integrated Security=True">
let db = new DB()

[<Fact>]
let AllDataTypes() = 
    let expected = [|
        typeof<int>, "AllDataTypesID"
        typeof<int64>, "bigintColumn"
        typeof<byte[]>, "binaryColumn"
        typeof<bool>, "bitColumn"
        typeof<string>, "charColumn"
        typeof<DateTime>, "dateColumn"
        typeof<DateTime Nullable>, "datetime24Column"
        typeof<DateTime Nullable>, "datetime2Column"
        typeof<DateTime Nullable>, "datetimeColumn"
        typeof<DateTimeOffset Nullable>, "datetimeoffset5Column"
        typeof<DateTimeOffset Nullable>, "datetimeoffsetColumn"
        typeof<decimal>, "decimalColumn"
        typeof<double>, "floatColumn"
        //typeof<SqlGeography>, "geographyColumn"
        //typeof<SqlGeometry>, "geometryColumn"
        //typeof<SqlHierarchyId Nullable>, "hierarchyidColumn"
        typeof<byte[]>, "imageColumn"
        typeof<int>, "intColumn"
        typeof<decimal>, "moneyColumn"
        typeof<string>, "ncharColumn"
        typeof<string>, "ntextColumn"
        typeof<decimal>, "numericColumn"
        typeof<string>, "nvarcharColumn"
        typeof<float32 Nullable>, "realColumn"
        typeof<DateTime Nullable>, "smalldatetimeColumn"
        typeof<int16>, "smallintColumn"
        typeof<decimal>, "smallmoneyColumn"
        //typeof<obj>, "sql_variantColumn"
        typeof<string>, "textColumn"
        typeof<TimeSpan Nullable>, "time4Column"
        typeof<TimeSpan Nullable>, "timeColumn"
        typeof<byte[]>, "timestampColumn"
        typeof<byte>, "tinyintColumn"
        typeof<Guid Nullable>, "uniqueidentifierColumn"
        typeof<byte[]>, "varbinaryColumn"
        typeof<string>, "varcharColumn"
        //typeof<string>, "xmlColumn"
    |]

    let actual =    
        typeof<DB.``dbo.AllDataTypes``>.GetProperties() 
        |> Array.map(fun p -> p.PropertyType, p.Name)
        |> Array.sortBy snd

    Assert.Equal(expected.Length, actual.Length)

    for x, y in Array.zip expected actual do
        Assert.Equal<_>(x, y, LanguagePrimitives.FastGenericEqualityComparer)

    let entityType = db.Model.GetEntityType( typeof<DB.``dbo.AllDataTypes``>)

    Assert.Equal(
        ValueGenerated.OnAddOrUpdate, 
        entityType.GetProperty("timestampColumn").ValueGenerated
    )

    Assert.Equal(Nullable 1, entityType.GetProperty("binaryColumn").GetMaxLength()    )
    Assert.Equal(Nullable 1, entityType.GetProperty("varbinaryColumn").GetMaxLength())

    Assert.Equal(Nullable 1, entityType.GetProperty("charColumn").GetMaxLength())
    Assert.Equal(Nullable 1, entityType.GetProperty("ncharColumn").GetMaxLength())
    Assert.Equal(Nullable 1, entityType.GetProperty("nvarcharColumn").GetMaxLength())
    Assert.Equal(Nullable 1, entityType.GetProperty("varcharColumn").GetMaxLength())

[<Fact>]
let OneToManyDependent() = 
    let e = db.Model.GetEntityType( typeof<DB.``dbo.OneToManyDependent``>)
    Assert.Equal<_ list>(
        [ "OneToManyDependentID1"; "OneToManyDependentID2" ],
        [ for p in e.GetPrimaryKey().Properties -> p.Name ]
    )
    Assert.Equal(Nullable 20, e.GetProperty("SomeDependentEndColumn").GetMaxLength())
    Assert.False(e.GetProperty("SomeDependentEndColumn").IsNullable)
    

[<Fact>]
let OneToOneFKToUniqueKeyDependent() = 
    let e = db.Model.GetEntityType( typeof<DB.``dbo.OneToOneFKToUniqueKeyDependent``>)
    ()
