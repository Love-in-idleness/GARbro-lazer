namespace GARbro.Scripting

open System
open System.IO
open System.Text
open System.Collections.Generic
open System.Text.RegularExpressions
open System.Threading
open ScriptSyntax

module internal GscLayout =
    // Numeric layouts from the published TSC/GSC format reference; compiler implementation is independent.
    let modern =
        "3:D 4:D 5:D 8: 9:H 10: 11: 12:ED 13:E 14:HDDDDDDDDDDDEEE 15:EDEEEEEEEEEE 16:E 17: 18:ED 19:E 20:EE 21:E 22:EEEE 23:EEEE 24:EE 25:DE 26: 27: 28:EEE 29:EE 30:EEEEEE 32:EEEEED 33:EEEEE 34:EEEEE 35:EE 36:EE 37:EE 38:EEEE 39:EEE 40:EE 41:EE 42:EE 43:EE 44:E 45:EE 46:E 47:EE 48:EEE 49:EE 50: 51: 52: 53:E 55: 56:EEEEE 57: 58: 59:EEEE 60:EEE 61:EE 62:EE 63:EEEE 64:EE 65:E 66:DEEE 67:E 68: 69: 70:EEEE 71:EEEE 72:E 73:EEE 74:E 75:EEEEE 77:EEEE 80:E 81:EDEEDDE 82:EEEEDE 83:E 84:ED 90:EEE 91:EEEEE 92:EE 93:EE 94:E 95:EE 96:EE 97:EE 98:EE 99:EEE 100:EEE 101:EE 102:E 103:EE 104:EEEE 105:EE 106:EEEEE 107:EEE 108:EEEE 110:EEE 111:EEE 112:E 113:EE 114:EE 115:EE 116:EE 117:EE 120:EE 121:ED 130:EEEE 131:EEEEE 132:EE 134:EEE 135:EEEEE 136:EEE 140:EE 141:EE 142:EE 150:ED 151:ED 152:EE 153:EE 154:EE 155:EE 156:EEE 157:EEEEE 158:EE 159:EE 200:DEEEEEEEEEE 201:EEEEE 202:EEE 210:DE 211:DEEE 212:D 213:EEE 220:EEE 221:EE 222: 223:EE 225:EEEEE 230:E 231:E 255:EEEEE"
        |> fun s -> s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun pair -> let p = pair.Split(':') in int p[0], p[1]) |> Map.ofArray
    let names =
        "8:end 9:rnd 10:hit 11:hitc 12:jump 13:wait 14:select 15:gosub 16:return 17:save 18:data 19:subscript 20:gload 21:gcls 22:gmove 23:quake 24:flash 25:call 26:queue 27:action 28:update 29:zupdate 30:load 32:font 33:move 34:movi 35:phase 36:cls 37:enabl 38:locmode 39:draw 40:depth 41:group 42:tbox 43:effect 44:effectdep 45:tone 46:tonedep 47:locgrid 48:face 49:mode 50:backup 51:resume 52:stop 53:autobackup 55:makesave 56:sysmode 57:logflash 58:endscene 59:swap 60:bgm_on 61:bgm_off 62:se 63:se_on 64:se_off 65:movie 66:voice 67:voice_off 68:se_wait 69:voice_wait 70:setclk 71:setclksys 72:resetclk 73:click 74:autoreset 75:setlink 77:setclksub 80:TCL 81:TXT 82:TXA 83:txcls 90:tboxloc 91:texloc 92:tboxback 93:cmploc 94:tboxcmp 95:texcolor 96:texsize 97:texfont 98:texmode 99:texindent 100:waitloc 101:faceloc 102:tclsmode 103:waitlod 104:waitcol 105:facedep 106:namloc 107:texpich 108:texruby 110:getloc 111:muldev 112:root 113:pow 114:getflag 115:menuon 116:menuset 117:menuget 120:fontsize 121:folder 130:numload 131:numreng 132:numenable 134:numloc 135:numset 136:num 140:gmenuon 141:gmenuset 142:gmenuget 150:strset 151:stradd 152:numstr 153:strnum 154:strcpy 155:strcat 156:strinput 157:strinprop 158:strsave 159:strload 200:insub 201:metamor 202:flagset 210:dynsel 211:dynans 212:dynnext 213:dyndo 220:map 221:mapload 222:mapcls 223:mapobj 225:locmap 230:bganim 231:await 255:excmd"
        |> fun s -> s.Split(' ') |> Array.map (fun pair -> let p = pair.Split(':') in int p[0], p[1]) |> Map.ofArray
    let schema name =
        let mutable result = modern
        let set op signature = result <- result.Add(op, signature)
        match name with
        | "modern" -> ()
        | "rscript19" | "rscript18" ->
            for op, signature in [48,"EE";62,"E";63,"EEE";64,"E";105,"E"] do set op signature
            if name = "rscript18" then set 38 "EEE"
        | "early" | "early-short-select" | "pre-codex" ->
            for op, count in [9,0;12,1;15,1;16,1;19,1;21,0;23,2;25,0;38,3;48,2;60,2;61,1;62,1;63,3;64,1;66,4;73,2;75,4] do set op (String('E', count))
            set 9 "H"; set 25 "D"; set 81 "EEEEDDE"
            if name <> "early" then set 14 "HDDDDDDDDDDD"
            if name = "pre-codex" then set 81 "EEEEDD"
        | _ -> invalid "Unsupported GSC instruction schema."
        result
    let kinds layout header opcode =
        if opcode &&& 0xf000 <> 0 then
            if opcode &&& 0xf000 = 0xf000 then (if header = 28 then "HH" else "HS")
            else if header = 28 then "HHH" else "HSS"
        else match Map.tryFind opcode layout with Some value -> value | None -> invalid "Unknown GSC instruction layout."
    let stringOperand op i =
        match op with
        | 12 | 15 | 121 | 150 | 151 -> i = 1
        | 14 -> i = 1 || (i >= 7 && i <= 11)
        | 32 -> i = 5
        | 210 | 211 | 212 -> i = 0
        | 81 -> i = 4 || i = 5
        | 82 -> i = 4
        | _ -> false
    let target op i = ((op = 3 || op = 4 || op = 5 || op = 200) && i = 0) || (op = 14 && i >= 2 && i <= 6)
    let size (kinds: string) = 2 + (kinds |> Seq.sumBy (fun c -> if c = 'H' || c = 'S' then 2 else 4))
    let label (offset: int) = "L_" + offset.ToString("x6")
    let operand kind (text: string) =
        let depth = if kind = 'E' then text |> Seq.takeWhile ((=) '@') |> Seq.length else 0
        let value = number (text.Substring(depth))
        if kind = 'E' then
            bounded 0L 65535L (int64 depth) |> ignore
            bounded (if depth = 0 then -32768L else 0L) (if depth = 0 then 32767L else 65535L) value |> ignore
            (int64 depth <<< 16) ||| (value &&& 65535L)
        else bounded (if kind = 'S' then -32768L else 0L) (if kind = 'S' then 32767L elif kind = 'H' then 65535L else int64 UInt32.MaxValue) value
    let hash (bytes: byte[]) = Array.fold (fun h b -> (h ^^^ uint64 b) * 1099511628211UL) 14695981039346656037UL bytes
    let namedEntries (trailer: byte[]) table names =
        if trailer |> Array.forall ((=) 0uy) then [||]
        else
            if table < 4 || table % 4 <> 0 || names < 1 || int64 table*2L + int64 names > int64 trailer.Length then invalid "Invalid GSC named-entry table."
            [|for i in 0..4..table-4 do
                let offset = int (BitConverter.ToUInt32(trailer, i))
                if offset < 0 || offset >= names then invalid "Invalid GSC named-entry name."
                if trailer[table*2+offset] <> 0uy then yield table+i|]

