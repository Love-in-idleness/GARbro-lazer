using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GARbro.Application;
using GameRes;
using GARbro.Domain;

internal static partial class Program
{
    static ResourceText text = new();
    static string SelectLanguage(ref string[] args)
    {
        string language = "auto";
        var remaining = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if(args[i]=="--"){remaining.AddRange(args.Skip(i));break;}
            if(args[i].StartsWith("--language=",StringComparison.Ordinal))language=args[i][11..];
            else
            {
                if(args[i]!="--language"){remaining.Add(args[i]);continue;}
                if(++i>=args.Length)throw new ArgumentException("Incomplete option: --language");
                language=args[i];
            }
            if (!GARbro.Domain.LanguageCatalog.IsSupported(language)) throw new ArgumentException("Invalid language: " + language);
        }
        text = new(language);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(text.Language);
        args = remaining.ToArray();
        return text.Language;
    }
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    static void Emit(object value) => Console.WriteLine(JsonSerializer.Serialize(value, Json));
    static bool JsonRequested(string[] args)=>args.TakeWhile(a=>a!="--").Contains("--json");
    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding=new System.Text.UTF8Encoding(false);
        if(args.FirstOrDefault()=="--garbro-isolated-core")
        {
            WorkerTemporaryFiles.InitializeFromEnvironment();
            return await RunCore(args.Skip(1).ToArray());
        }
        using var cancel=new CancellationTokenSource();
        Console.CancelKeyPress+=(s,e)=>{e.Cancel=true;cancel.Cancel();};
        try
        {
            string language = SelectLanguage(ref args);
            var invocation=CliArguments.Parse(args);
            if(invocation.Flags.Contains("--version"))
            {
                string version=typeof(Program).Assembly.GetName().Version.ToString(3);
                if(invocation.Flags.Contains("--json"))Emit(new {version});else Console.WriteLine(version);
                return 0;
            }
            if(invocation.Help){ShowHelp(invocation.Command);return 0;}
            double timeout=invocation.Values.TryGetValue("--timeout",out var seconds)?double.Parse(seconds,CultureInfo.InvariantCulture):300;
            long memory=invocation.Values.TryGetValue("--max-memory",out var bytes)?long.Parse(bytes,CultureInfo.InvariantCulture):1L<<30;
            var forwarded=new[]{"--garbro-isolated-core","--language",language}.Concat(args).ToArray();
            var run=await WorkerSupervisor.RunManagedAsync(typeof(Program).Assembly.Location,forwarded,
                new WorkerLimits(timeout,memory),cancellation:cancel.Token,
                output:Console.WriteLine,error:Console.Error.WriteLine);
            if(run.ExitCode is >=0 and <=7 or 130)return run.ExitCode;
            throw new WorkerOperationException("WorkerExited","Decoder worker exited unexpectedly.");
        }
        catch(Exception error)
        {
            int code=error switch {
                OperationCanceledException=>130,
                WorkerLimitException {Reason:"timeout"}=>130,
                WorkerLimitException=>3,
                ArgumentException or FormatException or OverflowException=>2,
                _=>7};
            if(JsonRequested(args))Emit(new {error=error.GetType().Name,message=error.Message,exitCode=code});
            else Console.Error.WriteLine(text.ErrorMessage(error.Message));
            return code;
        }
    }

    static void ShowHelp(string command)=>Console.WriteLine(text[string.IsNullOrEmpty(command)?"Cli.Help":"Cli.Help."+command]);

    static string FormatId(FormatInfo format) => format == null ? null : format.Assembly + ":" + format.Class;

    static FormatInfo InputReader(string selector, string requiredType)
    {
        if (selector == null) return null;
        var matches = ArchiveService.Formats().Where(f => f.Type is "archive" or "image")
            .Where(f => requiredType == null || f.Type == requiredType)
            .Where(f => FormatId(f) == selector || f.Tag.Equals(selector, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) throw new ArgumentException("Unknown input format: " + selector);
        if (matches.Length > 1) throw new ArgumentException("Ambiguous input format. Use assembly:class from formats --json: " + selector);
        return matches[0];
    }

    static async Task<int> RunCore(string[] args)
    {
        bool json = JsonRequested(args);
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        try
        {
            SelectLanguage(ref args);
            var invocation=CliArguments.Parse(args);
            string command=invocation.Command;
            if(invocation.Help){ShowHelp(command);return 0;}
            var values=invocation.Values;
            var files=invocation.Inputs.ToList();
            bool recursive=invocation.Flags.Contains("--recursive"),nested=invocation.Flags.Contains("--nested");
            string Get(string key, string fallback = null) => values.TryGetValue(key, out var value) ? value : fallback;
            long Number(string key, long fallback) => values.TryGetValue(key, out var value) ? long.Parse(value, CultureInfo.InvariantCulture) : fallback;
            string output = Get("--output");
            int maxDepth=checked((int)Number("--max-depth",8));
            long maxEntry=Number("--max-entry",2L<<30),maxTotal=Number("--max-total",16L<<30),maxPixels=Number("--max-pixels",100000000);
            double maxRatio=double.Parse(Get("--max-ratio","10000"),CultureInfo.InvariantCulture);
            if(maxDepth is <0 or >64 || maxEntry<0 || maxTotal<0 || maxPixels<=0 || !double.IsFinite(maxRatio) || maxRatio<=0)
                throw new ArgumentException("Invalid extraction limits.");
            var overwrite = Get("--overwrite", "fail") switch
            {
                "fail" => OverwritePolicy.Fail,
                "skip" => OverwritePolicy.Skip,
                "replace" => OverwritePolicy.Replace,
                _ => throw new ArgumentException("Invalid overwrite policy.")};
            if (values.ContainsKey("--timeout"))
                cancel.CancelAfter(TimeSpan.FromSeconds(double.Parse(Get("--timeout"),CultureInfo.InvariantCulture)));
            if (command == "formats")
            {
                string type = Get("--type");
                if (type is not (null or "archive" or "image" or "audio" or "script" or "video"))
                    throw new ArgumentException("Invalid resource type: " + type);
                var formats = type == "video" ? MediaOutputFormats.Video.Select(tag =>
                    new FormatInfo(tag, "video", tag == "MKV" ? text["VideoMkv"] : "MP4 (MPEG-4/AAC)", true, null, null)).ToArray() :
                    ArchiveService.Formats().Where(format => type == null || type == format.Type).ToArray();
                if (json) Emit(formats);
                else foreach (var format in formats) Console.WriteLine($"{format.Type}\t{format.Tag}\t{format.Description}");
                return 0;
            }

            if (command == "schemes")
            {
                if (invocation.Flags.Contains("--reset"))
                {
                    SchemeDatabase.Reset();
                    if (json) Emit(new { reset = true });
                    else Console.WriteLine(text["Cli.SchemesReset"]);
                    return 0;
                }
                if (files.Count != 1)
                    throw new ArgumentException("schemes requires one exported JSON file.");
                bool check = invocation.Flags.Contains("--check");
                var report = check ? ArchiveService.LoadSchemes(files[0]) : SchemeDatabase.Install(files[0], cancel.Token);
                if (json) Emit(report);
                else
                {
                    Console.WriteLine(text.Format("Cli.Applied", report.Applied.Length, report.Unported.Length, report.Failed.Count));
                    if (!check && report.Failed.Count == 0) Console.WriteLine(text["Cli.SchemesSaved"]);
                }
                return report.Failed.Count == 0 ? 0 : 3;
            }

            if (files.Count == 0) throw new ArgumentException("At least one input file is required.");
            if ((Get("--schemes") ?? SchemeDatabase.Active()) is string schemes)
            {
                var report = ArchiveService.LoadSchemes(schemes);
                if (report.Failed.Count > 0)
                    throw new InvalidDataException("Cannot import schemes for: " + string.Join(", ", report.Failed.Keys));
            }
            string optionsJson = Get("--options") is string config ? File.ReadAllText(config) : null;
            var provider = new JsonOptionsProvider(optionsJson, Get("--scheme"));
            if (Get("--encoding") is string encoding) new DecoderContext(encoding, Language: text.Language).Apply();
            string scriptFormat = Get("--script-format")?.Trim().ToUpperInvariant();
            bool convertScripts = scriptFormat != null;
            if (scriptFormat == "EXPORT") scriptFormat = null;
            if (scriptFormat is not (null or "RB") && !ScriptOutputFormats.Recompile.Contains(scriptFormat))
                throw new ArgumentException("Unsupported script output format.");
            bool discardOriginal = invocation.Flags.Contains("--discard-original-media");
            bool adjustOffset = invocation.Flags.Contains("--adjust-offset");
            if (command == "create")
            {
                if (output == null) throw new ArgumentException("create requires -o ARCHIVE.");
                var paths=files.Select(Path.GetFullPath).ToArray();
                var directories=paths.Select(Directory.Exists).ToArray();
                string source=ArchivePackingPlan.CommonRoot(paths,directories);
                var comparer=OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal;
                string[] selection=paths.Where((path,index)=>directories[index] && comparer.Equals(Path.TrimEndingDirectorySeparator(path),source)).Any()
                    ?null:paths.Select(path=>Path.GetRelativePath(source,path)).ToArray();
                var media = new ArchivePackingOptions(Get("--image-format"), Get("--audio-format"), Get("--video-format"), discardOriginal, convertScripts, scriptFormat);
                var decoder=new ScopedOptionsProvider(new("resource-pack",source,Context:new DecoderContext(Get("--encoding","auto"),Language:text.Language)));
                var result=ResourceService.PackArchive(source,selection,output,Get("--format"),media,optionsJson,
                    Get("--scheme"),overwrite,new ExtractionLimits(maxEntry,maxTotal),decoder,cancellation:cancel.Token);
                if(json)Emit(new {output=result.Output,format=result.Format,written=result.Written,entries=result.Entries,converted=result.Converted,bytes=result.Bytes});
                else Console.WriteLine(result.Written?text.Format("PackingCompleted",result.Entries,result.Converted)+" · "+result.Output:text["PackingSkipped"]+" · "+result.Output);
                return 0;
            }
            if (command == "extract")
            {
                if (output == null) throw new ArgumentException("extract requires -o DIRECTORY.");
                var format = InputReader(Get("--format"), "archive");
                var options = new ResourceExtractionOptions(Recursive: nested, MaxDepth: maxDepth,
                    ImageFormat: Get("--image-format"), AudioFormat: Get("--audio-format"), VideoFormat: Get("--video-format"),
                    RecurseDirectories: recursive, DiscardChildArchives: invocation.Flags.Contains("--discard-child-archives"),
                    DiscardOriginalMedia: discardOriginal, AdjustImageOffset: adjustOffset, ConvertScripts: convertScripts, ScriptFormat: scriptFormat);
                return Extract(files, output, FormatId(format), options, new(maxEntry, maxTotal, maxRatio), overwrite, provider, json, cancel.Token);
            }
            if (command == "convert")
            {
                if (files.Count != 1 || output == null)
                    throw new ArgumentException("convert requires one input and an output file.");
                var target = MediaOutputFormats.Normalize(Get("--to", ScriptOutputFormats.FromOutputName(output) ?? Path.GetExtension(output).TrimStart('.')));
                string input = Path.GetFullPath(files[0]); output = Path.GetFullPath(output);
                string entry = Get("--entry");
                bool image = !ScriptOutputFormats.Supports(target) && target != "AUDIO" &&
                    !MediaOutputFormats.Audio.Contains(target) && !MediaOutputFormats.Video.Contains(target);
                var format = InputReader(Get("--format"), entry != null ? "archive" : "image");
                if (adjustOffset && !image) throw new ArgumentException("Image offsets require an image output format.");
                if (input.Equals(output, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new IOException("Cannot replace a source document. Choose another destination.");
                if (image) ResourceService.ResolveImageOutput(target);
                string writtenPath;
                bool written;
                if (entry != null || !image)
                {
                    if (entry == null && format != null) throw new ArgumentException("Forced input formats are only supported for image conversion.");
                    using var resource = new ResourceSession(new(entry == null ? Path.GetDirectoryName(input) : input, InputFormatId: FormatId(format)), provider);
                    var result = ResourceService.ConvertFile(resource, entry ?? Path.GetFileName(input), output, target, overwrite,
                        maxEntry, adjustImageOffset: adjustOffset, cancellation: cancel.Token, maxPixels: maxPixels);
                    writtenPath = result.Path; written = result.Written;
                    if (json) Emit(new { input = files[0], output = writtenPath, format = target, written });
                }
                else
                {
                    var data = ArchiveService.ReadImage(input, FormatId(format), maxPixels, provider);
                    if (adjustOffset) data = ResourceService.AdjustOffset(data, maxPixels);
                    writtenPath = output; written = ArchiveService.WriteImage(data, output, target, overwrite, maxEntry);
                    if (json) Emit(new { input = files[0], output, width = data.Width, height = data.Height, data.OffsetX, data.OffsetY, written });
                }
                if (!json) Console.WriteLine(written ? writtenPath : text.Format("Cli.ConversionSkipped", writtenPath));
                return 0;
            }

            var reader = InputReader(Get("--format"), command == "list" ? "archive" : null);
            var inputs = new List<string>();
            var discovered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                if (Directory.Exists(file))
                {
                    if (!recursive)
                        throw new ArgumentException("A directory input requires --recursive.");
                        foreach(string found in Directory.EnumerateFiles(file,"*",new EnumerationOptions {RecurseSubdirectories=true,MaxRecursionDepth=maxDepth,AttributesToSkip=FileAttributes.ReparsePoint}).OrderBy(x=>x,StringComparer.Ordinal))
                    {
                        discovered.Add(found);
                        inputs.Add(found);
                    }
                }
                else
                    inputs.Add(file);
            }

            if (inputs.Count == 0)
                throw new ArgumentException("No input files were found.");
            var results = new List<object>();
            void ProbeMedia(string file, string formatId = null)
            {
                var details = ArchiveService.ProbeResource(file, provider, formatId);
                results.Add(details);
                if (!json) Console.WriteLine(JsonSerializer.Serialize(details, Json));
            }
            foreach (var file in inputs)
            {
                cancel.Token.ThrowIfCancellationRequested();
                try
                {
                    if (command == "probe" && (reader?.Type == "image" ||
                        reader == null && GARbro.Media.MediaInfo.IsVideoName(file)))
                    {
                        ProbeMedia(file, FormatId(reader));
                        continue;
                    }

                    ArchiveSession archive;
                    try { archive = ArchiveService.Open(file, FormatId(reader), provider); }
                    catch (UnknownFormatException) when (command == "probe" && reader == null)
                    {
                        ProbeMedia(file);
                        continue;
                    }
                    using (archive)
                    {
                        if (command == "probe")
                        {
                            results.Add(new { input = file, type = "archive", format = archive.Tag, count = archive.Entries.Count });
                            if (!json) Console.WriteLine(text.Format("Cli.ProbeResult", file, archive.Tag, archive.Entries.Count));
                        }
                        else
                        {
                            results.Add(new { input = file, format = archive.Tag, entries = archive.Entries });
                            if (!json) foreach (var entry in archive.Entries) Console.WriteLine($"{entry.Size, 12}\t{entry.Name}");
                        }
                    }
                }
                catch (UnknownFormatException) when (discovered.Contains(file))
                {
                    if (!json) Console.Error.WriteLine(text.Format("Cli.SkippedUnrecognized", file));
                }
            }

            if(results.Count==0)throw new UnknownFormatException();
            if (json)
                Emit(results);
            return 0;
        }
        catch (Exception error)
        {
            int code = error switch
            {
                OperationCanceledException => 130,
                MissingOptionsException or UnknownEncryptionScheme or InvalidEncryptionScheme => 4,
                PlatformNotSupportedException or DllNotFoundException => 6,
                UnknownFormatException => 1,
                ArgumentException or FormatException or OverflowException => 2,
                InvalidDataException or InvalidFormatException or EndOfStreamException => 3,
                IOException or UnauthorizedAccessException => 5,
                _ => 7
            };
            if (json)
                Emit(new { error = error.GetType().Name, message = error.Message, exitCode = code });
            else
                Console.Error.WriteLine(text.ErrorMessage(error.Message));
            return code;
        }
    }
}
