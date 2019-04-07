namespace HotReload.Listener


open HotReload.Library
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
        let newSet = updater.Update (Decrement) (initModel)
        printfn "Got new set: %A" newSet

