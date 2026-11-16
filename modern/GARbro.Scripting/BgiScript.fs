// Adapted from Bgi_asdisEN (MIT), Copyright (c) 2025 Daniel Wu. See docs/licenses/Bgi_asdisEN.txt.
namespace GARbro.Scripting

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Collections.Generic
open System.Threading
open ScriptSyntax

type internal BgiOp = { Op: int; Format: string; Name: string; Aliases: string[]; Variable: bool }
module internal BgiLayout =
    let tables = lazy (
        use input = typeof<BgiOp>.Assembly.GetManifestResourceStream("GARbro.Scripting.bgi-opcodes.json")
        use json = JsonDocument.Parse(input)
        ["v1"; "bp"] |> List.map (fun name ->
            let ops = [| for op in json.RootElement.GetProperty(name).EnumerateArray() ->
                            { Op=op.GetProperty("op").GetInt32()
                              Format=op.GetProperty("format").GetString()
                              Name=op.GetProperty("name").GetString()
                              Aliases=op.GetProperty("aliases").EnumerateArray() |> Seq.map (fun v -> v.GetString()) |> Seq.toArray
                              Variable=op.GetProperty("variable").GetBoolean() } |]
            name, ops) |> Map.ofList)
    let subgroups = set [0x7f;0x80;0x81;0x90;0x91;0x92;0xa0;0xb0;0xc0;0xd0;0xe0;0xff]
    let pattern op count =
        match op with
        | 0x0d -> "t" | 0x0e -> "Ht"
        | 0x12 | 0x1f | 0x2c | 0x2d | 0x2e | 0x2f | 0xec -> "v"
        | 0x15 -> if count=1 then "B" else "BR"
        | 0x1a | 0x1b | 0xd5 | 0xd8 | 0xed -> "Hv"
        | 0x36 | 0x3f -> "Bv" | 0x37 -> "BRv" | 0x3b -> "BR"
        | 0xd6 | 0xe4 | 0xe5 | 0xe6 | 0xe7 -> "Ht"
        | 0xd7 -> "HHt" | 0xda -> "BHv" | 0xdb -> "BHtv"
        | 0xdc -> "BRHv" | 0xdd -> "BRHtv" | 0xde -> "RH" | 0xdf -> "RHt"
        | 0xea | 0xf1 -> "HHv" | 0xf0 -> "IHv" | 0xf2 -> "HHHv"
        | _ -> invalid "Unknown BPD variable instruction."
    let branchBase op pos size =
        match op with 0xdc | 0xdd | 0xde | 0xdf -> pos+size-1 | _ -> pos+3
    let label (pos: int) = if pos < 0 then $"L-{-pos:x5}" else $"L{pos:x5}"
    let resolve (symbols: Dictionary<string,int>) (token: string) =
        match symbols.TryGetValue(token) with
        | true,pos -> int64 pos
        | _ when Regex.IsMatch(token,@"^L-?[0-9a-fA-F]{5,}$") ->
            if token.StartsWith("L-") then -Int64.Parse(token.Substring(2), Globalization.NumberStyles.HexNumber)
            else Int64.Parse(token.Substring(1), Globalization.NumberStyles.HexNumber)
        | _ -> number token
    let blob (arg: string) =
        let token = if arg.StartsWith("\"") then unquote arg else arg
        hex (if token.StartsWith("@hex:") then token.Substring(5) else token)
    let variable (op: int) (args: string[]) pos (symbols: Dictionary<string,int>) sizing =
        use output = new MemoryStream()
        use writer = new BinaryWriter(output)
        writer.Write(byte op)
        let n i = if i >= args.Length then invalid "Wrong BPD operand count." else number args[i]
        let write k v = writeInteger writer k v
        if subgroups.Contains(op) then
            if args.Length <> 1 then invalid "Unexpected BPD subsystem payload."
            write 'B' (n 0)
        elif op=0x03 then
            let mode = int (bounded 0L 255L (n 0))
            write 'B' (int64 mode)
            if mode < 128 then
                if args.Length < 2 || args.Length > 3 then invalid "Wrong BPD pushx operand count."
                if args[1].StartsWith("[") && args[1].EndsWith("]") then
                    let values = tokens true (args[1].Substring(1,args[1].Length-2))
                    if values.Length <> mode+1 then invalid "Wrong BPD pushx value count."
                    for value in values do sleb writer (number value)
                else writer.Write(blob args[1])
            elif mode &&& 0x7c = 0 then
                if args.Length <> 2 then invalid "Wrong BPD pushx operand count."
                write (match mode &&& 3 with 0 -> 'b' | 1 -> 'h' | 2 -> 'i' | _ -> 'q') (n 1)
            elif args.Length <> 1 then invalid "Unexpected BPD pushx payload."
        elif op=0xe2 || op=0xe3 then
            if args.Length < 1 || args.Length > 2 then invalid "Wrong BPD offset-list operand count."
            let bytes =
                if args[0].StartsWith("[") && args[0].EndsWith("]") then
                    use data = new MemoryStream()
                    use dw = new BinaryWriter(data)
                    for value in tokens true (args[0].Substring(1,args[0].Length-2)) do writeInteger dw 'h' (number value)
                    data.ToArray()
                else blob args[0]
            if bytes.Length=0 || bytes.Length%2<>0 || bytes.Length>512 then invalid "Invalid BPD offset list."
            writer.Write(byte (bytes.Length/2-1)); writer.Write(bytes)
        else
            let p = pattern op args.Length
            let expected = p |> Seq.sumBy (fun k -> if k='t' then 2 else 1)
            if expected <> args.Length then invalid "Wrong BPD operand count."
            let mutable i = 0
            let branches = ResizeArray<int*int64>()
            for kind in p do
                match kind with
                | 'v' -> sleb writer (n i)
                | 't' ->
                    let t = bounded 0L 3L (n i)
                    let v = bounded -536870912L 536870911L (n (i+1))
                    sleb writer ((v <<< 2) ||| t); i <- i+1
                | 'R' ->
                    branches.Add(int output.Position,if sizing then 0L else resolve symbols args[i]); writer.Write(0s)
                | _ -> write kind (n i)
                i <- i+1
            if not sizing then
                for at,target in branches do
                    let relative = bounded -32768L 32767L (target-int64 (branchBase op pos (int output.Length)))
                    output.Position <- int64 at; writer.Write(int16 relative)
        output.ToArray()

