using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameRes;
using GameRes.Formats.KiriKiri;
using GameRes.Formats.NitroPlus;
using GameRes.Formats.YuRis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GARbro.Application
{
    public enum OverwritePolicy
    {
        Fail,
        Skip,
        Replace
    }

    public sealed record ExtractionLimits(long MaxEntryBytes = 2L << 30, long MaxTotalBytes = 16L << 30, double MaxExpansionRatio = 10000, int MaxEntries = 262144);
    public sealed record ExtractionProgress(int Completed, int Total, string Entry, long Bytes, string OutputPath = null);
    public sealed record ExtractedFile(string Path, long Size, string Status);
    public sealed record FormatInfo(string Tag, string Type, string Description, bool CanWrite, string Class, string Assembly);
    public sealed record PreviewResult(string Kind, string Title, string Text, byte[] Image);
    public sealed record ArchiveEntryInfo(string Name, long Offset, uint Size, string Type);
    public sealed class MissingOptionsException : ResourceParametersRequiredException
    {
        public string FormatId { get; set; }
        public string OptionsJson { get; set; }
        public string[] CompletedPaths { get; set; }
        public string ContextKey { get; set; }
        public MissingOptionsException(string text) : base(text)
        {
        }
    }

    public interface IOptionsProvider
    {
        ResourceOptions GetOptions(IResource format, string notice);
    }

    public sealed class JsonOptionsProvider : IOptionsProvider
    {
        readonly JObject options;
        readonly string scheme;
        public JsonOptionsProvider(string json = null, string scheme = null)
        {
            options = json == null ? null : JObject.Parse(json);
            this.scheme = scheme ?? options?["$scheme"]?.Value<string>();
            options?.Remove("$scheme");
            if (options != null && options.Descendants().OfType<JProperty>().Any(p => p.Name is "$type" or "$ref" or "$id"))
                throw new ArgumentException("Resource options cannot contain type or reference metadata.");
        }

        public ResourceOptions GetOptions(IResource format, string notice)
        {
            var result = format.GetDefaultOptions();
            if (result == null)
                throw new MissingOptionsException(format.Tag + ": " + notice);
            if (options != null)
            {
                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.None,
                    MissingMemberHandling = MissingMemberHandling.Error
                };
                JsonConvert.PopulateObject(options.ToString(), result, settings);
            }

            if (scheme == "none" && result is not Xp3Options && result is not YpfOptions && options == null)
                throw new MissingOptionsException(format.Tag + ": " + notice + " Supply --options.");
            if (scheme != null && (scheme != "none" || result is Xp3Options || result is YpfOptions))
            {
                if (result is Xp3Options xp3)
                    xp3.Scheme = scheme == "none" ? Xp3Opener.GetScheme(null) : Xp3Opener.KnownSchemes.TryGetValue(scheme, out var cipher) ? cipher : throw new MissingOptionsException("Unknown XP3 scheme: " + scheme);
                else if (result is NpaOptions npa)
                    npa.Scheme = NpaOpener.KnownSchemes.TryGetValue(scheme, out var cipher) ? cipher : throw new MissingOptionsException("Unknown NPA scheme: " + scheme);
                else if (result is YpfOptions ypf)
                    ypf.Scheme = scheme;
                else
                    FormatParameters.Apply(format, result, scheme);
            }

            if (options == null && scheme == null && result is not YpfOptions)
                throw new MissingOptionsException(format.Tag + ": " + notice + " Supply --options or --scheme.");
            return result;
        }
    }

    public static class ArchiveService
    {
        internal static readonly object Gate = new();
        static bool bundledSchemesLoaded;
        static SchemeImportReport bundledSchemes;
        public static SchemeImportReport BundledSchemes
        {
            get
            {
                EnsureFormats();
                return bundledSchemes;
            }
        }

        internal static void EnsureFormats()
        {
            // Keep the provider in the dependency graph even when discovered through MEF.
            _ = typeof(GameRes.Formats.Liar.XflOpener).Assembly;
            lock (Gate)
            {
                if (!bundledSchemesLoaded)
                {
                    var path = System.IO.Path.Combine(FormatCatalog.Instance.DataDirectory, "Formats.json.gz");
                    if (System.IO.File.Exists(path))
                    {
                        bundledSchemes = ReadSchemesFile(path);
                        if (bundledSchemes.Failed.Count > 0)
                            throw new InvalidDataException("Bundled scheme import failed for: " + string.Join(", ", bundledSchemes.Failed.Keys));
                    }

                    bundledSchemesLoaded = true;
                }

            }
        }

        public static IReadOnlyList<FormatInfo> Formats()
        {
            EnsureFormats();
            lock(Gate)return FormatCatalog.Instance.Formats.Select(f=>new FormatInfo(f.Tag,f.Type,f.Description,f.CanWrite,f.GetType().FullName,f.GetType().Assembly.GetName().Name)).OrderBy(f=>f.Type).ThenBy(f=>f.Tag).ToArray();
        }

        public static SchemeImportReport LoadSchemes(string path)
        {
            EnsureFormats();
            lock (Gate)
                return ReadSchemesFile(path);
        }

        static SchemeImportReport ReadSchemesFile(string path)
        {
            using var raw = File.OpenRead(path);
            using Stream input = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? new System.IO.Compression.GZipStream(raw, System.IO.Compression.CompressionMode.Decompress, true) : raw;
            return FormatCatalog.Instance.DeserializePortableSchemeJson(input);
        }

        public static ArchiveSession Open(string path, string formatTag = null, IOptionsProvider options = null)
        {
            EnsureFormats();
            path = Path.GetFullPath(path);
            lock (Gate)
            {
                using var scope = new DecoderScope(Path.GetDirectoryName(path), options);
                ArcFile archive;
                if (formatTag == null)
                {
                    FormatCatalog.Instance.LastError = null;
                    archive = ArcFile.TryOpen(path);
                }
                else
                {
                    var format = FormatCatalog.Instance.ArcFormats.SingleOrDefault(f => f.Tag.Equals(formatTag, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Unknown archive format: " + formatTag);
                    var view = new ArcView(path);
                    try
                    {
                        archive = format.TryOpen(view);
                        if (archive == null)
                            view.Dispose();
                    }
                    catch
                    {
                        view.Dispose();
                        throw;
                    }
                }

                if (archive == null)
                {
                    if (scope.MissingOptions != null)
                        throw scope.MissingOptions;
                    CheckWrappedXp3(path);
                    throw new UnknownFormatException(FormatCatalog.Instance.LastError);
                }

                return new ArchiveSession(archive, path, options);
            }
        }

        internal static void CheckWrappedXp3(string path)
        {
            if (!Path.GetExtension(path).Equals(".xp3", StringComparison.OrdinalIgnoreCase))
                return;
            using var input = File.OpenRead(path);
            Span<byte> header = stackalloc byte[16];
            // The Fate patch and voice boxes share this encrypted QUICKBOX marker and 48-byte footer.
            if (input.Length >= header.Length + 48 && input.ReadAtLeast(header, header.Length, false) == header.Length &&
                BinaryPrimitives.ReadUInt32LittleEndian(header) == input.Length - 48 &&
                BinaryPrimitives.ReadUInt64LittleEndian(header[4..]) == 0xcdd13adc5430d915UL &&
                BinaryPrimitives.ReadUInt32LittleEndian(header[12..]) == 4)
                throw new WrappedXp3Exception();
        }

        sealed class WrappedXp3Exception : UnknownFormatException
        {
            public override string Message =>
                "This XP3 is inside a MoleBox package. Use demoleition VS on a copy of FateFD.exe and the package, then open the extracted XP3.";
        }

        public static object ProbeResource(string path,IOptionsProvider options=null)
        {
            path = Path.GetFullPath(path);
            if(GARbro.Media.MediaInfo.IsVideoName(path))
            {
                var video=MediaTools.Probe(path);
                return new {Input=path,Type="video",Format=video.VideoCodec,video.Width,video.Height,video.Duration,video.FrameRate,video.HasAudio,video.AudioCodec};
            }
            EnsureFormats();
            lock (Gate)
            {
                using var scope = new DecoderScope(Path.GetDirectoryName(path), options);
                using var input = BinaryStream.FromFile(path);
                var image = ImageFormat.FindFormat(input);
                if (image != null)
                    return new
                    {
                        Input = path,
                        Type = "image",
                        Format = image.Item1.Tag,
                        Width = image.Item2.Width,
                        Height = image.Item2.Height,
                        BitsPerPixel = image.Item2.BPP
                    };
                input.Position = 0;
                using var audio = AudioFormat.Read(input);
                if (audio != null)
                    return new
                    {
                        Input = path,
                        Type = "audio",
                        Format = audio.SourceFormat,
                        Channels = audio.Format.Channels,
                        SampleRate = audio.Format.SamplesPerSecond
                    };
                if(GARbro.Media.MediaInfo.IsAudioName(path))
                {
                    var info=MediaTools.ProbeAudio(path);
                    return new {Input=path,Type="audio",Format=info.Codec,info.Channels,SampleRate=info.SampleRate,info.Duration};
                }
                if(scope.MissingOptions!=null)throw scope.MissingOptions;
                throw new UnknownFormatException();
            }
        }

        public static void ConvertAudio(string path, string output, OverwritePolicy overwrite = OverwritePolicy.Fail, CancellationToken cancellation = default, long maxBytes = 2L << 30)
        {
            EnsureFormats();
            path = Path.GetFullPath(path);
            output = Path.GetFullPath(output);
            lock (Gate)
            {
                using var scope = new DecoderScope(Path.GetDirectoryName(path), null);
                using var input = BinaryStream.FromFile(path);
                using var audio = AudioFormat.Read(input) ?? throw new UnknownFormatException();
                if (audio.PcmSize > maxBytes || audio.PcmSize > uint.MaxValue)
                    throw new InvalidDataException("Audio exceeds the output limit.");
                if (File.Exists(output) && overwrite == OverwritePolicy.Skip)
                    return;
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                string temporary = WorkerTemporaryFiles.Create(Path.GetDirectoryName(output));
                try
                {
                    using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                    {
                        WaveAudio.WriteRiffHeader(target, audio.Format, 0);
                        long total = 0;
                        var buffer = new byte[81920];
                        int read;
                        while ((read = audio.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            total = checked(total + read);
                            if (total > maxBytes || total > uint.MaxValue)
                                throw new InvalidDataException("Audio exceeds the output limit.");
                            target.Write(buffer, 0, read);
                        }

                        target.Position = 0;
                        WaveAudio.WriteRiffHeader(target, audio.Format, (uint)total);
                    }

                    File.Move(temporary, output, overwrite == OverwritePolicy.Replace);
                }
                finally
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
            }
        }

        public static ImageData ReadImage(string path, string formatTag = null, long maxPixels = 100000000,IOptionsProvider options=null)
        {
            EnsureFormats();
            path = Path.GetFullPath(path);
            lock (Gate)
            {
                using var scope = new DecoderScope(Path.GetDirectoryName(path), options);
                using var input = BinaryStream.FromFile(path);
                ImageFormat format;
                ImageMetaData info;
                if (formatTag == null)
                {
                    var found = ImageFormat.FindFormat(input) ?? throw new UnknownFormatException();
                    format = found.Item1;
                    info = found.Item2;
                }
                else
                {
                    format = ImageFormat.FindByTag(formatTag) ?? throw new ArgumentException("Unknown image format: " + formatTag);
                    info = format.ReadMetaData(input) ?? throw new InvalidFormatException();
                }

                if (maxPixels <= 0 || info.Width == 0 || info.Height == 0 || info.Width > int.MaxValue || info.Height > int.MaxValue || (ulong)info.Width * info.Height > (ulong)maxPixels)
                    throw new InvalidDataException("Image dimensions exceed the pixel limit.");
                input.Position = 0;
                return format.Read(input, info);
            }
        }

        public static void WriteImage(ImageData image, string output, string formatTag = "PNG", OverwritePolicy overwrite = OverwritePolicy.Fail, long maxBytes = 2L << 30)
        {
            EnsureFormats();
            lock (Gate)
            {
                var encoder = ResourceService.ResolveImageOutput(formatTag);
                output = Path.GetFullPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                if (File.Exists(output) && overwrite == OverwritePolicy.Skip)
                    return;
                var temporary = WorkerTemporaryFiles.Create(Path.GetDirectoryName(output));
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite))
                    using (var bounded = new ResourceService.BoundedOutput(stream,maxBytes))
                        encoder.Write(bounded, image);
                    File.Move(temporary, output, overwrite == OverwritePolicy.Replace);
                }
                finally
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
            }
        }

        internal sealed class DecoderScope : IDisposable
        {
            readonly string previous;
            readonly IOptionsProvider provider;
            public MissingOptionsException MissingOptions { get; private set; }

            public DecoderScope(string directory, IOptionsProvider provider)
            {
                previous = Directory.GetCurrentDirectory();
                this.provider = provider ?? new JsonOptionsProvider();
                Directory.SetCurrentDirectory(directory);
                FormatCatalog.Instance.ParametersRequest += Request;
            }

            void Request(object source, ParametersRequestEventArgs args)
            {
                try
                {
                    args.Options = provider.GetOptions((IResource)source, args.Notice);
                    args.InputResult = true;
                }
                catch (MissingOptionsException e)
                {
                    e.FormatId = FormatIdentity.Of((IResource)source);
                    e.OptionsJson = JsonConvert.SerializeObject(((IResource)source).GetDefaultOptions(), Formatting.Indented);
                    MissingOptions = e;
                    throw;
                }
            }

            public void Dispose()
            {
                FormatCatalog.Instance.ParametersRequest -= Request;
                Directory.SetCurrentDirectory(previous);
            }
        }
    }

    public sealed class ArchiveSession : IDisposable
    {
        readonly ArcFile archive;
        readonly IOptionsProvider options;
        bool disposed;
        public string Path { get; }
        public string Tag => archive.Tag;
        public IReadOnlyList<ArchiveEntryInfo> Entries { get; }

        internal ArchiveSession(ArcFile archive, string path, IOptionsProvider options)
        {
            this.archive = archive;
            Path = path;
            this.options = options;
            Entries = archive.Dir.Select(e => new ArchiveEntryInfo(e.Name, e.Offset, e.Size, e.Type)).ToArray();
        }

        public Task<IReadOnlyList<ExtractedFile>> ExtractAsync(string root, OverwritePolicy overwrite = OverwritePolicy.Fail, ExtractionLimits limits = null, IProgress<ExtractionProgress> progress = null, CancellationToken cancellation = default, ISet<string> selection = null) => Task.Run(() => Extract(root, overwrite, limits ?? new(), progress, cancellation, selection), cancellation);
        IReadOnlyList<ExtractedFile> Extract(string root, OverwritePolicy overwrite, ExtractionLimits limits, IProgress<ExtractionProgress> progress, CancellationToken cancellation, ISet<string> selection)
        {
            lock (ArchiveService.Gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (limits.MaxEntryBytes < 0 || limits.MaxTotalBytes < 0 || limits.MaxEntries < 1 || !double.IsFinite(limits.MaxExpansionRatio) || limits.MaxExpansionRatio <= 0)
                    throw new ArgumentOutOfRangeException(nameof(limits));
                using var scope = new ArchiveService.DecoderScope(System.IO.Path.GetDirectoryName(Path), options);
                root = System.IO.Path.GetFullPath(root);
                var entries = archive.Dir.Where(e => selection == null || selection.Contains(e.Name)).ToArray();
                if (entries.Length > limits.MaxEntries)
                    throw new InvalidDataException("Archive has too many entries.");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var plan = entries.Select(e => (Entry: e, Target: ExtractionPath.Resolve(root, e.Name))).ToArray();
                foreach (var item in plan)
                    if (!seen.Add(item.Target))
                        throw new InvalidDataException("Colliding archive paths: " + item.Entry.Name);
                Directory.CreateDirectory(root);
                var result = new List<ExtractedFile>();
                long total = 0;
                int done = 0;
                foreach (var item in plan)
                {
                    cancellation.ThrowIfCancellationRequested();
                    ExtractionPath.RejectLinks(root, item.Target);
                    if (File.Exists(item.Target))
                    {
                        if (overwrite == OverwritePolicy.Fail)
                            throw new IOException("Output exists: " + item.Target);
                        if (overwrite == OverwritePolicy.Skip)
                        {
                            result.Add(new(item.Entry.Name, 0, "skipped"));
                            progress?.Report(new(++done, plan.Length, item.Entry.Name, total));
                            continue;
                        }
                    }

                    if (item.Entry is PackedEntry packed && packed.UnpackedSize > limits.MaxEntryBytes)
                        throw new InvalidDataException("Entry exceeds the output limit: " + item.Entry.Name);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(item.Target));
                    ExtractionPath.RejectLinks(root, item.Target);
                    string temporary = WorkerTemporaryFiles.Create(System.IO.Path.GetDirectoryName(item.Target));
                    long length = 0;
                    try
                    {
                        using (var input = archive.OpenEntry(item.Entry))
                        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[81920];
                            int read;
                            while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                            {
                                cancellation.ThrowIfCancellationRequested();
                                length = checked(length + read);
                                total = checked(total + read);
                                if (length > limits.MaxEntryBytes || total > limits.MaxTotalBytes || length > Math.Max(1L, item.Entry.Size) * limits.MaxExpansionRatio)
                                    throw new InvalidDataException("Extraction limit exceeded: " + item.Entry.Name);
                                output.Write(buffer, 0, read);
                            }
                        }

                        ExtractionPath.RejectLinks(root, item.Target);
                        File.Move(temporary, item.Target, overwrite == OverwritePolicy.Replace);
                        result.Add(new(item.Entry.Name, length, "extracted"));
                        progress?.Report(new(++done, plan.Length, item.Entry.Name, total));
                    }
                    finally
                    {
                        if (File.Exists(temporary))
                            File.Delete(temporary);
                    }
                }

                return result;
            }
        }

        public byte[] ReadEntry(string name, long limit = 16 << 20)
        {
            lock (ArchiveService.Gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                using var scope = new ArchiveService.DecoderScope(System.IO.Path.GetDirectoryName(Path), options);
                var entry = archive.Dir.Single(e => e.Name == name);
                using var input = archive.OpenEntry(entry);
                using var result = new MemoryStream();
                var buffer = new byte[81920];
                int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (result.Length + count > limit)
                        throw new InvalidDataException("Preview exceeds its size limit.");
                    result.Write(buffer, 0, count);
                }

                return result.ToArray();
            }
        }

        public PreviewResult Preview(string name)
        {
            lock (ArchiveService.Gate)
            {
                using var scope = new ArchiveService.DecoderScope(System.IO.Path.GetDirectoryName(Path), options);
                var data = ReadEntry(name);
                using (var input = BinaryStream.FromArray(data, name))
                {
                    var candidate = ImageFormat.FindFormat(input);
                    if (candidate != null)
                    {
                        var info = candidate.Item2;
                        if (info.Width == 0 || info.Height == 0 || (ulong)info.Width * info.Height > 25000000UL)
                            throw new InvalidDataException("Image is too large for a preview.");
                        input.Position = 0;
                        var image = candidate.Item1.Read(input, info);
                        using var png = new MemoryStream();
                        ImageFormat.Png.Write(png, image);
                        return new PreviewResult("image", name, $"{image.Width} × {image.Height} · {image.BPP} bit · ({image.OffsetX}, {image.OffsetY})", png.ToArray());
                    }
                }

                int length = Math.Min(data.Length, 65536);
                string text;
                try
                {
                    text = new System.Text.UTF8Encoding(false, true).GetString(data, 0, length);
                }
                catch (System.Text.DecoderFallbackException)
                {
                    text = Encodings.cp932.GetString(data, 0, length);
                }

                if (text.Count(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t') > Math.Max(1, text.Length / 20))
                    return new PreviewResult("binary", name, Convert.ToHexString(data.AsSpan(0, Math.Min(512, data.Length))), null);
                return new PreviewResult("text", name, text + (data.Length > length ? "\n…" : ""), null);
            }
        }

        public void Dispose()
        {
            lock (ArchiveService.Gate)
            {
                if (disposed)
                    return;
                archive.Dispose();
                disposed = true;
            }
        }
    }
}
