//namespace FSharp.Data.Test
//
//module Program = 
//    open FSharp.Data
//
//    [<EntryPoint>]
//    let main _ = 
//        printfn "Content %s" <| SqlFile<"GetNow.sql">.Text
//        stdin.ReadLine() |> ignore
//        0

open System
open System.IO
open System.Runtime.Caching

type SingleFileChangeMonitor(path) as this = 
    inherit ChangeMonitor()

    let file = new FileInfo(path)
    let watcher = new FileSystemWatcher( Path.GetDirectoryName(path) )

    do
        let dispose = ref true
        try
            watcher.NotifyFilter <- NotifyFilters.LastWrite ||| NotifyFilters.FileName
            watcher.Changed.Add <| fun args -> this.TriggerOnFileChange(args.Name)
            watcher.Deleted.Add <| fun args -> this.TriggerOnFileChange(args.Name)
            watcher.Renamed.Add <| fun args -> this.TriggerOnFileChange(args.OldName)
            watcher.Error.Add <| fun _ -> this.TriggerOnChange()
            watcher.EnableRaisingEvents <- true
            dispose := false
        finally 
            base.InitializationComplete()
            if !dispose 
            then 
                base.Dispose()

    member private __.TriggerOnChange() = 
        base.OnChanged(state = null)
    member private __.TriggerOnFileChange(fileName) = 
        if String.Compare(file.Name, fileName, StringComparison.OrdinalIgnoreCase) = 0  
        then 
            printfn "%s triggered on change for %s" (this.GetType().Name) path
            this.TriggerOnChange()

    override __.UniqueId = path + string file.LastWriteTimeUtc.Ticks + string file.Length;
    override __.Dispose( disposing) = if disposing then watcher.Dispose()

type ProvidedTypesCache<'T>(name, onItemRemoved) =

    let cache = new MemoryCache(name)

    member this.GetOrAdd(key, value: Lazy<'T>, monitors) = 
        let policy = CacheItemPolicy(RemovedCallback = CacheEntryRemovedCallback(onItemRemoved))
        monitors |> Seq.iter policy.ChangeMonitors.Add
        let existing = cache.AddOrGetExisting(key, value, policy)
        if existing <> null
        then
            (unbox<_ Lazy> existing).Value 
        else
            value.Value

    member internal __.Cache = cache
    
let cache = 
    new ProvidedTypesCache<_>(
        "test", 
        fun args -> 
            printfn "Item %s removed. Reason: %A. Keys left: %A" args.CacheItem.Key args.RemovedReason [ for x in args.Source -> x.Key ]
    )

do 
    let fileName = "Get42.sql"
    let fileFullName = Path.Combine( __SOURCE_DIRECTORY__, fileName)
    let result = cache.GetOrAdd(fileName, lazy File.ReadAllText(fileFullName), [ new SingleFileChangeMonitor( fileFullName) ]) 
    printfn "Result: %A" result

do 
    let fileName = "GetNow.sql"
    let fileFullName = Path.Combine( __SOURCE_DIRECTORY__, fileName)
    let result = cache.GetOrAdd(fileName, lazy File.ReadAllText(fileFullName), [ new SingleFileChangeMonitor( fileFullName) ]) 
    printfn "Result: %A" result

printfn "Press <ENTER> to pause"
stdin.ReadLine() |> ignore
printfn "Press <ENTER> again to quit. Items in cache: %A" [ for x in cache.Cache -> x.Key, x.Value ]
stdin.ReadLine() |> ignore
