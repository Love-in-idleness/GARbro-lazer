namespace GARbro.Scripting

open System
open System.Buffers.Binary
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json.Nodes
open System.Threading
open ScriptSyntax

module internal LucaData =
    type Entry = { Name:string; Offset:int; Data:byte[] }
    type Archive = { Header:byte[]; Index:int; Block:int; Entries:Entry[] }
    type Parameter = { Kind:string; Value:JsonNode; Encoding:Encoding; Target:string }
    type Command = { Offset:int; Opcode:byte; Flags:byte; Fixed:byte[]; Parameters:Parameter[]; Unknown:bool }
    let node value = JsonValue.Create(value:string) :> JsonNode
    let integer value = JsonValue.Create(value:int64) :> JsonNode
    let json () = JsonObject()
    let u32 (bytes:byte[]) position =
        if position<0 || position>bytes.Length-4 then invalid "Truncated Luca script archive."
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position,4))
    let readArchive (bytes:byte[]) (encoding:Encoding) =
        if bytes.Length<44 || bytes.Length>ScriptBinary.maximum then invalid "Invalid Luca script archive."
        let headerSize=int (bounded 36L (int64 bytes.Length) (int64 (u32 bytes 0)))
        let block=int (bounded 1L (int64 ScriptBinary.maximum) (int64 (u32 bytes 12)))
        let count=int (bounded 1L (int64 ((headerSize-36)/8)) (int64 (u32 bytes 4)))
        if headerSize%block<>0 then invalid "Invalid Luca archive block alignment."
        let mutable index=36
        while index<=headerSize-count*8 && u32 bytes index<>uint32 (headerSize/block) do index<-index+4
        if index>headerSize-count*8 then invalid "Invalid Luca archive index."
        let named=u32 bytes 32 &&& 512u<>0u
        let mutable namePosition=if named then int (bounded 36L (int64 (headerSize-1)) (int64 (u32 bytes (index-4)))) else 0
        let ranges=ResizeArray<int64*int64>()
        let entries=Array.init count (fun i ->
            let offset=int (bounded (int64 headerSize) (int64 bytes.Length) (int64 (u32 bytes (index+i*8))*int64 block))
            let length=int (bounded 0L (int64 (bytes.Length-offset)) (int64 (u32 bytes (index+i*8+4))))
            if ranges |> Seq.exists(fun (a,b)->int64 offset<b && a<int64 (offset+length)) then invalid "Overlapping Luca archive entries."
            ranges.Add(int64 offset,int64 (offset+length))
            let name=
                if named then
                    let start=namePosition
                    while namePosition<headerSize && bytes[namePosition]<>0uy do namePosition<-namePosition+1
                    if namePosition=headerSize then invalid "Unterminated Luca archive filename."
                    let result=ScriptBinary.decode encoding bytes[start..namePosition-1]
                    namePosition<-namePosition+1
                    result
                else string i
            {Name=name;Offset=offset;Data=bytes[offset..offset+length-1]})
        if entries |> Array.distinctBy(fun e->e.Name) |> Array.length <> count then invalid "Duplicate Luca archive entry names."
        {Header=bytes[..headerSize-1];Index=index;Block=block;Entries=entries}

    let profileEnc (profile:JsonNode) (key:string) = Encoding.GetEncoding(profile[key].GetValue<string>())
    let commands (data:byte[]) entryName (profile:JsonNode) (overrideEncoding:Encoding) (cancellation:CancellationToken) =
        let opcodes=profile["Opcodes"].AsArray()
        let layouts=profile["Parameters"].AsObject()
        let expressionEncoding=profileEnc profile "ExpressionEncoding"
        let textEncoding=if isNull overrideEncoding then profileEnc profile "TextEncoding" else overrideEncoding
        use input=new MemoryStream(data,false)
        use reader=new BinaryReader(input)
        let result=ResizeArray<Command>()
        while input.Position<input.Length do
            cancellation.ThrowIfCancellationRequested()
            if result.Count>=262144 then invalid "Script structure exceeds the size limit."
            let offset=int input.Position
            let length=int (reader.ReadUInt16())
            if length<4 || offset>data.Length-length-(length%2) then invalid "Invalid Luca instruction length."
            let opcode=reader.ReadByte()
            let flags=reader.ReadByte()
            if flags>3uy then invalid "Unsupported Luca instruction flags."
            let fixedLength=if flags>=2uy then 4 else int flags*2
            if fixedLength>length-4 then invalid "Truncated Luca instruction parameters."
            let fixedBytes=ScriptBinary.readBytes reader fixedLength
            let parameters=ScriptBinary.readBytes reader (length-4-fixedLength)
            input.Position<-int64 (offset+length+length%2)
            let missingOpcode=int opcode>=opcodes.Count
            let op=if missingOpcode then "" else opcodes[int opcode].GetValue<string>()
            // Missing branch layouts can hide addresses that move when dialogue grows.
            let unknown=missingOpcode ||
                        (not (layouts.ContainsKey(op)) &&
                         (op.Contains("GOTO",StringComparison.Ordinal) || op.Contains("JUMP",StringComparison.Ordinal) ||
                          op.Contains("CALL",StringComparison.Ordinal) || op="IFN" || op="IFY"))
            let rules=if layouts.ContainsKey(op) then layouts[op].AsArray() |> Seq.map(fun x->x.GetValue<string>()) |> Seq.toArray else [||]
            use raw=new MemoryStream(parameters,false)
            use values=new BinaryReader(raw)
            let variables=Dictionary<string,JsonNode>(StringComparer.Ordinal)
            let fields=ResizeArray<Parameter>()
            let mutable steps=0
            let add kind value encoding target = fields.Add({Kind=kind;Value=value;Encoding=encoding;Target=target});value
            let resolveEncoding = function "expression"->expressionEncoding | "text"->textEncoding | name->Encoding.GetEncoding(name)
            let readString prefixed (encoding:Encoding) =
                let width=if encoding.CodePage=1200 || encoding.CodePage=1201 then 2 else 1
                let count=if prefixed then int (values.ReadUInt16()) else -1
                let start=int raw.Position
                let mutable size=
                    if count>0 then (if encoding.CodePage=65001 then 65536-count else count*2) else 0
                if count=0 then
                    if start<=parameters.Length-width && parameters.AsSpan(start,width).IndexOfAnyExcept(0uy)<0 then raw.Position<-raw.Position+int64 width
                    ""
                else
                    if count<0 then
                        while start+size<=parameters.Length-width && (parameters.AsSpan(start+size,width).IndexOfAnyExcept(0uy)>=0) do size<-size+width
                    if start+size>parameters.Length-width || parameters.AsSpan(start+size,width).IndexOfAnyExcept(0uy)>=0 then invalid "Unterminated Luca script string."
                    raw.Position<-int64 (start+size+width)
                    ScriptBinary.decode encoding parameters[start..start+size-1]
            let rec run first last =
                let mutable i=first
                while i<last do
                    cancellation.ThrowIfCancellationRequested()
                    steps<-steps+1
                    if steps>262144 then invalid "Script structure exceeds the size limit."
                    let token=rules[i]
                    let parts=token.Split('@',2)
                    let kind=parts[0]
                    let pieces=kind.Split(':')
                    let conditional=pieces[0]="remaining" || pieces[0]="nonempty" || pieces[0]="equal" || pieces[0]="notequal" || pieces[0]="repeat" || kind="while"
                    if conditional then
                        let mutable ending=i+1
                        let mutable depth=1
                        while ending<last && depth>0 do
                            let next=rules[ending].Split(':')[0]
                            if next="end" then depth<-depth-1
                            elif List.contains next ["remaining";"nonempty";"equal";"notequal";"repeat";"while"] then depth<-depth+1
                            ending<-ending+1
                        if depth<>0 then invalid "Invalid Luca database parameter layout."
                        let bodyEnd=ending-1
                        match pieces[0] with
                        | "while" ->
                            while raw.Position<raw.Length do
                                let before=raw.Position
                                run (i+1) bodyEnd
                                if raw.Position<=before then invalid "Luca parameter loop made no progress."
                        | "repeat" ->
                            let count=int (bounded 0L 65535L (variables[pieces[1]].GetValue<int64>()))
                            for _ in 1..count do run (i+1) bodyEnd
                        | _ ->
                            let condition=
                                match pieces[0] with
                                | "remaining" -> raw.Length-raw.Position>Int64.Parse(pieces[1],Globalization.CultureInfo.InvariantCulture)
                                | "nonempty" -> variables[pieces[1]].GetValue<string>().Length>0
                                | "equal" -> pieces[2].Split(',') |> Array.contains (variables[pieces[1]].GetValue<int64>().ToString(Globalization.CultureInfo.InvariantCulture))
                                | "notequal" -> not (pieces[2].Split(',') |> Array.contains (variables[pieces[1]].GetValue<int64>().ToString(Globalization.CultureInfo.InvariantCulture)))
                                | _ -> false
                            if condition then run (i+1) bodyEnd
                        i<-ending
                    else
                        let value=
                            match pieces[0] with
                            | "u8" -> add kind (integer (int64 (values.ReadByte()))) null ""
                            | "u16" -> add kind (integer (int64 (values.ReadUInt16()))) null ""
                            | "u32" -> add kind (integer (int64 (values.ReadUInt32()))) null ""
                            | "peek16" -> let position=raw.Position in let value=integer (int64 (values.ReadUInt16())) in raw.Position<-position;value
                            | "s" | "l" ->
                                let encoding=resolveEncoding pieces[1]
                                add kind (node (readString (pieces[0]="l") encoding)) encoding ""
                            | "j" ->
                                let target=if pieces[1]="" then entryName else variables[pieces[1]].GetValue<string>()
                                add kind (integer (int64 (values.ReadUInt32()))) null target
                            | "raw" -> add kind (node (Convert.ToHexString(ScriptBinary.readBytes values (int (raw.Length-raw.Position))))) null ""
                            | "end" -> invalid "Unexpected Luca layout terminator."
                            | _ -> invalid "Unsupported Luca database parameter layout."
                        if parts.Length=2 then variables[parts[1]]<-value
                        i<-i+1
            run 0 rules.Length
            if raw.Position<raw.Length then add "raw" (node (Convert.ToHexString(ScriptBinary.readBytes values (int (raw.Length-raw.Position))))) null "" |> ignore
            result.Add({Offset=offset;Opcode=opcode;Flags=flags;Fixed=fixedBytes;Parameters=fields.ToArray();Unknown=unknown})
        result.ToArray()

    let isScript (data:byte[]) =
        data.Length>=4 && BinaryPrimitives.ReadUInt16LittleEndian(data)>=4us && data[3]<=3uy &&
        int (BinaryPrimitives.ReadUInt16LittleEndian(data))<=data.Length

    let commandJson (command:Command) =
        let item=json()
        item["offset"]<-integer (int64 command.Offset);item["opcode"]<-integer (int64 command.Opcode)
        let args=JsonArray()
        for parameter in command.Parameters do
            let arg=json()
            arg["kind"]<-node parameter.Kind;arg["value"]<-parameter.Value.DeepClone()
            if parameter.Kind.StartsWith("j:",StringComparison.Ordinal) then arg["entry"]<-node parameter.Target
            args.Add(arg)
        item["parameters"]<-args
        item

