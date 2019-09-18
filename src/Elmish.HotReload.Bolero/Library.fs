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
open Microsoft.JSInterop
open System
open System.Reflection


let getBrowserConsoleLoggerProvider jsRuntime =
    let assembly = typeof<BrowserConsoleLoggerExtensions>.Assembly
    let providerType = assembly.DefinedTypes |> Seq.find (fun t -> t.Name = "BrowserConsoleLoggerProvider")
    let constructor = providerType.GetConstructors().[0]
    constructor.Invoke([| jsRuntime |]) :?> ILoggerProvider


let createConnection jsRuntime navigationManager =
    let builder =
        HubConnectionBuilder()
            .WithUrlBlazor("http://localhost:9876/reloadhub", jsRuntime, navigationManager)
            .ConfigureLogging(fun b ->
                b.AddProvider(getBrowserConsoleLoggerProvider jsRuntime)
                    .AddBrowserConsole() |> ignore
//                    .SetMinimumLevel(LogLevel.Trace) |> ignore
                )

    builder.Services.AddLogging(fun b ->
        b.AddBrowserConsole() |> ignore
        ) |> ignore
    builder.Build()

let connect (log : ILogger) (hub : HubConnection) = async {
        let mutable connected = false
        while not connected do
            try
                do! hub.StartAsync() |> Async.AwaitTask
                connected <- true
            with e ->
                do! Async.Sleep 500
                log.LogInformation <| sprintf "Failed: %A" e.Message
                log.LogTrace (e.ToString())
                printfn "Hot reload reconnecting..."
        printfn "Connected!"
    }

let startConnection (log : ILogger) jsRuntime navigationManager reload =
    log.LogTrace "Attempting to start connection"
    let hub = createConnection jsRuntime navigationManager
    hub.On(methodName = "Update", handler = Action<string, byte[]>(fun fileName file ->
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
    connect log hub

module Program =
    let withHotReload log jsRuntime navigationManager
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

        (startConnection log jsRuntime navigationManager reload) |> Async.Start

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
