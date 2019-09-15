module Elmish.HotReload.Bolero.Core

open Blazor.Extensions.Logging
open BlazorSignalR
open Elmish
open Elmish.HotReload
open Elmish.HotReload.Core
open Elmish.HotReload.Types
open Microsoft.AspNetCore.SignalR.Client
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Microsoft.FSharp.Quotations
open System


let createConnection jsRuntime =
    let builder =
        HubConnectionBuilder()
            .WithUrlBlazor(url = "http://localhost:9876/reloadhub", jsRuntime = jsRuntime)
//            .ConfigureLogging(fun b ->
//                b
//                    .AddBrowserConsole()
//                    .SetMinimumLevel(LogLevel.Trace) |> ignore
//                )

//    builder.Services.AddLogging(fun b ->
//        b
//        |> ignore) |> ignore
    builder.Build()

let connect (hub : HubConnection) = async {
        let mutable connected = false
        while not connected do
            try
                do! hub.StartAsync() |> Async.AwaitTask
                connected <- true
            with e ->
                do! Async.Sleep 500
                printfn "Failed: %A" e.Message
                printfn "Hot reload reconnecting..."
        printfn "Connected!"
    }

let startConnection (log : ILogger) jsRuntime reload =
    log.LogTrace "Attempting to start connection"
    let hub = createConnection jsRuntime
    hub.On(methodName = "Update", handler = Action<string * byte[]>(fun (fileName, file) ->
        log.LogDebug <| sprintf "Received file, byte length: %i" file.Length
        try
            updateAssembly fileName file
        with ex ->
            log.LogError(ex, "Failed to update assembly")

        try
            reload()
        with ex ->
            log.LogError(ex,"Failed to reload!")
        )
    ) |> ignore
    connect hub

module Program =
    let withHotReload log jsRuntime
        (viewExpr : Expr<'model -> ('msg -> unit) -> 'view>)
        (updateExpr : Expr<'msg -> 'model -> 'model * Cmd<'msg>>)
        (program : Program<'arg, 'model, 'msg, 'view>) =

        let log =
            match log with
            | Some l -> l
            | None -> (new LoggerFactory()).CreateLogger() :> ILogger

        let updater = ProgramUpdater(log, program.init, program.update, program.view)

        let viewResolverInfo = Resolve.resolveView viewExpr
        let updateResolverInfo = Resolve.resolveUpdate updateExpr

        let reload () = reloadPipeline log updater viewResolverInfo updateResolverInfo

        (startConnection log jsRuntime reload) |> Async.Start

        let erasedProg : Program<'arg, obj, obj, 'view> =
            {
                init = updater.Init
                update = updater.Update
                view = updater.View
                setState = fun model -> updater.View model >> ignore
                subscribe = fun _ -> Cmd.none
                onError = program.onError
            }



        erasedProg
