using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using GameRes;
using GARbro.Domain;
using GARbro.Media;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace GARbro.Application;

/// <summary>Worker-only resource access. All VFS state lives in a disposable process.</summary>
public sealed class ResourceSession : IDisposable
{
    readonly ArchiveService.DecoderScope scope;
    readonly bool physical;
    readonly bool previousNestedMode;
    readonly ArcFile standalone;
    readonly ILocatedOptionsProvider scoped;
    Dictionary<string, Entry[]> index;
    internal bool Retained;
    public ResourceLocation Location { get; internal set; }
    public ArcFile Archive { get; }
    public bool Physical => physical;
    public bool Synthetic => Archive?.Tag == "DAT/BOGUS";
    public ResourceSession(ResourceLocation location, IOptionsProvider options = null, bool allowPlainFile = false, bool forcePlainFile = false)
    {
        ArchiveService.EnsureFormats();
        scoped = options as ILocatedOptionsProvider;
        Location = location with { RootPath = Path.GetFullPath(location.RootPath) };
        if (Location.Chain.Length > 64) throw new InvalidDataException("Archive nesting exceeds 64 levels.");
        physical = System.IO.Directory.Exists(Location.RootPath);
        if (!physical && Location.Fingerprint != null && ArchiveFingerprint.Read(Location.RootPath) != Location.Fingerprint)
            throw new IOException("Archive changed; reopen it before continuing.");
        string root = physical ? Location.RootPath : Path.GetDirectoryName(Location.RootPath);
        scoped?.SetLocation(Location);
        scope = new ArchiveService.DecoderScope(root, options);
        previousNestedMode = VFS.PreserveNestedArchives;
        VFS.PreserveNestedArchives = true;
        try
        {
            VFS.FullPath = new[] { root };
            VFS.Flush();
            if (!physical)
            {
                ArcFile PlainFile()
                {
                    if(new FileInfo(Location.RootPath).Length>uint.MaxValue)throw new InvalidDataException("Resource exceeds the size limit.");
                    return new GameRes.Formats.WrapSingleFileArchive(new ArcView(Location.RootPath),Path.GetFileName(Location.RootPath));
                }
                if(allowPlainFile && Location.Chain.Length==0 && (forcePlainFile || MediaInfo.IsAudioName(Location.RootPath) || MediaInfo.IsVideoName(Location.RootPath)))standalone=PlainFile();
                for (int depth = 0; standalone==null && depth <= Location.Chain.Length; depth++)
                {
                    var current = Location with { Archives = Location.Chain.Take(depth).ToArray(), Directory = "" };
                    scoped?.SetLocation(current);
                    try {VFS.FullPath = new[] { current.RootPath }.Concat(current.Chain).Append("").ToArray();}
                    catch(UnknownFormatException)when(depth==0 && scope.MissingOptions==null && FormatCatalog.Instance.LastError is not ResourceParametersRequiredException)
                    {
                        ArchiveService.CheckWrappedXp3(current.RootPath);
                        if (!allowPlainFile || Location.Chain.Length != 0) throw;
                        VFS.FullPath=new[]{root};VFS.Flush();
                        standalone=PlainFile();break;
                    }
                    if (VFS.CurrentArchive?.Tag == "DAT/BOGUS" && depth < Location.Chain.Length)
                        throw new UnknownFormatException();
                }
                Archive = standalone ?? VFS.CurrentArchive ?? throw new UnknownFormatException();
            }
        }
        catch
        {
            VFS.PreserveNestedArchives = previousNestedMode;
            standalone?.Dispose();scope.Dispose();
            if (scope.MissingOptions != null) throw scope.MissingOptions;
            throw;
        }
    }

    public BrowserListing List()
    {
        if (physical)
        {
            var items = new DirectoryInfo(Location.RootPath).EnumerateFileSystemInfos().Select(f =>
                new BrowserEntry(f.Name, f.Name, f is DirectoryInfo ? "directory" : LightweightResources.Type(f.Name, FormatCatalog.Instance.GetTypeFromName(f.Name)),
                    f is FileInfo file ? file.Length : 0, f is DirectoryInfo)).ToArray();
            return new(Location, "directory", true, items);
        }
        string prefix = Location.Directory.Replace('\\', '/');
        var entries = new List<BrowserEntry>();
        var dirs = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (var entry in Archive.Dir)
        {
            string name = entry.Name.Replace('\\', '/');
            int id = index++;
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string rest = name[prefix.Length..];
            if (rest.Length == 0) continue;
            int slash = rest.IndexOf('/');
            if (slash >= 0)
            {
                string directory = prefix + rest[..slash];
                if (dirs.Add(directory)) entries.Add(new(directory, rest[..slash], "directory", 0, true));
            }
            else entries.Add(new(entry.Name, rest, LightweightResources.Type(entry.Name, entry.Type ?? ""), entry.Size, false, id));
        }
        return new(Location with { Fingerprint = ArchiveFingerprint.Read(Location.RootPath) }, Synthetic ? "media" : Archive.Tag, false, entries.ToArray(), FormatIdentity.Of(Archive.Format));
    }

