namespace GARbro.Scripting

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json.Nodes
open System.Threading
open ScriptSyntax

[<AbstractClass; Sealed>]
type RgssScript private () =
    static member private Entries(value) =
        let root = match value with RubyArray values -> values | _ -> invalid "RGSS Scripts must be a Marshal array."
        root |> Seq.map (function
            | RubyArray entry when entry.Count=3 ->
                match entry[0],entry[1],entry[2] with
                | RubyInteger id,RubyString name,RubyString source -> entry,id,name,source
                | _ -> invalid "Invalid RGSS script entry."
            | _ -> invalid "Invalid RGSS script entry.") |> Seq.toArray

    static member private NameEncoding(bytes: RubyBytes, fallback: Encoding) =
        match bytes.Attributes.TryGetValue("encoding"),bytes.Attributes.TryGetValue("E") with
        | (true,RubyString name),_ -> Encoding.GetEncoding(Encoding.ASCII.GetString(name.Bytes))
        | (true,RubySymbol name),_ -> Encoding.GetEncoding(name)
        | _,(true,RubyBool true) -> Encoding.UTF8
        | _,(true,RubyBool false) -> Encoding.ASCII
        | _ -> fallback

    static member Export(data: byte[], name: string, encoding: Encoding, ruby: bool, cancellation: CancellationToken) =
        let value = RubyMarshal.load data cancellation
        let entries = RgssScript.Entries(value)
        let fallback = if isNull encoding then Encoding.GetEncoding(932) else encoding
        let sourceEncoding = if not (isNull encoding) then encoding elif name.EndsWith(".rvdata2",StringComparison.OrdinalIgnoreCase) then Encoding.UTF8 else fallback
        let scripts = JsonArray()
        let readable = StringBuilder()
        let mutable total = 0
        for i in 0..entries.Length-1 do
            cancellation.ThrowIfCancellationRequested()
            let _,id,title,compressed = entries[i]
            let textBytes = ScriptBinary.inflate compressed.Bytes (ScriptBinary.maximum-total) cancellation
            total <- total+textBytes.Length
            let titleEncoding = if isNull encoding then RgssScript.NameEncoding(title,fallback) else encoding
            let titleText = ScriptBinary.decode titleEncoding title.Bytes
            let text = ScriptBinary.decode sourceEncoding textBytes
            let item = JsonObject()
            item["index"] <- JsonValue.Create(i); item["id"] <- JsonValue.Create(id)
            item["name"] <- JsonValue.Create(titleText); item["source"] <- JsonValue.Create(text)
            item["nameEncoding"] <- JsonValue.Create(titleEncoding.WebName)
            item["sourceEncoding"] <- JsonValue.Create(sourceEncoding.WebName)
            scripts.Add(item)
            readable.Append("# ").Append(titleText.Replace("\r", " ").Replace("\n", " ")).Append('\n').Append(text).Append('\n') |> ignore
        if ruby then readable.ToString()
        else
            let result = JsonObject()
            result["schemaVersion"] <- JsonValue.Create(1); result["format"] <- JsonValue.Create("RGSS")
            result["extension"] <- JsonValue.Create(Path.GetExtension(name).ToLowerInvariant())
            result["original"] <- JsonValue.Create(Convert.ToBase64String(data)); result["scripts"] <- scripts
            result.ToJsonString(System.Text.Json.JsonSerializerOptions(WriteIndented=true))

    static member Compile(text: string, encoding: Encoding, cancellation: CancellationToken) =
        let document = JsonNode.Parse(text,documentOptions=System.Text.Json.JsonDocumentOptions(MaxDepth=128))
        if document["schemaVersion"].GetValue<int>()<>1 || document["format"].GetValue<string>()<>"RGSS" then
            invalid "Unsupported RGSS script JSON."
        let original = Convert.FromBase64String(document["original"].GetValue<string>())
        if original.Length>ScriptBinary.maximum then invalid "Script data exceeds the size limit."
        let entries = RgssScript.Entries(RubyMarshal.load original cancellation)
        let scripts = document["scripts"].AsArray()
        if scripts.Count<>entries.Length then invalid "RGSS JSON must preserve every script entry."
        let seen = HashSet<int>()
        let result = ResizeArray<RubyValue>()
        let mutable total = 0
        let mutable originalTotal = 0
        let mutable changed = false
        for script in scripts do
            cancellation.ThrowIfCancellationRequested()
            let index = script["index"].GetValue<int>()
            if index<0 || index>=entries.Length || not (seen.Add(index)) then invalid "Invalid or duplicate RGSS script index."
            let template,oldId,originalName,originalSource = entries[index]
            let entry=ResizeArray<RubyValue>(template)
            let name={originalName with Attributes=Dictionary(originalName.Attributes)}
            let source={originalSource with Attributes=Dictionary(originalSource.Attributes)}
            entry[1]<-RubyString name
            entry[2]<-RubyString source
            let titleEncoding = if isNull encoding then Encoding.GetEncoding(script["nameEncoding"].GetValue<string>()) else encoding
            let sourceEncoding = if isNull encoding then Encoding.GetEncoding(script["sourceEncoding"].GetValue<string>()) else encoding
            let newName = encode titleEncoding (script["name"].GetValue<string>())
            let newSource = cstring sourceEncoding (script["source"].GetValue<string>())
            total <- total+newSource.Length
            if total>ScriptBinary.maximum then invalid "Script data exceeds the size limit."
            let oldSource = ScriptBinary.inflate source.Bytes (ScriptBinary.maximum-originalTotal) cancellation
            originalTotal<-originalTotal+oldSource.Length
            entry[0] <- RubyInteger(script["id"].GetValue<int>())
            if index<>result.Count || script["id"].GetValue<int>()<>oldId || name.Bytes<>newName || oldSource<>newSource then changed<-true
            if name.Bytes<>newName then
                name.Bytes <- newName
                name.Attributes.Remove("encoding") |> ignore
                name.Attributes.Remove("E") |> ignore
                if titleEncoding.CodePage=65001 then name.Attributes["E"] <- RubyBool true
                elif titleEncoding.CodePage<>20127 then
                    name.Attributes["encoding"] <- RubyString { Bytes=Encoding.ASCII.GetBytes(titleEncoding.WebName); Attributes=Dictionary() }
            if oldSource<>newSource then source.Bytes <- ScriptBinary.deflate newSource
            result.Add(RubyArray entry)
        if changed then RubyMarshal.save (RubyArray result) cancellation else original

    static member OutputExtension(text: string) =
        let document = JsonNode.Parse(text)
        if document["schemaVersion"].GetValue<int>()<>1 || document["format"].GetValue<string>()<>"RGSS" then
            invalid "Unsupported RGSS script JSON."
        match document["extension"].GetValue<string>() with
        | ".rxdata" -> ".rxdata"
        | ".rvdata" -> ".rvdata"
        | ".rvdata2" -> ".rvdata2"
        | _ -> invalid "Unsupported RGSS Scripts extension."

