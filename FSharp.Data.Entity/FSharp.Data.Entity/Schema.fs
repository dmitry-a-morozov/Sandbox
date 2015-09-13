namespace FSharp.Data.Entity.DesignTime

open System
open FSharp.Quotations
open Microsoft.Data.Entity

type internal ForeignKey = {
    Name: string
    //Ordinal: int
    Column: string
    ParentTableSchema: string
    ParentTable: string
    ParentTableColumn: string
}

type internal IInformationSchema = 
    abstract GetTables: unit -> string[] 
    abstract GetColumns: tableName: string -> (string * Type)[]
    abstract GetForeignKeys : tableName: string -> (string * string)[]
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

