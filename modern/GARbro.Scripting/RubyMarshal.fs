namespace GARbro.Scripting

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Threading
open ScriptSyntax

type internal RubyValue =
    | RubyNil
    | RubyBool of bool
    | RubyInteger of int
    | RubySymbol of string
    | RubyString of RubyBytes
    | RubyArray of ResizeArray<RubyValue>
and internal RubyBytes = { mutable Bytes: byte[]; Attributes: Dictionary<string, RubyValue> }

module internal RubyMarshal =
    let load (data: byte[]) (cancellation: CancellationToken) =
        use input = new MemoryStream(data, false)
        use reader = new BinaryReader(input)
        if reader.ReadByte() <> 4uy || reader.ReadByte() <> 8uy then invalid "Unsupported Ruby Marshal version."
        let symbols = ResizeArray<string>()
        let objects = ResizeArray<RubyValue>()
        let mutable nodes = 0
        let integer () =
            let code = int (reader.ReadSByte())
            if code=0 then 0
            elif code>4 then code-5
            elif code < -4 then code+5
            else
                let mutable number = if code<0 then -1 else 0
                for i in 0..abs code-1 do
                    let mask = 255 <<< (i*8)
                    number <- (number &&& ~~~mask) ||| (int (reader.ReadByte()) <<< (i*8))
                number
        let length () = int (bounded 0L (min (int64 ScriptBinary.maximum) (input.Length-input.Position)) (int64 (integer())))
        let reference (values: ResizeArray<'a>) =
            let index = integer()
            if index<0 || index>=values.Count then invalid "Invalid Ruby Marshal reference."
            values[index]
        let rec read depth =
            cancellation.ThrowIfCancellationRequested()
            nodes <- nodes+1
            if depth>128 || nodes>262144 then invalid "Script structure exceeds the size limit."
            match char (reader.ReadByte()) with
            | '0' -> RubyNil
            | 'T' -> RubyBool true
            | 'F' -> RubyBool false
            | 'i' -> RubyInteger(integer())
            | ':' ->
                let value = Encoding.UTF8.GetString(ScriptBinary.readBytes reader (length()))
                symbols.Add(value)
                RubySymbol value
            | ';' -> RubySymbol(reference symbols)
            | '@' -> reference objects
            | '"' ->
                let value = RubyString { Bytes=ScriptBinary.readBytes reader (length()); Attributes=Dictionary() }
                objects.Add(value)
                value
            | '[' ->
                let count = length()
                let values = ResizeArray<RubyValue>()
                let value = RubyArray values
                objects.Add(value)
                for _ in 1..count do values.Add(read (depth+1))
                value
            | 'I' ->
                let value = read (depth+1)
                let attributes = match value with RubyString bytes -> bytes.Attributes | _ -> invalid "Unsupported Ruby Marshal instance variables."
                let count = length()
                for _ in 1..count do
                    let key = match read (depth+1) with RubySymbol name -> name | _ -> invalid "Invalid Ruby Marshal attribute."
                    if attributes.ContainsKey(key) then invalid "Duplicate Ruby Marshal attribute."
                    attributes.Add(key, read (depth+1))
                value
            | _ -> invalid "Unsupported Ruby Marshal value in Scripts."
        let result = read 0
        if input.Position<>input.Length then invalid "Unexpected Ruby Marshal trailing data."
        result

    let save value (cancellation: CancellationToken) =
        use output = new MemoryStream()
        use writer = new BinaryWriter(output)
        let symbols = Dictionary<string,int>(StringComparer.Ordinal)
        let objects = Dictionary<obj,int>(ReferenceEqualityComparer.Instance)
        writer.Write(4uy); writer.Write(8uy)
        let integer (number: int) =
            if number=0 then writer.Write(0uy)
            elif number>0 && number<123 then writer.Write(byte (number+5))
            elif number<0 && number > -124 then writer.Write(sbyte (number-5))
            else
                let bytes = BitConverter.GetBytes(number)
                let mutable count = 4
                let padding = if number<0 then 255uy else 0uy
                while count>1 && bytes[count-1]=padding do count <- count-1
                writer.Write(sbyte (if number<0 then -count else count))
                writer.Write(bytes,0,count)
        let symbol name =
            match symbols.TryGetValue(name) with
            | true,index -> writer.Write(byte ';'); integer index
            | _ ->
                symbols.Add(name,symbols.Count)
                let bytes = Encoding.UTF8.GetBytes(name)
                writer.Write(byte ':'); integer bytes.Length; writer.Write(bytes)
        let register (key: obj) =
            match objects.TryGetValue(key) with
            | true,index -> writer.Write(byte '@'); integer index; false
            | _ -> objects.Add(key,objects.Count); true
        let rec write depth value =
            cancellation.ThrowIfCancellationRequested()
            if depth>128 || output.Length>int64 ScriptBinary.maximum then invalid "Script structure exceeds the size limit."
            match value with
            | RubyNil -> writer.Write(byte '0')
            | RubyBool yes -> writer.Write(byte (if yes then 'T' else 'F'))
            | RubyInteger number -> writer.Write(byte 'i'); integer number
            | RubySymbol name -> symbol name
            | RubyString bytes when register bytes ->
                if bytes.Attributes.Count<>0 then writer.Write(byte 'I')
                writer.Write(byte '"'); integer bytes.Bytes.Length; writer.Write(bytes.Bytes)
                if bytes.Attributes.Count<>0 then
                    integer bytes.Attributes.Count
                    for item in bytes.Attributes do symbol item.Key; write (depth+1) item.Value
            | RubyArray values when register values ->
                writer.Write(byte '['); integer values.Count
                for item in values do write (depth+1) item
            | _ -> ()
        write 0 value
        output.ToArray()

