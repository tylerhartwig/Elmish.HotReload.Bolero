namespace HotReload.Listener

open Microsoft.AspNetCore.SignalR
open Microsoft.Extensions.DependencyModel
open Microsoft.FSharp.Reflection
open System
open System.IO
open System.Reflection
open System.Runtime.Loader

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



type ReloadHub() =
    inherit Hub()

//    static member private onChange (args : FileSystemEventArgs) =
//        let loadContextName = sprintf "HotReload:%A" DateTime.Now
////        let reloadDomain = AppDomain.CreateDomain(appDomainName)
//        let fileInfo = FileInfo(args.FullPath)
//        let files = fileInfo.Directory.EnumerateFiles()
//        let reloadContext = ReloadLoaderContext("/Users/tylerhartwig/experiments/HotReload/src/HotReload.Elmish/obj/Debug/netstandard2.0")
//        try
//            let assemblies =
//                files |> Seq.filter (fun f -> f.Extension = ".dll")
//                    |> Seq.map (fun f ->
//    //                let allBytes = File.ReadAllBytes(fileInfo.FullName)
//                    reloadContext.LoadFromAssemblyPath(fileInfo.FullName))
//
//            let elmishAssembly = assemblies |> Seq.find (fun a -> a.DefinedTypes |> Seq.exists (fun t -> t.Name = "Elmish"))
//            let elmishType = elmishAssembly.DefinedTypes |> Seq.find (fun t -> t.Name = "Elmish")
//            let members = elmishType.GetMembers()
//            let genericUpdate = members |> Array.find (fun m -> m.Name = "update") :?> MethodInfo
//            let initModel = members |> Array.find (fun m -> m.Name = "initModel") :?> PropertyInfo
//            let messageType = members |> Array.find (fun m -> m.Name = "Message") :?> Type
//            let isUnionType = FSharpType.IsUnion messageType
//            let unionCases = FSharpType.GetUnionCases messageType
//            let incrementCase = unionCases |> Array.find (fun c -> c.Name = "Increment")
//            let incrementValue = FSharpValue.MakeUnion(incrementCase, [| |])
//
//            let update = genericUpdate.MakeGenericMethod([| messageType |])
//            let updatedSet = update.Invoke(null, [|incrementValue; initModel.GetValue(null) |])
//
//            printfn "Dynamically called update with result %A" updatedSet
//        with ex -> ()
//        ()
//
//    member hub.UpdateClients fileNames =
//        hub.Clients.All.SendAsync(method = "Update", arg1 = fileNames)
//
//
//
//    static member FCSDebug () =
//        let binaryLocation = "/Users/tylerhartwig/experiments/HotReload/src/HotReload.Elmish/obj/Debug/netstandard2.0"
//        let notifyFilters = NotifyFilters.LastWrite
//        let watcher = new FileSystemWatcher()
//        watcher.Path <- binaryLocation
//        watcher.NotifyFilter <- notifyFilters
//        watcher.Filter <- "*.dll"
//
//        watcher.Changed.Add ReloadHub.onChange
//        watcher.Created.Add ReloadHub.onChange
//        watcher.Deleted.Add ReloadHub.onChange
//        watcher.Renamed.Add ReloadHub.onChange
//
//        watcher.EnableRaisingEvents <- true
//        ()

