module HotReload.Library.PortaCodeHelper

open Elmish
open System
open FSharp.Compiler.PortaCode.CodeModel
open FSharp.Compiler.PortaCode.Interpreter
//open HotReload.Library
open Microsoft.FSharp.Core
open Microsoft.FSharp.Reflection

let convertToIRecord record =
    FSharpValue.GetRecordFields record |> RecordValue

let convertToIUnion unionValue =
    let t = unionValue.GetType()
    let unionCaseInfo =
        FSharpType.GetUnionCases t
        |> Array.find (fun c -> c.Name = unionValue.ToString())
    UnionValue(unionCaseInfo.Tag, unionValue.ToString(), snd (FSharpValue.GetUnionFields(unionValue, t)))

let convertToI value =
    match value.GetType() with
    | t when FSharpType.IsRecord t ->
        convertToIRecord value |> box
    | t when FSharpType.IsUnion t ->
        convertToIUnion value |> box
    | t -> failwithf "Converting to interpreted type of %s is not supported" t.Name


let convertToCRecord ``type`` (RecordValue fields) =
    FSharpValue.MakeRecord(``type``, fields)

let getTargetCmdType (``type`` : Type) =
    let fun1Type = ``type``.GenericTypeArguments |> Array.head
    let fun2Type = fun1Type.GenericTypeArguments |> Array.head
    fun2Type.GenericTypeArguments |> Array.head

type DynamicHelp private () =
    static member private unboxer x =
        unbox x

    static member DynamicUnbox ``type`` x =
        let flags = System.Reflection.BindingFlags.NonPublic ||| System.Reflection.BindingFlags.Static
        let dUnbox = typeof<DynamicHelp>.GetMethod("unboxer", flags).GetGenericMethodDefinition()
        dUnbox.MakeGenericMethod([|``type``|]).Invoke(null, [|x|])

let rec convertToCType (``type`` : Type) (value : obj) =
    match value with
    | :? RecordValue as r ->
        convertToCRecord ``type`` r
    | :? List<obj> as l ->
        let innerType = ``type``.GenericTypeArguments |> Array.head
        l |> List.map (convertToCType innerType) |> box
    | :? Cmd<'a> as c ->
        let targetType = getTargetCmdType ``type``
        printfn "Target type is %s" targetType.Name
        let mapped = c |> Cmd.map (convertToCType targetType)
        printfn "Mapped cmd: %A of type: %A" mapped (mapped.GetType())
        mapped |> box
    | v when FSharpType.IsTuple(v.GetType()) ->
        // TODO add checks for safety
        let targetTypes = FSharpType.GetTupleElements(``type``)
        let fields = FSharpValue.GetTupleFields(v)
        let cValues =
            (targetTypes, fields) ||> Array.map2 (fun t f ->
                if t <> f.GetType() then
                    convertToCType t f
                else
                    f)
        FSharpValue.MakeTuple(cValues, ``type``)

    | v -> failwithf "Converting to compiled type is not supported from type of: %s" (v.GetType().Name)

let convertToC<'a> value = convertToCType typeof<'a> value |> unbox<'a>
