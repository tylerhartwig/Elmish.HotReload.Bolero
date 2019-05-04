module Elmish.HotReload.Resolve

open Elmish
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.DerivedPatterns
open Microsoft.FSharp.Quotations.Patterns
open System.Reflection

type ResolverInfo =
    | StaticProperty of moduleName : string * valueName : string * deps : obj list

let supportedViewSchemes = """
The following is supported:
let bound functions in modules with the shape: 'model -> ('msg -> unit) -> 'view
"""

let supportedUpdateSchemes = """
The following is supported:
let bound functions in modules with the shape: 'msg -> 'model -> 'model * Cmd<'msg>
"""

let makeFailMessage f supported = sprintf "Failed to resolve %A\n:%s" f supported


let rec eval = function
    | Value (v, t) -> v
    | Call (None, mi, args) -> mi.Invoke (null, evalAll args)
    | Call (Some e, mi, args) -> mi.Invoke(eval e, evalAll args)
    | arg -> failwithf "Cannot evaluate %A" arg

and evalAll args = List.map eval args |> List.toArray

let resolveView (viewExpr : Expr<'model -> ('msg -> unit) -> 'view>) =
    match viewExpr with
    | Lambda (_, Lambda (_, Call (None, methodInfo, _))) ->
        StaticProperty (methodInfo.DeclaringType.FullName, methodInfo.Name, [])
    | _ -> failwith (makeFailMessage viewExpr supportedViewSchemes)

let resolveUpdate (updateExpr : Expr<'msg -> 'model -> 'model * Cmd<'msg>>) =
    let rec r expr =
        match expr with
        | Lambdas (_, Call (None, methodInfo,_ ))
        | Lambda (_, Lambda (_, Call (None, methodInfo, _))) when methodInfo.ReturnType = typeof<'model * Cmd<'msg>> ->
            StaticProperty (methodInfo.DeclaringType.FullName, methodInfo.Name, [])
        | Let (label, depExpr, rest) ->
            let dep = eval depExpr
            let info = r rest

            match info with
            | StaticProperty (m, v, deps) -> StaticProperty (m, v, [dep] @ deps)
        | _ -> failwith (makeFailMessage updateExpr supportedUpdateSchemes)

    r updateExpr

let rec findFun resolveInfo (assemblies : seq<Assembly>) =
    match resolveInfo with
    | StaticProperty (moduleName, valueName, deps) ->
        assemblies
        |> Seq.tryPick (fun a -> a.DefinedTypes |> Seq.tryFind (fun t -> t.FullName = moduleName))
        |> Option.map (fun t -> t.DeclaredMembers |> Seq.find (fun m -> m.Name = valueName), deps)
