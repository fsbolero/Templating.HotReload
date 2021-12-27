// $begin{copyright}
//
// This file is part of Bolero
//
// Copyright (c) 2018 IntelliFactory and contributors
//
// Licensed under the Apache License, Version 2.0 (the "License"); you
// may not use this file except in compliance with the License.  You may
// obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
// implied.  See the License for the specific language governing
// permissions and limitations under the License.
//
// $end{copyright}

namespace Bolero.Templating.Client

open System
open System.Collections.Concurrent
open System.Threading.Tasks
open Microsoft.AspNetCore.Components
open Microsoft.AspNetCore.SignalR.Client
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Bolero
open Bolero.Templating
open Bolero.TemplatingInternals
open Elmish

type private CacheEntry =
    | Requested
    | Received of Parsing.ParsedTemplates

[<AbstractClass>]
type ClientBase() =

    let cache = ConcurrentDictionary<string, CacheEntry>()

    member this.StoreFileContent(filename, content) =
        cache[filename] <- Received (Parsing.ParseFileOrContent content "")

    member this.RefreshAllFiles() =
        cache
        |> Seq.choose (function
            | KeyValue (_, Requested) -> None
            | KeyValue (filename, Received _) -> Some (this.RequestFile filename))
        |> Task.WhenAll

    abstract RequestFile : string -> Task

    abstract SetOnChange : (unit -> unit) -> unit

    interface IClient with

        member this.RequestTemplate(filename, subtemplate) =
            let entry = cache.GetOrAdd(filename, fun filename ->
                this.RequestFile(filename) |> ignore
                Requested)
            match entry with
            | Requested ->
                None
            | Received tpl ->
                Some (fun vars ->
                    let tpl =
                        match subtemplate with
                        | null -> tpl.Main
                        | sub -> tpl.Nested[sub]
                    let expr = Parsing.Concat tpl.Expr
                    ConvertExpr.ConvertNode vars expr)

        member this.SetOnChange(callback) =
            this.SetOnChange(callback)

        member this.FileChanged(filename, content) =
            this.StoreFileContent(filename, content)

type SignalRClient(settings: HotReloadSettings, nav: NavigationManager, logger: ILogger<SignalRClient>) as this =
    inherit ClientBase()

    let hub =
        HubConnectionBuilder()
            .WithUrl(nav.ToAbsoluteUri(settings.Url))
            .Build()

    let mutable rerender = ignore

    let setupHandlers() =
        hub.On("FileChanged", fun filename content ->
            this.StoreFileContent(filename, content)
            rerender()
            Task.CompletedTask)
        |> ignore

    let connect() : Task = task {
        let mutable connected = false
        while not connected do
            try
                do! hub.StartAsync()
                connected <- true
            with _ ->
                do! Task.Delay(settings.ReconnectDelayInMs)
                logger.LogInformation("Hot reload reconnecting...")
        logger.LogInformation("Hot reload connected!")
        do! this.RefreshAllFiles()
        rerender()
    }

    do  hub.add_Closed(fun _ ->
            logger.LogInformation("Hot reload disconnected!")
            connect())
        setupHandlers()
        connect() |> ignore

    override this.RequestFile(filename) = task {
        try
            let! content = hub.InvokeAsync<string>("RequestFile", filename)
            return this.StoreFileContent(filename, content)
        with exn ->
            logger.LogError(exn, "Hot reload failed to request file")
    }

    override this.SetOnChange(callback) =
        rerender <- callback

module Program =

    let private registerClient (comp: ProgramComponent<_, _>) =
        let settings =
            let s = comp.Services.GetService<HotReloadSettings>()
            if obj.ReferenceEquals(s, null) then HotReloadSettings.Default else s
        let logger =
            let loggerFactory = comp.Services.GetService<ILoggerFactory>()
            loggerFactory.CreateLogger<SignalRClient>()
        let client = new SignalRClient(settings, comp.NavigationManager, logger)
        TemplateCache.client <- client
        client :> IClient

    let withHotReload (program: Program<ProgramComponent<'model, 'msg>, 'model, 'msg, Node>) =
        program
        |> Program.map
            (fun init (comp: ProgramComponent<'model, 'msg>) ->
                let client =
                    // In server mode, the IClient service is set by services.AddHotReload().
                    // In client mode, it is not set, so we create it here.
                    match comp.Services.GetService<IClient>() with
                    | null -> registerClient comp
                    | client -> client
                client.SetOnChange(comp.Rerender)
                init comp)
            id id id id

    [<Obsolete "Use withHotReload instead">]
    let inline withHotReloading program =
        withHotReload program
