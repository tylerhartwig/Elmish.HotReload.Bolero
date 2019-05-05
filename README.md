# Elmish.HotReload.Bolero

Elmish.HotReload.Bolero is a package that enables (experimental) stateful hot-reloading of the Elmish pipeline for Bolero. 
The "stateful" part of the reloading means that your model is preserved between reloads and the screen is automatically 
refreshed. 

This library is brand new, please open issues for any problems you encounter or suggestions you may have. 

####Enable HotReload 

Enabling Bolero Reload comes in 2 parts. Elmish.HotReload relies on the Elmish model and message types to be `obj` in 
order to hot-swap them individually at runtime.

1. Use a compiler directive to indicate a `ProgramComponent<obj, obj>()` will be used during debug, but a properly typed `ProgramComponent` will be used in Release.
2. Use `Program.withHotReload` to erase the `Program` component and indicate where your `view` and `update` functions live. 

 
```fsharp
type MyApp () =
#if !DEBUG
    inherit ProgramComponent<Model, Message> ()
#else
    inherit ProgramComponent<obj, obj>()
#endif

    override this.Program =
        Program.mkProgram (fun _ -> initModel, Cmd.none) update view
            |> Program.withErrorHandler (fun (msg, exn) -> printfn "Error: %s\n\n%A" msg exn)
#if DEBUG
            |> Program.withHotReload (None) <@ view @> <@ update @>
#endif

```

####Run with HotReloading

1. Install the `bolerolive` tool with the following command `dotnet install -g bolero-live`
2. Run `bolerolive` from your Bolero project directory. 

### How does this work?

#### File watch and compilation 

[FCSWatch](https://github.com/humhei/FCSWatch) takes care of watching your project and compiling it when updates occur. 
FCS watch also exposes a WebHook to indicate when an update has occured. This webhook is received by the Bolero CLI tool
which then reads the updated assemblies and updates the Bolero Web Server as well as all running clients.

#### Client Updates

The Bolero CLI Tool exposes a small webserver (to listen for the `FCSWatch` hook) and a SignalR Hub. The SignalR Hub is 
used for client communication. When your Bolero app starts up, `Elmish.HotReload.Bolero` registers the client with the 
SignalR Hub. After the CLI tool receives an update from `FCSWatch` the assemblies are streamed down to the client and 
the pipeline is reloaded.

#### Maintaining the model

`Elmish.HotReload` also contains some logic for magically mapping from a previous type of model, to a newly updated 
model. Currently the support is mainly aimed at models with the same shape can be mapped to themselves (an unfortunate 
artifact of reloading requires this). Some other model changes are supported, with deeper, more robust support to follow. 

### Known Issues

1. Refreshing the page will only yield the latest updates if running in Debug mode. 

    This is because `FCSWatch` does not currently update the distribution dll locations for Blazor and the bolero hotreload cli tool takes care of updated the default directory only.

2. Some model and message type changes fail at runtime.
    
    In order to do stateful hot-reloading, `Elmish.HotReload` must infer how to fill in a new model shape from the old model shape, effort on this will continue, however not all use-cases may be captured at this time. 
    To work around this issue, simple refresh your browser window. 
    