    public Stream OpenEntry(string name)
    {
        if (!physical) return Archive.OpenEntry(Find(name));
        string path = PhysicalPath(name); scoped?.SetLocation(new(path)); return File.OpenRead(path);
    }
    public Entry Find(string name)
    {
        index ??= Archive.Dir.GroupBy(e => e.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        return index.TryGetValue(name, out var entries) ? entries.Single() : throw new FileNotFoundException("Resource does not exist.", name);
    }
    public string PhysicalPath(string name)
    {
        string full = Path.GetFullPath(Path.Combine(Location.RootPath, name));
        string relative = Path.GetRelativePath(Location.RootPath, full);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar)) throw new IOException("Resource leaves its input directory.");
        return full;
    }
    public ResourceLocation Child(string name) => Synthetic ? throw new UnknownFormatException() : physical ? new(PhysicalPath(name)) : Location.EnterArchive(name);

    public void Dispose()
    {
        if (Retained) return;
        try
        {
            VFS.FullPath = new[] { physical ? Location.RootPath : Path.GetDirectoryName(Location.RootPath) };
            VFS.Flush();
        }
        finally { standalone?.Dispose(); VFS.PreserveNestedArchives = previousNestedMode; scope.Dispose(); }
    }
}

public static partial class ResourceService
{
    public static BrowserListing List(ResourceLocation location, IOptionsProvider options)
    {
        ArchiveService.EnsureFormats();
        using var session = BrowserDecoderSession.Open(location, options);
        return session.List();
    }

    public static ResourceFormatInfo[] Catalog()
    {
        ArchiveService.EnsureFormats();
        return FormatCatalog.Instance.Formats.Select(format => new ResourceFormatInfo(FormatIdentity.Of(format), format.Tag,
            format is ArchiveFormat ? "archive" : format is ImageFormat ? "image" : format is AudioFormat ? "audio" : "script", format.Description, format.Extensions.ToArray(), format.CanWrite,
            (format.Settings ?? Array.Empty<IResourceSetting>()).Select(s =>
            {
                object value = s.Value;
                return new FormatSettingInfo(s.Name, s.Text, s.Description, value is Encoding ? "encoding" :
                    value switch { bool => "bool", byte => "byte", sbyte => "sbyte", short => "short", ushort => "ushort",
                        int => "int", uint => "uint", long => "long", ulong => "ulong", float => "float", double => "double", decimal => "decimal", _ => "string" },
                    JsonSerializer.SerializeToElement(value is Encoding e ? (object)e.CodePage : value),
                    s is FixedGaugeSetting gauge ? gauge.ValuesSet.ToArray() : null);
            }).ToArray(), JsonConvert.SerializeObject(format.GetDefaultOptions(), Formatting.Indented), NamedSchemes(format)))
            .OrderBy(f => f.Type).ThenBy(f => f.Tag).ToArray();
    }

    static string[] NamedSchemes(IResource format) => format switch
    {
        GameRes.Formats.KiriKiri.Xp3Opener => new[] { "none" }.Concat(GameRes.Formats.KiriKiri.Xp3Opener.KnownSchemes.Keys).Distinct().Order().ToArray(),
        GameRes.Formats.NitroPlus.NpaOpener => GameRes.Formats.NitroPlus.NpaOpener.KnownSchemes.Keys.Order().ToArray(),
        GameRes.Formats.YuRis.YpfOpener => new[] { "none" }.Concat(GameRes.Formats.YuRis.YpfOpener.KnownSchemes.Keys).Distinct().Order().ToArray(),
        _ => FormatParameters.Names(format)
    };

