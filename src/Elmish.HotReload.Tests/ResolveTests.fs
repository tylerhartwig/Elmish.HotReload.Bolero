module Elmish.HotReload.Tests.ConfigTests

open Elmish
open Elmish.HotReload
open Elmish.HotReload
open Elmish.HotReload.Core
open System.Reflection
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


[<Fact>]
let ``can resolve simple view function`` () =
    let viewExpr = <@ simpleView @>
    let viewInfo = Resolve.resolveView viewExpr
    let viewMemInfo = Resolve.findFun viewInfo (Seq.singleton (typeof<TestModel>.Assembly))
    match viewMemInfo with
    | None ->
        failwith "Could not find member info"
    | Some (viewMemInfo, _) ->
        let viewPropGenericInfo = viewMemInfo :?> MethodInfo
        let viewPropInfo = viewPropGenericInfo.MakeGenericMethod [| dispatchType |]
        let viewFun = (fun m d -> viewPropInfo.Invoke(null, [| m; d |]))

        let dispatch = (fun _ -> ())

        let view = viewFun { value = 3 } dispatch :?> string

        Assert.Equal("3", view)




type Model = int
type Msg = RetrieveValue
type ValueProvider =
    abstract member GetValue : unit -> int

let constValueProvider x =
    { new ValueProvider with member __.GetValue () = x }

let updateWithSingleDependecy (provider : ValueProvider) msg (model : Model) =
    match msg with
    | RetrieveValue -> provider.GetValue (), Cmd.none

[<Fact>]
let ``can resolve update with single dependency`` () =
    let updateExpr = <@ updateWithSingleDependecy (constValueProvider 42) @>
    let updateInfo = Resolve.resolveUpdate updateExpr
    let updateFunGeneric = Resolve.findFun updateInfo (Seq.singleton typeof<TestModel>.Assembly)
    match updateFunGeneric with
    | Some (genericFun, deps) ->
        let updateFun = (genericFun :?> MethodInfo).MakeGenericMethod [| typeof<obj> |]

        let (newModel, _) = updateFun.Invoke(null, (deps @ [ RetrieveValue; 1 ]) |> List.toArray) |> unbox<int * Cmd<obj>>

        Assert.Equal(42, newModel)




type TwoDependencyModel = { value: int * int }

let updateWithDoubleDependecy (provider1 : ValueProvider) (provider2 : ValueProvider) msg (model : TwoDependencyModel) =
    match msg with
    | RetrieveValue -> { value = (provider1.GetValue (), provider2.GetValue()) }, Cmd.none

[<Fact>]
let ``can resolve update with two dependencies`` () =
    let updateExpr = <@ updateWithDoubleDependecy (constValueProvider 42) (constValueProvider 21) @>
    let updateInfo = Resolve.resolveUpdate updateExpr
    let updateFunGeneric = Resolve.findFun updateInfo (Seq.singleton typeof<TestModel>.Assembly)
    match updateFunGeneric with
    | Some (genericFun, deps) ->
        let updateFun = (genericFun :?> MethodInfo).MakeGenericMethod [| typeof<obj> |]

        let (model, _) = updateFun.Invoke(null, (deps @ [ RetrieveValue; { value = (1, 1) } ]) |> List.toArray) |> unbox<TwoDependencyModel * Cmd<obj>>

        Assert.Equal(42, fst model.value)
        Assert.Equal(21, snd model.value)



let updateWithTuple msg (model : int * int) =
    match msg with
    | RetrieveValue -> (42, 21), Cmd.none

[<Fact>]
let ``can resolve update with tuple model`` () =
    let updateExpr = <@ updateWithTuple @>
    let updateInfo = Resolve.resolveUpdate updateExpr
    let updateFunGeneric = Resolve.findFun updateInfo (Seq.singleton typeof<TestModel>.Assembly)
    match updateFunGeneric with
    | Some (genericFun, _) ->
        let updateFun = (genericFun :?> MethodInfo).MakeGenericMethod [| typeof<obj> |]

        let ((val1, val2), _) = updateFun.Invoke(null, [| RetrieveValue; 1; 1 |]) |> unbox<(int * int) * Cmd<obj>>

        Assert.Equal(42, val1)
        Assert.Equal(21, val2)


let viewWithFieldDepedency (provider : ValueProvider) (model : int) (dispatch : obj -> unit) : int * Cmd<obj> =
    (provider.GetValue()), Cmd.none

type TypeHoldingDependency () =
    let valueProvider = constValueProvider 42

    member __.getViewExpr () =
        <@ viewWithFieldDepedency valueProvider @>

let depProvider = TypeHoldingDependency ()

[<Fact>]
let ``can resolve view with field depedency`` () =
    let viewExpr = depProvider.getViewExpr ()
    let viewInfo = Resolve.resolveView viewExpr
    let viewFun = Resolve.findFun viewInfo (Seq.singleton typeof<TestModel>.Assembly)
    match viewFun with
    | Some (viewFun, deps) ->
        Assert.Equal(1, deps.Length)

