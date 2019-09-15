namespace Elmish.HotReload.Bolero.Cli

open Elmish.HotReload.Bolero.Cli
open Elmish.HotReload.Bolero.Cli.CommandParser
open FcsWatch.Binary
open System.Threading.Tasks
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting

module Program =

    let CreateWebHostBuilder listenerConfig =
        let args = listenerConfig.WebArgs
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(fun builder ->
                builder
                    .UseStartup<Startup>()
                    .UseUrls(listenerConfig.BaseUrl) |> ignore )

    let runListener listenerConfig =
        CreateWebHostBuilder(listenerConfig).Build().RunAsync() |> Async.AwaitTask

    let exitTask = TaskCompletionSource<unit>()

    let watchProject fcsWatchConfig =
        runFcsWatcher exitTask.Task fcsWatchConfig.Config fcsWatchConfig.ProjectFile


    [<EntryPoint>]
    let main argv =
        printfn "Starting command line tool"

        let arguArgs, additionalArgs = CommandParser.splitArgs argv
        let argResults = parser.Parse arguArgs

        let fcsWatchConfig = processFcsWatchArgs additionalArgs parser.PrintUsage argResults
        let listenerConfig = processListenerArgs parser.PrintUsage argResults

        let webHookTask = runListener listenerConfig
        let watchTask = watchProject fcsWatchConfig

        [ webHookTask; watchTask ] |> Async.Parallel |> Async.RunSynchronously |> ignore

        0 // Exit code
