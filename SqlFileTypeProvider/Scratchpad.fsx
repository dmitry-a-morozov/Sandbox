#r "System.Runtime.Caching"

open System
open System.IO
open System.Runtime.Caching

type SingleFileChangeMonitor(path) as this = 
    inherit FileChangeMonitor()

    let file = new FileInfo(path)
    let watcher = new FileSystemWatcher(path)
    do
        let dispose = ref true
        try
            watcher.NotifyFilter <- NotifyFilters.LastWrite ||| NotifyFilters.Size
            watcher.EnableRaisingEvents <- true
            watcher.Changed.Add(callback = this.TriggerOnChange)
            watcher.Deleted.Add(callback = this.TriggerOnChange)
            watcher.Renamed.Add(callback = this.TriggerOnChange)
            dispose := false
        finally 
            base.InitializationComplete()
            if !dispose then base.Dispose()

    member private __.TriggerOnChange _ = 
        base.OnChanged(state = null)

    override __.FilePaths = System.Collections.ObjectModel.ReadOnlyCollection( [| path |])
    override __.LastModified = DateTimeOffset(file.LastWriteTimeUtc)
    override __.UniqueId = path + string file.LastWriteTimeUtc.Ticks + string file.Length;
    override __.Dispose( disposing) = if disposing then watcher.Dispose()
    
let cache = new MemoryCache("test")
let policy = CacheItemPolicy()
let fileName = "Get42.sql"
let fileFullName = Path.Combine( __SOURCE_DIRECTORY__, fileName)
policy.ChangeMonitors.Add <| new SingleFileChangeMonitor( fileFullName)
cache.Add(fileName, File.ReadAllLines(fileFullName), policy)

let trackCache = cache.CreateCacheEntryChangeMonitor [ fileName ]

trackCache.NotifyOnChanged(fun _ ->
    printfn "Cache change"
    printfn "All keys: %A" <| [ for x in cache -> x.Key ]
    printfn "Done" 
)

