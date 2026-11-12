using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using GameRes;
using GameRes.Formats.KiriKiri;
using GameRes.Formats.NScripter;

namespace GARbro.Application;

internal static class ScriptTools
{
    internal static bool IsBgi(Stream input) => StartsWith(input, "BurikoCompiledScriptVer1.00\0"u8);

    static bool StartsWith(Stream input, ReadOnlySpan<byte> signature)
    {
        Span<byte> header = stackalloc byte[signature.Length];
        return input.ReadAtLeast(header, header.Length, false) == header.Length && header.SequenceEqual(signature);
    }

    static string FindProgram(string name)
    {
        string file = name + (OperatingSystem.IsWindows() ? ".exe" : "");
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).AsEnumerable();
        if (OperatingSystem.IsMacOS()) directories = directories.Concat(new[] { "/opt/homebrew/bin", "/usr/local/bin" });
        return directories.Where(Path.IsPathFullyQualified).Select(path => Path.Combine(path, file)).FirstOrDefault(File.Exists);
    }

    static string GscEncoding() => Encodings.ResourceEncodingOverride?.CodePage switch
    {
        null => "auto",
        932 => "cp932",
        936 => "gbk",
        54936 => "gb18030",
        949 or 950 or >= 1250 and <= 1258 => $"cp{Encodings.ResourceEncodingOverride.CodePage}",
        _ => throw new NotSupportedException("This text encoding is not supported for GSC conversion. Choose Auto, CP932 or GBK.")
    };

    static string BgiEncoding() => Encodings.ResourceEncodingOverride?.CodePage switch
    {
        null or 932 => "cp932",
        936 => "gbk",
        950 => "big5",
        65001 => "utf-8",
        _ => throw new NotSupportedException("This text encoding is not supported for BGI conversion. Choose Auto, CP932 or GBK.")
    };

    internal static void Convert(string input, string originalName, Stream destination, string format, long maxBytes, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using (var source = File.OpenRead(input))
        {
            var encoding = Encodings.ResourceEncodingOverride;
            switch (format)
            {
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
                case "BSD":
                    if (!IsBgi(source)) throw new InvalidDataException("Unsupported BGI script signature.");
                    break;
            }
        }
        ConvertWithHelper(input, destination, format, maxBytes, cancellation);
    }

    static void ConvertWithHelper(string input, Stream destination, string format, long maxBytes, CancellationToken cancellation)
    {
        if (OperatingSystem.IsAndroid()) throw new NotSupportedException("Script conversion requires a desktop helper.");
        var (name, variable) = format switch
        {
            "TSC" => ("liarsofttool", "GARBRO_LIARSOFTTOOL"),
            "RPY" => ("unrpyc", "GARBRO_UNRPYC"),
            "TJS" => ("tjs2dec", "GARBRO_TJS2DEC"),
            "JSON" => ("msg_tool", "GARBRO_MSG_TOOL"),
            "RB" => ("rvpacker-txt-rs", "GARBRO_RVPACKER"),
            _ => ("Bgi_asdisEN", "GARBRO_BGI_ASDIS")
        };
        string unavailable = $"Script converter unavailable: {name} ({variable})";
        bool bgi = format is "BSD" or "BPD";
        string configured = Environment.GetEnvironmentVariable(variable);
        string helper = configured ?? FindProgram(name);
        string common = null;
        if (bgi)
        {
            if (string.IsNullOrEmpty(configured) || !Path.IsPathFullyQualified(configured))
                throw new NotSupportedException(unavailable);
            common = Path.Combine(configured, "src", "common");
            helper = format == "BSD" ? Path.Combine(configured, "src", "v1", "bgidis.py")
                                     : Path.Combine(configured, "src", "bp", "bpdis.py");
            if (!Directory.Exists(common)) throw new NotSupportedException(unavailable);
        }
        if (string.IsNullOrEmpty(helper) || !Path.IsPathFullyQualified(helper) || !File.Exists(helper))
            throw new NotSupportedException(unavailable);

        string script = (bgi || format == "RPY") && Path.GetExtension(helper).Equals(".py", StringComparison.OrdinalIgnoreCase) ? helper : null;
        if (script != null)
            helper = FindProgram(OperatingSystem.IsWindows() ? "python" : "python3") ?? throw new NotSupportedException(unavailable);
        string directory = Path.GetDirectoryName(input);
        string output = format switch
        {
            "TJS" => Path.Combine(directory, "result.tjs"),
            "RB" => Path.Combine(directory, "json", "Scripts.rb"),
            _ => Path.ChangeExtension(input, format.ToLowerInvariant())
        };
        if (format == "RB")
        {
            string data = Directory.CreateDirectory(Path.Combine(directory, "Data")).FullName;
            string extension = Path.GetExtension(input);
            File.Move(input, Path.Combine(data, "Scripts" + extension));
            // rvpacker detects the RGSS version from System's extension. Marshal nil is enough for an isolated script export.
            File.WriteAllBytes(Path.Combine(data, "System" + extension), new byte[] { 4, 8, (byte)'0' });
        }
        var start = new ProcessStartInfo(helper)
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        if (script != null)
        {
            start.ArgumentList.Add(script);
            start.Environment["PYTHONIOENCODING"] = "utf-8";
            start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        }
        if (bgi) start.Environment["PYTHONPATH"] = common;
        string[] arguments = format switch
        {
            "TSC" => new[] { "--gsc-to-tsc", "--encoding", GscEncoding(), "--output", output, input },
            "RPY" => new[] { "-p", "1", "--", input },
            "TJS" => new[] { "tjs", input },
            "JSON" => new[] { "export", "-t", "artemis-asb", "-T", "custom", "--artemis-asb-no-format-lua", input, output },
            "RB" => new[] { "json", "generate", "-i", directory, "--comment-prefix", "# " },
            _ => new[] { input, "-e", "-c", BgiEncoding() }
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        if (format == "RB" && Encodings.ResourceEncodingOverride is { } rgssEncoding)
        {
            start.ArgumentList.Add("--read-encoding");
            start.ArgumentList.Add(rgssEncoding.WebName);
        }
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new NotSupportedException(unavailable);
        }
        catch (Win32Exception error) { throw new NotSupportedException(unavailable, error); }

        process.StandardInput.Close();
        var errors = MediaTools.ReadErrors(process);
        var log = new StringBuilder();
        bool decompiled = false;
        var stdout = format == "TJS" ? System.Threading.Tasks.Task.Run(() =>
        {
            using var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
            ResourceService.CopyBounded(process.StandardOutput.BaseStream, target, maxBytes);
        }) : WorkerSupervisor.ReadLinesAsync(process.StandardOutput, line =>
        {
            // unrpyc 2.x reports per-file errors in its summary even when it exits zero.
            if (line.StartsWith("> 1 file ", StringComparison.Ordinal) &&
                line.EndsWith(" successfully decompiled.", StringComparison.Ordinal)) decompiled = true;
            if (log.Length < 4096) log.AppendLine(line[..Math.Min(line.Length, 4096 - log.Length)]);
        }, maxCharacters: 64 << 10);
        var clock = Stopwatch.StartNew();
        using var parent = Process.GetCurrentProcess();
        long memoryLimit = WorkerSupervisor.ChildMemoryLimit;
        try
        {
            while (!process.WaitForExit(50))
            {
                cancellation.ThrowIfCancellationRequested();
                if (stdout.IsFaulted) stdout.GetAwaiter().GetResult();
                if (errors.IsFaulted) errors.GetAwaiter().GetResult();
                if (File.Exists(output) && new FileInfo(output).Length > maxBytes)
                    throw new InvalidDataException("Output limit exceeded.");
                if (clock.Elapsed.TotalMinutes >= 5) throw new IOException("Script conversion exceeded its time limit.");
                if (WorkerSupervisor.ResidentBytes(process) + WorkerSupervisor.ResidentBytes(parent) > memoryLimit)
                    throw new WorkerLimitException("Decoder memory limit exceeded.", "memory");
            }
            cancellation.ThrowIfCancellationRequested();
            stdout.GetAwaiter().GetResult();
            string detail = errors.GetAwaiter().GetResult().Trim();
            if (process.ExitCode != 0 || (format == "RPY" && !decompiled) || !File.Exists(output) ||
                (format != "RB" && new FileInfo(output).Length == 0))
                throw new InvalidDataException("Script conversion failed: " + (detail.Length == 0 ? log.ToString().Trim() : detail));
            ExtractionPath.RejectLinks(start.WorkingDirectory, output);
            using var source = File.OpenRead(output);
            ResourceService.CopyBounded(source, destination, maxBytes);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(2000);
        }
    }
}
