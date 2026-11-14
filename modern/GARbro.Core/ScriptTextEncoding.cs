using System;
using System.IO;
using System.Text;
using System.Threading;

namespace GameRes;

public static class ScriptTextEncoding
{
    static readonly UTF8Encoding Utf8 = new(false, true);
    static readonly Encoding[] BomEncodings = {
        new UTF32Encoding(true, true), Encoding.UTF32, Encoding.BigEndianUnicode,
        Encoding.Unicode, Encoding.UTF8
    };

    public static void WriteUtf8(Stream source, Stream destination, Encoding encoding, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        byte[] bytes = new byte[8192];
        long start = source.CanSeek ? source.Position : 0;
        int count = source.ReadAtLeast(bytes, 4, false);
        int offset = 0;
        foreach (var candidate in BomEncodings)
        {
            var bom = candidate.Preamble;
            if (!bytes.AsSpan(0, count).StartsWith(bom)) continue;
            encoding = candidate;
            offset = bom.Length;
            break;
        }

        if (encoding == null && source.CanSeek)
        {
            var probe = Utf8.GetDecoder();
            var probeChars = new char[Utf8.GetMaxCharCount(bytes.Length)];
            try
            {
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    probe.GetChars(bytes.AsSpan(0, count), probeChars, flush: count == 0);
                    if (count == 0) break;
                    count = source.Read(bytes);
                }
                encoding = Utf8;
            }
            catch (DecoderFallbackException) { encoding = Encodings.cp932; }
            source.Position = start;
            count = source.ReadAtLeast(bytes, 4, false);
        }
        encoding = (encoding ?? Encodings.cp932).WithFatalFallback();
        var decoder = encoding.GetDecoder();
        var chars = new char[encoding.GetMaxCharCount(bytes.Length)];
        using var writer = new StreamWriter(destination, Utf8, 4096, leaveOpen: true);
        try
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                int decoded = decoder.GetChars(bytes.AsSpan(offset, count - offset), chars, flush: count == 0);
                writer.Write(chars, 0, decoded);
                if (count == 0) break;
                count = source.Read(bytes);
                offset = 0;
            }
        }
        catch (DecoderFallbackException error)
        {
            throw new InvalidDataException("Script text does not match its encoding.", error);
        }
    }
}
