namespace Elmish.HotReload.Bolero.Cli

open Elmish.HotReload.Bolero.Cli
open Elmish.HotReload.Bolero.Cli.CommandParser
open FcsWatch.Binary
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Hosting

module Program =

    let CreateWebHostBuilder listenerConfig =
        WebHost.CreateDefaultBuilder(listenerConfig.WebArgs)
            .UseStartup<Startup>()
            .UseUrls(listenerConfig.BaseUrl)

    let runListener listenerConfig =
        CreateWebHostBuilder(listenerConfig).Build().RunAsync() |> Async.AwaitTask

    let watchProject fcsWatchConfig =
        async { return runFcsWatcher fcsWatchConfig.Config fcsWatchConfig.ProjectFile }


    [<EntryPoint>]
    let main argv =

        let arguArgs, additionalArgs = CommandParser.splitArgs argv
        let argResults = parser.Parse arguArgs

        let fcsWatchConfig = processFcsWatchArgs additionalArgs parser.PrintUsage argResults
        let listenerConfig = processListenerArgs parser.PrintUsage argResults

        let webHookTask = runListener listenerConfig
        let watchTask = watchProject fcsWatchConfig

        [ webHookTask; watchTask ] |> Async.Parallel |> Async.RunSynchronously |> ignore

        0 // Exit code
