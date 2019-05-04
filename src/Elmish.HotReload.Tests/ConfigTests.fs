module Elmish.HotReload.Tests.ConfigTests

open Elmish
open Elmish.HotReload.Core
open System.Reflection
open Xunit

type TestModel =
    {
        value: int
    }

type TestMessage =
    | Decrement
    | Increment

let initialModel = { value = 0 }

let simpleView model dispatch =
    model.value.ToString()

let simpleUpdate msg model =
    match msg with
    | Decrement ->
        { model with value = model.value - 1 }, Cmd.none
    | Increment ->
        { model with value = model.value + 1 }, Cmd.none

let dispatchType = typeof<obj -> unit>

type ProgramUnit<'model> () =
    let mutable currentModel : 'model option = None

    member __.model
        with get() =
            match currentModel with
            | Some m -> m
            | None -> failwith "Model has not been initialized yet"
        and set value = currentModel <- Some value


[<Fact>]
let ``can resolve simple view function`` () =
    let viewExpr = <@ simpleView @>
    let viewInfo = resolveView viewExpr
    let viewMemInfo = findMember viewInfo (Seq.singleton (typeof<TestModel>.Assembly))
    match viewMemInfo with
    | None ->
        failwith "Could not find member info"
    | Some viewMemInfo ->
        let viewPropGenericInfo = viewMemInfo :?> MethodInfo
        let viewPropInfo = viewPropGenericInfo.MakeGenericMethod [| dispatchType |]
        let viewFun = (fun m d -> viewPropInfo.Invoke(null, [| m; d |]))

        let dispatch = (fun _ -> ())

        let view = viewFun { value = 3 } dispatch :?> string

        Assert.Equal("3", view)



let resolveView () =
    let viewExpr = <@ simpleView @>
    let viewInfo = resolveView viewExpr
    let viewMemInfo = findMember viewInfo (Seq.singleton (typeof<TestModel>.Assembly))
    match viewMemInfo with
    | None ->
        failwith "Could not find member info"
    | Some viewMemInfo ->
        let viewPropGenericInfo = viewMemInfo :?> MethodInfo
        let viewPropInfo = viewPropGenericInfo.MakeGenericMethod [| dispatchType |]
        (fun m d -> viewPropInfo.Invoke(null, [| m; d |]))

let makeProgram () =
    let mutable disp = (fun _ -> ())
    let x init =
        let sub dispatch =
            disp <- dispatch
        Cmd.ofSub sub
    Program.mkProgram (fun _ -> initialModel, Cmd.none) simpleUpdate simpleView
        |> Program.withSubscription x, disp









