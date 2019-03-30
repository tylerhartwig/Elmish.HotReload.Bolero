namespace HotReload.Listener

open System.IO
open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.SignalR

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