module HotReload.Library

open Microsoft.AspNetCore.SignalR.Client
open BlazorSignalR
open FSharp.Compiler.PortaCode
open FSharp.Compiler.PortaCode.CodeModel
open FSharp.Compiler.PortaCode.Interpreter
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

let rec tryFindMemberByName name (decls: DDecl[]) =
    decls |> Array.tryPick (function
        | DDeclEntity (_, ds) -> tryFindMemberByName name ds
        | DDeclMember (membDef, body, _range) -> if membDef.Name = name then Some (membDef, body) else None
        | _ -> None)

let tryFindMemberInFile memberName (_, file : DFile) = tryFindMemberByName memberName file.Code
let tryFindMemberInFiles memberName files =
    files |> Array.filter (fun (_, file) -> box file.Code <> null)
        |> Array.choose (tryFindMemberInFile memberName)
        |> Array.tryHead


let interpreter = EvalContext(System.Reflection.Assembly.Load)

let handleUpdate (files : (string * DFile)[]) =
    printfn "Created interpreter! %A" interpreter

    lock interpreter (fun () ->
//        let toAdd = files |> Array.filter (fun (_, file) -> file.Code <> null)
//        printfn "Adding: %A" toAdd
        files |> Array.iter (fun (fileName, file) -> interpreter.AddDecls file.Code)
        files |> Array.iter (fun (fileName, file) -> interpreter.EvalDecls (envEmpty, file.Code))
      )
    let mem = tryFindMemberInFiles "update" files
    match mem with
    | Some (def, expr) ->
        try
            printfn "Found member!"
            let entity = interpreter.ResolveEntity(def.EnclosingEntity)
            printfn "Got entity! %A" entity
            match entity with
            | UEntity mainmeth -> printfn "mainmeth in %s" mainmeth.Name
            | REntity ty -> printfn "got member of type %s" ty.FullName
            let (def, memberValue) = interpreter.GetExprDeclResult(entity, def.Name)
            printfn "Got member value! %A" memberValue
            let value = Interpreter.getVal memberValue

            printfn "Got Value!: %A" value
        with e ->
            printfn "Got exception: %A" e

    | None ->
        printfn "could not find member"



let startConnection () =
    let hub = createConnection()
    hub.On("Update", fun json -> deserialize json |> handleUpdate) |> ignore
    connect hub |> Async.Start
