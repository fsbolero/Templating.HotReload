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

#r "paket: groupref fake //"
#load "paket-files/fsbolero/bolero/tools/Utility.fsx"

open System.IO
open System.Net
open System.Text
open System.Text.RegularExpressions
open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.IO.FileSystemOperators
open Utility

let config = getArg "-c" "Debug"
let version = getArgOpt "-v" >> Option.defaultWith (fun () ->
    CreateProcess.fromRawCommand ".paket/nbgv" ["get-version"; "-v"; "SemVer2"]
    |> CreateProcess.redirectOutput
    |> Proc.run
    |> fun r -> r.Result.Output.Trim()
)
let verbosity = getFlag "--verbose" >> function
    | true -> "n"
    | false -> "m"
let buildArgs o =
    sprintf "-c:%s -v:%s" (config o) (verbosity o)

Target.description "Run the compilation phase proper"
Target.create "build" (fun o ->
    dotnet "build" "Bolero.HotReload.sln %s" (buildArgs o)
)

Target.description "Create the NuGet packages"
Target.create "pack" (fun o ->
    Fake.DotNet.Paket.pack (fun p ->
        { p with
            OutputPath = "build"
            Version = version o
            ToolPath = ".paket/paket"
        }
    )
)

Target.description "Run the test project in client-side mode"
Target.create "run-client" (fun _ ->
    dotnet "run" "-p tests/Server --bolero:serverside=false"
)

Target.description "Run the test project in server-side mode"
Target.create "run-server" (fun _ ->
    dotnet "run" "-p tests/Server --bolero:serverside=true"
)

"build"
    ==> "pack"

"build" ==> "run-client"
"build" ==> "run-server"

Target.runOrDefaultWithArguments "build"
