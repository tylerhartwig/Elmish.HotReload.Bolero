open FSharp.Compiler.PortaCode.ProcessCommandLine

[<EntryPoint>]
let main argv =
    try
        ProcessCommandLine argv
    with e ->
        printfn "Error: %s\n\t%s" e.Message e.StackTrace
        1

