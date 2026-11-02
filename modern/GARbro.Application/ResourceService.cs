using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using GameRes;
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
    readonly ScopedOptionsProvider scoped;
    public ResourceLocation Location { get; }
    public ArcFile Archive { get; }
    public bool Physical => physical;
    public bool Synthetic => Archive?.Tag == "DAT/BOGUS";
    public ResourceSession(ResourceLocation location, IOptionsProvider options = null)
    {
        scoped = options as ScopedOptionsProvider;
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
                for (int depth = 0; depth <= Location.Chain.Length; depth++)
                {
                    var current = Location with { Archives = Location.Chain.Take(depth).ToArray(), Directory = "" };
                    scoped?.SetLocation(current);
                    VFS.FullPath = new[] { current.RootPath }.Concat(current.Chain).Append("").ToArray();
                    if (VFS.CurrentArchive?.Tag == "DAT/BOGUS" && depth < Location.Chain.Length)
                        throw new UnknownFormatException();
                }
                Archive = VFS.CurrentArchive ?? throw new UnknownFormatException();
            }
        }
        catch
        {
            VFS.PreserveNestedArchives = previousNestedMode;
            scope.Dispose();
            if (scope.MissingOptions != null) throw scope.MissingOptions;
            throw;
        }
    }

    public BrowserListing List()
    {
        if (physical)
        {
            var items = new DirectoryInfo(Location.RootPath).EnumerateFileSystemInfos().Select(f =>
                new BrowserEntry(f.Name, f.Name, f is DirectoryInfo ? "directory" : MediaInfo.IsVideoName(f.Name) ? "video" : FormatCatalog.Instance.GetTypeFromName(f.Name),
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
            else entries.Add(new(entry.Name, rest, MediaInfo.IsVideoName(entry.Name) ? "video" : entry.Type, entry.Size, false, id));
        }
        return new(Location with { Fingerprint = ArchiveFingerprint.Read(Location.RootPath) }, Synthetic ? "media" : Archive.Tag, false, entries.ToArray(), FormatIdentity.Of(Archive.Format));
    }

    public Stream OpenEntry(string name)
    {
        if (!physical) return Archive.OpenEntry(Find(name));
        string path = PhysicalPath(name); scoped?.SetLocation(new(path)); return File.OpenRead(path);
    }
    public Entry Find(string name) => Archive.Dir.Single(e => e.Name == name);
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
        try
        {
            VFS.FullPath = new[] { physical ? Location.RootPath : Path.GetDirectoryName(Location.RootPath) };
            VFS.Flush();
        }
        finally { VFS.PreserveNestedArchives = previousNestedMode; scope.Dispose(); }
    }
}

public static partial class ResourceService
{
    public static BrowserListing List(ResourceLocation location, IOptionsProvider options)
    {
        ArchiveService.Formats();
        using var session = new ResourceSession(location, options);
        return session.List();
    }

    public static ResourceFormatInfo[] Catalog()
    {
        ArchiveService.Formats();
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
        string forcedType = null, long offset = 0, IOptionsProvider options = null)
    {
        using var session = new ResourceSession(location, options);
        if (!session.Physical && (forcedType == "image" || forcedType == null && session.Find(name).Type == "image"))
        {
            using var decoder = session.Archive.OpenImage(session.Find(name));
            if ((ulong)decoder.Info.Width * decoder.Info.Height > 25000000) throw new InvalidDataException("Image is too large for a preview.");
            var image = decoder.Image;
            using var png = new MemoryStream(); ImageFormat.Png.Write(png, image);
            return new("image", name, $"{image.Width} × {image.Height} · {image.BPP} bit · ({image.OffsetX}, {image.OffsetY})", png.ToArray());
        }
        string scratch = WorkerTemporaryFiles.CreateDirectory(Path.GetTempPath());
        try
        {
            string path = Path.Combine(scratch, Path.GetFileName(name));
            using (var source = session.OpenEntry(name))
            using (var target = File.Create(path)) CopyBounded(source, target, MediaInfo.IsVideoName(name) ? 2L << 30 : 512L << 20);
            if (forcedType is not ("script" or "binary" or "image" or "audio"))
            {
                byte[] signature = new byte[(int)Math.Min(256, new FileInfo(path).Length)];
                using (var input = File.OpenRead(path)) input.ReadExactly(signature);
                if (forcedType == "video" || MediaInfo.HasVideoHeader(name, signature))
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
                    using var png = new MemoryStream();
                    ImageFormat.Png.Write(png, image);
                    return new("image", name, $"{image.Width} × {image.Height} · {image.BPP} bit · ({image.OffsetX}, {image.OffsetY})", png.ToArray());
                }
                using var audioInput = new BinaryStream(File.OpenRead(path), name);
                using var audio = AudioFormat.Read(audioInput);
                if (audio != null)
                    return new("audio", name, $"{audio.SourceFormat.ToUpperInvariant()} · {audio.Format.SamplesPerSecond} Hz · {audio.Format.Channels} ch · {audio.PcmSize / (double)Math.Max(1, audio.Format.AverageBytesPerSecond):F1} s");
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
            if (offset < 0 || offset > file.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            byte[] header = new byte[Math.Min(4, file.Length)]; file.ReadExactly(header);
            Encoding encoding = context.ResolveEncoding() ?? new UTF8Encoding(false, true);
            int bom = 0;
            if (header.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) { encoding = Encoding.UTF32; bom = 4; }
            else if (header.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, true); bom = 4; }
            else if (header.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = Encoding.Unicode; bom = 2; }
            else if (header.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = Encoding.BigEndianUnicode; bom = 2; }
            else if (header.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) { encoding = Encoding.UTF8; bom = 3; }
            file.Position = offset;
            byte[] data = new byte[Math.Min(65536, file.Length - offset)];
            file.ReadExactly(data);
            string text;
            try
            {
                int skip = offset == 0 ? Math.Min(bom, data.Length) : 0;
                text = encoding.GetString(data, skip, data.Length - skip);
            }
            catch (DecoderFallbackException) { text = Encodings.ResourceEncoding.GetString(data); }
            bool binary = forcedType == "binary" || (forcedType != "script" && text.Count(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) > Math.Max(1, text.Length / 20));
            if (binary)
            {
                var hex = new StringBuilder();
                for (int i = 0; i < data.Length; i += 16)
                {
                    var row = data.AsSpan(i, Math.Min(16, data.Length - i));
                    hex.Append((offset + i).ToString("X8")).Append("  ").Append(Convert.ToHexString(row)).Append("  ");
                    foreach (byte b in row) hex.Append(b >= 32 && b < 127 ? (char)b : '.');
                    hex.AppendLine();
                }
                text = hex.ToString();
            }
            return new(binary ? "binary" : "text", name, text, Offset: offset, More: offset + data.Length < file.Length);
        }
        finally { System.IO.Directory.Delete(scratch, true); }
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
