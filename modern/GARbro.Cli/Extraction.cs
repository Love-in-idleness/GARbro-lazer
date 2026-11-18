using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using GARbro.Application;
using GameRes;

internal static partial class Program
{
    sealed record ExtractionInput(string Source, ResourceLocation Location, string Output, bool Discovered, bool RequireArchive);

    static bool IsWithin(string path, string child)
    {
        string relative = Path.GetRelativePath(path, child);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    static string ResolveLinks(string path, Dictionary<string, string> directories, int links = 0)
    {
        if (links > 40) throw new IOException("Too many symbolic links in input or output path: " + path);
        string parent = Path.GetDirectoryName(path);
        if (parent == null) return path;
        if (!directories.TryGetValue(parent, out string resolvedParent))
            directories[parent] = resolvedParent = ResolveLinks(parent, directories, links);
        string candidate = Path.Combine(resolvedParent, Path.GetFileName(path));
        FileSystemInfo info = Directory.Exists(candidate) ? new DirectoryInfo(candidate) : new FileInfo(candidate);
        string target = info.LinkTarget;
        return target == null ? candidate : ResolveLinks(Path.GetFullPath(target, resolvedParent), directories, links + 1);
    }

    static int Extract(IReadOnlyList<string> inputs, string output, string inputFormat,
        ResourceExtractionOptions options, ExtractionLimits limits, OverwritePolicy overwrite,
        IOptionsProvider provider, bool json, CancellationToken cancellation)
    {
        output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        bool conversion = options.ConvertScripts || options.ImageFormat != null || options.AudioFormat != null || options.VideoFormat != null;
        var plan = new List<ExtractionInput>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string source, string path, string root, bool discovered, bool requireArchive)
        {
            if (!destinations.Add(root)) throw new IOException("Input roots have the same destination: " + root);
            plan.Add(new(source, new(path, InputFormatId: inputFormat), root, discovered, requireArchive));
        }
        foreach (string source in inputs)
        {
            cancellation.ThrowIfCancellationRequested();
            string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
            string name = Path.GetFileName(path);
            if (!Directory.Exists(path))
            {
                if (!File.Exists(path)) throw new FileNotFoundException("Input file does not exist.", path);
                Add(source, path, inputs.Count == 1 ? output : ExtractionPath.Resolve(output, name),
                    discovered: false, requireArchive: !conversion || inputFormat != null);
                continue;
            }
            if (options.RecurseDirectories != true) throw new ArgumentException("A directory input requires --recursive.");
            if (conversion && inputFormat == null)
            {
                Add(source, path, inputs.Count == 1 ? output : ExtractionPath.Resolve(output, name),
                    discovered: false, requireArchive: false);
                continue;
            }
            var scan = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = options.MaxDepth, AttributesToSkip = FileAttributes.ReparsePoint };
            bool inPlace = Path.GetRelativePath(path, output) == ".";
            bool outputInsideSource = !inPlace && IsWithin(path, output);
            foreach (string entry in Directory.EnumerateFiles(path, "*", scan).OrderBy(file => file, StringComparer.Ordinal))
            {
                cancellation.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(path, entry);
                if (inPlace && relative.Split(Path.DirectorySeparatorChar)[..^1]
                    .Any(part => part.EndsWith(".unpacked", StringComparison.OrdinalIgnoreCase))) continue;
                if (outputInsideSource && IsWithin(output, entry)) continue;
                if (inputs.Count > 1) relative = Path.Combine(name, relative);
                Add(entry, entry, ExtractionPath.Resolve(output, relative + ".unpacked"),
                    discovered: true, requireArchive: true);
            }
        }

        var protectedInputs = new HashSet<string>(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var directories = new Dictionary<string, string>(protectedInputs.Comparer);
        string physicalOutput = ResolveLinks(output, directories);
        foreach (var job in plan.Where(job => File.Exists(job.Location.RootPath)))
        {
            string source = job.Location.RootPath;
            string physicalSource = ResolveLinks(source, directories);
            protectedInputs.Add(source);
            if (IsWithin(physicalOutput, physicalSource))
                protectedInputs.Add(Path.GetFullPath(Path.Combine(output, Path.GetRelativePath(physicalOutput, physicalSource))));
        }
        if (plan.Any(job => protectedInputs.Contains(job.Output)))
            throw new IOException("Extraction cannot replace its input archive.");
        var results = new List<object>();
        bool failed = false;
        foreach (var job in plan)
        {
            cancellation.ThrowIfCancellationRequested();
            if (limits.MaxEntries <= 0) throw new InvalidDataException("Recursive extraction has too many entries.");
            ResourceJobReport report;
            try
            {
                report = ResourceService.Extract(job.Location, job.Output, options, limits, overwrite, null, provider, null,
                    requireArchive: job.RequireArchive, protectedInputs: protectedInputs, cancellation: cancellation);
            }
            catch (UnknownFormatException) when (job.Discovered)
            {
                if (!json) Console.Error.WriteLine(text.Format("Cli.SkippedUnrecognized", job.Source));
                continue;
            }
            limits = limits with { MaxTotalBytes = limits.MaxTotalBytes - report.Bytes, MaxEntries = limits.MaxEntries - report.Entries };
            var files = report.Files.Select(file => file with { Path = Path.GetRelativePath(job.Output, file.Path) }).ToArray();
            results.Add(new { input = job.Source, format = report.Format, output = job.Output, files, report.Errors, report.Bytes, report.Entries });
            failed |= report.Errors.Length > 0;
            if (!json)
            {
                Console.WriteLine(text.Format("Cli.ExtractSummary", job.Source, files.Count(f => f.Status is "extracted" or "converted"),
                    files.Count(f => f.Status == "skipped"), files.Count(f => f.Status == "retained"), report.Errors.Length, job.Output));
                foreach (string error in report.Errors) Console.Error.WriteLine(text.ErrorMessage(error));
            }
        }
        if (results.Count == 0) throw new UnknownFormatException();
        if (json) Emit(results);
        return failed ? 3 : 0;
    }
}
