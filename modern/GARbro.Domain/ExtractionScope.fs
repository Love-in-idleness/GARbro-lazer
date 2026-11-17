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
        | "scripts.rxdata" | "scripts.rvdata" | "scripts.rvdata2" -> "RGSS-JSON"
        | "script.pak" -> "LUCA-JSON"
        | _ ->
            match Path.GetExtension(fileName) with
            | ".gsc" -> "TSC"
            | ".rpyc" | ".rpymc" -> "RPY"
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

    static member Recompile = [| "GSC"; "NSCRIPT"; "DSM"; "BGI"; "BGI-BP"; "ASB"; "RGSS"; "LUCA" |]

    static member CanExport(name: string, format: string) =
        ScriptOutputFormats.ForInput(name)=format || (format="RB" && ScriptOutputFormats.ForInput(name)="RGSS-JSON")

    static member CanRecompile(name: string, format: string) =
        let extension = Path.GetExtension(ScriptOutputFormats.FileName(name)).ToLowerInvariant()
        match format, extension with
        | "GSC", ".tsc" | "NSCRIPT", ".txt" | "DSM", ".txt"
        | "BGI", ".bsd" | "BGI-BP", ".bpd" | "ASB", ".json" | "RGSS", ".json" | "LUCA", ".json" -> true
        | _ -> false

    static member HasRecompileInput(name: string) =
        ScriptOutputFormats.Recompile |> Array.exists (fun target -> ScriptOutputFormats.CanRecompile(name, target))

    static member FromOutputName(name: string) =
        let name = ScriptOutputFormats.FileName(name).ToLowerInvariant()
        match name, Path.GetExtension(name) with
        | _, ".rpym" | _, ".rpy" -> "RPY"
        | _,_ when name.EndsWith(".rgss.json",StringComparison.Ordinal) -> "RGSS-JSON"
        | _,_ when name.EndsWith(".luca.json",StringComparison.Ordinal) -> "LUCA-JSON"
        | _, ".rxdata" | _, ".rvdata" | _, ".rvdata2" -> "RGSS"
        | "script.pak", _ -> "LUCA"
        | "nscript.dat", _ -> "NSCRIPT"
        | _, ".gsc" -> "GSC"
        | _, ".dsm" -> "DSM"
        | _, "._bs" -> "BGI"
        | _, "._bp" -> "BGI-BP"
        | _, ".asb" -> "ASB"
        | _ -> null

    static member RecompileOutput(path: string, format: string) =
        match format with
        | "RGSS" ->
            let original=if path.EndsWith(".rgss.json",StringComparison.OrdinalIgnoreCase) then path.Substring(0,path.Length-10) else path
            if List.contains (Path.GetExtension(original).ToLowerInvariant()) [".rxdata";".rvdata";".rvdata2"] then original
            else Path.ChangeExtension(original,"rvdata2")
        | "LUCA" ->
            if path.EndsWith(".luca.json",StringComparison.OrdinalIgnoreCase) then path.Substring(0,path.Length-10)
            else Path.Combine(Path.GetDirectoryName(path),"SCRIPT.PAK")
        | "NSCRIPT" -> Path.Combine(Path.GetDirectoryName(path), "nscript.dat")
        | "DSM" -> Path.Combine(Path.GetDirectoryName(path), "data.dsm")
        | "BGI" -> Path.ChangeExtension(path, null)
        | "BGI-BP" -> Path.ChangeExtension(path, "_bp")
        | "GSC" | "ASB" -> Path.ChangeExtension(path, format.ToLowerInvariant())
        | _ -> invalidArg "format" "Unsupported script output format."

    static member ExportOutput(path: string, format: string) =
        match format with
        | "RGSS-JSON" -> path+".rgss.json"
        | "LUCA-JSON" -> path+".luca.json"
        | "RPY" when Path.GetExtension(path).Equals(".rpymc",StringComparison.OrdinalIgnoreCase) -> Path.ChangeExtension(path,"rpym")
        | _ -> Path.ChangeExtension(path,format.ToLowerInvariant())

    static member Supports(format: string) =
        match format with
        | "TSC" | "RPY" | "KS" | "TJS" | "AST" | "JSON" | "BSD" | "BPD" | "TXT" | "RB" | "RGSS-JSON" | "LUCA-JSON" -> true
        | _ -> ScriptOutputFormats.Recompile |> Array.contains format

    static member UsesBinaryPreview(name: string) =
        match ScriptOutputFormats.ForInput(name) with
        | "TSC" | "RPY" | "JSON" | "BSD" | "BPD" | "RB" | "RGSS-JSON" | "LUCA-JSON" -> true
        | _ -> false
