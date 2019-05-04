namespace Elmish.HotReload.Bolero.Cli

open Elmish.HotReload.Bolero.Cli
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting





type Startup () =
    member this.ConfigureServices(services : IServiceCollection) =
        services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1) |> ignore
        services.AddSignalR() |> ignore

    member this.Configure(app : IApplicationBuilder, env : IHostingEnvironment) =
        if env.IsDevelopment() then
            app.UseDeveloperExceptionPage() |> ignore

        app.UseCors(fun builder ->
            builder.WithOrigins("*")
                .AllowAnyHeader()
                .WithMethods("GET", "POST")
                .AllowCredentials() |> ignore) |> ignore

        app.UseSignalR(fun route -> route.MapHub<ReloadHub>(new PathString("/reloadhub"))) |> ignore

        app.UseMvcWithDefaultRoute() |> ignore



