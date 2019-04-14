module HotReload.Library.Reload

open Microsoft.FSharp.Reflection
open Microsoft.AspNetCore.SignalR.Client
open BlazorSignalR
open Elmish
open FSharp.Compiler.PortaCode
open FSharp.Compiler.PortaCode.CodeModel
open FSharp.Compiler.PortaCode.Interpreter
open HotReload.Library
open HotReload.Library.PortaCodeHelper
open Newtonsoft.Json

let createConnection () =
    (new HubConnectionBuilder())
        .WithUrlBlazor(url = "http://localhost:5050/reloadhub")
        .Build()

let connect (hub : HubConnection) = async {
        let mutable connected = false
        while not connected do
            try
                do! hub.StartAsync() |> Async.AwaitTask
                connected <- true
            with e ->
                do! Async.Sleep 500
                printfn "Failed: %A" e.Message
                printfn "Hot reload reconnecting..."
        printfn "Connected!"
    }

type TupleRecord<'a, 'b> =
    {
        Item1 : 'a
        Item2 : 'b
    }

let toTuple r = (r.Item1, r.Item2)

let deserialize str = JsonConvert.DeserializeObject<(string * DFile)[]>(str)

let rec tryFindEntityByName name (decls: DDecl[]) =
    decls |> Array.tryPick (function
        | DDeclEntity (entityDef, ds) ->
            if entityDef.Name = name then
                Some(entityDef)
            else
                match ds with
                | [| |] -> None
                | ds -> tryFindEntityByName name ds
        | _ -> None)

let rec tryFindMemberByName name (decls: DDecl[]) =
    decls |> Array.tryPick (function
        | DDeclEntity (_, ds) -> tryFindMemberByName name ds
        | DDeclMember (membDef, body, _range) -> if membDef.Name = name then Some (membDef, body) else None
        | _ -> None)

let tryFindEntityInFile name (_, file : DFile) = tryFindEntityByName name file.Code
let tryFindMemberInFile memberName (_, file : DFile) = tryFindMemberByName memberName file.Code
//let findMembersInFiles memberName files =
//    files |> Array.filter (fun (_, file) -> box file.Code <> null)
//        |> Array.choose(tryFindMemberInFile memberName)

let tryFindEntityInFiles memberName files =
    files |> Array.filter (fun (_, file) -> box file.Code <> null)
        |> Array.choose(tryFindEntityInFile memberName)
        |> Array.tryHead

let tryFindMemberInFiles memberName files =
    files |> Array.filter (fun (_, file) -> box file.Code <> null)
        |> Array.choose (tryFindMemberInFile memberName)
        |> Array.tryHead


let interpreter = EvalContext(System.Reflection.Assembly.Load)


type ProgramUpdater<'msg,'model>(initial) =
    let mutable update : Option<obj -> obj -> obj * Cmd<obj>> = None

    member __.SwapUpdate newUpdate =
        update <- Some newUpdate

    member __.Update (msg : 'msg) (model : 'model) =
        match update with
        | Some update ->
            let (iModel, cmd) = update msg model
            (convertToC<'model> iModel, Cmd.map unbox<'msg> cmd)
        | None ->
            initial msg model


type Updater<'msg, 'model>(update : 'msg -> 'model -> 'model * Cmd<'msg>) =
    member __.Update (message : 'msg) (model : 'model) =
        let (m, c) = update message model
        (m, Cmd.map box c)
//        (convertToC<'model> iModel, Cmd.map unbox<'msg> cmd)

let getMessageValue (files : (string * DFile)[]) =
    let mems = tryFindMemberInFiles "initModel" files

    printfn "Found Increment member: %A" mems

//    let (Some e) = mems
//
//    let enclosing = interpreter.ResolveEntity(e)
//    let entity = interpreter.
    mems

let handleUpdate  (files : (string * DFile)[]) =
    lock interpreter (fun () ->
        files |> Array.iter (fun (fileName, file) -> interpreter.AddDecls file.Code)
        files |> Array.iter (fun (fileName, file) -> interpreter.EvalDecls (envEmpty, file.Code))
      )

    let mem = tryFindMemberInFiles "myHotReload" files
    match mem with
    | Some (def, expr) ->
        try
//            printfn "Found member!"
            let entity = interpreter.ResolveEntity(def.EnclosingEntity)
//            printfn "Got entity! %A" entity

            let (def, memberValue) = interpreter.GetExprDeclResult(entity, def.Name)
//            printfn "Got member value! %A" memberValue

            let value = getVal memberValue
//            printfn "Got Value!: %A" value

            match value with
            | :? Updater<obj, obj> as updater ->
//                printfn "Found Updater!"
                updater

        with e ->
            printfn "Got exception: %A" e
            failwithf "Got exception: %A" e

    | None ->
        printfn "could not find member"
        failwith "could not find member"



let startConnection (updater : ProgramUpdater<'msg,'model>) =
    let hub = createConnection()
    hub.On("Update", fun json ->
        printfn "Received update"
        let updatePack = deserialize json |> handleUpdate
        printfn "Found updatePack"

        printfn "swapping update"
        updater.SwapUpdate(fun msg model ->
            printfn "calling swapped update"
            updatePack.Update (convertToI msg) (convertToI model)
        )
        printfn "update swapped"
    ) |> ignore
    connect hub

module Program =
    let withHotReload (program : Program<'arg, 'model, 'msg, 'view>) =
        let updater = ProgramUpdater(program.update)

        (startConnection updater) |> Async.Start

        { program with
            update = updater.Update
        }
