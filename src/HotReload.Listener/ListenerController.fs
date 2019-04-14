namespace HotReload.Listener


open HotReload.Library.Reload
open HotReload.Library.PortaCodeHelper
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
    let deserialize str = JsonConvert.DeserializeObject<(string * DFile)[]>(str)




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

    [<HttpPut("debug")>]
    member this.Debug () =
        let reader = new StreamReader(this.Request.Body)
        let json = reader.ReadToEnd ()
        printfn "Received %s" json

        let files = Interpreter.deserialize json

        let updater = handleUpdate files
        getMessageValue files

        let pair = updater.Update (convertToI Increment) (convertToI initModel) |> convertToC<(Model * Cmd<Message>)>
        let (model, cmd) = pair
//        let cModel = convertToCRecord typeof<Model> (model :?> RecordValue) |> unbox<Model>

        printfn "Got Pair: %A" pair
        printfn "Got new model: %A" model
        printfn "Got new command: %A" cmd

