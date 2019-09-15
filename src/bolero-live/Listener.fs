namespace Elmish.HotReload.Bolero.Cli

open Elmish.HotReload.Bolero.Cli
open Microsoft.AspNetCore.SignalR
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

type Startup () =
    member this.ConfigureServices(services : IServiceCollection) =
        services.AddControllers() |> ignore
        services.AddSignalR() |> ignore

    member this.Configure(app : IApplicationBuilder, env : IHostEnvironment) =
        app.UseCors(fun builder ->
            builder.WithOrigins("*")
                .AllowAnyHeader()
                .WithMethods("GET", "POST") |> ignore) |> ignore

        app.UseRouting() |> ignore

        app.UseEndpoints(fun builder ->
            builder.MapHub<ReloadHub>("/ReloadHub") |> ignore

            builder.MapPut("/update", fun context ->
                let hub = context.RequestServices.GetService<IHubContext<ReloadHub>>()
                let controller = ListenerController(hub)
                controller.Update(context.Request)
                ) |> ignore
        ) |> ignore
