namespace HotReload.Listener

open HotReload.Listener
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.SignalR
open Newtonsoft.Json
open System.IO
open System.Threading.Tasks


type Updates =
    | Run
    | Rerun of string list


[<Route("/")>]
[<ApiController>]
type ListenerController(hub : IHubContext<ReloadHub>) =
    inherit ControllerBase()

    [<HttpPut("update")>]
    member this.Update () =
        let reader = new StreamReader(this.Request.Body)
        let json = reader.ReadToEnd ()
        printfn "Received %s" json

        hub.Clients.All.SendAsync("Update", json)


    [<HttpPut("watch")>]
    member this.Watch() =
        let reader = new StreamReader(this.Request.Body)
        let json = reader.ReadToEnd ()
        let update = JsonConvert.DeserializeObject<Updates> json
        match update with
        | Rerun dllUpdates ->
            let fileList = dllUpdates |> List.map (Path.GetFileName)
            printfn "Sending client file list: %A" fileList
            let fileName = fileList |> List.head
            let file = dllUpdates |> List.head
            let fileContents = File.ReadAllBytes(file)
            hub.Clients.All.SendAsync(method = "Update", arg1 = (fileName, fileContents))
        | Run ->
            Task.FromResult(()) :> Task


//    [<HttpPut("debug")>]
//    member this.Debug () =
//        let reader = new StreamReader(this.Request.Body)
//        let json = reader.ReadToEnd ()
//        printfn "Received %s" json
//
//        let files = Interpreter.deserialize json
//
//        let updater = handleUpdate files
//
//        let pair = updater.Update (convertToI Increment) (convertToI initModel) |> convertToC<(Model * Cmd<Message>)>
//        let (model, cmd) = pair
////        let cModel = convertToCRecord typeof<Model> (model :?> RecordValue) |> unbox<Model>
//
//        printfn "Got Pair: %A" pair
//        printfn "Got new model: %A" model
//        printfn "Got new command: %A" cmd

