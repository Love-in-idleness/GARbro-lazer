namespace GARbro.Scripting

open System
open System.IO
open System.Text
open System.Text.Json
open System.Threading
open ScriptSyntax

[<AbstractClass; Sealed>]
type AsbScript private () =
    static member Compile(text: string, encoding: Encoding, cancellation: CancellationToken) =
        use document = JsonDocument.Parse(text)
        if document.RootElement.ValueKind <> JsonValueKind.Array then invalid "ASB source must be a command array."
        use output = new MemoryStream()
        use writer = new BinaryWriter(output, Encoding.UTF8, true)
        let writeString value =
            let data = encode encoding value
            writer.Write(uint32 data.Length); writer.Write(data); writer.Write(0uy)
        writer.Write([|65uy;83uy;66uy;0uy;0uy|])
        writer.Write(uint32 (document.RootElement.GetArrayLength()))
        for item in document.RootElement.EnumerateArray() do
            cancellation.ThrowIfCancellationRequested()
            if item.ValueKind = JsonValueKind.String then
                writer.Write(1u); writeString (item.GetString())
            elif item.ValueKind = JsonValueKind.Object then
                writer.Write(0u); writeString (item.GetProperty("name").GetString())
                writer.Write(item.GetProperty("line_number").GetUInt32())
                let attributes = item.GetProperty("attributes").EnumerateObject() |> Seq.sortWith (fun a b -> StringComparer.Ordinal.Compare(a.Name, b.Name)) |> Seq.toArray
                writer.Write(uint32 attributes.Length)
                for attribute in attributes do writeString attribute.Name; writeString (attribute.Value.GetString())
            else invalid "Invalid ASB command."
        output.ToArray()

    static member Export(data: byte[], encoding: Encoding, cancellation: CancellationToken) =
        use input = new MemoryStream(data, false)
        use reader = new BinaryReader(input)
        if reader.ReadBytes(5) <> [|65uy;83uy;66uy;0uy;0uy|] then invalid "Unsupported ASB script signature."
        let readString () =
            let length = int (bounded 0L (input.Length-input.Position-5L) (int64 (reader.ReadUInt32())))
            let bytes = reader.ReadBytes(length)
            if reader.ReadByte() <> 0uy then invalid "Unterminated ASB string."
            (strict encoding).GetString(bytes)
        use output = new MemoryStream()
        use json = new Utf8JsonWriter(output, JsonWriterOptions(Indented=true))
        let count = int (bounded 0L (input.Length/4L) (int64 (reader.ReadUInt32())))
        json.WriteStartArray()
        for _ in 1..count do
            cancellation.ThrowIfCancellationRequested()
            match reader.ReadUInt32() with
            | 1u -> json.WriteStringValue(readString())
            | 0u ->
                json.WriteStartObject(); json.WriteString("name", readString()); json.WriteNumber("line_number", reader.ReadUInt32())
                json.WriteStartObject("attributes")
                let attributes = int (bounded 0L (input.Length/10L) (int64 (reader.ReadUInt32())))
                for _ in 1..attributes do let name = readString() in json.WriteString(name, readString())
                json.WriteEndObject(); json.WriteEndObject()
            | _ -> invalid "Unknown ASB command type."
        if input.Position <> input.Length then invalid "Unexpected ASB trailing data."
        json.WriteEndArray(); json.Flush()
        Encoding.UTF8.GetString(output.ToArray())
