namespace FSharp.Data.Entity.DesignTime

open System
open FSharp.Quotations
open Microsoft.Data.Entity

type IInformationSchema = 
    abstract GetTables: unit -> string[] 
    abstract GetColumns: tableName: string -> (string * Type)[]
    abstract ModelConfiguration: Expr<string[] * ModelBuilder -> unit>

//type Column = {
//    Name: string
//    DbTypeName: Type
//    IsNullable: bool
//    IsIdentity: bool
//    IsReadOnly: bool
//    IsPartOfPrimaryKey: bool
//    DefaultValue: obj option
//}
//
//type ForeignKey = {
//    Name: string
//    Ordinal: int
//    Column: string
//    ParentTableSchema: string
//    ParentTable: string
//    ParentTableColumn: string
//}
//
