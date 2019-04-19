module HotReload.Library.Reload

open HotReload.Library.Helpers
open Microsoft.AspNetCore.SignalR.Client
open Microsoft.Extensions.DependencyModel
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Quotations.Patterns
open Microsoft.FSharp.Quotations.ExprShape
open Microsoft.FSharp.Reflection
open BlazorSignalR
open Elmish
open HotReload.Library
open Microsoft.Extensions.Logging
open Newtonsoft.Json
open System
open System.Threading.Tasks
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Runtime.Loader

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



type HotReloadPackage =
    abstract member Update : obj -> obj -> obj * Cmd<obj>
    abstract member View : obj -> Dispatch<obj> -> obj

type ElmishHotReloadPackage<'msg, 'model, 'view>(update : 'msg -> 'model -> 'model * Cmd<'msg>, view : 'model -> Dispatch<'msg> -> 'view) =
    interface HotReloadPackage with
        member __.Update (message : obj) (model : obj) =
            let (m : 'model, c : Cmd<'msg>) = update (unbox<'msg> message) (unbox<'model> model)
            (box m, Cmd.map box c)

        member __.View (model : obj) (dispatch : Dispatch<obj>) =
            let dispatch' = FSharpValue.MakeFunction(typeof<'msg -> unit>, fun m -> dispatch m |> box)
            let v = view (unbox<'model> model) (unbox<Dispatch<'msg>> dispatch')
            (box v)

type ProgramUpdater<'arg, 'msg, 'model, 'view>(log : ILogger, initialModel, initialUpdate, initialView) =
    let mutable reloadPackage : Option<HotReloadPackage> = None

    member __.Swap package = reloadPackage <- Some package

    member __.Init (a : 'arg) =
        let (model : 'model, cmd : Cmd<'msg>) = initialModel a
        (box model, Cmd.map box cmd)

    member __.View (model : obj) (dispatch : Dispatch<obj>) =
        log.LogDebug <| sprintf "Calling view with model: %A" model
        match reloadPackage with
        | None ->
            log.LogDebug "Calling initial view function"
            let dispatch = FSharpValue.MakeFunction(typeof<'msg -> unit>, fun m -> dispatch m |> box)
            initialView (unbox<'model> model) (unbox<'msg -> unit> dispatch)
        | Some package ->
            log.LogDebug "Calling hot-reload view function"
            let v = package.View model dispatch
            v :?> 'view

    member __.Update (msg : obj) (model : obj) =
        match reloadPackage with
        | None ->
            log.LogDebug "Calling initail update function"
            let (m : 'model, c : Cmd<'msg>) = initialUpdate (unbox<'msg> msg) (unbox<'model> model)
            (box m, Cmd.map box c)
        | Some package ->
            log.LogDebug "Calling hot-reload update function"
            package.Update msg model

let cachedAssemblies = Dictionary<string, byte[]>(HashIdentity.Structural)

let fetchDlls (log : ILogger) assemblies getAssembly =
    assemblies |> List.map (fun assemblyName ->
        async {
            log.LogDebug <| sprintf "Delegating fetch for: %s" assemblyName
            let! assembly = getAssembly assemblyName
            return (assemblyName, assembly)
        })
        |> List.map (fun asy ->
            let (name, assembly) = asy |> Async.RunSynchronously
            log.LogDebug <| sprintf "Received dll from delegate: %s" name
            cachedAssemblies.[name] <- assembly)
//    |> Async.Parallel
//    |> Async.map (Array.iter (fun (name, assembly) ->
//        log.LogDebug <| sprintf "Received dll from delegate: %s" name
//        cachedAssemblies.[name] <- assembly))

let startConnection (log : ILogger) reload getAssembly =
    let hub = createConnection()
    hub.On(methodName = "Update", handler = Action<string * byte[]>(fun (fileName, file) ->
        log.LogDebug <| sprintf "Received file, byte length: %i" file.Length
        cachedAssemblies.[fileName] <- file
        try
            reload()
        with ex ->
            log.LogError(ex,"Failed to reload!")
        )
//        log.LogDebug <| sprintf "Received update command with file list: %A" fileList
////        async {
//        log.LogDebug <| sprintf "Fetching dlls"
//        fetchDlls log fileList getAssembly
//        reload () )
////            let! _ = fetchDlls log fileList getAssembly
////            return reload ()
////        } |> Async.RunSynchronously)
    ) |> ignore
    connect hub

type ReloadLoaderContext(folder) =
    inherit AssemblyLoadContext ()

    let folderPath = folder

    override this.Load assemblyName =
        let deps = DependencyContext.Default
        let res = deps.CompileLibraries |> Seq.filter (fun d -> d.Name.Contains(assemblyName.Name)) |> Seq.toList

        if res.Length > 0 then
            Assembly.Load(new AssemblyName((res |> List.head).Name))
        else
            let expectedFileInfo = FileInfo(sprintf "%s%c%s.dll" folderPath Path.DirectorySeparatorChar assemblyName.Name)

            if File.Exists (expectedFileInfo.FullName) then
                this.LoadFromAssemblyPath(expectedFileInfo.FullName)
            else
                Assembly.Load(assemblyName)




let rec findMember (log : ILogger) memberName (assemblies : seq<Assembly>) =
    log.LogDebug <| sprintf "looking for member %s" memberName
    let memberTypes = MemberTypes.All
    let bindingFlags = BindingFlags.Default
    assemblies |> Seq.collect (fun a -> a.DefinedTypes)
    |> Seq.map (fun t -> log.LogDebug <| sprintf "Found Type: %s" t.Name; t)
    |> Seq.map (fun t ->
        t.GetMembers() |> Array.iter (fun m -> log.LogDebug <| sprintf "Found member %s in type %s" m.Name t.Name)
        t)
    |> Seq.collect (fun t ->
        t.GetMembers() |> Array.filter(fun m -> m.Name = memberName))
//                      bindingFlags,
//                      MemberFilter(fun mem name -> mem.Name = (name :?> string)),
//                      memberName))


let mutable hotReloadContext = ReloadLoaderContext("N/A")

let reloadPipeline (log : ILogger) (updater : ProgramUpdater<_,_,_,_>) memberName =
    log.LogDebug "Clearing previous load context"
    hotReloadContext <- ReloadLoaderContext("Reload Context")
    log.LogDebug "Populating reload context"
    let assemblies = cachedAssemblies |> Seq.map (fun kvp -> Assembly.Load(kvp.Value))
    log.LogDebug "Looking for member info"
    let members = assemblies |> findMember log memberName
    members |> Seq.iter (fun m -> log.LogDebug <| sprintf "Found member %s in type %s" m.Name m.DeclaringType.Name)
    log.LogDebug "taking first member info"
    let memInfo = members |> Seq.head
    log.LogDebug "Resolving updatePackage"
    let updatePackage =
        match memInfo with
        | :? PropertyInfo as p ->
            p.GetValue(null)
        | info -> failwithf "Cannot get reload package from member info: %A" info
    log.LogDebug "Swapping update package"
    updater.Swap (updatePackage :?> HotReloadPackage)


let handleUpdate getAssembly assemblyNames =
    let _ = fetchDlls assemblyNames getAssembly


    ()

module Program =
    let withHotReload log getAssembly (program : Program<'arg, 'model, 'msg, 'view>) (*(packageExpr : Expr<HotReloadPackage>)*) =
        let updater = ProgramUpdater(log, program.init, program.update, program.view)

//        let (moduleName, propertyName) =
//            match packageExpr with
//            | PropertyGet (_, propInfo, _) ->
//                (propInfo.DeclaringType.Name, propInfo.Name)
//            | _ -> failwith "Only module.value is currently supported"



//        (startConnection updater) |> Async.Start

        let reload () = reloadPipeline log updater "myHotReload"

        (startConnection log reload getAssembly) |> Async.Start

        let erasedProg : Program<'arg, obj, obj, 'view> =
            {
                init = updater.Init
                update = updater.Update
                view = updater.View
                setState = fun model -> updater.View model >> ignore
                subscribe = fun _ -> Cmd.none
                onError = program.onError
            }

        erasedProg

//
//let runHotReload (arg: 'arg) (program: Program<'arg, 'model, 'msg, 'view>) =
//        let (model,cmd) = program.init arg
//        let inbox = MailboxProcessor.Start(fun (mb:MailboxProcessor<'msg>) ->
//            let rec loop (state:'model) =
//                async {
//                    let! msg = mb.Receive()
//                    let newState =
//                        try
//                            let (model',cmd') = program.update msg state
//                            program.setState model' mb.Post
//                            cmd' |> List.iter (fun sub -> sub mb.Post)
//                            model'
//                        with ex ->
//                            program.onError ("Unable to process a message:", ex)
//                            state
//                    return! loop newState
//                }
//            loop model
//        )
//        program.setState model inbox.Post
//        let sub =
//            try
//                program.subscribe model
//            with ex ->
//                program.onError ("Unable to subscribe:", ex)
//                Cmd.none
//        sub @ cmd |> List.iter (fun sub -> sub inbox.Post)
//
//let rec tryFindEntityByName name (decls: DDecl[]) =
//    decls |> Array.tryPick (function
//        | DDeclEntity (entityDef, ds) ->
//            if entityDef.Name = name then
//                Some(entityDef)
//            else
//                match ds with
//                | [| |] -> None
//                | ds -> tryFindEntityByName name ds
//        | _ -> None)
//
//let rec tryFindMemberByName name (decls: DDecl[]) =
//    decls |> Array.tryPick (function
//        | DDeclEntity (_, ds) -> tryFindMemberByName name ds
//        | DDeclMember (membDef, body, _range) -> if membDef.Name = name then Some (membDef, body) else None
//        | _ -> None)
//
//let tryFindEntityInFile name (_, file : DFile) = tryFindEntityByName name file.Code
//let tryFindMemberInFile memberName (_, file : DFile) = tryFindMemberByName memberName file.Code
////let findMembersInFiles memberName files =
////    files |> Array.filter (fun (_, file) -> box file.Code <> null)
////        |> Array.choose(tryFindMemberInFile memberName)
//
//let tryFindEntityInFiles memberName files =
//    files |> Array.filter (fun (_, file) -> box file.Code <> null)
//        |> Array.choose(tryFindEntityInFile memberName)
//        |> Array.tryHead
//
//let tryFindMemberInFiles memberName files =
//    files |> Array.filter (fun (_, file) -> box file.Code <> null)
//        |> Array.choose (tryFindMemberInFile memberName)
//        |> Array.tryHead
//
//
//let interpreter = EvalContext(System.Reflection.Assembly.Load)
//
//
//type ProgramUpdater<'arg, 'msg, 'model, 'view>(initialModel, initialUpdate, initialView) =
//    let mutable update : Option<obj -> obj -> obj * Cmd<obj>> = None
//    let mutable view : Option<obj -> Dispatch<obj> -> obj> = None
//
//    member __.SwapView newView =
//        view <- Some newView
//
//    member __.SwapUpdate newUpdate =
//        update <- Some newUpdate
//
//    member __.Init (a : 'arg) =
//        let (model : 'model, cmd : Cmd<'msg>) = initialModel a
//        (box model, Cmd.map box cmd)
//
//    member __.View (model : obj) (dispatch : Dispatch<obj>) =
//        match view with
//        | Some view ->
//            if model.GetType() = typeof<'model> then
//                view (convertToI model) dispatch |> unbox<'view>
//            else
//                view model dispatch |> unbox<'view>
//        | None ->
//            let view : 'view = initialView (unbox<'model> model) (fun (msg : 'msg) -> dispatch (box msg))
//            view
//
//    member __.Update (msg : obj) (model : obj) =
//        match update with
//        | Some update ->
//            if model.GetType() = typeof<'model> && msg.GetType() = typeof<'msg> then
//                update (convertToI msg) (convertToI model)
//            else
//                update msg model
////            (box (convertToC<'model> iModel), Cmd.map box (Cmd.map unbox<'msg> cmd))
//        | None ->
//            let (model : 'model, cmd : Cmd<'msg>) = initialUpdate (unbox<'msg> msg) (unbox<'model> model)
//            (box model, Cmd.map box cmd)
////    member __.Update (msg : obj)  (model : obj) =
////        match update with
////        | Some update ->
////            let (iModel, cmd) = update msg model
////            (box (convertToC<'model> iModel), Cmd.map box (Cmd.map unbox<'msg> cmd))
////        | None ->
////            let (model : 'model, cmd : Cmd<'msg>) = initialUpdate (unbox<'msg> msg) (unbox<'model> model)
////            (box model, Cmd.map box cmd)
//
//
//type ElmishHotReloadPackage<'msg, 'model, 'view>(update : 'msg -> 'model -> 'model * Cmd<'msg>, view : 'model -> Dispatch<'msg> -> 'view) =
//    member __.Update (message : 'msg) (model : 'model) =
//        let (m, c) = update message model
//        (m, Cmd.map box c)
//
//    member __.View (model : 'model) (dispatch : Dispatch<'msg>) =
//        let v = view model dispatch
//        v
//
//
//let handleUpdate  (files : (string * DFile)[]) =
//    lock interpreter (fun () ->
//        files |> Array.iter (fun (fileName, file) -> interpreter.AddDecls file.Code)
//        files |> Array.iter (fun (fileName, file) -> interpreter.EvalDecls (envEmpty, file.Code))
//      )
//
//    let mem = tryFindMemberInFiles "myHotReload" files
//    match mem with
//    | Some (def, expr) ->
//        try
//            let entity = interpreter.ResolveEntity(def.EnclosingEntity)
//            let (def, memberValue) = interpreter.GetExprDeclResult(entity, def.Name)
//            let value = getVal memberValue
//            match value with
//            | :? ElmishHotReloadPackage<obj, obj, obj> as updater ->
//                updater
//
//        with e ->
//            printfn "Got exception: %A" e
//            failwithf "Got exception: %A" e
//
//    | None ->
//        printfn "could not find member"
//        failwith "could not find member"
//
//
//
//let startConnection (updater : ProgramUpdater<_,'msg,'model, _>) =
//    let hub = createConnection()
//    hub.On("Update", fun json ->
//        printfn "Received update"
//        let updatePack = deserialize json |> handleUpdate
//        printfn "Found updatePack"
//
//        printfn "swapping update"
//        updater.SwapUpdate(fun msg model ->
//            printfn "calling swapped update"
//            updatePack.Update msg model)
//        printfn "update swapped"
//
//        printfn "swapping view"
//        updater.SwapView(fun model dispatch ->
//            printfn "calling swapped view"
//            updatePack.View model dispatch)
//        printfn "view swapped"
//    ) |> ignore
//    connect hub
//
//module Program =
//    let withHotReload (program : Program<'arg, 'model, 'msg, 'view>) =
//        let updater = ProgramUpdater(program.init, program.update, program.view)
//
//        (startConnection updater) |> Async.Start
//
//        let erasedProg : Program<'arg, obj, obj, 'view> =
//            {
//                init = updater.Init
//                update = updater.Update
//                view = updater.View
//                setState = fun model -> updater.View model >> ignore
//                subscribe = fun _ -> Cmd.none
//                onError = program.onError
//            }
//
//        erasedProg
