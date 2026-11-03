using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameRes;
using GARbro.Media;
using Newtonsoft.Json;
using NAudio.Wave;

namespace GARbro.Application;

public static partial class ResourceService
{
    public static ResourceJobReport Extract(ResourceLocation location, string output, ResourceExtractionOptions options,
        ExtractionLimits limits, OverwritePolicy overwrite, string[] selection, IOptionsProvider provider,
        IProgress<ExtractionProgress> progress, string[] completedPaths = null)
    {
        options ??= new(); limits ??= new();
        if (options.MaxDepth is < 0 or > 64 || limits.MaxEntries < 1 || limits.MaxEntryBytes < 0 || limits.MaxTotalBytes < 0
            || !double.IsFinite(limits.MaxExpansionRatio) || limits.MaxExpansionRatio <= 0) throw new ArgumentException("Invalid extraction limits.");
        var files = new List<ExtractedFile>();
        var errors = new List<string>();
        var queue = new Queue<(ResourceLocation Location, string Output, int Depth, string[] Selection, bool Required, string Copy)>();
        var removals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        output = Path.GetFullPath(output);
        static bool Inside(string root, string path)
        {
            string relative = Path.GetRelativePath(root, path);
            return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar);
        }
        bool inPlace = System.IO.Directory.Exists(location.RootPath) && Path.GetRelativePath(location.RootPath, output) == ".";
        bool outputWithinInput = System.IO.Directory.Exists(location.RootPath) && !inPlace && Inside(location.RootPath, output);
        var completed = new HashSet<string>(completedPaths ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        queue.Enqueue((location, output, 0, selection, true, null));
        long total = 0;
        foreach (string path in completed)
        {
            string relative = Path.GetRelativePath(output, path);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar)) throw new IOException("Invalid retry output.");
            ExtractionPath.RejectLinks(output, path);
            if (File.Exists(path)) total = checked(total + new FileInfo(path).Length);
        }
        if (total > limits.MaxTotalBytes) throw new InvalidDataException("Previous outputs exceed the job limit.");
        int count = 0;
        var generated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (queue.Count != 0)
        {
            var task = queue.Dequeue();
            try
            {
                using var session = new ResourceSession(task.Location, provider);
                if (session.Synthetic && !task.Required) continue;
                if (session.Physical)
                {
                    var directory = new DirectoryInfo(task.Location.RootPath);
                    foreach (var item in directory.EnumerateFileSystemInfos().OrderBy(f => f.Name, StringComparer.Ordinal))
                    {
                        if ((item.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if (task.Selection != null && !task.Selection.Contains(item.Name, StringComparer.Ordinal)) continue;
                        if (item is DirectoryInfo)
                        {
                            if (outputWithinInput && Inside(output, item.FullName) || inPlace && item.Name.EndsWith(".unpacked", StringComparison.OrdinalIgnoreCase)) continue;
                            if (options.Recursive && task.Depth < options.MaxDepth)
                                queue.Enqueue((new(item.FullName), Path.Combine(task.Output, item.Name), task.Depth + 1, null, false, null));
                        }
                        else queue.Enqueue((new(item.FullName), Path.Combine(task.Output, item.Name + ".unpacked"), task.Depth, null, false, null));
                    }
                    continue;
                }
                var entries = session.Archive.Dir.Where(e => task.Selection == null || task.Selection.Any(s =>
                    e.Name == s || e.Name.Replace('\\', '/').StartsWith(s.TrimEnd('/', '\\') + "/", StringComparison.Ordinal))).ToArray();
                bool current = task.Location.RootPath == location.RootPath && task.Location.Chain.SequenceEqual(location.Chain);
                bool Forced(Entry entry) => current && options.TypeOverrides?.ContainsKey(entry.Name) == true;
                foreach (var entry in entries.Where(Forced)) entry.Type = options.TypeOverrides[entry.Name] == "binary" ? "" : options.TypeOverrides[entry.Name];
                var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in entries)
                    if (!targets.Add(ExtractionPath.Resolve(task.Output, e.Name))) throw new InvalidDataException("Colliding archive paths: " + e.Name);
                var originalTargets = new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries)
                {
                    string extension = options.ImageFormat != null && entry.Type == "image" ? options.ImageFormat.ToLowerInvariant() :
                        options.ConvertAudio && entry.Type == "audio" && !new[] { ".wav", ".mp3", ".ogg" }.Contains(Path.GetExtension(entry.Name).ToLowerInvariant()) ? "wav" : null;
                    if (extension == null) continue;
                    string raw = ExtractionPath.Resolve(task.Output, entry.Name), converted = Path.ChangeExtension(raw, extension);
                    if (!converted.Equals(raw, StringComparison.OrdinalIgnoreCase) && !targets.Add(converted))
                        throw new IOException("Conversion output collides with another resource: " + converted);
                }
                var children = new List<(ResourceLocation, string, int, string[], bool, string)>();
                foreach (var entry in entries)
                {
                    bool video = !Forced(entry) && MediaInfo.IsVideoName(entry.Name);
                    if (video) entry.Type = "video";
                    bool archiveCandidate = !video && (entry.Type == "archive" || !Forced(entry) && FormatCatalog.Instance.GetTypeFromName(entry.Name) == "archive");
                    if (options.Recursive && !Forced(entry) && !archiveCandidate && entry.Size >= 4)
                    {
                        using var signatureStream = session.Archive.OpenBinaryEntry(entry);
                        uint signature = signatureStream.Signature;
                        signatureStream.Position = 0;
                        video = MediaInfo.HasVideoHeader(entry.Name, signatureStream.ReadBytes((int)Math.Min(256, signatureStream.Length)));
                        if (video) entry.Type = "video";
                        archiveCandidate = !video && signature != 0 && FormatCatalog.Instance.ArcFormats.Any(f => f.Signatures.Contains(signature));
                    }
                    string category = entry.Type is "image" or "audio" or "script" or "archive" or "video" ? entry.Type : "";
                    if (options.Types is { Length: > 0 } && !options.Types.Contains(category) && !(options.Recursive && archiveCandidate)) continue;
                    if (++count > limits.MaxEntries) throw new InvalidDataException("Recursive extraction has too many entries.");
                    string target = ExtractionPath.Resolve(task.Output, entry.Name);
                    if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(task.Location.RootPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new IOException("Extraction cannot replace its input archive.");
                    ExtractionPath.RejectLinks(output, target);
                    bool retained = completed.Contains(target) && File.Exists(target);
                    if (!retained && File.Exists(target))
                    {
                        if (overwrite == OverwritePolicy.Skip) { files.Add(new(target, 0, "skipped")); continue; }
                        if (overwrite == OverwritePolicy.Fail) throw new IOException("Output exists: " + target);
                    }
                    if (!retained)
                    {
                    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string temporary = WorkerTemporaryFiles.Create(Path.GetDirectoryName(target));
                    long size;
                    try
                    {
                        using (var input = session.Archive.OpenEntry(entry))
                        using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                        {
                            long ratioLimit = (long)Math.Min(long.MaxValue, Math.Max(1L, entry.Size) * limits.MaxExpansionRatio);
                            size = CopyBounded(input, destination, Math.Min(Math.Min(limits.MaxEntryBytes, limits.MaxTotalBytes - total), ratioLimit));
                        }
                        ExtractionPath.RejectLinks(output, target);
                        File.Move(temporary, target, overwrite == OverwritePolicy.Replace);
                        total += size;
                        files.Add(new(target, size, "extracted"));
                        completed.Add(target);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                    else files.Add(new(target, new FileInfo(target).Length, "retained"));
                    progress?.Report(new(files.Count, Math.Max(files.Count, entries.Length), entry.Name, total, target));
                    if (options.ImageFormat != null && entry.Type == "image")
                    {
                        string converted = Path.ChangeExtension(target, options.ImageFormat.ToLowerInvariant());
                        if (!string.Equals(converted, target, StringComparison.OrdinalIgnoreCase))
                        {
                            var result = ConvertFile(session, entry.Name, converted, options.ImageFormat, overwrite, Math.Min(limits.MaxEntryBytes, limits.MaxTotalBytes - total), completed, options.AdjustImageOffset, originalTargets);
                            total += result.Bytes;
                            files.Add(new(result.Path, result.Bytes, "converted"));
                            progress?.Report(new(files.Count, Math.Max(files.Count, entries.Length), entry.Name, total, result.Path));
                            if (options.DiscardOriginalMedia && (result.Written || completed.Contains(result.Path))) removals.Add(target);
                            if (result.Written) completed.Add(result.Path);
                        }
                    }
                    if (options.ConvertAudio && entry.Type == "audio" && !new[] { ".wav", ".mp3", ".ogg" }.Contains(Path.GetExtension(target).ToLowerInvariant()))
                    {
                        string converted = Path.ChangeExtension(target, "wav");
                        if (!string.Equals(converted, target, StringComparison.OrdinalIgnoreCase))
                        {
                            var result = ConvertFile(session, entry.Name, converted, "AUDIO", overwrite, Math.Min(limits.MaxEntryBytes, limits.MaxTotalBytes - total), completed, prohibited: originalTargets);
                            total += result.Bytes;
                            files.Add(new(result.Path, result.Bytes, "converted"));
                            progress?.Report(new(files.Count, Math.Max(files.Count, entries.Length), entry.Name, total, result.Path));
                            if (options.DiscardOriginalMedia && (result.Written || completed.Contains(result.Path))) removals.Add(target);
                            if (result.Written) completed.Add(result.Path);
                        }
                    }
                    if (options.Recursive && task.Depth < options.MaxDepth && archiveCandidate)
                    {
                        string childOutput = target + ".unpacked";
                        if (targets.Any(p => p.Equals(childOutput, StringComparison.OrdinalIgnoreCase) || p.StartsWith(childOutput + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            || !generated.Add(childOutput)) throw new IOException("Nested archive output collides with an existing entry: " + childOutput);
                        children.Add((session.Child(entry.Name), childOutput, task.Depth + 1, null, false, target));
                    }
                    else if (options.Recursive && archiveCandidate) errors.Add("Depth limit: " + entry.Name);
                }
                foreach (var child in children) queue.Enqueue(child);
                if (options.DiscardChildArchives && task.Copy != null) removals.Add(task.Copy);
            }
            catch (MissingOptionsException error) { error.CompletedPaths = completed.ToArray(); throw; }
            catch (UnknownFormatException) when (!task.Required) { }
            catch (Exception error) when (!task.Required && error is not ResourceParametersRequiredException)
            { errors.Add(task.Location.DisplayPath + ": " + error.Message); }
        }
        // Defer cleanup until every queued archive succeeds. Parameter retries,
        // cancellation and depth/output limits retain the recoverable copies.
        if (errors.Count == 0)
            foreach (string path in removals)
            {
                ExtractionPath.RejectLinks(output, path);
                try
                {
                    File.Delete(path);
                    files.RemoveAll(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { errors.Add(path + ": " + error.Message); }
            }
        return new(files.ToArray(), errors.ToArray(), total, Math.Min(count, limits.MaxEntries));
    }

    public static void CreateArchive(string source, string[] inputs, string output, string formatId,
        string optionsJson, string scheme, OverwritePolicy overwrite, IProgress<ExtractionProgress> progress)
    {
        ArchiveService.Formats();
        var format = FormatCatalog.Instance.ArcFormats.Single(f => FormatIdentity.Of(f) == formatId);
        if (!format.CanWrite) throw new NotSupportedException("This format is read-only.");
        source = Path.GetFullPath(source); output = Path.GetFullPath(output);
        if (File.Exists(output) && overwrite == OverwritePolicy.Fail) throw new IOException("Output exists: " + output);
        if (File.Exists(output) && overwrite == OverwritePolicy.Skip) return;
        string stage = WorkerTemporaryFiles.CreateDirectory(Path.GetTempPath());
        string temporary = null;
        try
        {
            var files = new List<string>();
            foreach (string input in inputs)
            {
                string full = ExtractionPath.Resolve(source, input);
                if (System.IO.Directory.Exists(full)) files.AddRange(System.IO.Directory.EnumerateFiles(full, "*", new EnumerationOptions
                    { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = 64 }));
                else files.Add(full);
            }
            var entries = new List<Entry>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (string file in files.Distinct(StringComparer.Ordinal).Where(f => f != output))
            {
                string name = format.IsHierarchic ? Path.GetRelativePath(source, file).Replace('\\', '/') : Path.GetFileName(file);
                if (!names.Add(name)) throw new IOException("Colliding input paths: " + name);
                string staged = ExtractionPath.Resolve(stage, name);
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(staged));
                using (var input = File.OpenRead(file))
                using (var target = File.Create(staged)) total += CopyBounded(input, target, Math.Min(2L << 30, (16L << 30) - total));
                if (entries.Count >= 262144) throw new InvalidDataException("Too many input files.");
                entries.Add(new Entry { Name = name, Size = checked((uint)new FileInfo(staged).Length), Type = FormatCatalog.Instance.GetTypeFromName(name) });
            }
            if (entries.Count == 0) throw new ArgumentException("Select at least one regular file to create an archive.");
            using var scope = new ArchiveService.DecoderScope(stage, null);
            VFS.FullPath = new[] { stage };
            var options = format.GetDefaultOptions();
            if (!string.IsNullOrWhiteSpace(optionsJson) || scheme != null)
                options = new JsonOptionsProvider(optionsJson ?? "{}", scheme).GetOptions(format, "Create archive");
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(output));
            temporary = WorkerTemporaryFiles.Create(Path.GetDirectoryName(output));
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite))
            using (var bounded = new BoundedOutput(file, 16L << 30))
                format.Create(bounded, entries, options, (n, e, text) =>
                { progress?.Report(new(n, entries.Count, e?.Name ?? text ?? "", 0)); return ArchiveOperation.Continue; });
            File.Move(temporary, output, overwrite == OverwritePolicy.Replace);
        }
        finally
        {
            if (temporary != null && File.Exists(temporary)) File.Delete(temporary);
            System.IO.Directory.Delete(stage, true);
        }
    }

    public static ResourceConversion ConvertFile(ResourceSession session, string name, string output, string formatId, OverwritePolicy overwrite, long maxBytes = 2L << 30, ISet<string> completed = null, bool adjustImageOffset = false, ISet<string> prohibited = null)
    {
        string scratch = WorkerTemporaryFiles.CreateDirectory(Path.GetTempPath());
        string temporary = null;
        try
        {
            string path = Path.Combine(scratch, Path.GetFileName(name));
            using (var sourceInput = session.OpenEntry(name))
            using (var target = File.Create(path)) CopyBounded(sourceInput, target, 2L << 30);
            using var input = new BinaryStream(File.OpenRead(path), name);
            using var audio = formatId is "WAV" or "AUDIO" ? AudioFormat.Read(input) ?? throw new UnknownFormatException() : null;
            bool passthrough = audio != null && formatId == "AUDIO" && new[] { "wav", "mp3", "ogg" }.Contains(audio.SourceFormat);
            if (passthrough) output = Path.ChangeExtension(output, audio.SourceFormat);
            string sourcePath = session.Physical ? session.PhysicalPath(name) : session.Location.RootPath;
            if (Path.GetFullPath(output).Equals(Path.GetFullPath(sourcePath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("Cannot replace a source document. Choose another destination.");
            ExtractionPath.RejectLinks(Path.GetDirectoryName(Path.GetFullPath(output)), output);
            if (prohibited?.Contains(output) == true) throw new IOException("Conversion output collides with another resource: " + output);
            if (completed?.Contains(output) == true && File.Exists(output)) return new(output, 0, false);
            if (File.Exists(output))
            {
                if (overwrite == OverwritePolicy.Skip) return new(output, 0, false);
                if (overwrite == OverwritePolicy.Fail) throw new IOException("Output exists: " + output);
            }
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(output));
            temporary = WorkerTemporaryFiles.Create(Path.GetDirectoryName(output));
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite))
            using (var bounded = new BoundedOutput(file, maxBytes))
            {
                if (audio != null)
                {
                    if (passthrough) { audio.Source.Position = 0; CopyBounded(audio.Source, bounded, maxBytes); }
                    else AudioFormat.Wav.Write(audio, bounded);
                }
                else
                {
                    using var decoder = !session.Physical ? session.Archive.OpenImage(session.Find(name)) : null;
                    var image = decoder?.Image ?? ImageFormat.Read(input) ?? throw new UnknownFormatException();
                    if (adjustImageOffset) image = AdjustOffset(image);
                    var format = FormatCatalog.Instance.ImageFormats.Single(f => FormatIdentity.Of(f) == formatId || f.Tag == formatId);
                    if (!format.CanWrite) throw new NotSupportedException("This image format is read-only.");
                    format.Write(bounded, image);
                }
            }
            File.Move(temporary, output, overwrite == OverwritePolicy.Replace);
            return new(output, new FileInfo(output).Length);
        }
        finally { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); System.IO.Directory.Delete(scratch, true); }
    }

    public static void CopyOriginalMedia(ResourceSession session, string name, string converted, OverwritePolicy overwrite)
    {
        string copy = Path.ChangeExtension(converted, Path.GetExtension(name));
        if (Path.GetFullPath(copy).Equals(Path.GetFullPath(converted), StringComparison.OrdinalIgnoreCase)) return;
        if (session.Physical && Path.GetFullPath(copy).Equals(session.PhysicalPath(name), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return;
        if (File.Exists(copy) && overwrite == OverwritePolicy.Skip) return;
        string directory = Path.GetDirectoryName(Path.GetFullPath(copy));
        ExtractionPath.RejectLinks(directory, copy);
        if (File.Exists(copy) && overwrite == OverwritePolicy.Fail) throw new IOException("Output exists: " + copy);
        string temporary = WorkerTemporaryFiles.Create(directory);
        try
        {
            using (var source = session.OpenEntry(name))
            using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) CopyBounded(source, destination, 2L << 30);
            ExtractionPath.RejectLinks(directory, copy); File.Move(temporary, copy, overwrite == OverwritePolicy.Replace);
        }
        finally { File.Delete(temporary); }
    }

    internal static ImageData AdjustOffset(ImageData image)
    {
        if (image.OffsetX == 0 && image.OffsetY == 0) return image;
        long width = (long)image.Width + image.OffsetX, height = (long)image.Height + image.OffsetY;
        if (width <= 0 || height <= 0) return image;
        if (width * height > 100000000) throw new InvalidDataException("Adjusted image exceeds the pixel limit.");
        // Convert palette and sub-byte pixels before placing/cropping on a transparent canvas.
        var source = image.Bitmap.Convert(GARbro.Imaging.PixelFormats.Bgra32);
        int stride = checked((int)image.Width * 4);
        var pixels = new byte[checked(stride * (int)image.Height)]; source.CopyPixels(pixels, stride, 0);
        int destinationStride = checked((int)width * 4);
        var destination = new byte[checked(destinationStride * (int)height)];
        int sx = Math.Max(-image.OffsetX, 0), sy = Math.Max(-image.OffsetY, 0);
        int dx = Math.Max(image.OffsetX, 0), dy = Math.Max(image.OffsetY, 0);
        int copy = checked(((int)image.Width - sx) * 4);
        for (int y = sy; y < image.Height; y++) Buffer.BlockCopy(pixels, y * stride + sx * 4, destination, (y - sy + dy) * destinationStride + dx * 4, copy);
        return ImageData.Create(new ImageMetaData { Width = (uint)width, Height = (uint)height, BPP = 32 }, GARbro.Imaging.PixelFormats.Bgra32, null, destination, destinationStride);
    }

    public static AudioArtifact PrepareAudio(ResourceLocation location, string name, string output, IOptionsProvider options)
    {
        using var session = BrowserDecoderSession.Open(location, options);
        string scratch = WorkerTemporaryFiles.CreateDirectory(Path.GetTempPath());
        string temporary = null;
        try
        {
            string source = session.Physical ? session.PhysicalPath(name) : BrowserDecoderSession.Materialize(session,name,scratch);
            using var binary = new BinaryStream(File.OpenRead(source), name);
            using var audio = AudioFormat.Read(binary) ?? throw new UnknownFormatException();
            var f = audio.Format;
            if (f.Channels is < 1 or > 8 || f.SamplesPerSecond is < 1000 or > 384000) throw new InvalidDataException("Unsupported audio parameters.");
            var waveFormat = NAudio.Wave.WaveFormat.CreateCustomFormat((WaveFormatEncoding)f.FormatTag, (int)f.SamplesPerSecond, f.Channels,
                (int)f.AverageBytesPerSecond, f.BlockAlign, f.BitsPerSample);
            using var raw = new RawSourceWaveStream(audio, waveFormat);
            var samples = raw.ToSampleProvider();
            var pcm = new NAudio.Wave.SampleProviders.SampleToWaveProvider16(samples);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(output));
            temporary = WorkerTemporaryFiles.Create(Path.GetDirectoryName(output));
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite))
            using (var bounded = new BoundedOutput(file, 512L << 20))
            using (var writer = new WaveFileWriter(bounded, pcm.WaveFormat))
            {
                byte[] buffer = new byte[81920]; int read;
                while ((read = pcm.Read(buffer, 0, buffer.Length)) != 0) writer.Write(buffer, 0, read);
            }
            File.Move(temporary, output);
            long bytes = new FileInfo(output).Length;
            return new(output, bytes, pcm.WaveFormat.SampleRate, pcm.WaveFormat.Channels,
                Math.Max(0, bytes - 44) / (double)pcm.WaveFormat.AverageBytesPerSecond);
        }
        finally
        {
            if (temporary != null && File.Exists(temporary)) File.Delete(temporary);
            System.IO.Directory.Delete(scratch, true);
        }
    }

    sealed class BoundedOutput(Stream inner, long limit) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set { if (value < 0 || value > limit) throw new IOException("Output limit exceeded."); inner.Position = value; } }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] b, int o, int n) => inner.Read(b, o, n);
        public override long Seek(long offset, SeekOrigin origin) { long p = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? Position + offset : Length + offset; Position = p; return p; }
        public override void SetLength(long n) { if (n < 0 || n > limit) throw new IOException("Output limit exceeded."); inner.SetLength(n); }
        public override void Write(byte[] b, int o, int n) { if (Position > limit - n) throw new IOException("Output limit exceeded."); inner.Write(b, o, n); }
    }
}
