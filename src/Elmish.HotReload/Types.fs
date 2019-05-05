module Elmish.HotReload.Types

open Elmish
open Microsoft.Extensions.Logging
open Microsoft.FSharp.Reflection


type ReloadModelWrapper =
    {
        reloadSwitch : bool
        model : obj
    }

let initWrapped model =
    { model = model; reloadSwitch = true }

type HotReloadPackage =
    abstract member Update : (obj -> obj -> obj * Cmd<obj>) option
    abstract member View : (obj -> (obj -> unit) -> obj) option

type ElmishHotReloadPackage<'msg, 'model, 'view>(update : 'msg -> 'model -> 'model * Cmd<'msg>, view : 'model -> Dispatch<'msg> -> 'view) =
    let view' (model : obj) (dispatch : obj -> unit) =
        let dispatch' = FSharpValue.MakeFunction(typeof<'msg -> unit>, fun m -> dispatch m |> box)
        let wrappedModel = unbox<ReloadModelWrapper> model
        let v = view (unbox<'model> wrappedModel.model) (unbox<'msg -> unit> dispatch')
        (box v)

    let update' (message : obj) (model : obj) =
        let wrappedModel = unbox<ReloadModelWrapper> model
        let (m : 'model, c : Cmd<'msg>) = update (unbox<'msg> message) (unbox<'model> wrappedModel.model)
        (box m, Cmd.map box c)

    interface HotReloadPackage with
        member __.Update = Some update'

        member __.View = Some view'


type KickView = | KickView

type ProgramUpdater<'arg, 'msg, 'model, 'view>(log : ILogger, initialModel, initialUpdate, initialView) =
    let mutable reloadPackage : HotReloadPackage =
        { new HotReloadPackage with
            member __.Update = None;
            member __.View = None }

    let mutable dispatcher = fun _ -> ()

    member __.Swap package = reloadPackage <- package

    member __.ForceRefresh () =
        dispatcher KickView

    member __.Init (a : 'arg) =
        log.LogDebug <| sprintf "Starting reload init"
        let (model : 'model, cmd : Cmd<'msg>) = initialModel a
        let wrapped = initWrapped model
        (box wrapped, Cmd.map box cmd)

    member __.View (model : obj) (dispatch : obj -> unit) =
        log.LogDebug <| sprintf "Starting reload view with model: %A" model
        let wrappedModel = model :?> ReloadModelWrapper
        let model = wrappedModel.model
        dispatcher <- fun x ->
            log.LogDebug <| sprintf "Sending real kick! with %A" x
            dispatch x
        log.LogDebug <| sprintf "Calling view with model: %A" model
        match reloadPackage.View with
        | None ->
            log.LogDebug "View is initial"
            let morphedModel =
                if model.GetType() <> typeof<'model> then
                    Morph.morphAny typeof<'model> model
                else model

            log.LogDebug "Calling initial view function"
            let dispatch = FSharpValue.MakeFunction(typeof<'msg -> unit>, fun m -> dispatch m |> box)
            log.LogDebug <| sprintf "Unboxing model: %A as %A" morphedModel typeof<'model>
            let unboxedModel = (unbox<'model> morphedModel)
            log.LogDebug <| sprintf "Unboxing dispatch: %A as %A" dispatch typeof<'msg -> unit>
            let unboxedDispatch = (unbox<'msg -> unit> dispatch)
            initialView unboxedModel unboxedDispatch
        | Some view ->
            log.LogDebug "View is reloaded"
            let v = view model dispatch
            log.LogDebug <| sprintf "Made new view: %A" v
            v :?> 'view

    member __.Update (msg : obj) (model : obj) =
        let wrappedModel = model :?> ReloadModelWrapper
        let model = wrappedModel.model

        if msg.GetType() = typeof<KickView> then
            log.LogDebug <| sprintf "Got Kick, sending back model: %A" model
            let newModel = { wrappedModel with reloadSwitch = not wrappedModel.reloadSwitch }
            (box newModel, Cmd.none)
        else
            match reloadPackage.Update with
            | None ->
                let morphedModel =
                    if model.GetType() <> typeof<'model> then
                        Morph.morphAny typeof<'model> model
                    else model

                log.LogDebug "Calling initail update function"
                let (m : 'model, c : Cmd<'msg>) = initialUpdate (unbox<'msg> msg) (unbox<'model> morphedModel)
                (box { wrappedModel with model = m }, Cmd.map box c)
            | Some update ->
                log.LogDebug "Calling hot-reload update function"
                let newModel, cmd = update msg model
                (box { wrappedModel with model = newModel }, cmd)


