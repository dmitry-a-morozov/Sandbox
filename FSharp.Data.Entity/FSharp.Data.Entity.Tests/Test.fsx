﻿
#I @"..\packages"

#r @"System.Transactions.dll"
#r @"EntityFramework.Core.7.0.0-beta6\lib\net45\EntityFramework.Core.dll"
#r @"EntityFramework.Relational.7.0.0-beta6\lib\net45\EntityFramework.Relational.dll"
#r @"EntityFramework.SqlServer.7.0.0-beta6\lib\net45\EntityFramework.SqlServer.dll"
#r @"Microsoft.Framework.Caching.Abstractions.1.0.0-beta6\lib\net45\Microsoft.Framework.Caching.Abstractions.dll"
#r @"Microsoft.Framework.Caching.Memory.1.0.0-beta6\lib\net45\Microsoft.Framework.Caching.Memory.dll"
#r @"Microsoft.Framework.Configuration.1.0.0-beta6\lib\net45\Microsoft.Framework.Configuration.dll"
#r @"Microsoft.Framework.Configuration.Abstractions.1.0.0-beta6\lib\net45\Microsoft.Framework.Configuration.Abstractions.dll"
#r @"Microsoft.Framework.Configuration.Binder.1.0.0-beta6\lib\net45\Microsoft.Framework.Configuration.Binder.dll"
#r @"Microsoft.Framework.DependencyInjection.1.0.0-beta6\lib\net45\Microsoft.Framework.DependencyInjection.dll"
#r @"Microsoft.Framework.DependencyInjection.Abstractions.1.0.0-beta6\lib\net45\Microsoft.Framework.DependencyInjection.Abstractions.dll"
#r @"Microsoft.Framework.Logging.1.0.0-beta6\lib\net45\Microsoft.Framework.Logging.dll"
#r @"Microsoft.Framework.Logging.Abstractions.1.0.0-beta6\lib\net45\Microsoft.Framework.Logging.Abstractions.dll"
#r @"Microsoft.Framework.OptionsModel.1.0.0-beta6\lib\net45\Microsoft.Framework.OptionsModel.dll"
#r @"Microsoft.SqlServer.Types.dll"
#r @"Remotion.Linq.2.0.0-alpha-004\lib\net45\Remotion.Linq.dll"
#r @"Ix-Async.1.2.4\lib\net45\System.Interactive.Async.dll"

#r @"..\FSharp.Data.Entity\bin\Debug\FSharp.Data.Entity.dll"

open FSharp.Data.Entity

type AdventureWorks = DbContext<"Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True">

open Microsoft.Data.Entity

let db = 
    new AdventureWorks(fun optionsBuilder ->
        System.Diagnostics.Debug.WriteLine "Test"
        optionsBuilder.UseSqlServer("Data Source=.;Initial Catalog=AdventureWorks2014;Integrated Security=True") |> ignore
    )

let shift = new AdventureWorks.``HumanResources.Shift``(ShiftID = 12y, Name = "French coffer break")
printfn "%A" shift.Name



