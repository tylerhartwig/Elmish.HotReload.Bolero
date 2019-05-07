module Elmish.HotReload.Resolve

open Elmish
open Microsoft.FSharp.Reflection
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.DerivedPatterns
open Microsoft.FSharp.Quotations.Patterns
open System.Reflection
open System.Reflection

type ResolverInfo =
    | StaticProperty of moduleName : string * valueName : string * deps : obj list
    | ObjectProperty of typeName : string * self : obj * valueName : string * deps : obj list

//let supportedViewSchemes = """
//The following is supported:
//let bound functions in modules with the shape: 'model -> ('msg -> unit) -> 'view
//"""
//
//let supportedUpdateSchemes = """
//The following is supported:
//let bound functions in modules with the shape: 'msg -> 'model -> 'model * Cmd<'msg>
//"""

let makeFailMessage f = sprintf "Failed to resolve %A\n" f


let rec eval = function
    | Value (v, t) -> v
    | Call (None, mi, args) -> mi.Invoke (null, evalAll args)
    | Call (Some e, mi, args) -> mi.Invoke(eval e, evalAll args)
    | FieldGet (thisObj, fieldInfo) ->
        match thisObj with
        | Some o -> fieldInfo.GetValue(eval o)
        | None -> fieldInfo.GetValue(null)
    | arg -> failwithf "Cannot evaluate %A" arg

and evalAll args = List.map eval args |> List.toArray

let rec resolve<'expected> = function
    | Lambdas (_, Call (None, methodInfo, _))
    | Lambda (_, Lambda (_, Call (None, methodInfo, _))) when methodInfo.ReturnType = typeof<'expected> ->
        StaticProperty (methodInfo.DeclaringType.FullName, methodInfo.Name, [])
    | Let (label, depExpr, rest) ->
        let dep = eval depExpr
        let info = resolve<'expected> rest

        match info with
        | StaticProperty (m, v, deps) -> StaticProperty (m, v, [dep] @ deps)
    | expr -> failwith (makeFailMessage expr)


let resolveView (viewExpr : Expr<'model -> ('msg -> unit) -> 'view>) =
    resolve<'view> viewExpr

let resolveUpdate (updateExpr : Expr<'msg -> 'model -> 'model * Cmd<'msg>>) =
    resolve<'model * Cmd<'msg>> updateExpr

let rec findFun resolveInfo (assemblies : seq<Assembly>) =
    match resolveInfo with
    | StaticProperty (moduleName, valueName, deps) ->
        assemblies
        |> Seq.tryPick (fun a -> a.DefinedTypes |> Seq.tryFind (fun t -> t.FullName = moduleName))
        |> Option.map (fun t -> t.DeclaredMembers |> Seq.find (fun m -> m.Name = valueName), deps)


let makeFunFromMemberInfo (memInfo : MemberInfo) deps self =
    match memInfo with
    | :? MethodInfo as methInfo ->
        fun arg1 arg2 ->
            methInfo.Invoke(self, deps @ [ arg1; arg2 ] |> List.toArray)


let rec makeFun resolveInfo (assemblies : seq<Assembly>) =
    match resolveInfo with
    | StaticProperty (moduleName, valueName, deps) ->
        assemblies
        |> Seq.tryPick (fun a -> a.DefinedTypes |> Seq.tryFind (fun t -> t.FullName = moduleName))
        |> Option.map (fun t -> t.DeclaredMembers |> Seq.find (fun m -> m.Name = valueName), deps)
        |> Option.map (fun (mInfo, deps) -> makeFunFromMemberInfo mInfo deps null)
//    | ObjectProperty (typeName, self, valueName, deps) ->
//        assemblies
//        |> Seq.tryPick (fun a -> a.DefinedTypes |> Seq.tryFind (fun t -> t.FullName = typeName))
//        |> Option.map (fun t -> t.DeclaredMembers |> Seq.find)

