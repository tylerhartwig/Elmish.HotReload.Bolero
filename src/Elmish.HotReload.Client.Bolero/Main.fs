module HotReload.Client.Main

open Blazor.Extensions.Logging
open Bolero
open Bolero.Html
open Elmish
open Elmish.HotReload.Bolero.Core
open HotReload.Elmish
open Microsoft.Extensions.Logging

type Templ = Template<"main.html">

let view model dispatch =
    Templ()
        .Increment(fun _ -> dispatch Increment)
        .Value(textf " %i " model.value)
        .Decrement(fun _ -> dispatch Decrement)
        .Elt()


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

    member this.GetLogger () = 
        this.Services.GetService(typeof<ILogger<MyApp>>) :?> ILogger


    override this.Program =
        let log = this.GetLogger()

        Program.mkProgram (fun _ -> initModel, Cmd.none) update view
            |> Program.withErrorHandler (fun (msg, exn) -> printfn "Error: %s\n\n%A" msg exn)
#if DEBUG
            |> Program.withHotReload (Some log) this.JSRuntime this.NavigationManager <@ view @> <@ update @>
#endif

