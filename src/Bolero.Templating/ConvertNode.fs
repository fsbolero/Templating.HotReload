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

module Bolero.Templating.Client.ConvertExpr

open System
open Microsoft.AspNetCore.Components
open Bolero
open Bolero.Templating

/// Event handler type whose argument is the given type.
let EventHandlerOf (argType: Type) : Type =
    typedefof<Action<_>>.MakeGenericType([|argType|])

let WrapExpr (innerType: Parsing.HoleType) (outerType: Parsing.HoleType) (expr: obj) : option<obj> =
    let inline bindValueOf (e: obj) = fst (e :?> obj * Action<ChangeEventArgs>)
    if innerType = outerType then None else
    match innerType, outerType with
    | Parsing.Html, Parsing.String ->
        box (Node.Text (unbox expr))
    | Parsing.AttributeValue, Parsing.String ->
        box (string expr)
    | Parsing.Event _, Parsing.Event _ ->
        expr
    | Parsing.Html, Parsing.DataBinding _ ->
        box (Node.Text (string (bindValueOf expr)))
    | (Parsing.String | Parsing.AttributeValue), Parsing.DataBinding _ ->
        box (string (bindValueOf expr))
    | a, b -> failwithf "Hole name used multiple times with incompatible types (%A, %A)" a b
    |> Some

let WrapAndConvert (vars: Map<string, obj>) (subst: list<Parsing.VarSubstitution>) convert expr =
    let vars = (vars, subst) ||> List.fold (fun vars wrap ->
        let unwrapped = vars[wrap.name]
        let wrapped = WrapExpr wrap.innerType wrap.outerType unwrapped
        Map.add wrap.name (defaultArg wrapped unwrapped) vars
    )
    convert vars expr

let rec ConvertAttrTextPart (vars: Map<string, obj>) (text: Parsing.Expr) : string =
    match text with
    | Parsing.Concat texts ->
        texts |> List.map (ConvertAttrTextPart vars >> unbox<string>) |> String.Concat
    | Parsing.PlainHtml text ->
        text
    | Parsing.VarContent varName ->
        vars[varName].ToString()
    | Parsing.WrapVars (subst, text) ->
        WrapAndConvert vars subst ConvertAttrTextPart text
    | Parsing.Fst _ | Parsing.Snd _ | Parsing.Attr _ | Parsing.Elt _ | Parsing.HtmlRef _ ->
        failwithf "Invalid text: %A" text

let rec ConvertAttrValue (vars: Map<string, obj>) (text: Parsing.Expr) : obj =
    match text with
    | Parsing.Concat texts ->
        texts |> List.map (ConvertAttrTextPart vars >> unbox<string>) |> String.Concat |> box
    | Parsing.PlainHtml text ->
        box text
    | Parsing.VarContent varName ->
        vars[varName]
    | Parsing.Fst varName ->
        FSharp.Reflection.FSharpValue.GetTupleField(vars[varName], 0)
    | Parsing.Snd varName ->
        FSharp.Reflection.FSharpValue.GetTupleField(vars[varName], 1)
    | Parsing.WrapVars (subst, text) ->
        WrapAndConvert vars subst ConvertAttrValue text
    | Parsing.Attr _ | Parsing.Elt _ | Parsing.HtmlRef _ ->
        failwithf "Invalid attr value: %A" text

let rec ConvertAttr (vars: Map<string, obj>) (attr: Parsing.Expr) : Attr =
    match attr with
    | Parsing.Concat attrs ->
        Attr.Attrs (List.map (ConvertAttr vars) attrs)
    | Parsing.Attr (name, value) ->
        Attr.Make name (ConvertAttrValue vars value)
    | Parsing.VarContent varName ->
        vars[varName] :?> Attr
    | Parsing.WrapVars (subst, attr) ->
        WrapAndConvert vars subst ConvertAttr attr
    | Parsing.HtmlRef varName ->
        TemplatingInternals.Ref.MakeAttr (vars[varName] :?> HtmlRef)
    | Parsing.Fst _ | Parsing.Snd _ | Parsing.PlainHtml _ | Parsing.Elt _ ->
        failwithf "Invalid attribute: %A" attr

let rec ConvertNode (vars: Map<string, obj>) (node: Parsing.Expr) : Node =
    match node with
    | Parsing.Concat nodes ->
        Node.Concat (List.map (ConvertNode vars) nodes)
    | Parsing.PlainHtml str ->
        Node.RawHtml str
    | Parsing.Elt (name, attrs, children) ->
        Node.Elt name (List.map (ConvertAttr vars) attrs) (List.map (ConvertNode vars) children)
    | Parsing.VarContent varName ->
        vars[varName] :?> Node
    | Parsing.WrapVars (subst, node) ->
        WrapAndConvert vars subst ConvertNode node
    | Parsing.Fst _ | Parsing.Snd _ | Parsing.Attr _ | Parsing.HtmlRef _ ->
        failwithf "Invalid node: %A" node
