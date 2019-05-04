module HotReload.Client.Main

open Blazor.Extensions.Logging
open Bolero
open Bolero.Html
open Elmish
open Elmish.HotReload.Core
open Elmish.HotReload.Bolero.Core
open HotReload.Elmish
open Microsoft.AspNetCore.Blazor.Browser.Http
open Microsoft.AspNetCore.Blazor.Browser.Services
open Microsoft.Extensions.Logging
open System.Net.Http
open System.Runtime.Loader

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













type MyApp () =
    inherit ProgramComponent<obj, obj>()

    let log = createLogger "Bolero.HotReload" ()

    override this.Program =
        Program.mkProgram (fun _ -> initModel, Cmd.none) update view
            |> Program.withErrorHandler (fun (msg, exn) -> printfn "Error: %s\n\n%A" msg exn)
            |> Program.withHotReload log <@ view @> <@ update @>

