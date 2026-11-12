namespace GARbro.Domain

open System
open System.IO

/// Selection is an intersection with the browsed directory, never a request to widen the scope.
[<AbstractClass; Sealed>]
type ExtractionScope private () =
    static member private Normal(name: string) = (if isNull name then "" else name).Replace('\\', '/')
    static member RelativeName(directory: string, name: string) =
        let prefix = ExtractionScope.Normal(directory).TrimEnd('/')
        let name = ExtractionScope.Normal(name)
        if prefix = "" then name
        elif name.StartsWith(prefix + "/", StringComparison.Ordinal) then name.Substring(prefix.Length + 1)
        else null
    static member Includes(directory: string, selection: string[], name: string) =
        let relative = ExtractionScope.RelativeName(directory, name)
        not (String.IsNullOrEmpty relative) &&
        (isNull selection || (selection |> Array.exists (fun selected ->
            let selected = ExtractionScope.Normal(selected).TrimEnd('/')
            selected <> "" && (ExtractionScope.Normal(name) = selected ||
                ExtractionScope.Normal(name).StartsWith(selected + "/", StringComparison.Ordinal)))))

/// Stable format identifiers shared by extraction and batch conversion.
[<AbstractClass; Sealed>]
type MediaOutputFormats private () =
    static member Normalize(format: string) =
        if String.IsNullOrWhiteSpace format then raise (ArgumentException("Choose an output format."))
        elif format.Contains(':') then format.Trim()
        else
            match format.Trim().TrimStart('.').ToUpperInvariant() with
            | "JPG" -> "JPEG"
            | "TIF" -> "TIFF"
            | "AAC" -> "M4A"
            | value -> value
    static member Audio = [| "AUTO"; "WAV"; "FLAC"; "M4A" |]
    static member Video = [| "MP4"; "MKV" |]
    static member Extension(format: string) =
        match format with
        | "AUTO" | "AUDIO" | "WAV" -> "wav"
        | "FLAC" -> "flac"
        | "M4A" -> "m4a"
        | "MP4" -> "mp4"
        | "MKV" -> "mkv"
        | _ -> raise (ArgumentException("Unsupported media output format."))

[<AbstractClass; Sealed>]
type ImageOutputFormats private () =
    static member Common = [| "PNG"; "WEBP"; "BMP"; "JPEG"; "TIFF"; "TGA" |]

[<AbstractClass; Sealed>]
type ScriptOutputFormats private () =
    static member private FileName(name: string) =
        Path.GetFileName(if isNull name then "" else name.Replace('\\', '/'))

    static member ForInput(name: string) =
        let fileName = ScriptOutputFormats.FileName(name).ToLowerInvariant()
        match fileName with
        | "nscript.dat" | "data.dsm" -> "TXT"
        | "scripts.rxdata" | "scripts.rvdata" | "scripts.rvdata2" -> "RB"
        | _ ->
            match Path.GetExtension(fileName) with
            | ".gsc" -> "TSC"
            | ".rpyc" -> "RPY"
            | ".ks" -> "KS"
            | ".tjs" -> "TJS"
            | ".ast" -> "AST"
            | ".asb" -> "JSON"
            | "._bs" -> "BSD"
            | "._bp" -> "BPD"
            | _ -> null

    static member NeedsSignature(name: string) =
        let fileName = ScriptOutputFormats.FileName(name)
        fileName <> "" && not (Path.HasExtension fileName)

    static member Supports(format: string) =
        match format with
        | "TSC" | "RPY" | "KS" | "TJS" | "AST" | "JSON" | "BSD" | "BPD" | "TXT" | "RB" -> true
        | _ -> false

    static member UsesBinaryPreview(name: string) =
        match ScriptOutputFormats.ForInput(name) with
        | "TSC" | "RPY" | "JSON" | "BSD" | "BPD" | "RB" -> true
        | _ -> false
