module HotReload.Client.Main

open Blazor.Extensions.Logging
open Bolero
open Bolero.Html
open Elmish
open Elmish.HotReload.Bolero.Core
open Elmish.HotReload.Types
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


let createLogger context () =
    (new LoggerFactory())
        .AddBrowserConsole(LogLevel.Trace)
        .CreateLogger(context)




type MyApp () =
#if !DEBUG
    inherit ProgramComponent<Model, Message> ()
#else
    inherit ProgramComponent<obj, obj>()
#endif
    override this.Program =
        Program.mkProgram (fun _ -> initModel, Cmd.none) update view
            |> Program.withErrorHandler (fun (msg, exn) -> printfn "Error: %s\n\n%A" msg exn)
#if DEBUG
            |> Program.withHotReload (None) <@ view @> <@ update @>
#endif

