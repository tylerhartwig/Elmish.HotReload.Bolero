module HotReload.Client.Main

open HotReload
open HotReload.Library.Reload
open Elmish
open Bolero
open Bolero.Html


let view model dispatch =
    concat [
        button [on.click (fun _ -> dispatch Decrement)] [text "-"]
        span [] [textf " %i " model.value]
        button [on.click (fun _ -> dispatch Increment)] [text "+"]
    ]



type MyApp() =
    inherit ProgramComponent<Model, Message>()


    override this.Program =
        Program.mkProgram (fun _ -> initModel, Cmd.none) update view
            |> Program.withHotReload
            |> Program.withErrorHandler (fun (msg, exn) -> printfn "Error: %s\n\n%A" msg exn)

