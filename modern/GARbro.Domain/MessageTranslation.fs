namespace GARbro.Domain

open System

/// Translates an application-owned diagnostic prefix without changing its context.
[<AbstractClass; Sealed>]
type MessageTranslation private () =
    static member Translate(message: string, rows: string, language: string) : string =
        if language <> "zh-CN" || String.IsNullOrEmpty message then message
        else
            rows.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun row -> row.TrimEnd('\r'))
            |> Array.map (fun row -> row.Split('|', 2))
            |> Array.sortByDescending (fun pair -> pair[0].Length)
            |> Array.tryFind (fun pair -> message.StartsWith(pair[0], StringComparison.Ordinal))
            |> Option.map (fun pair -> pair[1] + message.Substring(pair[0].Length))
            |> Option.defaultValue message
