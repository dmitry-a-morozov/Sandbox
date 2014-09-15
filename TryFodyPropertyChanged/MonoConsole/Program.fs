open System
open System.ComponentModel
open PropertyChanged

[<ImplementPropertyChanged>]
type Person() = 
    member val GivenNames = "" with get, set
    member val FamilyName = "" with get, set
    member this.FullName = String.Format("{0} {1}", this.GivenNames, this.FamilyName);

let changes = ResizeArray()
let model = Person()
let inpc : INotifyPropertyChanged = model |> box |> unbox
inpc.PropertyChanged.Add ( fun args -> changes.Add(args.PropertyName, model.GetType().GetProperty(args.PropertyName).GetValue(model, [||]).ToString()))

model.GivenNames <- "Roger"
model.FamilyName <- "Federed"
model.GivenNames <- "Roger"

let expected = [| ("FullName", "Roger "); ("GivenNames", "Roger"); ("FullName", "Roger Federed"); ("FamilyName", "Federed") |]  
assert (expected = Array.ofSeq changes)
printf "Result: %A\n" changes
printf "Press <Enter> to exit ...\n"
stdin.ReadLine() |> ignore