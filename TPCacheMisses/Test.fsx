#r @"bin\Debug\TPCacheMisses.dll"

//Run script below to see that instantiation function is called 3 time for every declaration of type provider

open TPCacheMisses

let x = new InstantiationFunction<"UltimateAnswer">(42)
x.UltimateAnswer

let y = new InstantiationFunction<"WhatTimeIsThat", "System.DateTime">(System.DateTime.Now)
y.WhatTimeIsThat
