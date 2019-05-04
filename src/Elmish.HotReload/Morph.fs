module Elmish.HotReload.Morph

open System
open Elmish.HotReload
open Microsoft.FSharp.Reflection
open System.Reflection

let comparePropertyInfo (propA : PropertyInfo) (propB : PropertyInfo) =
    propA.PropertyType = propB.PropertyType && propA.Name = propB.Name

let comparePropertyInfos aProps bProps =
    (aProps, bProps) ||> Array.map2 comparePropertyInfo |> Array.fold (&&) true

let toPropertyNameMap = Array.map (fun (p : PropertyInfo) -> p.Name, p) >> Map.ofArray
let toFieldMap properties fields =
    (properties, fields) ||> Array.zip
    |> Array.map (fun (p : PropertyInfo, v) -> p.Name, (p, v))
    |> Map.ofArray

let mapUnionCaseDirect (targetCase : UnionCaseInfo) (sourceCase : UnionCaseInfo) sourceValues =
    FSharpValue.MakeUnion(targetCase, sourceValues)

let mapRecordDirect targetType sourceType sourceRecord =
    FSharpValue.MakeRecord(targetType, (FSharpValue.GetRecordFields(sourceRecord)))

let pairBy pred targetProps sourceProps =
    let rec p pairs targetsMissing targetsLeft (sourceProps : PropertyInfo list) =
        match targetProps with
        | (tp : PropertyInfo)::tail ->
            let sp = pred tp sourceProps
            match sp with
            | Some sourceProp -> p ((tp, sourceProp)::pairs) targetsMissing tail (List.filter (fun s -> s = sourceProp) sourceProps)
            | None -> p pairs (targetsMissing @ [tp]) tail sourceProps
        | [] -> pairs, targetsLeft, sourceProps

    p [] [] targetProps sourceProps

let pairByName = pairBy (fun tp sources -> List.tryFind (fun sp -> sp.Name = tp.Name) sources)
let pairBySingleType = pairBy (fun tp sources ->
    let ofType = List.filter (fun (sp : PropertyInfo) -> sp.PropertyType.Name = tp.PropertyType.Name) sources
    List.tryExactlyOne ofType)


let rec morphUnion targetType sourceType sourceCase =
    let targetCases = FSharpType.GetUnionCases targetType
    let (sourceCase, sourceValues) = FSharpValue.GetUnionFields(sourceCase, sourceType)

    let targetCase = targetCases |> Array.find (fun c -> c.Name = sourceCase.Name)

    match sourceCase.GetFields(), targetCase.GetFields() with
    | st, tt when comparePropertyInfos st tt ->
        mapUnionCaseDirect targetCase sourceCase sourceValues
    | st, tt when st.Length = tt.Length ->
        let targetValues =
            (tt, sourceValues) ||> Array.zip
            |> Array.map (fun (p, o) -> morphAny p.PropertyType o)
        FSharpValue.MakeUnion(targetCase, targetValues)
    | _ ->
        failwithf "morphing %A to %A is not supported yet" sourceCase targetType


and morphRecordJaggedFields targetFieldTypes sourceFieldTypes sourceFields =
    let targetProps = targetFieldTypes |> Array.toList
    let sourceProps = (sourceFieldTypes, sourceFields) ||> toFieldMap

    let rec morph targetProps (sourceProps : Map<string, (PropertyInfo * obj)>) =
        match targetProps with
        | (tp : PropertyInfo)::tail ->
            match sourceProps |> Map.tryFind tp.Name with
            | Some (sp, v) ->
                let sourceType = sp.GetType()
                if sourceType.IsPrimitive && tp.PropertyType.IsPrimitive && sourceType <> tp.PropertyType then
                    Activator.CreateInstance tp.PropertyType :: (morph tail (sourceProps |> Map.remove tp.Name))
                else
                    morphAny tp.PropertyType v :: (morph tail (sourceProps |> Map.remove tp.Name))
            | None -> Activator.CreateInstance tp.PropertyType :: (morph tail sourceProps)
        | [] -> []

    morph targetProps sourceProps


and morphRecord targetType sourceType sourceRecord =
    let targetFieldTypes = FSharpType.GetRecordFields targetType
    let sourceFieldTypes = FSharpType.GetRecordFields sourceType
    let sourceFields = FSharpValue.GetRecordFields sourceRecord

    match sourceFieldTypes, targetFieldTypes with
    | st, tt when st.Length <> tt.Length ->
        let targetFields = morphRecordJaggedFields tt st sourceFields |> List.toArray
        FSharpValue.MakeRecord(targetType, targetFields)
    | st, tt when comparePropertyInfos st tt ->
        mapRecordDirect targetType sourceType sourceRecord
    | st, tt ->
        let targetFields =
            (targetFieldTypes, sourceFields) ||> Array.zip
            |> Array.map (fun (p, o) -> morphAny p.PropertyType o)
        FSharpValue.MakeRecord(targetType, targetFields)
    | _ ->
        failwithf "morphing %A to %A is not supported yet" sourceRecord targetType


and morphAny targetType sourceObj =
    let sourceType = sourceObj.GetType()
    match targetType, sourceType with
    | tt, st when tt = st -> sourceObj
    | tt, st when FSharpType.IsUnion tt && FSharpType.IsUnion st ->
        morphUnion tt st sourceObj
    | tt, st when FSharpType.IsRecord tt && FSharpType.IsRecord st ->
        morphRecord tt st sourceObj
    | _ -> failwithf "morphing type %A to type %A is not supported yet" sourceType targetType

let morph<'a> sourceObj =
    morphAny typeof<'a> sourceObj |> unbox<'a>
