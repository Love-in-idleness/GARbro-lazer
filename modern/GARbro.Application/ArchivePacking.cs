using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using GameRes;
using GARbro.Domain;

namespace GARbro.Application;

public static partial class ResourceService
{
    public static ArchiveFormat ResolveArchiveOutput(string format, string output)
    {
        ArchiveService.EnsureFormats();
        var writers = FormatCatalog.Instance.ArcFormats.Where(f => f.CanWrite).ToArray();
        var exact = writers.FirstOrDefault(f => FormatIdentity.Of(f) == format);
        if (exact != null) return exact;
        var matches = writers.Where(f => format == null
            ? f.Extensions.Contains(Path.GetExtension(output).TrimStart('.'), StringComparer.OrdinalIgnoreCase)
            : f.Tag.Equals(format, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) throw new ArgumentException("Unknown writable archive format: " + (format ?? Path.GetExtension(output)));
        if (matches.Length > 1) throw new ArgumentException("Ambiguous archive format: " + string.Join(", ", matches.Select(FormatIdentity.Of)));
        return matches[0];
    }

    public static ArchivePackingResult PackArchive(string source, string[] inputs, string output, string formatId,
        ArchivePackingOptions packing = null, string optionsJson = null, string scheme = null,
        OverwritePolicy overwrite = OverwritePolicy.Fail, ExtractionLimits limits = null,
        IOptionsProvider decoderOptions = null, IProgress<ExtractionProgress> progress = null, CancellationToken cancellation = default)
    {
        packing ??= new(); limits ??= new(); cancellation.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(source); ArgumentException.ThrowIfNullOrWhiteSpace(output);
        if (limits.MaxEntryBytes < 0 || limits.MaxTotalBytes < 0 || limits.MaxEntries < 1) throw new ArgumentException("Invalid extraction limits.");
        bool convert = packing.ImageFormat != null || packing.AudioFormat != null || packing.VideoFormat != null || packing.ConvertScripts;
        if (packing.DiscardOriginalMedia && !convert)
            throw new ArgumentException("Discarding originals requires a conversion format.");
        if (packing.ImageFormat != null) ResolveImageOutput(packing.ImageFormat);
        if (packing.AudioFormat != null && !MediaOutputFormats.Audio.Contains(MediaOutputFormats.Normalize(packing.AudioFormat))) throw new ArgumentException("Unsupported media output format.");
        if (packing.VideoFormat != null && !MediaOutputFormats.Video.Contains(MediaOutputFormats.Normalize(packing.VideoFormat))) throw new ArgumentException("Unsupported media output format.");
        var format = ResolveArchiveOutput(formatId, output);
        var options = format.GetDefaultOptions();
        bool hasOptions = !string.IsNullOrWhiteSpace(optionsJson) && optionsJson.Trim() != "null";
        if (options == null)
        {
            if (scheme is not (null or "none") || hasOptions && Newtonsoft.Json.Linq.JObject.Parse(optionsJson).HasValues)
                throw new ArgumentException("This archive format has no creation parameters.");
        }
        else if (hasOptions || scheme != null) options = new JsonOptionsProvider(hasOptions ? optionsJson : "{}", scheme).GetOptions(format, "Create archive");
        source = Path.GetFullPath(source); output = Path.GetFullPath(output);
        ExtractionPath.RejectLinks(Path.GetPathRoot(source), source);
        ExtractionPath.RejectLinks(Path.GetPathRoot(output), output);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        if (Directory.Exists(output)) throw new IOException("Output exists: " + output);
        if (File.Exists(output))
        {
            if (overwrite == OverwritePolicy.Fail) throw new IOException("Output exists: " + output);
            if (overwrite == OverwritePolicy.Skip) return new(output, format.Tag, false, 0, 0, 0);
        }
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var clock = Stopwatch.StartNew(); long lastUpdate = -50; string lastPhase = null;
        void Report(ExtractionProgress update)
        {
            if (progress == null || lastPhase == update.Phase && clock.ElapsedMilliseconds - lastUpdate < 50 && !(update.Total > 0 && update.Completed >= update.Total)) return;
            lastPhase = update.Phase; lastUpdate = clock.ElapsedMilliseconds; progress.Report(update);
        }
        var files = new List<string>(); var seen = new HashSet<string>(comparer); var visited = new HashSet<string>(comparer);
        void Collect(string path, int depth)
        {
            cancellation.ThrowIfCancellationRequested();
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return;
            if (Directory.Exists(path))
            {
                if (!visited.Add(path)) return;
                if (depth > 64) throw new InvalidDataException("Packing directory nesting exceeds 64 levels.");
                foreach (var item in Directory.EnumerateFileSystemEntries(path)) Collect(item, depth + 1);
            }
            else if (!comparer.Equals(path, output) && seen.Add(path))
            {
                if (files.Count >= limits.MaxEntries) throw new InvalidDataException("Too many input files.");
                files.Add(path); Report(new(files.Count, 0, Path.GetRelativePath(source, path), 0, Phase: "collect"));
            }
        }
        if (inputs == null) Collect(source, 0);
        else foreach (string input in inputs)
        {
            string full = ExtractionPath.Resolve(source, input);
            if (comparer.Equals(full, output)) throw new IOException("Cannot replace a source document. Choose another destination.");
            Collect(full, 0);
        }
        if (files.Count == 0) throw new ArgumentException("Select at least one regular file to create an archive.");
        string stage = WorkerTemporaryFiles.CreateDirectory(Path.GetTempPath()), temporary = null;
        try
        {
            string prepared = Directory.CreateDirectory(Path.Combine(stage, "entries")).FullName;
            var entries = new List<Entry>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            byte[] buffer = new byte[81920];
            long bytes = 0, readBytes = 0; int convertedCount = 0;
            long entryLimit = Math.Min(limits.MaxEntryBytes, uint.MaxValue);
            void Add(string input, string name)
            {
                name = ArchivePackingPlan.EntryName(name, format.IsHierarchic);
                if (!names.Add(name)) throw new IOException("Colliding input paths: " + name);
                if (entries.Count >= limits.MaxEntries) throw new InvalidDataException("Too many input files.");
                string destination = ExtractionPath.Resolve(prepared, name);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                using var from = File.OpenRead(input); using var to = File.Create(destination);
                int count; long length = 0;
                while ((count = from.Read(buffer)) > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (length > entryLimit - count || bytes > limits.MaxTotalBytes - count) throw new InvalidDataException("Resource exceeds the size limit.");
                    to.Write(buffer, 0, count); length += count; bytes += count;
                }
                entries.Add(new Entry { Name = name, Size = checked((uint)length), Type = FormatCatalog.Instance.GetTypeFromName(name) });
            }
            var conversion = new ResourceExtractionOptions(ImageFormat: packing.ImageFormat, AudioFormat: packing.AudioFormat,
                VideoFormat: packing.VideoFormat, ConvertScripts: packing.ConvertScripts, ScriptFormat: packing.ScriptFormat);
            for (int index = 0; index < files.Count; index++)
            {
                string file = files[index];
                cancellation.ThrowIfCancellationRequested();
                var fingerprint = ArchiveFingerprint.Read(file);
                if (fingerprint.Length > entryLimit || readBytes > limits.MaxTotalBytes - fingerprint.Length) throw new InvalidDataException("Resource exceeds the size limit.");
                readBytes += fingerprint.Length;
                string relative = Path.GetRelativePath(source, file);
                bool original = true;
                if (convert)
                {
                    Report(new(index, files.Count, relative, bytes, Phase: "convert"));
                    using var session = new ResourceSession(new(Path.GetDirectoryName(file)), decoderOptions);
                    string name = Path.GetFileName(file);
                    string targetRoot = Directory.CreateDirectory(Path.Combine(stage, "convert", index.ToString(System.Globalization.CultureInfo.InvariantCulture))).FullName;
                    var target = MediaConversionTarget(session, name, Path.Combine(targetRoot, name), conversion);
                    if (target.Format != null)
                    {
                        ResourceConversion result;
                        try { result = ConvertFile(session, name, target.Output, target.Format, OverwritePolicy.Fail, Math.Min(entryLimit, limits.MaxTotalBytes - bytes), cancellation: cancellation); }
                        catch (Exception error) when (error is not OperationCanceledException and not MissingOptionsException)
                        { throw new IOException("Packing conversion failed: " + relative + " — " + error.Message, error); }
                        if (!result.Passthrough)
                        {
                            Add(result.Path, Path.Combine(Path.GetDirectoryName(relative), Path.GetFileName(result.Path)));
                            convertedCount++; original = !packing.DiscardOriginalMedia;
                        }
                        File.Delete(result.Path);
                    }
                }
                if (original) Add(file, relative);
                if (ArchiveFingerprint.Read(file) != fingerprint) throw new IOException("Packing input changed: " + relative);
            }
            cancellation.ThrowIfCancellationRequested();
            using var scope = new ArchiveService.DecoderScope(prepared, null); VFS.FullPath = new[] { prepared };
            Directory.CreateDirectory(Path.GetDirectoryName(output)); temporary = WorkerTemporaryFiles.Create(Path.GetDirectoryName(output));
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite))
            using (var bounded = new BoundedOutput(file, limits.MaxTotalBytes))
                format.Create(bounded, entries, options, (n, entry, message) =>
                { cancellation.ThrowIfCancellationRequested(); Report(new(n, entries.Count, entry?.Name ?? message ?? "", bytes, Phase: "write")); return ArchiveOperation.Continue; });
            cancellation.ThrowIfCancellationRequested(); ExtractionPath.RejectLinks(Path.GetPathRoot(output), output);
            File.Move(temporary, output, overwrite == OverwritePolicy.Replace);
            return new(output, format.Tag, true, entries.Count, convertedCount, new FileInfo(output).Length);
        }
        finally { if (temporary != null) File.Delete(temporary); Directory.Delete(stage, true); }
    }
}
