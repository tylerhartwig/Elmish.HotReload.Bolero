module Elmish.HotReload.Core

open System
open Elmish
open Elmish.HotReload
open Elmish.HotReload.ReloadContext
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.ExprShape
open Microsoft.FSharp.Quotations.Patterns
open Microsoft.FSharp.Reflection
open Microsoft.Extensions.Logging
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Runtime.Loader

let comparePropertyInfo (propA : PropertyInfo) (propB : PropertyInfo) =
    propA.PropertyType = propB.PropertyType && propA.Name = propB.Name

let comparePropertyInfos aProps bProps =
    (aProps, bProps) ||> Array.map2 comparePropertyInfo |> Array.fold (&&) true

let mapUnionCaseDirect (targetCase : UnionCaseInfo) (sourceCase : UnionCaseInfo) sourceValues =
    FSharpValue.MakeUnion(targetCase, sourceValues)

let morphUnion targetType sourceType sourceCase =
    let targetCases = FSharpType.GetUnionCases targetType
    let (sourceCase, sourceValues) = FSharpValue.GetUnionFields(sourceCase, sourceType)

    let targetCase = targetCases |> Array.find (fun c -> c.Name = sourceCase.Name)

    if comparePropertyInfos (sourceCase.GetFields()) (targetCase.GetFields()) then
        mapUnionCaseDirect targetCase sourceCase sourceValues
    else
        failwithf "morphing %A to %A is not supported yet" sourceCase targetType


let mapRecordDirect targetType sourceType sourceRecord =
    FSharpValue.MakeRecord(targetType, (FSharpValue.GetRecordFields(sourceRecord)))

let morphRecord targetType sourceType sourceRecord =
    let targetFields = FSharpType.GetRecordFields targetType
    let sourceFields = FSharpType.GetRecordFields sourceType

    if comparePropertyInfos sourceFields targetFields then
        mapRecordDirect targetType sourceType sourceRecord
    else
        failwithf "morphing record %A to %A is not supported yet" sourceRecord targetType


let morphAny targetType sourceType sourceObj =
    match targetType, sourceType with
    | tt, st when FSharpType.IsUnion tt && FSharpType.IsUnion st ->
        morphUnion tt st sourceObj
    | tt, st when FSharpType.IsRecord tt && FSharpType.IsRecord st ->
        morphRecord tt st sourceObj
    | _ -> failwithf "morphing type %A to type %A is not supported yet" sourceType targetType

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
            let morphedModel =
                if model.GetType() <> typeof<'model> then
                    morphAny typeof<'model> (model.GetType()) model
                else model

            log.LogDebug "Calling initial view function"
            let dispatch = FSharpValue.MakeFunction(typeof<'msg -> unit>, fun m -> dispatch m |> box)
            log.LogDebug <| sprintf "Unboxing model: %A as %A" morphedModel typeof<'model>
            let unboxedModel = (unbox<'model> morphedModel)
            log.LogDebug <| sprintf "Unboxing dispatch: %A as %A" dispatch typeof<'msg -> unit>
            let unboxedDispatch = (unbox<'msg -> unit> dispatch)
            initialView unboxedModel unboxedDispatch
        | Some view ->
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
                log.LogDebug "Calling initail update function"
                let (m : 'model, c : Cmd<'msg>) = initialUpdate (unbox<'msg> msg) (unbox<'model> model)
                (box { wrappedModel with model = m }, Cmd.map box c)
            | Some update ->
                log.LogDebug "Calling hot-reload update function"
                let newModel, cmd = update msg model
                (box { wrappedModel with model = newModel }, cmd)

type ResolverInfo =
    | StaticProperty of moduleName : string * valueName : string


let resolveView (viewExpr : Expr<'model -> ('msg -> unit) -> 'view>) =
    match viewExpr with
    | Lambda (_, Lambda (_, Call (None, methodInfo, _))) ->
        StaticProperty (methodInfo.DeclaringType.FullName, methodInfo.Name)

let resolveUpdate (updateExpr : Expr<'msg -> 'model -> 'model * Cmd<'msg>>) =
    match updateExpr with
    | Lambda (msg, Lambda (model, Call (None, methodInfo, _))) ->
        StaticProperty (methodInfo.DeclaringType.FullName, methodInfo.Name)



let private cachedAssemblies = Dictionary<string, byte[]>(HashIdentity.Structural)

let mutable reloadContext = ReloadLoaderContext()

let updateAssembly assemblyName assembly =
    reloadContext.cacheAssembly assemblyName (Assembly.Load(rawAssembly = assembly))
    cachedAssemblies.[assemblyName] <- assembly

let rec findMember resolveInfo (assemblies : seq<Assembly>) =
    match resolveInfo with
    | StaticProperty (moduleName, valueName) ->
        assemblies
        |> Seq.tryPick (fun a -> a.DefinedTypes |> Seq.tryFind (fun t -> t.FullName = moduleName))
        |> Option.map (fun t -> t.DeclaredMembers |> Seq.find (fun m -> m.Name = valueName))







let reloadPipeline (log : ILogger) (updater : ProgramUpdater<'arg,'msg,'model,'view>) viewResolverInfo updateResolverInfo =
    log.LogDebug "Populating reload context"
    let assemblies = reloadContext.allAssemblies ()

    log.LogDebug "Looking for member info"
    let viewMemInfo = assemblies |> findMember viewResolverInfo
    let updateMemInfo = assemblies |> findMember updateResolverInfo

    log.LogDebug "Resolving reload package"

    log.LogDebug "Resolving updatePackage"
    let view =
        match viewMemInfo with
        | None ->
            log.LogDebug "Did not find member info"
            None
        | Some memInfo ->
            log.LogDebug "Found view member info"
            match memInfo with
            | :? MethodInfo as methodInfo ->
                log.LogDebug "Member info is method info, resolving"
                Some (fun m (d : obj -> unit) ->


                    let d' = FSharpValue.MakeFunction(typeof<'msg -> unit>, fun m -> d m |> box)
                    methodInfo.Invoke(null, [| m; d' |]))

    let update =
        match updateMemInfo with
        | None ->
            log.LogDebug "Did not find update info"
            None
        | Some memInfo ->
            log.LogDebug "Found update member info"
            match memInfo with
            | :? MethodInfo as methodInfo ->
                log.LogDebug <| sprintf "Update member info is method info (%A), resolving" methodInfo
                Some (fun msg model ->
                    let [| msgParam; modelParam |] = methodInfo.GetParameters()

                    let mInfo = methodInfo.MakeGenericMethod(msgParam.ParameterType)
                    log.LogDebug <| sprintf "Specific Update method: %A" mInfo

                    let morphedMsg =
                        if msg.GetType() <> msgParam.ParameterType then
                            morphAny (msgParam.ParameterType) (msg.GetType()) msg
                        else msg

                    let morphedModel =
                        if model.GetType() <> modelParam.ParameterType then
                            morphAny (modelParam.ParameterType) (model.GetType()) model
                        else model

                    let result = mInfo.Invoke(null, [| morphedMsg; morphedModel |])
                    log.LogDebug <| sprintf "Update result is: %A" result
                    let [| t1; t2 |] = FSharpValue.GetTupleFields result
                    (t1, Cmd.none) )

    let updatePackage =
        { new HotReloadPackage with
            member __.View = view
            member __.Update = update
        }
    log.LogDebug "Swapping update package"
    updater.Swap updatePackage

    log.LogDebug "Kicking View"
    updater.ForceRefresh ()
