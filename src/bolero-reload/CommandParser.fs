module Elmish.HotReload.Bolero.Cli.CommandParser

open System
open System.IO
open Argu
open FcsWatch.Core
open FcsWatch.Binary
open FcsWatch.Binary.Main

type Arguments =
    // Hot Reload Argument
    | Port of int

    // FCS Arguments
    | Working_Dir of string
    | Project_File of string

    with
        interface IArgParserTemplate with
            member x.Usage =
                match x with
                | Port _ -> "Port for FCSWatch webhook and client communication, default is: 9876"
                | Working_Dir _ -> "From FCSWatch: Specific working directory, default is current directory"
                | Project_File _ -> "From FCSWatch: Entry project file, default is exact fsproj file in working dir"


type ListenerConfig =
    {
        WebArgs : string[]
        BaseUrl : string
    }

let defaultPort = 9876

let processListenerBaseUrl (args : ParseResults<Arguments>) =
    let port =
        match args.TryGetResult Port with
        | Some p -> p
        | None -> defaultPort

    sprintf "http://localhost:%i" port

type ProcessResult =
    { Config : BinaryConfig
      ProjectFile : string }

let processListenerArgs usage (args : ParseResults<Arguments>) =
    try
        let baseUrl = processListenerBaseUrl args

        {
            WebArgs = [|  |]
            BaseUrl = baseUrl
        }
    with ex ->
        let usage = usage ()
        failwithf "%A\n%s" ex.Message usage

let processFcsWatchArgs additionalBinaryArgs usage (args : ParseResults<Arguments>) =
    try
        let defaultConfig = BinaryConfig.DefaultValue

        let workingDir = args.GetResult (Working_Dir, defaultConfig.WorkingDir)

        let projectFile =
            match args.TryGetResult Project_File with
            | Some projectFile -> projectFile
            | None ->
                Directory.EnumerateFiles(workingDir, "*.fsproj")
                |> Seq.filter (fun file -> file.EndsWith ".fsproj")
                |> Seq.toList
                |> function
                    | [] ->
                        failwithf "No project file found, no compilation arguments given and no project file found in \"%s\"" Environment.CurrentDirectory
                    | [ file ] ->
                        printfn "Using implicit project file '%s'" file
                        file
                    | file1 :: file2 :: _ ->
                        failwithf "multiple project files found, e.g. %s and %s" file1 file2

        let webhook = sprintf "%s/update" (processListenerBaseUrl args)
        { ProjectFile = projectFile
          Config =
              { BinaryConfig.DefaultValue with
                  WorkingDir = workingDir
                  Webhook = Some webhook
                  AdditionalBinaryArgs = additionalBinaryArgs }
        }
    with ex ->
        let usage = usage ()
        failwithf "%A\n%s" ex.Message usage


let splitArgs args =
    let arguArgs =
        args |> Array.takeWhile (fun x -> x <> "--")
    let additionalArgs =
        args
        |> Array.skipWhile (fun x -> x <> "--")
        |> (fun a -> if Array.tryHead a = Some "--" then Array.tail a else a)
    (arguArgs, additionalArgs)



let parser = ArgumentParser.Create<Arguments>(programName = "bolero-reload.exe")