[<AbstractClass; Sealed>]
type GscScript private () =
    static member Compile(text: string, encoding: Encoding, cancellation: CancellationToken) =
        let lines = text.Replace("\r", "").Split('\n')
        if text.Contains(";@gsc-raw-v1 ") then
            let header = lines |> Array.filter (fun l -> l.StartsWith(";@gsc-raw-v1 "))
            if header.Length <> 1 then invalid "Invalid GSC raw envelope."
            let m = Regex.Match(header[0], @"^;@gsc-raw-v1 size=(\d+) fnv1a64=([0-9a-fA-F]{16})$")
            if not m.Success || not (lines |> Array.contains ";@gsc-raw-end") then invalid "Invalid GSC raw envelope."
            let bytes = lines |> Array.filter (fun l -> l.StartsWith(";@gsc-raw ")) |> Array.collect (fun l -> hex (l.Substring(10)))
            if int64 bytes.Length <> number m.Groups[1].Value || GscLayout.hash bytes <> UInt64.Parse(m.Groups[2].Value, Globalization.NumberStyles.HexNumber) then invalid "GSC raw checksum mismatch."
            bytes
        else
            let metadata = Dictionary<string,string>(StringComparer.Ordinal)
            let instructions = ResizeArray<int*string*string[]*int>()
            let labels = Dictionary<string,int>(StringComparer.Ordinal)
            let blocks = ResizeArray<int16[]>()
            let mutable offset, header, layout = 0, 0, Map.empty
            for i in 0..lines.Length-1 do
                cancellation.ThrowIfCancellationRequested()
                try
                    let line = lines[i].Trim()
                    if line.StartsWith(";@gsc-") then
                        let p = line.IndexOf(' ')
                        let key, value = if p < 0 then line, "" else line.Substring(0,p), line.Substring(p+1).Trim()
                        if not (metadata.TryAdd(key,value)) then invalid "Duplicate GSC metadata."
                        match key with
                        | ";@gsc-byte-format" -> header <- match value with "modern-36" -> 36 | "legacy-28" -> 28 | _ -> invalid "Unsupported GSC byte format."
                        | ";@gsc-schema" -> layout <- GscLayout.schema value
                        | ";@gsc-trailer" | ";@gsc-trailer-header" | ";@gsc-text-encoding" -> ()
                        | _ -> invalid "Unknown GSC metadata."
                    else
                        let parts = tokens false line
                        if parts.Length > 0 then
                            if parts[0].StartsWith(":") then
                                if parts.Length <> 1 || not (labels.TryAdd(parts[0].Substring(1), offset)) then invalid "Duplicate or malformed GSC label."
                            elif parts[0] = "*datablock" then
                                if parts.Length < 3 || number parts[1] <> int64 blocks.Count || number parts[2] <> int64 (parts.Length-3) || parts.Length-3 > 32767 then invalid "Invalid GSC data block."
                                blocks.Add(parts[3..] |> Array.map (fun t -> int16 (GscLayout.operand 'S' t)))
                            else
                                if header = 0 || layout.IsEmpty then invalid "TSC needs byte-format and schema metadata before commands."
                                let op, args =
                                    match parts[0] with
                                    | "*vm" | "*opcode" when parts.Length > 1 -> int (GscLayout.operand 'H' parts[1]), parts[2..]
                                    | "*jz" -> 3,parts[1..] | "*jnz" -> 4,parts[1..] | "*goto" -> 5,parts[1..]
                                    | name ->
                                        match GscLayout.names |> Map.toSeq |> Seq.tryFind (fun (_,n) -> "*"+n = name) with
                                        | Some (op,_) -> op,parts[1..]
                                        | None -> invalid ("Unknown script command: " + name)
                                let kinds = GscLayout.kinds layout header op
                                if kinds.Length <> args.Length then invalid "Wrong script operand count."
                                instructions.Add(op,kinds,args,i+1)
                                offset <- offset + GscLayout.size kinds
                with :? InvalidDataException as error -> raise (InvalidDataException($"Script line {i+1}: {error.Message}",error))
            if header = 0 || layout.IsEmpty then invalid "TSC needs byte-format and schema metadata."
            if header = 28 && (metadata.ContainsKey(";@gsc-trailer") || metadata.ContainsKey(";@gsc-trailer-header") || metadata[";@gsc-schema"] = "modern") then invalid "Invalid legacy GSC metadata."
            use strings = new MemoryStream()
            use indexes = new MemoryStream()
            use index = new BinaryWriter(indexes)
            let stringIds = Dictionary<string,int>(StringComparer.Ordinal)
            let intern value =
                match stringIds.TryGetValue(value) with
                | true,id -> id
                | _ ->
                    let id = stringIds.Count
                    index.Write(uint32 strings.Length); strings.Write(encode encoding value); strings.WriteByte(0uy)
                    stringIds.Add(value,id); id
            intern "" |> ignore
            use code = new MemoryStream()
            use writer = new BinaryWriter(code)
            for op,kinds,args,line in instructions do
                cancellation.ThrowIfCancellationRequested()
                writer.Write(uint16 op)
                for i in 0..args.Length-1 do
                    let value =
                        if GscLayout.stringOperand op i then int64 (intern (unquote args[i]))
                        elif GscLayout.target op i then
                            match labels.TryGetValue(args[i]) with true,p -> int64 p | _ -> invalid ($"Script line {line}: unknown label {args[i]}")
                        else GscLayout.operand kinds[i] args[i]
                    if kinds[i] = 'H' || kinds[i] = 'S' then writer.Write(uint16 value) else writer.Write(uint32 value)
            use data = new MemoryStream()
            use dataIndex = new MemoryStream()
            use dw = new BinaryWriter(data)
            use iw = new BinaryWriter(dataIndex)
            for block in blocks do iw.Write(uint32 (data.Length/2L)); dw.Write(uint16 block.Length); for value in block do dw.Write(value)
            let trailer = if metadata.ContainsKey(";@gsc-trailer") then hex metadata[";@gsc-trailer"] else Array.zeroCreate (if header = 36 then 9 else 0)
            let sizes = if metadata.ContainsKey(";@gsc-trailer-header") then tokens false metadata[";@gsc-trailer-header"] |> Array.map (fun t -> int (bounded 0L (64L<<<20) (number t))) else [|4;1|]
            if sizes.Length <> 2 then invalid "Invalid GSC trailer header."
            for p in GscLayout.namedEntries trailer sizes[0] sizes[1] do
                let name = GscLayout.label (int (BitConverter.ToUInt32(trailer,p)))
                match labels.TryGetValue(name) with
                | true,target when int64 target < code.Length -> BitConverter.GetBytes(uint32 target).CopyTo(trailer,p)
                | _ -> invalid ("Missing GSC named-entry label: " + name)
            let sections = [|code.ToArray(); indexes.ToArray(); strings.ToArray(); dataIndex.ToArray(); data.ToArray(); trailer|]
            use output = new MemoryStream()
            use final = new BinaryWriter(output)
            let length = header + (sections |> Array.sumBy Array.length)
            for value in [|length-(if header=28 then int data.Length/2 else 0);header;int code.Length;int indexes.Length;int strings.Length;int dataIndex.Length;int data.Length/2|] do final.Write(uint32 value)
            if header = 36 then final.Write(uint32 sizes[0]); final.Write(uint32 sizes[1])
            for section in sections do final.Write(section)
            output.ToArray()

    static member Export(bytes: byte[], encoding: Encoding, cancellation: CancellationToken) =
        let raw () =
            let result = StringBuilder($";@gsc-raw-v1 size={bytes.Length} fnv1a64={GscLayout.hash bytes:x16}\n")
            for chunk in Array.chunkBySize 48 bytes do result.Append(";@gsc-raw ").Append(Convert.ToHexString(chunk).ToLowerInvariant()).AppendLine() |> ignore
            result.AppendLine(";@gsc-raw-end").ToString()
        try
            use input = new MemoryStream(bytes,false)
            use reader = new BinaryReader(input)
            let declared = int (reader.ReadUInt32())
            let header = int (reader.ReadUInt32())
            if header <> 28 && header <> 36 then invalid "Unsupported GSC header."
            let lengths = Array.init 5 (fun _ -> int (bounded 0L (int64 bytes.Length) (int64 (reader.ReadUInt32()))))
            let table,names = if header = 36 then int (reader.ReadUInt32()),int (reader.ReadUInt32()) else 4,1
            let take n =
                let value = reader.ReadBytes(n)
                if value.Length <> n then invalid "Truncated GSC section."
                value
            let code, index, strings, dataIndex, data = take lengths[0],take lengths[1],take lengths[2],take lengths[3],take (lengths[4]*2)
            let trailer = take (if header = 36 then declared-int input.Position else 0)
            if input.Position <> input.Length || index.Length%4 <> 0 || dataIndex.Length%4 <> 0 then invalid "Invalid GSC section lengths."
            let readString id =
                if id < 0 || id >= index.Length/4 then invalid "Invalid GSC string index."
                let start = int (BitConverter.ToUInt32(index,id*4))
                if start < 0 || start >= strings.Length then invalid "Invalid GSC string offset."
                let finish = Array.IndexOf(strings,0uy,start)
                if finish < 0 then invalid "Unterminated GSC string."
                (strict encoding).GetString(strings,start,finish-start)
            let parse name =
                let layout = GscLayout.schema name
                use stream = new MemoryStream(code,false)
                use cr = new BinaryReader(stream)
                let instructions = ResizeArray<int*int*string*int64[]>()
                let targets = HashSet<int>()
                while stream.Position < stream.Length do
                    cancellation.ThrowIfCancellationRequested()
                    let pos,op = int stream.Position,int (cr.ReadUInt16())
                    let kinds = GscLayout.kinds layout header op
                    let values =
                        kinds |> Seq.mapi (fun i kind ->
                            let v = match kind with 'H' -> int64 (cr.ReadUInt16()) | 'S' -> int64 (cr.ReadInt16()) | _ -> int64 (cr.ReadUInt32())
                            if GscLayout.stringOperand op i then readString (int v) |> ignore
                            if GscLayout.target op i then targets.Add(int v) |> ignore
                            v) |> Seq.toArray
                    instructions.Add(pos,op,kinds,values)
                let boundaries = instructions |> Seq.map (fun (p,_,_,_)->p) |> HashSet<int>
                boundaries.Add(code.Length) |> ignore
                for p in GscLayout.namedEntries trailer table names do targets.Add(int (BitConverter.ToUInt32(trailer,p))) |> ignore
                if targets |> Seq.exists (fun t -> not (boundaries.Contains(t))) then invalid "Invalid GSC code target."
                let byteFormat = if header=36 then "modern-36" else "legacy-28"
                let result = StringBuilder($";@gsc-byte-format {byteFormat}\n;@gsc-schema {name}\n")
                if header=36 then result.AppendLine($";@gsc-trailer-header {table} {names}").AppendLine(";@gsc-trailer " + Convert.ToHexString(trailer)) |> ignore
                for pos,op,kinds,values in instructions do
                    if result.Length > (64 <<< 20) then invalid "Output limit exceeded."
                    if targets.Contains(pos) then result.AppendLine(":" + GscLayout.label pos) |> ignore
                    let command = if op &&& 0xf000 <> 0 then $"vm 0x{op:x4}" elif op=3 then "jz" elif op=4 then "jnz" elif op=5 then "goto" else Map.tryFind op GscLayout.names |> Option.defaultValue ($"opcode {op}")
                    result.Append('*').Append(command) |> ignore
                    for i in 0..values.Length-1 do
                        let v = values[i]
                        let text =
                            if GscLayout.stringOperand op i then quote (readString (int v))
                            elif GscLayout.target op i then GscLayout.label (int v)
                            elif kinds[i]='E' then let depth = int (v >>> 16) in String('@',depth) + (if depth=0 then string (int16 v) else string (v &&& 65535L))
                            else string v
                        result.Append(' ').Append(text) |> ignore
                    result.AppendLine() |> ignore
                if targets.Contains(code.Length) then result.AppendLine(":" + GscLayout.label code.Length) |> ignore
                for i in 0..dataIndex.Length/4-1 do
                    let pos = int (BitConverter.ToUInt32(dataIndex,i*4))*2
                    if pos < 0 || pos+2 > data.Length then invalid "Invalid GSC data index."
                    let count = int (BitConverter.ToUInt16(data,pos))
                    if pos+2+count*2 > data.Length then invalid "Truncated GSC data block."
                    result.Append($"*datablock {i} {count}") |> ignore
                    for j in 0..count-1 do result.Append(' ').Append(BitConverter.ToInt16(data,pos+2+j*2)) |> ignore
                    result.AppendLine() |> ignore
                result.ToString()
            [|(if header=36 then "modern" else "early");"rscript19";"rscript18";"early";"early-short-select";"pre-codex"|]
            |> Array.tryPick (fun name -> try Some (parse name) with :? InvalidDataException | :? EndOfStreamException | :? DecoderFallbackException -> None)
            |> Option.defaultWith raw
        with :? InvalidDataException | :? EndOfStreamException | :? ArgumentException -> raw()