    public static ResourcePreview Preview(ResourceLocation location, string name, DecoderContext context,
        string forcedType = null, long offset = 0, IOptionsProvider options = null, string previewMode = "auto", string previewEncoding = null, string output = null, int width = 0, int height = 0)
    {
        using var session = BrowserDecoderSession.Open(location, options);
        if (previewMode == "hex" || forcedType == null && ScriptOutputFormats.UsesBinaryPreview(name))
        {
            using var input = session.OpenEntry(name);
            return LightweightResources.ReadPage(input, name, offset, true, previewEncoding ?? context.TextEncoding);
        }
        if (!session.Physical && (forcedType == "image" || forcedType == null && session.Find(name).Type == "image"))
        {
            using var decoder = session.Archive.OpenImage(session.Find(name));
            if ((ulong)decoder.Info.Width * decoder.Info.Height > 25000000) throw new InvalidDataException("Image is too large for a preview.");
            var image = decoder.Image;
            return ImagePreview(image, name, output, width, height);
        }
        string scratch = WorkerTemporaryFiles.CreateDirectory(Path.GetTempPath());
        try
        {
            string path = session.Physical ? session.PhysicalPath(name) : BrowserDecoderSession.Materialize(session, name, scratch);
            if (forcedType == "binary" || forcedType == null && LightweightResources.IsTextName(name) && LightweightResources.IsPlainTextFile(path))
            {
                using var input = File.OpenRead(path);
                return LightweightResources.ReadPage(input, name, offset, forcedType == "binary", previewEncoding ?? context.TextEncoding);
            }
            if(forcedType==null && MediaInfo.IsAudioName(name))forcedType="audio";
            if (forcedType is not ("script" or "binary" or "image" or "audio"))
            {
                byte[] signature = new byte[(int)Math.Min(256, new FileInfo(path).Length)];
                using (var input = File.OpenRead(path)) input.ReadExactly(signature);
                if (forcedType == "video" || MediaInfo.IsVideoName(name) || MediaInfo.HasVideoHeader(name, signature))
                {
                    var info = MediaTools.Probe(path);
                    return new("video", name, $"{info.Width} × {info.Height} · {info.VideoCodec.ToUpperInvariant()} · {TimeSpan.FromSeconds(info.Duration):hh\\:mm\\:ss}", MediaTools.Poster(path, info));
                }
            }
            if (forcedType != "script" && forcedType != "binary")
            {
                using var imageInput = new BinaryStream(File.OpenRead(path), name);
                var candidate = ImageFormat.FindFormat(imageInput);
                if (candidate != null && forcedType != "audio")
                {
                    if ((ulong)candidate.Item2.Width * candidate.Item2.Height > 25000000) throw new InvalidDataException("Image is too large for a preview.");
                    imageInput.Position = 0;
                    var image = candidate.Item1.Read(imageInput, candidate.Item2);
                    return ImagePreview(image, name, output, width, height);
                }
                using var audioInput = new BinaryStream(File.OpenRead(path), name);
                using var audio = AudioFormat.Read(audioInput);
                if (audio != null)
                    return new("audio", name, $"{audio.SourceFormat.ToUpperInvariant()} · {audio.Format.SamplesPerSecond} Hz · {audio.Format.Channels} ch · {audio.PcmSize / (double)Math.Max(1, audio.Format.AverageBytesPerSecond):F1} s");
                if(forcedType=="audio")
                {
                    var info=MediaTools.ProbeAudio(path);
                    return new("audio",name,$"{info.Codec.ToUpperInvariant()} · {info.SampleRate} Hz · {info.Channels} ch · {info.Duration:F1} s");
                }
            }
            if (forcedType != "binary")
            {
                using var scriptInput = new BinaryStream(File.OpenRead(path), name);
                var script = ScriptFormat.FindFormat(scriptInput);
                if (script != null)
                {
                    scriptInput.Position = 0;
                    using var converted = script.ConvertFrom(scriptInput);
                    string decoded = Path.Combine(scratch, "decoded.txt");
                    using (var destination = File.Create(decoded)) CopyBounded(converted, destination, 512L << 20);
                    path = decoded;
                    forcedType = "script";
                }
            }
            using var file = File.OpenRead(path);
            return LightweightResources.ReadPage(file, name, offset, forcedType == "binary", previewEncoding ?? context.TextEncoding, forcedType == "script");
        }
        finally { System.IO.Directory.Delete(scratch, true); }
    }

    static ResourcePreview ImagePreview(ImageData image, string name, string output, int width, int height)
    {
        string info = $"{image.Width} × {image.Height} · {image.BPP} bit · ({image.OffsetX}, {image.OffsetY})";
        if (output == null)
        {
            using var png = new MemoryStream(); ImageFormat.Png.Write(png,image);
            return new("image",name,info,png.ToArray());
        }
        if (!File.Exists(output) || new FileInfo(output).Length != 0) throw new IOException("Invalid preview output.");
        byte[] pixels = image.Bitmap.ToBgra32();
        int sourceWidth=(int)image.Width,sourceHeight=(int)image.Height;
        double scale=width>0&&height>0?Math.Min(1,Math.Min((double)width/sourceWidth,(double)height/sourceHeight)):1;
        int w=Math.Max(1,(int)(sourceWidth*scale)),h=Math.Max(1,(int)(sourceHeight*scale));
        if((long)w*h*4>100L<<20)throw new InvalidDataException("Image is too large for a preview.");
        if(w!=sourceWidth || h!=sourceHeight)
        {
            byte[] resized=new byte[checked(w*h*4)];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                int src=((int)((long)y*sourceHeight/h)*sourceWidth+(int)((long)x*sourceWidth/w))*4;
                Buffer.BlockCopy(pixels,src,resized,(y*w+x)*4,4);
            }
            pixels=resized;
        }
        File.WriteAllBytes(output,pixels);
        return new("image",name,info,Pixels:new(output,pixels.Length,w,h));
    }

    internal static long CopyBounded(Stream source, Stream target, long limit)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer)) != 0)
        {
            total = checked(total + read);
            if (total > limit) throw new InvalidDataException("Resource exceeds the size limit.");
            target.Write(buffer, 0, read);
        }
        return total;
    }
}
