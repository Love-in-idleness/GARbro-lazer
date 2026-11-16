namespace GARbro.Scripting

open System
open System.IO
open System.Text
open System.Text.RegularExpressions
open System.Globalization

module internal ScriptSyntax =
    let invalid message = raise (InvalidDataException(message))
    let number (text: string) =
        let sign, digits = if text.StartsWith("-") then -1L, text.Substring(1) else 1L, text
        try
            sign * (if digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase) then Int64.Parse(digits.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) else Int64.Parse(digits, CultureInfo.InvariantCulture))
        with :? FormatException | :? OverflowException -> invalid ("Invalid script number: " + text)
    let bounded low high value =
        if value < low || value > high then invalid "Script operand is out of range."
        value
    let strict (encoding: Encoding) = Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
    let encode encoding (value: string) =
        if value.Contains('\000') then invalid "Script strings cannot contain null bytes."
        try (strict encoding).GetBytes(value)
        with :? EncoderFallbackException as e -> raise (InvalidDataException("Script text cannot be represented by the target encoding.", e))
    let cstring (encoding: Encoding) value =
        if encoding.CodePage = 1200 || encoding.CodePage = 1201 || encoding.CodePage = 12000 || encoding.CodePage = 12001 then
            invalid "This text encoding is not supported for script compilation."
        let bytes = encode encoding value
        if Array.contains 0uy bytes then invalid "This text encoding is not supported for script compilation."
        bytes
    let hex (value: string) =
        try Convert.FromHexString(value.Replace(":", "").Replace(" ", ""))
        with :? FormatException -> invalid "Invalid script hexadecimal data."
    let unquote (text: string) =
        if text.Length < 2 || text[0] <> '"' || text[text.Length-1] <> '"' then invalid "Expected a quoted script string."
        let result = StringBuilder()
        let mutable i = 1
        while i < text.Length - 1 do
            let mutable ch = text[i]
            if ch = '\\' then
                i <- i + 1
                if i >= text.Length-1 then invalid "Unterminated script escape."
                ch <- match text[i] with
                      | 'a' -> '\a' | 'b' -> '\b' | 't' -> '\t' | 'n' -> '\n' | 'v' -> '\v' | 'f' -> '\f' | 'r' -> '\r'
                      | '"' -> '"' | '\\' -> '\\' | _ -> invalid "Unknown script escape."
            result.Append(ch) |> ignore
            i <- i + 1
        result.ToString()
    let quote (text: string) =
        "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\a", "\\a").Replace("\b", "\\b").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\v", "\\v").Replace("\f", "\\f").Replace("\r", "\\r") + "\""
    let tokens comma (text: string) =
        let result = ResizeArray<string>()
        let token = StringBuilder()
        let mutable quoted, escaped, depth, comment = false, false, 0, false
        let flush () =
            if token.Length > 0 then result.Add(token.ToString().Trim()); token.Clear() |> ignore
        let mutable i = 0
        while i < text.Length && not comment do
            let c = text[i]
            if quoted then
                token.Append(c) |> ignore
                if escaped then escaped <- false
                elif c = '\\' then escaped <- true
                elif c = '"' then quoted <- false
            elif c = ';' && not comma then comment <- true
            elif c = '/' && i+1 < text.Length && text[i+1] = '/' then comment <- true
            elif c = '"' then quoted <- true; token.Append(c) |> ignore
            elif c = '[' then depth <- depth + 1; token.Append(c) |> ignore
            elif c = ']' then depth <- depth - 1; token.Append(c) |> ignore
            elif depth = 0 && (if comma then c = ',' else Char.IsWhiteSpace(c)) then flush()
            else token.Append(c) |> ignore
            i <- i + 1
        if quoted || depth <> 0 then invalid "Unterminated script string or list."
        flush()
        result.ToArray()
    let encodedLiteral encoding token =
        let value = unquote token
        use output = new MemoryStream()
        let mutable pos = 0
        for m in Regex.Matches(value, @"\{\{([0-9a-fA-F]{2}(?::[0-9a-fA-F]{2})*)\}\}") do
            output.Write(cstring encoding (value.Substring(pos, m.Index-pos)))
            output.Write(hex m.Groups[1].Value)
            pos <- m.Index + m.Length
        if pos=0 && value.StartsWith("@hex:") then output.Write(hex (value.Substring(5)))
        else output.Write(cstring encoding (value.Substring(pos)))
        output.ToArray()
    let writeInteger (writer: BinaryWriter) kind value =
        match kind with
        | 'b' -> writer.Write(sbyte (bounded -128L 127L value))
        | 'B' -> writer.Write(byte (bounded 0L 255L value))
        | 'h' -> writer.Write(int16 (bounded -32768L 32767L value))
        | 'H' -> writer.Write(uint16 (bounded 0L 65535L value))
        | 'i' -> writer.Write(int32 (bounded (int64 Int32.MinValue) (int64 Int32.MaxValue) value))
        | 'I' -> writer.Write(uint32 (bounded 0L (int64 UInt32.MaxValue) value))
        | 'q' -> writer.Write(value)
        | _ -> invalid "Invalid script operand type."
    let width = function 'b' | 'B' -> 1 | 'h' | 'H' -> 2 | 'i' | 'I' -> 4 | 'q' -> 8 | _ -> invalid "Invalid script operand type."
    let sleb (writer: BinaryWriter) (value: int64) =
        let mutable remaining, more = value, true
        while more do
            let mutable b = byte (remaining &&& 127L)
            remaining <- remaining >>> 7
            more <- not ((remaining = 0L && b &&& 64uy = 0uy) || (remaining = -1L && b &&& 64uy <> 0uy))
            if more then b <- b ||| 128uy
            writer.Write(b)
