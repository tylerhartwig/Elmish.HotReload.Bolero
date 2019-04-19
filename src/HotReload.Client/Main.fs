module HotReload.Client.Main

open Blazor.Extensions.Logging
open System.Net.Http
open System.Net
open System.Threading
open HotReload
open HotReload.Library.Reload
open Elmish
open Bolero
open Bolero.Html
open Microsoft.AspNetCore.Blazor.Browser.Http
open Microsoft.AspNetCore.Blazor.Browser.Services
open Microsoft.Extensions.Logging

let view model dispatch =
    concat [
        button [on.click (fun _ -> dispatch Decrement)] [text "-"]
        span [] [textf " %i " model.value]
        button [on.click (fun _ -> dispatch Increment)] [text "+"]
    ]

let myHotReload = ElmishHotReloadPackage(update, view)

let createLogger context () =
    (new LoggerFactory())
        .AddBrowserConsole(LogLevel.Trace)
        .CreateLogger(context)


type MyApp() =
    inherit ProgramComponent<obj, obj>()

    let log = createLogger "Bolero.HotReload" ()

    let getAssembly name =
        async {
            let c = new HttpClient(new BrowserHttpMessageHandler())
            c.BaseAddress <- new System.Uri(BrowserUriHelper.Instance.GetBaseUri())
            let filePath = sprintf "_framework/_bin/%s" name
            try
                log.LogDebug <| sprintf "Attempting to fetch: %s" filePath
                return! c.GetByteArrayAsync(filePath) |> Async.AwaitTask
            with ex ->
                log.LogError(ex, "Failed to fetch resource")
                return Array.empty
        }

    override this.Program =
        Program.mkProgram (fun _ -> initModel, Cmd.none) update view
            |> Program.withErrorHandler (fun (msg, exn) -> printfn "Error: %s\n\n%A" msg exn)
            |> Program.withHotReload log getAssembly

