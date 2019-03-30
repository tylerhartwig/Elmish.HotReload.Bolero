namespace HotReload.Listener.Controller

open FSharp.Compiler.PortaCode.CodeModel
open FSharp.Compiler.PortaCode.Interpreter
open System.IO
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.SignalR
open HotReload.Listener
open Newtonsoft.Json


module Interpreter =
    let interpreter = EvalContext(System.Reflection.Assembly.Load)

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

    let handleUpdate (files : (string * DFile)[]) =
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
                let value = getVal memberValue

                printfn "Got Value!: %A" value
            with e ->
                printfn "Got exception: %A" e

        | None ->
            printfn "could not find member"

[<Route("/")>]
[<ApiController>]
type ListenerController(hub : IHubContext<ReloadHub>) =
    inherit ControllerBase()

    [<HttpPut("update")>]
    member this.Update () =
        let reader = new StreamReader(this.Request.Body)
        let json = reader.ReadToEnd ()
        let files = JsonConvert.DeserializeObject<(string * DFile)[]>(json)
        Interpreter.handleUpdate files
        //hub.Clients.All.SendAsync("Update", json)
