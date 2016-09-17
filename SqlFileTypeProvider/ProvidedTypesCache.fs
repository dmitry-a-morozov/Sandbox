namespace FSharp.Data.SqlClient

open System
open System.Runtime.Caching
open ProviderImplementation.ProvidedTypes

type ProvidedTypesCache(name, onChange) =

    let cache = new MemoryCache(name)
    let changeMonitors = System.Collections.Concurrent.ConcurrentBag()
    [<VolatileField>]
    let mutable disposing = false

    member this.GetOrAdd(key, value: Lazy<ProvidedTypeDefinition>, monitors) = 
        let policy = CacheItemPolicy()
        monitors |> Seq.iter policy.ChangeMonitors.Add
        let existing = cache.AddOrGetExisting(key, value, policy)
        let cacheItem = 
            if existing = null 
            then 
                let m = cache.CreateCacheEntryChangeMonitor [ key ]
                m.NotifyOnChanged(fun _ -> 
                    if not disposing 
                    then onChange()
                )
                changeMonitors.Add(m)
                value 
            else 
                unbox existing

        cacheItem.Value

    interface IDisposable with 
        member __.Dispose() = 
            disposing <- true
            let m = ref null
            while changeMonitors.TryTake(m) do
                m.Value.Dispose()                
            cache.Dispose()

open System.IO

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

    member private __.TriggerOnChange() = base.OnChanged(state = null)
    member private __.TriggerOnFileChange(fileName) = 
        if String.Compare(file.Name, fileName, StringComparison.OrdinalIgnoreCase) = 0  
        then 
            this.TriggerOnChange()

    override __.UniqueId = path + string file.LastWriteTimeUtc.Ticks + string file.Length;
    override __.Dispose( disposing) = if disposing then watcher.Dispose()

