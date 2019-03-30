module HotReload.Library

open Microsoft.FSharp.Reflection
open Microsoft.AspNetCore.SignalR.Client
open BlazorSignalR
open Elmish
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
            let value = Interpreter.getVal memberValue

            match value with
            | :? MethodLambdaValue as mlv ->
                let (MethodLambdaValue lambda) = mlv

                updater.SwapUpdate(fun msg model ->
                    try
                        let untypedUpdater = lambda ([||], [| |])

                        printfn "Call successful! Result: %A" untypedUpdater

                        let updater = untypedUpdater :?> 'msg -> 'model -> 'model * Cmd<'msg>

                        printfn "Successfully cast: %A" updater

                        updater msg model
//                        |> unbox<'model * Cmd<'msg>>
                    with e ->
                        printfn "Update call failed: %A" e
                        model, Cmd.none)


            printfn "Got Value!: %A" value
        with e ->
            printfn "Got exception: %A" e

    | None ->
        printfn "could not find member"



let startConnection updater =
    let hub = createConnection()
    hub.On("Update", fun json -> deserialize json |> handleUpdate updater) |> ignore
    connect hub |> Async.Start



module Program =
    let withHotReload (program : Program<_, _, _,_>) =
        let updater = ProgramUpdater(program.update)

        startConnection updater

        { program with
            update = updater.Update
        }
