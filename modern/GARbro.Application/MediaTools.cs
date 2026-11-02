using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GARbro.Media;

namespace GARbro.Application;

/// <summary>External codec processes; no native video decoder is loaded in the GUI.</summary>
public static class MediaTools
{
    public const string Formats = "mpeg,mpegts,mpegvideo,m4v,mov,mp4,m4a,3gp,3g2,mj2,matroska,webm,avi,asf,ogg,flv,ivf";
    public static string Resolve(string tool)
    {
        string filename = tool + (OperatingSystem.IsWindows() ? ".exe" : "");
        string bundled = Path.Combine(AppContext.BaseDirectory, "media", filename);
        if (File.Exists(bundled)) return bundled;
        // Finder does not inherit an interactive shell's Homebrew PATH.
        if (OperatingSystem.IsMacOS())
            foreach (string directory in new[] { "/opt/homebrew/bin", "/usr/local/bin" })
                if (File.Exists(Path.Combine(directory, filename))) return Path.Combine(directory, filename);
        return filename;
    }
    public static string[] Input(string path) => new[] { "-v", "error", "-nostdin", "-threads", "2", "-filter_threads", "1",
        "-max_alloc", "268435456", "-protocol_whitelist", "file,pipe", "-format_whitelist", Formats, "-i", path };
    public static Process Start(string tool, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Resolve(tool)) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        try { return Process.Start(start) ?? throw new IOException("Cannot start the video decoder."); }
        catch (Win32Exception error) { throw new IOException("FFmpeg video tools are unavailable.", error); }
    }
    public static async Task<string> ReadErrors(Process process)
    {
        var text = new StringBuilder(); char[] buffer = new char[1024]; int count;
        while ((count = await process.StandardError.ReadAsync(buffer)) > 0)
            if (text.Length < 16384) text.Append(buffer, 0, Math.Min(count, 16384 - text.Length));
        return text.ToString();
    }
    public static byte[] Run(string tool, IEnumerable<string> arguments, long maxBytes, int timeout = 30000)
    {
        using var process = Start(tool, arguments);
        var errors = ReadErrors(process);
        var output = Task.Run(() =>
        {
            using var result = new MemoryStream(); ResourceService.CopyBounded(process.StandardOutput.BaseStream, result, maxBytes);
            return result.ToArray();
        });
        try
        {
            if (!process.WaitForExit(timeout)) throw new IOException("Video probing exceeded its time limit.");
            var bytes = output.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new InvalidDataException("The video decoder could not read this media.");
            errors.GetAwaiter().GetResult(); return bytes;
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
    public static VideoInfo Probe(string path)
    {
        var args = new[] { "-v", "error", "-max_alloc", "268435456", "-protocol_whitelist", "file,pipe", "-format_whitelist", Formats,
            "-print_format", "json", "-show_streams", "-show_format", path };
        return MediaInfo.Parse(Encoding.UTF8.GetString(Run("ffprobe", args, 1L << 20)));
    }
    public static byte[] Poster(string path, VideoInfo info)
    {
        var size = MediaInfo.Fit(info.Width, info.Height);
        return Run("ffmpeg", Input(path).Concat(new[] { "-map", "0:v:0", "-frames:v", "1", "-vf", $"scale={size.Width}:{size.Height}",
            "-f", "image2pipe", "-c:v", "bmp", "pipe:1" }), 8L << 20);
    }
}
