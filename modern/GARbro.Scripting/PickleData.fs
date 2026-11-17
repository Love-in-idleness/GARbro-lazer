namespace GARbro.Scripting

open System
open System.Buffers.Binary
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Numerics
open System.Text
open System.Text.RegularExpressions
open System.Threading
open ScriptSyntax

type internal PyValue =
    | PyNone
    | PyMark
    | PyBool of bool
    | PyInteger of BigInteger
    | PyFloat of double
    | PyText of string
    | PyBytes of byte[]
    | PyList of ResizeArray<PyValue>
    | PyTuple of PyValue[]
    | PyDict of ResizeArray<PyValue * PyValue>
    | PyGlobal of string * string
    | PyObject of PyInstance
and internal PyInstance =
    { Module: string; Name: string; Arguments: PyValue[]
      Fields: Dictionary<string,PyValue>; Items: ResizeArray<PyValue>; mutable State: PyValue }

module internal PickleData =
    let rec text = function
        | PyText s -> s
        | PyBytes b -> ScriptBinary.decode Encoding.UTF8 b
        | PyInteger i -> i.ToString(CultureInfo.InvariantCulture)
        | PyFloat f -> f.ToString("R",CultureInfo.InvariantCulture)
        | PyBool true -> "True" | PyBool false -> "False" | PyNone -> "None"
        | PyGlobal(m,n) -> m+"."+n
        | PyObject o when o.Name="PyExpr" && o.Arguments.Length>0 -> text o.Arguments[0]
        | _ -> invalid "Expected a script expression."

    let values = function
        | PyList list -> list.ToArray()
        | PyTuple items -> items
        | PyObject o when o.Name="GroupedLine" -> o.Arguments
        | PyObject o when o.Name.Contains("List") || o.Name.Contains("Set") -> o.Items.ToArray()
        | PyNone -> [||]
        | _ -> invalid "Expected a script sequence."

    let pairs = function
        | PyDict pairs -> pairs.ToArray()
        | PyObject o when o.Name.Contains("Dict") ->
            o.Items |> Seq.map (fun item -> match values item with [|a;b|] -> a,b | _ -> invalid "Invalid script dictionary.") |> Seq.toArray
        | PyNone -> [||]
        | _ -> invalid "Expected a script dictionary."

    let field name = function
        | PyObject o -> match o.Fields.TryGetValue(name) with true,v -> v | _ -> PyNone
        | PyDict items -> items |> Seq.tryPick (fun (k,v) -> if text k=name then Some v else None) |> Option.defaultValue PyNone
        | _ -> PyNone

    let present = function PyNone -> false | _ -> true
    let truth = function PyNone | PyBool false | PyText "" -> false | PyInteger n -> not n.IsZero | _ -> true

    let load (data: byte[]) (cancellation: CancellationToken) =
        use input = new MemoryStream(data,false)
        use reader = new BinaryReader(input)
        let stack = ResizeArray<PyValue>()
        let memo = Dictionary<int,PyValue>()
        let push v =
            if stack.Count>=262144 then invalid "Script structure exceeds the size limit."
            stack.Add(v)
        let pop () =
            if stack.Count=0 then invalid "Invalid Pickle stack."
            let i=stack.Count-1
            let v=stack[i]
            stack.RemoveAt(i)
            v
        let peek () = if stack.Count=0 then invalid "Invalid Pickle stack." else stack[stack.Count-1]
        let marked () =
            let mutable i=stack.Count-1
            while i>=0 && (match stack[i] with PyMark -> false | _ -> true) do i<-i-1
            if i<0 then invalid "Missing Pickle mark."
            let result=stack.GetRange(i+1,stack.Count-i-1).ToArray()
            stack.RemoveRange(i,stack.Count-i)
            result
        let line () =
            use output=new MemoryStream()
            let mutable b=reader.ReadByte()
            while b<>10uy do
                if output.Length>=int64 ScriptBinary.maximum then invalid "Script data exceeds the size limit."
                output.WriteByte(b); b<-reader.ReadByte()
            Encoding.Latin1.GetString(output.ToArray())
        let size (n: uint64) =
            if n>uint64 ScriptBinary.maximum || n>uint64 (input.Length-input.Position) then invalid "Truncated Pickle data."
            int n
        let integer (s:string) =
            match BigInteger.TryParse(s.TrimEnd('L'),NumberStyles.Integer,CultureInfo.InvariantCulture) with
            | true,n -> PyInteger n | _ -> invalid "Invalid Pickle integer."
        let array (a:PyValue[]) = PyList(ResizeArray a)
        let dictionary (a:PyValue[]) =
            if a.Length%2<>0 then invalid "Invalid Pickle dictionary."
            PyDict(ResizeArray(Array.chunkBySize 2 a |> Array.map (fun p -> p[0],p[1])))
        let addItems target (items:PyValue[]) =
            let list=match target with PyList l -> l | PyObject o -> o.Items | _ -> invalid "Invalid Pickle list."
            if list.Count+items.Length>262144 then invalid "Script structure exceeds the size limit."
            list.AddRange(items)
        let setItems target (items:PyValue[]) =
            if items.Length%2<>0 then invalid "Invalid Pickle dictionary."
            match target with
            | PyDict d ->
                if d.Count+items.Length/2>262144 then invalid "Script structure exceeds the size limit."
                for pair in Array.chunkBySize 2 items do d.Add(pair[0],pair[1])
            | PyObject o ->
                for pair in Array.chunkBySize 2 items do o.Items.Add(PyTuple pair)
            | _ -> invalid "Invalid Pickle dictionary."
        let rec construct callable args =
            let arguments=values args
            match callable with
            | PyGlobal(("copy_reg"|"copyreg"),"_reconstructor") when arguments.Length>=1 -> construct arguments[0] (PyTuple [||])
            | PyGlobal(("copy_reg"|"copyreg"),"__newobj__") when arguments.Length>=1 -> construct arguments[0] (PyTuple arguments[1..])
            | PyGlobal("_codecs","encode") when arguments.Length>=2 && (text arguments[1]="latin1" || text arguments[1]="latin-1") ->
                PyBytes(Encoding.Latin1.GetBytes(text arguments[0]))
            | PyGlobal(("builtins"|"__builtin__"),("set"|"frozenset")) -> array (if arguments.Length=0 then [||] else values arguments[0])
            | PyGlobal(("builtins"|"__builtin__"),("str"|"unicode")) when arguments.Length=1 -> PyText(text arguments[0])
            | PyGlobal(("builtins"|"__builtin__"),"bytes") when arguments.Length=0 -> PyBytes [||]
            | PyGlobal("collections",("OrderedDict"|"defaultdict")) -> PyDict(ResizeArray())
            | PyGlobal("inspect","_ParameterKind") when arguments.Length=1 -> arguments[0]
            | PyGlobal(m,n) when m.StartsWith("renpy.",StringComparison.Ordinal) ->
                PyObject {Module=m;Name=n;Arguments=arguments;Fields=Dictionary(StringComparer.Ordinal);Items=ResizeArray();State=PyNone}
            | PyGlobal(m,n) -> invalid ("Unsupported Pickle constructor in Ren'Py script: "+m+"."+n)
            | _ -> invalid "Unsupported Pickle constructor in Ren'Py script."
        let applyState target state =
            match target with
            | PyObject o ->
                o.State<-state
                let apply value = for k,v in pairs value do o.Fields[text k]<-v
                match state with
                | PyDict _ -> apply state
                | PyTuple items when o.Name="PyCode" && items.Length>=4 ->
                    o.Fields["source"]<-items[1];o.Fields["location"]<-items[2];o.Fields["mode"]<-items[3]
                | PyTuple items ->
                    for item in items do match item with PyDict _ -> apply item | PyNone -> () | _ -> ()
                | PyNone -> ()
                | _ -> invalid "Unsupported Ren'Py object state."
            | _ -> invalid "Invalid Pickle object state."
        let put index =
            if index<0 || memo.Count>=1048576 then invalid "Script structure exceeds the size limit."
            memo[index]<-peek()
        let get index = match memo.TryGetValue(index) with true,v -> push v | _ -> invalid "Invalid Pickle reference."
        let numberLine () = match Int32.TryParse(line(),NumberStyles.None,CultureInfo.InvariantCulture) with true,n -> n | _ -> invalid "Invalid Pickle reference."
        let readGlobal () = let m=line() in PyGlobal(m,line())
        let unescape (s:string) =
            Regex.Replace(s,@"\\(U[0-9a-fA-F]{8}|u[0-9a-fA-F]{4}|x[0-9a-fA-F]{2}|[0-7]{1,3}|.)",MatchEvaluator(fun m ->
                let e=m.Groups[1].Value
                match e[0] with
                | 'U'|'u'|'x' -> Char.ConvertFromUtf32(Int32.Parse(e.Substring(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture))
                | c when c>='0' && c<='7' -> string (char (Convert.ToInt32(e,8)))
                | 'n' -> "\n" | 'r' -> "\r" | 't' -> "\t" | 'b' -> "\b" | 'f' -> "\f" | 'v' -> "\v" | 'a' -> "\a"
                | '\\' -> "\\" | '\'' -> "'" | '"' -> "\"" | _ -> "\\"+e))
        let mutable result=None
        let mutable operations=0
        while result.IsNone do
            cancellation.ThrowIfCancellationRequested()
            operations<-operations+1
            if operations>10000000 then invalid "Script structure exceeds the size limit."
            match reader.ReadByte() with
            | 0x80uy -> if reader.ReadByte()>5uy then invalid "Unsupported Pickle protocol."
            | 0x95uy -> size (reader.ReadUInt64()) |> ignore
            | 0x2euy ->
                let value=pop()
                if stack.Count<>0 || input.Position<>input.Length then invalid "Unexpected Pickle trailing data."
                result<-Some value
            | 0x28uy -> push PyMark
            | 0x30uy -> pop() |> ignore
            | 0x31uy -> marked() |> ignore
            | 0x32uy -> push(peek())
            | 0x4euy -> push PyNone
            | 0x88uy -> push(PyBool true)
            | 0x89uy -> push(PyBool false)
            | 0x49uy -> let s=line() in push(if s="00" then PyBool false elif s="01" then PyBool true else integer s)
            | 0x4auy -> push(PyInteger(BigInteger(reader.ReadInt32())))
            | 0x4buy -> push(PyInteger(BigInteger(reader.ReadByte())))
            | 0x4duy -> push(PyInteger(BigInteger(reader.ReadUInt16())))
            | 0x4cuy -> push(integer (line()))
            | 0x8auy -> push(PyInteger(BigInteger(ScriptBinary.readBytes reader (size (uint64 (reader.ReadByte()))))))
            | 0x8buy -> push(PyInteger(BigInteger(ScriptBinary.readBytes reader (size (uint64 (reader.ReadUInt32()))))))
            | 0x46uy -> push(PyFloat(Double.Parse(line(),CultureInfo.InvariantCulture)))
            | 0x47uy -> push(PyFloat(BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(ScriptBinary.readBytes reader 8))))
            | 0x53uy ->
                let s=line()
                if s.Length<2 || s[0]<>s[s.Length-1] || (s[0]<>'\'' && s[0]<>'"') then invalid "Invalid Pickle string."
                push(PyBytes(Encoding.Latin1.GetBytes(unescape (s.Substring(1,s.Length-2)))))
            | 0x56uy -> push(PyText(unescape (line())))
            | 0x54uy | 0x42uy -> push(PyBytes(ScriptBinary.readBytes reader (size (uint64 (reader.ReadUInt32())))))
            | 0x55uy | 0x43uy -> push(PyBytes(ScriptBinary.readBytes reader (size (uint64 (reader.ReadByte())))))
            | 0x8euy | 0x96uy -> push(PyBytes(ScriptBinary.readBytes reader (size (reader.ReadUInt64()))))
            | 0x58uy -> push(PyText(ScriptBinary.decode Encoding.UTF8 (ScriptBinary.readBytes reader (size (uint64 (reader.ReadUInt32()))))))
            | 0x8cuy -> push(PyText(ScriptBinary.decode Encoding.UTF8 (ScriptBinary.readBytes reader (size (uint64 (reader.ReadByte()))))))
            | 0x8duy -> push(PyText(ScriptBinary.decode Encoding.UTF8 (ScriptBinary.readBytes reader (size (reader.ReadUInt64())))))
            | 0x5duy | 0x8fuy -> push(array [||])
            | 0x6cuy | 0x91uy -> push(array (marked()))
            | 0x61uy -> let v=pop() in addItems (peek()) [|v|]
            | 0x65uy | 0x90uy -> let a=marked() in addItems (peek()) a
            | 0x29uy -> push(PyTuple [||])
            | 0x74uy -> push(PyTuple(marked()))
            | 0x85uy -> push(PyTuple [|pop()|])
            | 0x86uy -> let b=pop() in let a=pop() in push(PyTuple [|a;b|])
            | 0x87uy -> let c=pop() in let b=pop() in let a=pop() in push(PyTuple [|a;b;c|])
            | 0x7duy -> push(PyDict(ResizeArray()))
            | 0x64uy -> push(dictionary (marked()))
            | 0x73uy -> let v=pop() in let k=pop() in setItems (peek()) [|k;v|]
            | 0x75uy -> let a=marked() in setItems (peek()) a
            | 0x70uy -> put(numberLine())
            | 0x71uy -> put(int (reader.ReadByte()))
            | 0x72uy -> put(reader.ReadInt32())
            | 0x94uy -> put memo.Count
            | 0x67uy -> get(numberLine())
            | 0x68uy -> get(int (reader.ReadByte()))
            | 0x6auy -> get(reader.ReadInt32())
            | 0x63uy -> push(readGlobal())
            | 0x93uy -> let n=text(pop()) in let m=text(pop()) in push(PyGlobal(m,n))
            | 0x52uy | 0x81uy -> let args=pop() in let cls=pop() in push(construct cls args)
            | 0x92uy ->
                let kwargs=pop()
                if (pairs kwargs).Length<>0 then invalid "Unsupported Pickle keyword constructor."
                let args=pop()
                let cls=pop()
                push(construct cls args)
            | 0x69uy -> let cls=readGlobal() in push(construct cls (PyTuple(marked())))
            | 0x6fuy ->
                let a=marked()
                if a.Length=0 then invalid "Invalid Pickle object."
                push(construct a[0] (PyTuple a[1..]))
            | 0x62uy -> let state=pop() in applyState (peek()) state
            | _ -> invalid "Unsupported Pickle opcode in Ren'Py script."
        result.Value

