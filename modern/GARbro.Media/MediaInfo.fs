namespace GARbro.Media

open System
open System.Globalization
open System.IO
open System.Text.Json

[<CLIMutable>]
type VideoInfo =
    { Width: int; Height: int; Duration: float; FrameRate: float
      HasAudio: bool; VideoCodec: string; AudioCodec: string }

[<CLIMutable>]
type VideoSize = { Width: int; Height: int }

/// Container recognition and bounded preview geometry shared by worker and UI.
[<AbstractClass; Sealed>]
type MediaInfo private () =
    static member IsVideoName(name: string) =
        match Path.GetExtension(name).ToLowerInvariant() with
        | ".mpg" | ".mpeg" | ".mpe" | ".m2v" | ".mp4" | ".m4v" | ".mov" | ".avi"
        | ".wmv" | ".asf" | ".mkv" | ".webm" | ".ogv" | ".ts" | ".m2ts"
        | ".flv" | ".vob" | ".3gp" | ".ivf" -> true
        | _ -> false

    static member IsAudioName(name: string) =
        match Path.GetExtension(name).ToLowerInvariant() with
        | ".wav" | ".mp3" | ".ogg" | ".opus" | ".aif" | ".aiff" | ".flac" | ".aac" | ".m4a" | ".wma" -> true
        | _ -> false

    static member HasAudioHeader(header: byte[]) =
        let at offset (text: string) =
            header.Length >= offset + text.Length &&
            System.Text.Encoding.ASCII.GetString(header,offset,text.Length) = text
        at 0 "ID3" || at 0 "fLaC" || at 0 "OggS" ||
        (at 0 "RIFF" && at 8 "WAVE") || (at 0 "FORM" && (at 8 "AIFF" || at 8 "AIFC")) ||
        (header.Length >= 4 && header[0] = 0xffuy && header[1] &&& 0xe0uy = 0xe0uy && header[2] &&& 0x0cuy <> 0x0cuy)

    static member HasVideoHeader(name: string, header: byte[]) =
        let starts (bytes: byte[]) = bytes.Length <= header.Length && (bytes |> Seq.indexed |> Seq.forall (fun (i, b) -> header[i] = b))
        let at offset (text: string) =
            header.Length >= offset + text.Length &&
            System.Text.Encoding.ASCII.GetString(header, offset, text.Length) = text
        starts [|0uy; 0uy; 1uy; 0xbauy|] || starts [|0uy; 0uy; 1uy; 0xb3uy|] ||
        starts [|0x1auy; 0x45uy; 0xdfuy; 0xa3uy|] ||
        starts [|0x30uy; 0x26uy; 0xb2uy; 0x75uy; 0x8euy; 0x66uy; 0xcfuy; 0x11uy|] ||
        (at 0 "RIFF" && at 8 "AVI ") || at 4 "ftyp" || at 4 "moov" || at 4 "mdat" ||
        at 0 "FLV" || at 0 "DKIF" ||
        (Path.GetExtension(name).Equals(".ogv", StringComparison.OrdinalIgnoreCase) && at 0 "OggS") ||
        (MediaInfo.IsVideoName(name) && header.Length > 188 && header[0] = 0x47uy && header[188] = 0x47uy)

    static member Fit(width: int, height: int) : VideoSize =
        if width <= 0 || height <= 0 || width > 32768 || height > 32768 then
            raise (InvalidDataException("Invalid video dimensions."))
        if int64 width * int64 height > 67108864L then
            raise (InvalidDataException("Video exceeds the pixel limit."))
        let scale = min 1.0 (min (960.0 / float width) (540.0 / float height))
        { Width = max 2 (int (float width * scale) / 2 * 2)
          Height = max 2 (int (float height * scale) / 2 * 2) }

    static member Parse(json: string) : VideoInfo =
        use document = JsonDocument.Parse(json, JsonDocumentOptions(MaxDepth = 32))
        let property (node: JsonElement) (name: string) =
            match (if node.ValueKind = JsonValueKind.Object then node.TryGetProperty(name) else (false, Unchecked.defaultof<JsonElement>)) with
            | true, value -> value
            | _ -> Unchecked.defaultof<JsonElement>
        let text node name =
            let value = property node name
            if value.ValueKind = JsonValueKind.Undefined || value.ValueKind = JsonValueKind.Null then "" else value.ToString()
        let number (value: string) =
            match Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture) with
            | true, value when Double.IsFinite(value) -> value
            | _ -> 0.0
        let integer node name =
            match Int32.TryParse(text node name) with
            | true, value -> value
            | _ -> 0
        let streams = property document.RootElement "streams"
        if streams.ValueKind <> JsonValueKind.Array then raise (InvalidDataException("No video stream was found."))
        let all = streams.EnumerateArray() |> Seq.toArray
        let video = all |> Array.tryFind (fun node ->
            text node "codec_type" = "video" && integer (property node "disposition") "attached_pic" <> 1)
        match video with
        | None -> raise (InvalidDataException("No video stream was found."))
        | Some video ->
            let width, height = integer video "width", integer video "height"
            MediaInfo.Fit(width, height) |> ignore
            let duration = max (number (text video "duration")) (number (text (property document.RootElement "format") "duration"))
            if duration > 604800.0 then raise (InvalidDataException("Video duration exceeds the preview limit."))
            let rate = (text video "avg_frame_rate").Split('/')
            let fps = if rate.Length = 2 && number rate[1] > 0.0 then number rate[0] / number rate[1] else 0.0
            let audio = all |> Array.tryFind (fun node -> text node "codec_type" = "audio")
            { Width = width; Height = height; Duration = max 0.0 duration
              FrameRate = if fps > 0.0 then min 30.0 fps else 25.0
              HasAudio = audio.IsSome; VideoCodec = text video "codec_name"
              AudioCodec = audio |> Option.map (fun node -> text node "codec_name") |> Option.defaultValue "" }

[<CLIMutable>]
type AudioInfo = { SampleRate: int; Channels: int; Duration: float; Codec: string }

[<AbstractClass; Sealed>]
type AudioMetadata private () =
    static member Parse(json: string) : AudioInfo =
        use document = JsonDocument.Parse(json, JsonDocumentOptions(MaxDepth = 32))
        let property (node: JsonElement) (name: string) =
            match (if node.ValueKind = JsonValueKind.Object then node.TryGetProperty(name) else (false, Unchecked.defaultof<JsonElement>)) with
            | true, value -> value
            | _ -> Unchecked.defaultof<JsonElement>
        let text node name =
            let value = property node name
            if value.ValueKind = JsonValueKind.Undefined then "" else value.ToString()
        let number (value: string) =
            match Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture) with
            | true, result when Double.IsFinite(result) -> result
            | _ -> 0.0
        let streams = property document.RootElement "streams"
        if streams.ValueKind <> JsonValueKind.Array then raise (InvalidDataException("No audio stream was found."))
        let stream = streams.EnumerateArray() |> Seq.tryFind (fun node -> text node "codec_type" = "audio")
        match stream with
        | None -> raise (InvalidDataException("No audio stream was found."))
        | Some audio ->
            let rate, channels = int (number (text audio "sample_rate")), int (number (text audio "channels"))
            if rate < 1000 || rate > 384000 || channels < 1 || channels > 8 then raise (InvalidDataException("Unsupported audio parameters."))
            let duration = max (number (text audio "duration")) (number (text (property document.RootElement "format") "duration"))
            if duration > 604800.0 then raise (InvalidDataException("Audio duration exceeds the preview limit."))
            { SampleRate=rate; Channels=channels; Duration=max 0.0 duration; Codec=text audio "codec_name" }
