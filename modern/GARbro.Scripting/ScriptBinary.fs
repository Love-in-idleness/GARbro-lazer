namespace GARbro.Scripting

open System
open System.Buffers.Binary
open System.IO
open System.IO.Compression
open System.Text
open System.Threading
open ScriptSyntax

module internal ScriptBinary =
    let maximum = 64 * 1024 * 1024

    let copyBounded (input: Stream) (output: Stream) limit (cancellation: CancellationToken) =
        let buffer = Array.zeroCreate<byte> 16384
        let mutable total = 0
        let mutable count = input.Read(buffer)
        while count <> 0 do
            cancellation.ThrowIfCancellationRequested()
            if total > limit-count then invalid "Script data exceeds the size limit."
            output.Write(buffer, 0, count)
            total <- total+count
            count <- input.Read(buffer)

    let inflate (data:byte[]) limit cancellation =
        if data.Length<6 then invalid "Truncated script data."
        use source = new MemoryStream(data, false)
        use compressed = new ZLibStream(source, CompressionMode.Decompress)
        use output = new MemoryStream()
        copyBounded compressed output limit cancellation
        let bytes=output.ToArray()
        // ZLibStream can accept a truncated trailer; validate RFC 1950's checksum.
        let mutable a=1u
        let mutable b=0u
        for start in 0..5552..bytes.Length-1 do
            cancellation.ThrowIfCancellationRequested()
            for i in start..min (start+5551) (bytes.Length-1) do
                a<-a+uint32 bytes[i];b<-b+a
            a<-a%65521u;b<-b%65521u
        if (b<<<16 ||| a)<>BinaryPrimitives.ReadUInt32BigEndian(ReadOnlySpan<byte>(data,data.Length-4,4)) then
            invalid "Invalid compressed script data."
        bytes

    let deflate (data: byte[]) =
        use output = new MemoryStream()
        do
            use compressed = new ZLibStream(output, CompressionLevel.Optimal, true)
            compressed.Write(data)
        output.ToArray()

    let readBytes (reader: BinaryReader) length =
        if length < 0 || int64 length > reader.BaseStream.Length-reader.BaseStream.Position then
            invalid "Truncated script data."
        reader.ReadBytes(length)

    let decode (encoding: Encoding) (data: byte[]) =
        try (strict encoding).GetString(data)
        with :? DecoderFallbackException as error -> raise (InvalidDataException("Script text cannot be decoded with the selected encoding.", error))
