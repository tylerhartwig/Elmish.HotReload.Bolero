module HotReload.Client.Main

open HotReload
open HotReload.Library
open Elmish
open Bolero
open Bolero.Html

type Model =
    {
        value: int
    }

let initModel =
    {
        value = 0
    }


type Message =
    | Increment
    | Decrement

let update (message : Message) (model : Model) =
    match message with
    | Increment ->
        { model with value = model.value + 1 }, Cmd.none
    | Decrement ->
        { model with value = model.value - 1 }, Cmd.none

let view model dispatch =
    concat [
        button [on.click (fun _ -> dispatch Decrement)] [text "-"]
        span [] [textf " %i " model.value]
        button [on.click (fun _ -> dispatch Increment)] [text "+"]
    ]










type Updater(update) =
    member __.UniqueUpdate message model = update message model

let myHotReload = Updater(update)


type public ReloadPackage() =
    member __.UniqueUpdate = fun message model -> update message model


type MyApp() =
    inherit ProgramComponent<Model, Message>()


    override this.Program =
        Program.mkProgram (fun _ -> initModel, Cmd.none) update view
            |> Program.withHotReload
            |> Program.withErrorHandler (fun (msg, exn) -> printfn "Error: %s\n\n%A" msg exn)

