namespace Elmish.HotReload.Bolero.Cli

open Microsoft.AspNetCore.Http
open Newtonsoft.Json
open Microsoft.AspNetCore.SignalR
open System.IO
open System.Threading.Tasks
open Microsoft.Extensions.Logging


type ReloadHub (log : ILogger<ReloadHub>) = 
    inherit Hub()

    override this.OnConnectedAsync() =
        log.LogError "Client Connected!"
        Task.FromResult(true) :> Task

type Updates =
    | Run
    | Rerun of string list

type ListenerController (hub : IHubContext<ReloadHub>) =

    static member val workingDir : string = "" with get, set

    member this.Update(request : HttpRequest) =
        printfn "Starting Update"
        async {
            let reader = new StreamReader(request.Body)
            let! json = reader.ReadToEndAsync () |> Async.AwaitTask
            printfn "Read Json"
            let update = JsonConvert.DeserializeObject<Updates> json
            match update with
            | Rerun dllUpdates ->
                let fileList = dllUpdates |> List.map (Path.GetFileName)
                let fileName = fileList |> List.head
                let file = dllUpdates |> List.head
                let fileContents = File.ReadAllBytes(file)

                printfn "Sending to clients" 
                try
                    do! hub.Clients.All.SendAsync(method = "Update", arg1 = fileName, arg2 = fileContents) |> Async.AwaitTask
//                    do! hub.Clients.All.SendAsync(method = "Update", arg1 = ()) |> Async.AwaitTask
                    printfn "Finished sending to clients"
                with ex -> 
                    printfn "Failed to send to client:\n%s" (ex.ToString())
            | Run ->
                do! Task.FromResult(()) |> Async.AwaitTask
        } |> Async.StartAsTask :> Task
