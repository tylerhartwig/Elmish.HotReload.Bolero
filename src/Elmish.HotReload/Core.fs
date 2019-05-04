module Elmish.HotReload.Core

open System
open Elmish
open Elmish.HotReload.Types

open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.ExprShape
open Microsoft.FSharp.Quotations.Patterns
open Microsoft.FSharp.Reflection
open Microsoft.Extensions.Logging
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Runtime.Loader

let private cachedAssemblies = Dictionary<string, byte[]>(HashIdentity.Structural)

let updateAssembly assemblyName assembly = cachedAssemblies.[assemblyName] <- assembly



let reloadPipeline (log : ILogger) (updater : ProgramUpdater<'arg,'msg,'model,'view>) viewResolverInfo updateResolverInfo =
    log.LogDebug "Populating reload context"
    let assemblies = cachedAssemblies |> Seq.map(fun kvp -> Assembly.Load kvp.Value)

    log.LogDebug "Looking for member info"
    let viewMemInfo = assemblies |> Resolve.findFun viewResolverInfo
    let updateMemInfo = assemblies |> Resolve.findFun updateResolverInfo

    log.LogDebug "Resolving reload package"

    log.LogDebug "Resolving updatePackage"
    let view =
        match viewMemInfo with
        | None ->
            log.LogDebug "Did not find member info"
            None
        | Some (memInfo, _) ->
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
        | Some (memInfo, _) ->
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
                            Morph.morphAny (msgParam.ParameterType)  msg
                        else msg

                    let morphedModel =
                        if model.GetType() <> modelParam.ParameterType then
                            Morph.morphAny (modelParam.ParameterType)  model
                        else model

                    let result = mInfo.Invoke(null, [| morphedMsg; morphedModel |])
                    log.LogDebug <| sprintf "Update result is: %A" result
                    let [| t1; t2 |] = FSharpValue.GetTupleFields result
                    (t1, Cmd.none) )

    let reloadPackage =
        { new HotReloadPackage with
            member __.View = view
            member __.Update = update
        }

    log.LogDebug "Swapping reload package"
    updater.Swap reloadPackage

    log.LogDebug "Kicking View"
    updater.ForceRefresh ()