[<AbstractClass;Sealed>]
type LucaScript private () =
    static member ValidateProfile(profileJson:string) =
        let profile=JsonNode.Parse(profileJson)
        LucaData.profileEnc profile "ExpressionEncoding" |> ignore
        LucaData.profileEnc profile "TextEncoding" |> ignore
        let opcodes=profile["Opcodes"].AsArray()
        if opcodes.Count=0 || opcodes.Count>256 then invalid "Invalid Luca scheme opcode table."
        let layouts=profile["Parameters"].AsObject()
        if layouts.Count>256 then invalid "Luca scheme has too many parameter layouts."
        for layout in layouts do
            let rules=layout.Value.AsArray()
            if rules.Count>4096 then invalid "Luca scheme parameter layout is too large."
            let mutable depth=0
            for rule in rules do
                let token=rule.GetValue<string>().Split('@')[0]
                let kind=token.Split(':')[0]
                if List.contains kind ["remaining";"nonempty";"equal";"notequal";"repeat";"while"] then depth<-depth+1
                elif kind="end" then depth<-depth-1
                elif not (List.contains kind ["u8";"u16";"u32";"peek16";"s";"l";"j";"raw"]) then invalid "Unsupported Luca database parameter layout."
                if depth<0 || depth>32 then invalid "Invalid Luca database parameter layout."
            if depth<>0 then invalid "Invalid Luca database parameter layout."

    static member IsArchive(input:Stream) =
        if not input.CanSeek || input.Length<44L || input.Length>int64 ScriptBinary.maximum then false
        else
            let position=input.Position
            try
                input.Position<-0L
                use reader=new BinaryReader(input,Encoding.UTF8,true)
                let size=int64 (reader.ReadUInt32())
                let count=int64 (reader.ReadUInt32())
                reader.ReadUInt32() |> ignore
                let block=int64 (reader.ReadUInt32())
                size>=44L && size<input.Length && count>0L && count<=(size-36L)/8L && block>0L && size%block=0L
            finally input.Position<-position

    static member Export(data:byte[], profileName:string, profileJson:string, encoding:Encoding, cancellation:CancellationToken) =
        LucaScript.ValidateProfile(profileJson)
        let profile=JsonNode.Parse(profileJson)
        let archive=LucaData.readArchive data (LucaData.profileEnc profile "ExpressionEncoding")
        let entries=JsonArray()
        for i in 0..archive.Entries.Length-1 do
            cancellation.ThrowIfCancellationRequested()
            let entry=archive.Entries[i]
            let result=LucaData.json()
            result["index"]<-LucaData.integer (int64 i);result["name"]<-LucaData.node entry.Name
            let commands=JsonArray()
            if LucaData.isScript entry.Data then
                for command in LucaData.commands entry.Data entry.Name profile encoding cancellation do commands.Add(LucaData.commandJson command)
            result["commands"]<-commands
            entries.Add(result)
        let document=LucaData.json()
        document["schemaVersion"]<-LucaData.integer 1L;document["format"]<-LucaData.node "LUCA"
        document["scheme"]<-LucaData.node profileName
        document["encoding"]<-LucaData.node (if isNull encoding then "auto" else encoding.WebName)
        document["original"]<-LucaData.node (Convert.ToBase64String(data));document["entries"]<-entries
        document.ToJsonString(System.Text.Json.JsonSerializerOptions(WriteIndented=true))

    static member SchemeName(text:string) =
        let node=JsonNode.Parse(text)
        if node["schemaVersion"].GetValue<int>()<>1 || node["format"].GetValue<string>()<>"LUCA" then invalid "Unsupported Luca script JSON."
        node["scheme"].GetValue<string>()

    static member Compile(text:string, profileJson:string, encoding:Encoding, cancellation:CancellationToken) =
        LucaScript.ValidateProfile(profileJson)
        let document=JsonNode.Parse(text,documentOptions=System.Text.Json.JsonDocumentOptions(MaxDepth=128))
        LucaScript.SchemeName(text) |> ignore
        let profile=JsonNode.Parse(profileJson)
        let original=Convert.FromBase64String(document["original"].GetValue<string>())
        let archive=LucaData.readArchive original (LucaData.profileEnc profile "ExpressionEncoding")
        let selectedEncoding=
            if not (isNull encoding) then encoding
            elif document["encoding"].GetValue<string>()="auto" then null
            else Encoding.GetEncoding(document["encoding"].GetValue<string>())
        let entries=document["entries"].AsArray()
        if entries.Count<>archive.Entries.Length then invalid "Luca JSON must preserve every archive entry."
        let buffers=ResizeArray<byte[]>()
        let labels=Dictionary<string*int,int>()
        let patches=ResizeArray<int*int*string*int>()
        let mutable modified=false
        let mutable unresolved=false
        for i in 0..entries.Count-1 do
            cancellation.ThrowIfCancellationRequested()
            let source=archive.Entries[i]
            let entry=entries[i]
            if entry["index"].GetValue<int>()<>i || entry["name"].GetValue<string>()<>source.Name then invalid "Luca JSON must preserve archive names and order."
            let commands=if LucaData.isScript source.Data then LucaData.commands source.Data source.Name profile selectedEncoding cancellation else [||]
            let edited=entry["commands"].AsArray()
            if edited.Count<>commands.Length then invalid "Luca JSON must preserve the instruction structure."
            use output=new MemoryStream()
            use writer=new BinaryWriter(output)
            for j in 0..commands.Length-1 do
                let command=commands[j]
                let item=edited[j]
                if item["offset"].GetValue<int>()<>command.Offset || item["opcode"].GetValue<byte>()<>command.Opcode then invalid "Luca JSON instruction metadata was changed."
                labels.Add((source.Name,command.Offset),int output.Position)
                let start=output.Position
                writer.Write(0us);writer.Write(command.Opcode);writer.Write(command.Flags);writer.Write(command.Fixed)
                let args=item["parameters"].AsArray()
                if args.Count<>command.Parameters.Length then invalid "Luca JSON parameter structure was changed."
                for k in 0..args.Count-1 do
                    let value=args[k]
                    let parameter=command.Parameters[k]
                    if value["kind"].GetValue<string>()<>parameter.Kind then invalid "Luca JSON parameter type was changed."
                    if not (JsonNode.DeepEquals(value["value"],parameter.Value)) then modified<-true
                    match parameter.Kind.Split(':')[0] with
                    | "u8" -> writer.Write(value["value"].GetValue<byte>())
                    | "u16" -> writer.Write(value["value"].GetValue<uint16>())
                    | "u32" -> writer.Write(value["value"].GetValue<uint32>())
                    | "raw" ->
                        let bytes=hex (value["value"].GetValue<string>())
                        if bytes.Length<>(hex (parameter.Value.GetValue<string>())).Length then invalid "Raw Luca parameters must keep their length."
                        writer.Write(bytes)
                    | "s" | "l" as kind ->
                        let bytes=encode parameter.Encoding (value["value"].GetValue<string>())
                        if kind="l" then
                            let length=if parameter.Encoding.CodePage=65001 then (65536-bytes.Length)%65536 else bytes.Length/2
                            if length<0 || length>65535 || (parameter.Encoding.CodePage<>65001 && bytes.Length%2<>0) then invalid "Luca script string is too long or misaligned."
                            writer.Write(uint16 length)
                        writer.Write(bytes)
                        writer.Write(0uy)
                        if parameter.Encoding.CodePage=1200 || parameter.Encoding.CodePage=1201 then writer.Write(0uy)
                    | "j" ->
                        if value["entry"].GetValue<string>()<>parameter.Target || not (JsonNode.DeepEquals(value["value"],parameter.Value)) then invalid "Luca jump metadata was changed."
                        patches.Add(i,int output.Position,parameter.Target,int (parameter.Value.GetValue<int64>()))
                        writer.Write(0u)
                    | _ -> invalid "Unsupported Luca parameter type."
                let length=output.Position-start
                if length>65535L then invalid "Luca instruction exceeds its size limit."
                let finish=output.Position
                output.Position<-start;writer.Write(uint16 length);output.Position<-finish
                if length%2L<>0L then writer.Write(0uy)
                if command.Unknown then unresolved<-true
            labels.Add((source.Name,source.Data.Length),if commands.Length=0 then source.Data.Length else int output.Position)
            buffers.Add(if commands.Length=0 then source.Data else output.ToArray())
        if not modified then original
        else
            if unresolved then invalid "Unknown Luca instructions prevent safe script modification."
            for entryIndex,position,target,oldOffset in patches do
                let label=
                    match labels.TryGetValue((target,oldOffset)) with
                    | true,label -> label
                    | _ -> invalid "Unresolved Luca script jump."
                BinaryPrimitives.WriteUInt32LittleEndian(buffers[entryIndex].AsSpan(position,4),uint32 label)
            use output=new MemoryStream()
            output.Write(archive.Header)
            for i in 0..buffers.Count-1 do
                cancellation.ThrowIfCancellationRequested()
                let padding=(int64 archive.Block-output.Position%int64 archive.Block)%int64 archive.Block
                if output.Position+padding+int64 (buffers[i].Length)>int64 ScriptBinary.maximum then invalid "Script data exceeds the size limit."
                output.Write(Array.zeroCreate<byte> (int padding))
                let offset=output.Position
                output.Write(buffers[i])
                let finish=output.Position
                output.Position<-int64 (archive.Index+i*8)
                use index=new BinaryWriter(output,Encoding.UTF8,true)
                index.Write(uint32 (offset/int64 archive.Block));index.Write(uint32 (buffers[i].Length))
                output.Position<-finish
                if LucaData.isScript buffers[i] then
                    let checkedCommands=LucaData.commands buffers[i] archive.Entries[i].Name profile selectedEncoding cancellation
                    let editedEntry=entries[i]
                    let editedCommands=editedEntry["commands"].AsArray()
                    if checkedCommands.Length<>editedCommands.Count then invalid "Luca text modification changed the parameter structure."
                    for j in 0..checkedCommands.Length-1 do
                        let editedCommand=editedCommands[j]
                        let wanted=editedCommand["parameters"].AsArray()
                        let actual=checkedCommands[j].Parameters
                        if wanted.Count<>actual.Length then invalid "Luca text modification changed the parameter structure."
                        for k in 0..actual.Length-1 do
                            let wantedParameter=wanted[k]
                            if wantedParameter["kind"].GetValue<string>()<>actual[k].Kind ||
                               (not (actual[k].Kind.StartsWith("j:",StringComparison.Ordinal)) && not (JsonNode.DeepEquals(wantedParameter["value"],actual[k].Value))) then
                                invalid "Luca text modification changed the parameter structure."
            output.ToArray()

