module Elmish.HotReload.ReloadContext

open System.IO
open System.Reflection
open System.Runtime.Loader
open System.Collections.Generic
open Microsoft.Extensions.DependencyModel

type ReloadLoaderContext() =
    inherit AssemblyLoadContext ()

    let mutable assemblies = Dictionary<string, Assembly>(HashIdentity.Structural)

    member __.allAssemblies () =
        assemblies |> Seq.map (fun kvp -> kvp.Value)

    member __.cacheAssembly name (assembly : Assembly) =
        assemblies.[assembly.GetName().Name] <- assembly

    override this.Load assemblyName =
        assemblies.[assemblyName.Name]

