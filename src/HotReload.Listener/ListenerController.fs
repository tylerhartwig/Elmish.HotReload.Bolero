namespace HotReload.Listener


open Microsoft.FSharp.Reflection
open FSharp.Compiler.PortaCode.CodeModel
open FSharp.Compiler.PortaCode.Interpreter
open Elmish
open HotReload.Client.Main
open System.IO
open System.Reflection
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.SignalR
open HotReload.Listener
open Newtonsoft.Json



module Interpreter =
    let interpreter = EvalContext(System.Reflection.Assembly.Load)


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

    type ProgramUpdater<'msg,'model>(initial) =
        let mutable update : 'msg -> 'model -> 'model * Cmd<'msg> = initial

        member __.SwapUpdate newUpdate =
            update <- newUpdate

        member __.Update msg model = update msg model


    let handleUpdate (updater : ProgramUpdater<'msg, 'model>) (files : (string * DFile)[]) =
        printfn "Created interpreter! %A" interpreter

        lock interpreter (fun () ->
    //        let toAdd = files |> Array.filter (fun (_, file) -> file.Code <> null)
    //        printfn "Adding: %A" toAdd
            files |> Array.iter (fun (fileName, file) -> interpreter.AddDecls file.Code)
            files |> Array.iter (fun (fileName, file) -> interpreter.EvalDecls (envEmpty, file.Code))
          )

        let getTypeForRef (ref : DEntityRef) =
            let (DEntityRef name) = ref

            match interpreter.ResolveEntity(ref) with
            | REntity t -> t
            | _ -> failwith "Couldn't resolve entity type"

        let mem = tryFindMemberInFiles "UniqueUpdate" files
        match mem with
        | Some (def, expr) ->
            try
                printfn "Found member!"
                let entity = interpreter.ResolveEntity(def.EnclosingEntity)
                printfn "Got entity! %A" entity
                let (def, memberValue) = interpreter.GetExprDeclResult(entity, def.Name)
                printfn "Got member value! %A" memberValue
                let value = getVal memberValue

                match value with
                | :? MethodLambdaValue as mlv ->
                    let (MethodLambdaValue lambda) = mlv

//                    updater.SwapUpdate(fun msg model ->
//                        try
                    let untypedUpdater = lambda ([||], [| |])

                    printfn "Call successful! Result: %A" untypedUpdater


                    let genericUpdater = untypedUpdater :?> obj -> obj -> obj * Cmd<obj>

//                    let updater = FSharpValue.MakeFunction(typeof<>, untypedUpdater)

                    printfn "Successfully cast: %A" genericUpdater

                    let model = initModel
                    let message = Message.Increment

                    let newSet = genericUpdater (box message) (box model)
                    printfn "Got new set! %A" newSet


//                    updater msg model
    //                        |> unbox<'model * Cmd<'msg>>
//                        with e ->
//                            printfn "Update call failed: %A" e
//                            model, Cmd.none)


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
        printfn "Received %s" json

        let files = Interpreter.deserialize json
//
        Interpreter.handleUpdate (Interpreter.ProgramUpdater(fun (msg : Message) (model : Model) ->
            printfn "Received message: %A" msg
            model, Cmd.none)) files

        hub.Clients.All.SendAsync("Update", json)