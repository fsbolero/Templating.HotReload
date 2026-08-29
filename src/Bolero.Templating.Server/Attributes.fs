namespace Bolero.Templating.Server

open System

[<AttributeUsage(AttributeTargets.Parameter ||| AttributeTargets.Field)>]
type internal TimeSpanConstantAttribute(miliseconds) =
    inherit Attribute()
    member _.Value = TimeSpan.FromMilliseconds miliseconds

