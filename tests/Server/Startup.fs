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

namespace Bolero.Test.Server

open Microsoft.AspNetCore
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Bolero.Server
open Bolero.Templating.Server
open Microsoft.Extensions.Logging

module Page =
    open Bolero.Html
    open Bolero.Server.Html

    let index = doctypeHtml {
        head {
            title { "Bolero Templating Hot Reload test" }
            meta { attr.charset "UTF-8" }
            ``base`` { attr.href "/" }
        }
        body {
            div { attr.id "main"; rootComp<Bolero.Test.Client.Main.MyApp> }
            boleroScript
        }
    }

type Startup(config: IConfiguration) =

    member this.ConfigureServices(services: IServiceCollection) =
        services.AddMvc() |> ignore
        services.AddServerSideBlazor() |> ignore
        services
            .AddBoleroHost()
            .AddHotReload(__SOURCE_DIRECTORY__ + "/../Client")
        |> ignore

    member this.Configure(app: IApplicationBuilder, log: ILogger<Startup>) =
        app.UseStaticFiles()
            .UseRouting()
            .UseBlazorFrameworkFiles()
            .UseEndpoints(fun endpoints ->
                endpoints.UseHotReload()
                endpoints.MapBlazorHub() |> ignore
                endpoints.MapFallbackToBolero(Page.index) |> ignore)
        |> ignore

module Program =
    [<EntryPoint>]
    let Main args =
        WebHost.CreateDefaultBuilder(args)
            .UseStaticWebAssets()
            .UseStartup<Startup>()
            .Build()
            .Run()
        0
