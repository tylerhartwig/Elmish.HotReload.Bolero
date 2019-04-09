module HotReload.Elmish

open Elmish
open HotReload.Library

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

let myHotReload = Updater(update)
