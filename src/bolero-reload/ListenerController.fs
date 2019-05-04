namespace Elmish.HotReload.Bolero.Cli

open Newtonsoft.Json
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.SignalR
open System.IO
open System.Threading.Tasks



type ReloadHub () = inherit Hub()

type Updates =
    | Run
    | Rerun of string list

[<Route("/")>]
[<ApiController>]
type ListenerController (hub : IHubContext<ReloadHub>) =
    inherit ControllerBase ()

    [<HttpPut("update")>]
    member this.Update() =
        let reader = new StreamReader(this.Request.Body)
        let json = reader.ReadToEnd ()
        let update = JsonConvert.DeserializeObject<Updates> json
        match update with
        | Rerun dllUpdates ->
            let fileList = dllUpdates |> List.map (Path.GetFileName)
            let fileName = fileList |> List.head
            let file = dllUpdates |> List.head
            let fileContents = File.ReadAllBytes(file)

            let tmpPath = "/Users/tylerhartwig/experiments/HotReload/src/HotReload.Client/bin/Debug/netstandard2.0/dist/_framework/_bin"
            File.WriteAllBytes(Path.Combine(tmpPath, fileName), fileContents)

            hub.Clients.All.SendAsync(method = "Update", arg1 = (fileName, fileContents))
        | Run ->
            Task.FromResult(()) :> Task
