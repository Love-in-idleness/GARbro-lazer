namespace GARbro.Domain

open System
open System.Globalization

/// UI language resolution is independent of resource/text encoding.
[<AbstractClass; Sealed>]
type LanguageCatalog private () =
    static member Codes = [| "en-US"; "zh-CN"; "ru-RU"; "ja-JP" |]
    static member IsSupported(code: string) =
        code = "auto" || Array.contains code LanguageCatalog.Codes
    static member Resolve(code: string, systemLanguage: string) =
        if code <> "auto" && LanguageCatalog.IsSupported code then code
        else
            let name = if String.IsNullOrWhiteSpace systemLanguage then "en" else systemLanguage
            let prefix = name.Split([| '-'; '_' |]) |> Array.head
            match prefix.ToLowerInvariant() with
            | "zh" -> "zh-CN"
            | "ru" -> "ru-RU"
            | "ja" -> "ja-JP"
            | _ -> "en-US"
