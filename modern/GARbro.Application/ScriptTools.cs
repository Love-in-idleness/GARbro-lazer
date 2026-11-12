using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using GameRes;

namespace GARbro.Application;

internal static class ScriptTools
{
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

    internal static void Convert(string input, Stream destination, string format, long maxBytes, CancellationToken cancellation)
    {
        if (OperatingSystem.IsAndroid()) throw new NotSupportedException("Script conversion requires a desktop helper.");
        cancellation.ThrowIfCancellationRequested();
        bool gsc = format == "TSC";
        string name = gsc ? "liarsofttool" : "unrpyc";
        string variable = gsc ? "GARBRO_LIARSOFTTOOL" : "GARBRO_UNRPYC";
        string helper = Environment.GetEnvironmentVariable(variable) ?? FindProgram(name);
        string unavailable = $"Script converter unavailable: {name} ({variable})";
        if (string.IsNullOrEmpty(helper) || !Path.IsPathFullyQualified(helper) || !File.Exists(helper))
            throw new NotSupportedException(unavailable);

        string script = !gsc && Path.GetExtension(helper).Equals(".py", StringComparison.OrdinalIgnoreCase) ? helper : null;
        if (script != null)
            helper = FindProgram(OperatingSystem.IsWindows() ? "python" : "python3") ?? throw new NotSupportedException(unavailable);
        string output = Path.ChangeExtension(input, format.ToLowerInvariant());
        var start = new ProcessStartInfo(helper)
        {
            WorkingDirectory = Path.GetDirectoryName(input), UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        if (script != null) start.ArgumentList.Add(script);
        if (!gsc)
        {
            start.Environment["PYTHONIOENCODING"] = "utf-8";
            start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        }
        string[] arguments = gsc
            ? new[] { "--gsc-to-tsc", "--encoding", GscEncoding(), "--output", output, input }
            : new[] { "-p", "1", "--", input };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
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
        var stdout = WorkerSupervisor.ReadLinesAsync(process.StandardOutput, line =>
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
            if (process.ExitCode != 0 || (!gsc && !decompiled) || !File.Exists(output) || new FileInfo(output).Length == 0)
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
