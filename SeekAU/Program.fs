// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.


open System

[<EntryPoint>]
let main argv = 
    ApplySeekAU.startApply
    Console.ReadLine() |> ignore
    0 // return an integer exit code
