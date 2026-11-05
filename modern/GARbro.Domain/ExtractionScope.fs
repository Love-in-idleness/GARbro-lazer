namespace GARbro.Domain

open System

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