[<AbstractClass; Sealed>]
type BgiScript private () =
    static member Compile(text: string, bp: bool, encoding: Encoding, cancellation: CancellationToken) =
        let ops = BgiLayout.tables.Value[if bp then "bp" else "v1"]
        let lookup = Dictionary<string,BgiOp>(StringComparer.Ordinal)
        for op in ops do for name in Array.append [|op.Name|] op.Aliases do lookup[name] <- op
        let symbols = Dictionary<string,int>(StringComparer.Ordinal)
        let defines = Dictionary<string,string>(StringComparer.Ordinal)
        let instructions = ResizeArray<BgiOp*string[]*int*int>()
        let imports = ResizeArray<string>()
        let extras = ResizeArray<string*uint32 option*int>()
        let mutable padding: byte[] option = None
        let mutable signature: string option = None
        let strings = ResizeArray<string option*byte[]>()
        let mutable pos = 0
        let payload op (args: string[]) =
            if not bp && op.Op=0x16 && args.Length=0 then "" else op.Format
        let lines = text.Replace("\r", "").Split('\n')
        for i in 0..lines.Length-1 do
            cancellation.ThrowIfCancellationRequested()
            try
                let line = lines[i].Trim()
                let parts = tokens false line
                if parts.Length > 0 then
                    let directive = parts[0]
                    if directive.StartsWith("#string_table") then invalid "This script needs an external string table. Use a self-contained script source."
                    elif directive = "#strings" then ()
                    elif directive = "#header" && parts.Length=2 && not bp then signature <- Some (unquote parts[1])
                    elif directive = "#header_padding" && parts.Length=2 && not bp then padding <- Some (hex (unquote parts[1]))
                    elif directive = "#import" && parts.Length=2 && not bp then imports.Add(unquote parts[1])
                    elif directive = "#extra_import" && parts.Length=2 && not bp then extras.Add(unquote parts[1],None,0)
                    elif directive = "#export" && parts.Length=3 && not bp then extras.Add(unquote parts[1],Some (uint32 (bounded 0L (int64 UInt32.MaxValue) (number parts[2]))),0)
                    elif directive = "#extra_import_gap" && parts.Length=2 && not bp then extras.Add("",None,int (bounded 0L (64L<<<20) (number parts[1])))
                    elif (directive="#strdata" || directive="#gap") && parts.Length=2 then strings.Add(None,hex (unquote parts[1]))
                    elif directive = "#define" && parts.Length=3 then defines.Add(parts[1],parts[2])
                    elif line.StartsWith("\"") && parts.Length=1 then strings.Add(Some parts[0],Array.append (encodedLiteral encoding parts[0]) [|0uy|])
                    elif parts.Length=1 && directive.EndsWith(":") then
                        if not (symbols.TryAdd(directive.TrimEnd(':'),pos)) then invalid "Duplicate BGI label."
                    else
                        let comment = Regex.Replace(line,@"\s*//[^""\r\n]*$", "")
                        let m = Regex.Match(comment,@"^([A-Za-z_][A-Za-z0-9_:]*)\((.*)\);$")
                        if not m.Success then invalid "Invalid BGI instruction."
                        let name = m.Groups[1].Value
                        let name = match defines.TryGetValue(name) with true,v -> v | _ -> name
                        let op = match lookup.TryGetValue(name) with true,v -> v | _ -> invalid ("Unknown script command: " + name)
                        let args = tokens true m.Groups[2].Value
                        let fmt = payload op args
                        if not op.Variable && args.Length <> fmt.Length && not (bp && op.Op=5 && args.Length=2) then invalid "Wrong BGI operand count."
                        instructions.Add(op,args,pos,i+1)
                        pos <- pos + (if bp && op.Variable then (BgiLayout.variable op.Op args pos symbols true).Length else (if bp then 1 else 4) + (fmt |> Seq.sumBy width))
            with :? InvalidDataException as error -> raise (InvalidDataException($"Script line {i+1}: {error.Message}",error))
        if not bp && signature.IsNone then invalid "BGI source needs a header."
        let explicit = strings.Count > 0
        let stringStart = if bp && not explicit then (pos+15)/16*16 else pos
        use pool = new MemoryStream()
        let stringMap = Dictionary<string,int>(StringComparer.Ordinal)
        for token,bytes in strings do
            match token with
            | Some t when not (stringMap.ContainsKey(t)) -> stringMap.Add(t,stringStart+int pool.Length)
            | _ -> ()
            pool.Write(bytes)
        let intern token =
            match stringMap.TryGetValue(token) with
            | true,p -> p
            | _ ->
                let p = stringStart+int pool.Length
                pool.Write(encodedLiteral encoding token); pool.WriteByte(0uy); stringMap.Add(token,p); p
        for op,args,_,_ in instructions do
            for arg in args do if arg.StartsWith("\"") && (not bp || op.Op=5) then intern arg |> ignore
        for item in stringMap do symbols[item.Key] <- item.Value
        use output = new MemoryStream()
        use writer = new BinaryWriter(output,Encoding.UTF8,true)
        if bp then
            writer.Write(16u)
            let total = stringStart+int pool.Length
            writer.Write(uint32 (if explicit then total else (total+15)/16*16)); writer.Write(0u); writer.Write(0u)
        else
            let sigBytes = encode encoding signature.Value
            if sigBytes.Length>28 then invalid "Invalid BGI header length."
            writer.Write(sigBytes); writer.Write(Array.zeroCreate<byte> (28-sigBytes.Length))
            use body = new MemoryStream()
            use hw = new BinaryWriter(body)
            let name value = hw.Write(encode encoding value); hw.Write(0uy)
            hw.Write(uint32 imports.Count)
            for value in imports do name value
            if extras.Count>0 then
                hw.Write(uint32 (extras |> Seq.filter (fun (_,_,gap)->gap=0) |> Seq.length))
                for value,offset,gap in extras do
                    if gap>0 then hw.Write(Array.zeroCreate<byte> gap)
                    else name value; match offset with Some v -> hw.Write(v) | _ -> ()
            let pad = padding |> Option.defaultWith (fun () -> Array.zeroCreate<byte> ((32-(32+int body.Length)%32)%32))
            writer.Write(uint32 (4+int body.Length+pad.Length)); writer.Write(body.ToArray()); writer.Write(pad)
        let beginCode = output.Position
        for op,args,ipos,line in instructions do
            cancellation.ThrowIfCancellationRequested()
            try
                if bp && op.Variable then writer.Write(BgiLayout.variable op.Op args ipos symbols false)
                else
                    if bp then writer.Write(byte op.Op) else writer.Write(uint32 op.Op)
                    let fmt = payload op args
                    let args = if bp && op.Op=5 && args.Length=2 then [|args[1]|] else args
                    for i in 0..args.Length-1 do
                        let arg = args[i]
                        let value =
                            if arg.StartsWith("\"") then int64 (intern arg)-(if bp then int64 ipos else 0L)
                            elif bp && (op.Op=5 || op.Op=6 || symbols.ContainsKey(arg)) then BgiLayout.resolve symbols arg-int64 ipos
                            else BgiLayout.resolve symbols arg
                        writeInteger writer fmt[i] value
            with :? InvalidDataException as error -> raise (InvalidDataException($"Script line {line}: {error.Message}",error))
        while output.Position-beginCode < int64 stringStart do writer.Write(0uy)
        writer.Write(pool.ToArray())
        if bp && not explicit then while output.Position%16L<>0L do writer.Write(0uy)
        output.ToArray()

    static member Export(data: byte[], encoding: Encoding, cancellation: CancellationToken) =
        use input = new MemoryStream(data,false)
        use reader = new BinaryReader(input)
        if data.Length<36 || Encoding.ASCII.GetString(data,0,26)<>"BurikoCompiledScriptVer1.00" then invalid "Unsupported BGI script signature."
        input.Position <- 28L
        let start = 28+int (reader.ReadUInt32())
        if start<36 || start>data.Length then invalid "Invalid BGI header."
        let count = int (bounded 0L (int64 data.Length) (int64 (reader.ReadUInt32())))
        let result = StringBuilder("#header \"BurikoCompiledScriptVer1.00\"\n")
        let cstring offset =
            if offset<0 || offset>=data.Length then invalid "Invalid BGI string offset."
            let finish = Array.IndexOf(data,0uy,offset)
            if finish<0 then invalid "Unterminated BGI string."
            (strict encoding).GetString(data,offset,finish-offset),finish+1
        for _ in 1..count do
            let value,finish = cstring (int input.Position)
            input.Position <- int64 finish
            result.AppendLine("#import " + quote value) |> ignore
        if input.Position>int64 start then invalid "Invalid BGI import table."
        result.AppendLine("#header_padding " + quote (Convert.ToHexString(reader.ReadBytes(start-int input.Position)))) |> ignore
        let ops = BgiLayout.tables.Value["v1"] |> Array.map (fun o -> o.Op,o) |> Map.ofArray
        let targets = HashSet<int>()
        let records = ResizeArray<int*string>()
        let mutable codeEnd = data.Length-start
        while input.Position < int64 (start+codeEnd) do
            cancellation.ThrowIfCancellationRequested()
            let pos = int input.Position-start
            let op = int (reader.ReadUInt32())
            let info = Map.tryFind op ops |> Option.defaultWith (fun () -> invalid "Unknown BGI instruction.")
            let args = info.Format |> Seq.mapi (fun i kind ->
                let v = if kind='i' then int64 (reader.ReadInt32()) else int64 (reader.ReadUInt32())
                if (op=3 || op=0x7f) && i=0 then
                    let p = int v
                    if p<pos+4+info.Format.Length*4 then invalid "Invalid BGI string location."
                    codeEnd <- min codeEnd p
                    quote (fst (cstring (start+p)))
                elif (op=1 || op=0x7e) && i=0 then targets.Add(int v) |> ignore; BgiLayout.label (int v)
                else string v) |> Seq.toArray
            records.Add(pos,info.Name+"("+String.Join(", ",args)+");")
        let boundaries = records |> Seq.map fst |> HashSet<int>
        if targets |> Seq.exists (fun t -> not (boundaries.Contains(t))) then invalid "Invalid BGI code target."
        for pos,line in records do
            if targets.Contains(pos) then result.AppendLine(BgiLayout.label pos+":") |> ignore
            result.AppendLine(line) |> ignore
        // Inline strings are pooled on recompilation; their original bytes remain available for unchanged references.
        result.ToString()

    static member ExportBp(data: byte[], encoding: Encoding, cancellation: CancellationToken) =
        use input = new MemoryStream(data,false)
        use reader = new BinaryReader(input)
        if data.Length<16 || reader.ReadUInt32()<>16u || int64 (reader.ReadUInt32())<>int64 data.Length-16L || reader.ReadUInt64()<>0UL then invalid "Unsupported BPD script header."
        let integer = function
            | 'b' -> int64 (reader.ReadSByte()) | 'B' -> int64 (reader.ReadByte())
            | 'h' | 'R' -> int64 (reader.ReadInt16()) | 'H' -> int64 (reader.ReadUInt16())
            | 'i' -> int64 (reader.ReadInt32()) | 'I' -> int64 (reader.ReadUInt32())
            | 'q' -> reader.ReadInt64() | _ -> invalid "Invalid BPD operand type."
        let slebValue () =
            let mutable value,shift,more,last = 0L,0,true,0uy
            while more do
                if shift>=35 then invalid "BPD variable integer is too large."
                last <- reader.ReadByte()
                value <- value ||| (int64 (last &&& 127uy) <<< shift)
                shift <- shift+7; more <- last &&& 128uy <> 0uy
            if last &&& 64uy <> 0uy then value <- value ||| (-1L <<< shift)
            bounded (int64 Int32.MinValue) (int64 Int32.MaxValue) value
        let ops = BgiLayout.tables.Value["bp"] |> Array.map (fun o -> o.Op,o) |> Map.ofArray
        let records = ResizeArray<int*string>()
        let targets = HashSet<int>()
        let mutable codeEnd = data.Length-16
        while input.Position < int64 (16+codeEnd) do
            cancellation.ThrowIfCancellationRequested()
            let pos = int input.Position-16
            // BPD aligns its final pool to 16 bytes with zero padding.
            if int input.Position+1=16+codeEnd && data[int input.Position]=0uy then input.Position <- input.Position+1L
            else
                let op = int (reader.ReadByte())
                let info = Map.tryFind op ops |> Option.defaultWith (fun () -> invalid "Unknown BPD instruction.")
                let args = ResizeArray<string>()
                if info.Variable then
                    if BgiLayout.subgroups.Contains(op) then args.Add(string (reader.ReadByte()))
                    elif op=0x03 then
                        let mode = int (reader.ReadByte())
                        args.Add(string mode)
                        if mode<128 then args.Add("[" + String.Join(", ",[|for _ in 0..mode -> string (slebValue())|]) + "]")
                        elif mode &&& 0x7c=0 then args.Add(string (integer (match mode &&& 3 with 0->'b' | 1->'h' | 2->'i' | _->'q')))
                    elif op=0xe2 || op=0xe3 then
                        let count = int (reader.ReadByte())+1
                        args.Add("[" + String.Join(", ",[|for _ in 1..count -> string (reader.ReadInt16())|]) + "]")
                    else
                        let count =
                            if op=0x15 then
                                let flag = data[int input.Position]
                                if flag &&& 8uy=0uy then 1 else 2
                            else 0
                        let branches = ResizeArray<int*int64>()
                        for kind in BgiLayout.pattern op count do
                            match kind with
                            | 'v' -> args.Add(string (slebValue()))
                            | 't' -> let value = slebValue() in args.Add(string (value &&& 3L)); args.Add(string (value >>> 2))
                            | 'R' -> branches.Add(args.Count,integer 'R'); args.Add("")
                            | _ -> args.Add(string (integer kind))
                        for i,relative in branches do
                            let target = BgiLayout.branchBase op pos (int input.Position-16-pos)+int relative
                            targets.Add(target) |> ignore; args[i] <- BgiLayout.label target
                else
                    for kind in info.Format do
                        let value = integer kind
                        if op=5 then
                            let offset = pos+int value
                            if offset<pos+3 || offset>=data.Length-16 then invalid "Invalid BPD string location."
                            codeEnd <- min codeEnd offset
                            let finish = Array.IndexOf(data,0uy,16+offset)
                            if finish<0 then invalid "Unterminated BPD string."
                            let raw = data[16+offset..finish-1]
                            let text = try (strict encoding).GetString(raw) with :? DecoderFallbackException -> "@hex:" + Convert.ToHexString(raw)
                            args.Add(quote text)
                        elif op=6 then targets.Add(pos+int value) |> ignore; args.Add(BgiLayout.label (pos+int value))
                        else args.Add(string value)
                if int input.Position>16+codeEnd then invalid "BPD strings overlap code."
                records.Add(pos,info.Name+"("+String.Join(", ",args)+");")
        let boundaries = records |> Seq.map fst |> HashSet<int>
        if targets |> Seq.exists (fun p -> not (boundaries.Contains(p))) then invalid "Invalid BPD code target."
        let result = StringBuilder()
        for pos,line in records do
            if targets.Contains(pos) then result.AppendLine(BgiLayout.label pos+":") |> ignore
            result.AppendLine(line) |> ignore
        result.ToString()
