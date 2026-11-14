namespace GARbro.Domain

open System
open System.Globalization
open System.Text.Json

/// Format settings keep their numeric type and use the same syntax in every UI language.
[<AbstractClass; Sealed>]
type FormatSettingValues private () =
    static member Parse(kind: string, text: string) : JsonElement =
        let culture = CultureInfo.InvariantCulture
        let integer = NumberStyles.Integer
        let floating = NumberStyles.Float
        let value : obj =
            match kind with
            | "byte" -> box (Byte.Parse(text, integer, culture))
            | "sbyte" -> box (SByte.Parse(text, integer, culture))
            | "short" -> box (Int16.Parse(text, integer, culture))
            | "ushort" -> box (UInt16.Parse(text, integer, culture))
            | "int" -> box (Int32.Parse(text, integer, culture))
            | "uint" -> box (UInt32.Parse(text, integer, culture))
            | "long" -> box (Int64.Parse(text, integer, culture))
            | "ulong" -> box (UInt64.Parse(text, integer, culture))
            | "decimal" -> box (Decimal.Parse(text, floating, culture))
            | "float" ->
                let number = Single.Parse(text, floating, culture)
                if not (Single.IsFinite(number)) then raise (FormatException())
                box number
            | "double" ->
                let number = Double.Parse(text, floating, culture)
                if not (Double.IsFinite(number)) then raise (FormatException())
                box number
            | _ -> box text
        JsonSerializer.SerializeToElement(value)
