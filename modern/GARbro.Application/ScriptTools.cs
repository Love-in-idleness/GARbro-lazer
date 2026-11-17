using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using GARbro.Domain;
using GARbro.Scripting;
using GameRes;
using GameRes.Formats.KiriKiri;
using GameRes.Formats.NScripter;
using GameRes.Formats.Luca;
using Furikiri.Echo;

namespace GARbro.Application;

internal static class ScriptTools
{
    internal static bool IsBgi(Stream input) => StartsWith(input, "BurikoCompiledScriptVer1.00\0"u8);

    static bool StartsWith(Stream input, ReadOnlySpan<byte> signature)
    {
        Span<byte> header = stackalloc byte[signature.Length];
        return input.ReadAtLeast(header, header.Length, false) == header.Length && header.SequenceEqual(signature);
    }

    static Encoding CompileEncoding() => Encodings.ResourceEncodingOverride ?? Encodings.cp932;

    static void PrepareCompilation(string input, CancellationToken cancellation)
    {
        using (var source = File.OpenRead(input))
        using (var normalized = File.Create(input + ".utf8"))
        {
            if (source.Length > 64L << 20) throw new InvalidDataException("Resource exceeds the size limit.");
            ScriptTextEncoding.WriteEncoded(source, normalized, new UTF8Encoding(false, true), cancellation);
        }
        File.Move(input + ".utf8", input, true);
    }

