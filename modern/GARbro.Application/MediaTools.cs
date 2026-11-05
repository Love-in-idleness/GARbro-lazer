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
    public const string MemoryVariable = "GARBRO_MEDIA_MEMORY_LIMIT";
    public const string Formats = "mpeg,mpegts,mpegvideo,m4v,mov,mp4,m4a,3gp,3g2,mj2,matroska,webm,avi,asf,ogg,flv,ivf,wav,aiff,mp3,aac,flac";
    public static string Resolve(string tool)
    {
        string filename = tool + (OperatingSystem.IsWindows() ? ".exe" : "");
        string bundled = Path.Combine(AppContext.BaseDirectory, "media", filename);
        if (File.Exists(bundled)) return bundled;
        // An explicit PATH (including CI's pinned helper) takes precedence.
        // Finder may not inherit the interactive shell's Homebrew PATH.
        if (OperatingSystem.IsMacOS())
        {
            foreach(string directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
                if(!string.IsNullOrEmpty(directory) && File.Exists(Path.Combine(directory,filename)))return Path.Combine(directory,filename);
            foreach (string directory in new[] { "/opt/homebrew/bin", "/usr/local/bin" })
                if (File.Exists(Path.Combine(directory, filename))) return Path.Combine(directory, filename);
        }
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
        catch (Win32Exception error) { throw new IOException("FFmpeg media tools are unavailable.", error); }
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
            long limit = long.TryParse(Environment.GetEnvironmentVariable(MemoryVariable), out long configured) && configured >= 64L << 20 ? configured : 1L << 30;
            using var parent = Process.GetCurrentProcess();
            var clock = Stopwatch.StartNew();
            while (!process.WaitForExit(50))
            {
                if (output.IsFaulted) output.GetAwaiter().GetResult();
                if (clock.ElapsedMilliseconds > timeout) throw new IOException("Video probing exceeded its time limit.");
                if (WorkerSupervisor.ResidentBytes(process) + WorkerSupervisor.ResidentBytes(parent) > limit)
                    throw new WorkerLimitException("Video decoder memory limit exceeded.", "memory");
            }
            var bytes = output.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new InvalidDataException("The video decoder could not read this media.");
            errors.GetAwaiter().GetResult(); return bytes;
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
    public static long Convert(string input, string output, string format, long maxBytes)
    {
        string[] encoding = format switch
        {
            "WAV" => new[]{"-map","0:a:0","-vn","-c:a","pcm_s32le","-f","wav"},
            "FLAC" => new[]{"-map","0:a:0","-vn","-c:a","flac","-f","flac"},
            "M4A" => new[]{"-map","0:a:0","-vn","-c:a","aac","-b:a","192k","-movflags","frag_keyframe+empty_moov","-f","mp4"},
            "MP4" => new[]{"-map","0:V:0","-map","0:a:0?","-c:v","mpeg4","-q:v","3","-vf","pad=ceil(iw/2)*2:ceil(ih/2)*2","-pix_fmt","yuv420p","-c:a","aac","-b:a","192k","-movflags","frag_keyframe+empty_moov","-f","mp4"},
            "MKV" => new[]{"-map","0:V:0","-map","0:a:0?","-c","copy","-f","matroska"},
            _ => throw new ArgumentException("Unsupported media output format.")
        };
        if(format=="MKV")return ConvertSeekable(input,output,encoding,maxBytes);
        using var process = Start("ffmpeg",new[]{"-xerror"}.Concat(Input(input)).Concat(encoding).Concat(new[]{"pipe:1"}));
        var errors = ReadErrors(process);
        var copy = Task.Run(() =>
        {
            using var target = new FileStream(output,FileMode.CreateNew,FileAccess.Write);
            return ResourceService.CopyBounded(process.StandardOutput.BaseStream,target,maxBytes);
        });
        try
        {
            long limit=long.TryParse(Environment.GetEnvironmentVariable(MemoryVariable),out long configured) && configured>=64L<<20?configured:1L<<30;
            using var parent=Process.GetCurrentProcess();var clock=Stopwatch.StartNew();
            while(!process.WaitForExit(50))
            {
                if(copy.IsFaulted)copy.GetAwaiter().GetResult();
                if(clock.ElapsedMilliseconds>300000)throw new IOException("Media conversion exceeded its time limit.");
                if(WorkerSupervisor.ResidentBytes(process)+WorkerSupervisor.ResidentBytes(parent)>limit)
                    throw new WorkerLimitException("Media conversion memory limit exceeded.","memory");
            }
            long bytes=copy.GetAwaiter().GetResult();errors.GetAwaiter().GetResult();
            if(process.ExitCode!=0 || bytes==0)throw new InvalidDataException("The media could not be converted to the selected format.");
            if(format=="WAV")
            {
                // FFmpeg writes unknown RIFF/data sizes to a pipe; finish the seekable file.
                using var wave=new FileStream(output,FileMode.Open,FileAccess.ReadWrite);
                using var reader=new BinaryReader(wave,System.Text.Encoding.ASCII,true);
                using var writer=new BinaryWriter(wave,System.Text.Encoding.ASCII,true);
                wave.Position=4;writer.Write(checked((uint)(wave.Length-8)));
                wave.Position=12;
                while(wave.Position<=wave.Length-8)
                {
                    string chunk=System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));uint length=reader.ReadUInt32();
                    if(chunk=="data") {wave.Position-=4;writer.Write(checked((uint)(wave.Length-wave.Position-4)));break;}
                    if(length>wave.Length-wave.Position)throw new InvalidDataException("Invalid converted WAV header.");
                    wave.Position+=length+(length&1);
                }
            }
            return bytes;
        }
        finally
        {
            if(!process.HasExited)process.Kill(true);
            process.WaitForExit(2000);
            try {copy.GetAwaiter().GetResult();} catch { }
        }
    }
    static long ConvertSeekable(string input,string output,string[] encoding,long maxBytes)
    {
        // Matroska needs a seekable output to write duration and seek indexes. The
        // muxer's limit stops at the first packet beyond the budget; reject that
        // output instead of accepting FFmpeg's successful-but-truncated result.
        if(maxBytes<0)throw new InvalidDataException("Output limit exceeded.");
        long fileLimit=maxBytes==long.MaxValue?long.MaxValue:maxBytes+1;
        using var process=Start("ffmpeg",new[]{"-xerror"}.Concat(Input(input)).Concat(encoding).Concat(new[]{"-fs",fileLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),"-n",output}));
        var errors=ReadErrors(process);var clock=Stopwatch.StartNew();using var parent=Process.GetCurrentProcess();
        long memory=long.TryParse(Environment.GetEnvironmentVariable(MemoryVariable),out long configured) && configured>=64L<<20?configured:1L<<30;
        try
        {
            while(!process.WaitForExit(50))
            {
                if(File.Exists(output) && new FileInfo(output).Length>maxBytes)throw new InvalidDataException("Output limit exceeded.");
                if(clock.ElapsedMilliseconds>300000)throw new IOException("Media conversion exceeded its time limit.");
                if(WorkerSupervisor.ResidentBytes(process)+WorkerSupervisor.ResidentBytes(parent)>memory)
                    throw new WorkerLimitException("Media conversion memory limit exceeded.","memory");
            }
            errors.GetAwaiter().GetResult();long bytes=File.Exists(output)?new FileInfo(output).Length:0;
            if(bytes>maxBytes)throw new InvalidDataException("Output limit exceeded.");
            if(process.ExitCode!=0 || bytes==0)throw new InvalidDataException("The media could not be converted to the selected format.");
            return bytes;
        }
        finally {if(!process.HasExited)process.Kill(true);process.WaitForExit(2000);}
    }
    static string Metadata(string path)
    {
        var args = new[] { "-v", "error", "-max_alloc", "268435456", "-protocol_whitelist", "file,pipe", "-format_whitelist", Formats,
            "-print_format", "json", "-show_streams", "-show_format", path };
        return Encoding.UTF8.GetString(Run("ffprobe",args,1L<<20));
    }
    public static VideoInfo Probe(string path) => MediaInfo.Parse(Metadata(path));
    public static AudioInfo ProbeAudio(string path) => AudioMetadata.Parse(Metadata(path));
    public static byte[] Poster(string path, VideoInfo info)
    {
        var size = MediaInfo.Fit(info.Width, info.Height);
        return Run("ffmpeg", Input(path).Concat(new[] { "-map", "0:V:0", "-frames:v", "1", "-vf", $"scale={size.Width}:{size.Height}",
            "-f", "image2pipe", "-c:v", "bmp", "pipe:1" }), 8L << 20);
    }
}