    internal static void Convert(string input, string originalName, Stream destination, string format, long maxBytes, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (ScriptOutputFormats.Recompile.Contains(format)) PrepareCompilation(input, cancellation);
        using (var source = File.OpenRead(input))
        {
            var encoding = Encodings.ResourceEncodingOverride;
            if (format is "GSC" or "BGI" or "BGI-BP" or "ASB" or "TSC" or "BSD" or "BPD" or "JSON")
            {
                if (source.Length > 64L << 20) throw new InvalidDataException("Resource exceeds the size limit.");
                byte[] converted;
                if (ScriptOutputFormats.Recompile.Contains(format))
                {
                    string text = File.ReadAllText(input, new UTF8Encoding(false, true));
                    converted = format switch
                    {
                        "GSC" => GscScript.Compile(text, CompileEncoding(), cancellation),
                        "ASB" => AsbScript.Compile(text, encoding ?? Encoding.UTF8, cancellation),
                        _ => BgiScript.Compile(text, format == "BGI-BP", CompileEncoding(), cancellation)
                    };
                }
                else
                {
                    byte[] bytes = File.ReadAllBytes(input);
                    string text = format switch
                    {
                        "TSC" => GscScript.Export(bytes, CompileEncoding(), cancellation),
                        "JSON" => AsbScript.Export(bytes, encoding ?? Encoding.UTF8, cancellation),
                        "BPD" => BgiScript.ExportBp(bytes, CompileEncoding(), cancellation),
                        _ => BgiScript.Export(bytes, CompileEncoding(), cancellation)
                    };
                    converted = Encoding.UTF8.GetBytes(text);
                }
                if (converted.LongLength > maxBytes) throw new InvalidDataException("Output limit exceeded.");
                cancellation.ThrowIfCancellationRequested();
                destination.Write(converted);
                return;
            }
            if (format is "RPY" or "RB" or "RGSS-JSON" or "RGSS" or "LUCA-JSON" or "LUCA" ||
                format == "TJS" && StartsWith(source, "TJS2100\0"u8))
            {
                if (source.Length > 64L << 20) throw new InvalidDataException("Resource exceeds the size limit.");
                source.Position = 0;
                byte[] data = File.ReadAllBytes(input);
                byte[] converted;
                switch (format)
                {
                    case "RPY": converted = Encoding.UTF8.GetBytes(RenpyScript.Export(data, cancellation)); break;
                    case "RB":
                    case "RGSS-JSON": converted = Encoding.UTF8.GetBytes(RgssScript.Export(data, originalName, encoding, format == "RB", cancellation)); break;
                    case "RGSS": converted = RgssScript.Compile(new UTF8Encoding(false, true).GetString(data), encoding, cancellation); break;
                    case "LUCA-JSON":
                    case "LUCA":
                        var provider = FormatCatalog.Instance.ScriptFormats.OfType<LucaScriptFormat>().Single();
                        string text = format == "LUCA" ? new UTF8Encoding(false, true).GetString(data) : null;
                        var profile = provider.SelectProfile(text == null ? null : LucaScript.SchemeName(text));
                        converted = format == "LUCA" ? LucaScript.Compile(text, profile.Json, encoding, cancellation) :
                            Encoding.UTF8.GetBytes(LucaScript.Export(data, profile.Name, profile.Json, encoding, cancellation));
                        break;
                    default:
                        TjsScriptValidation.Validate(data);
                        converted = Encoding.UTF8.GetBytes(new Decompiler(input).Decompile());
                        break;
                }
                cancellation.ThrowIfCancellationRequested();
                if (converted.LongLength > maxBytes) throw new InvalidDataException("Output limit exceeded.");
                destination.Write(converted);
                return;
            }
            switch (format)
            {
                case "NSCRIPT":
                case "DSM":
                    var targetEncoding = format == "DSM" ? new UTF8Encoding(false, true) : CompileEncoding();
                    if (format == "NSCRIPT" && targetEncoding.CodePage is 1200 or 1201 or 12000 or 12001)
                        throw new NotSupportedException("This text encoding is not supported for NScripter compilation.");
                    using (var encoded = new MemoryStream())
                    {
                        ScriptTextEncoding.WriteEncoded(source, encoded, targetEncoding, cancellation); encoded.Position = 0;
                        using var binary = new BinaryStream(encoded, originalName, true);
                        ScriptFormat compiler = format == "NSCRIPT" ? new NSOpener() : new DsmConverter();
                        using var compiled = compiler.ConvertBack(binary);
                        ResourceService.CopyBounded(compiled, destination, maxBytes);
                    }
                    return;
                case "TXT":
                    if (source.Length > 64L << 20) throw new InvalidDataException("Resource exceeds the size limit.");
                    bool nscript = VFS.IsPathEqualsToFileName(originalName.Replace('\\', '/'), "nscript.dat");
                    ScriptFormat decoder = nscript ? new NSOpener() : new DsmConverter();
                    using (var binary = new BinaryStream(source, originalName))
                    using (var text = decoder.ConvertFrom(binary))
                        ScriptTextEncoding.WriteUtf8(text, destination, nscript ? encoding : Encoding.UTF8, cancellation);
                    return;
                case "AST":
                    ScriptTextEncoding.WriteUtf8(source, destination, encoding ?? Encoding.UTF8, cancellation);
                    return;
                case "KS":
                case "TJS":
                    if (format == "TJS" && StartsWith(source, "TJS2100\0"u8))
                        break;
                    source.Position = 0;
                    Span<byte> prefix = stackalloc byte[5];
                    bool wrapped = source.ReadAtLeast(prefix, prefix.Length, false) == prefix.Length &&
                        prefix[0] == 0xfe && prefix[1] == 0xfe && prefix[2] < 3 && prefix[3] == 0xff && prefix[4] == 0xfe;
                    if (wrapped && prefix[2] < 2 && source.Length > 64L << 20)
                        throw new InvalidDataException("Resource exceeds the size limit.");
                    source.Position = 0;
                    using (var text = wrapped ? new NoCrypt().EntryReadFilter(
                        new Xp3Entry { Name = originalName, UnpackedSize = (uint)source.Length }, source) : source)
                        ScriptTextEncoding.WriteUtf8(text, destination, encoding, cancellation);
                    return;

            }
        }
        throw new NotSupportedException("The selected output format does not match this script.");
    }

}
