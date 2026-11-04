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

internal static class Program
{
    static ResourceText text = new();
    static string SelectLanguage(ref string[] args)
    {
        string language = "auto";
        var remaining = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] != "--language") { remaining.Add(args[i]); continue; }
            if (++i >= args.Length) throw new ArgumentException("Incomplete option: --language");
            language = args[i];
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
            double timeout=300;
            long memory=1L<<30;
            var forwarded=new List<string> {"--garbro-isolated-core"};
            if(args.Length>0)forwarded.Add(args[0]);
            for(int i=1;i<args.Length;i++)
            {
                string argument=args[i];
                if(argument is "--timeout" or "--max-memory")
                {
                    if(i+1>=args.Length) throw new ArgumentException("Incomplete option: "+argument);
                    string value=args[++i];
                    if(argument=="--max-memory")memory=long.Parse(value,CultureInfo.InvariantCulture);
                    else
                    {
                        timeout=double.Parse(value,CultureInfo.InvariantCulture);
                        forwarded.Add(argument);forwarded.Add(value);
                    }
                }
                else
                {
                    forwarded.Add(argument);
                    if(argument.StartsWith('-') && argument is not ("--json" or "--recursive" or "--nested") && i+1<args.Length)
                        forwarded.Add(args[++i]);
                }
            }
            forwarded.Add("--language"); forwarded.Add(language);
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
            if(args.Contains("--json"))Emit(new {error=error.GetType().Name,message=error.Message,exitCode=code});
            else Console.Error.WriteLine(text.ErrorMessage(error.Message));
            return code;
        }
    }

    static async Task<int> RunCore(string[] args)
    {
        bool json = args.Contains("--json");
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        try
        {
            SelectLanguage(ref args);
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                Console.WriteLine(text["Cli.Help"]);
                return 0;
            }

            var values = new Dictionary<string, string>();
            var files = new List<string>();
            bool recursive = false, nested = false;
            var allowed = new HashSet<string>
            {
                "-o",
                "--output",
                "--format",
                "--type",
                "--to",
                "--overwrite",
                "--options",
                "--scheme",
                "--max-depth",
                "--max-entry",
                "--max-total",
                "--max-ratio",
                "--timeout",
                "--schemes",
                "--max-pixels"
                ,"--encoding"
            };
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--json")
                    continue;
                if (args[i] == "--recursive")
                {
                    recursive = true;
                    continue;
                }
                if (args[i] == "--nested") { nested = true; continue; }

                if (args[i].StartsWith('-'))
                {
                    if (!allowed.Contains(args[i]) || i + 1 >= args.Length)
                        throw new ArgumentException("Invalid or incomplete option: " + args[i]);
                    string name = args[i];
                    values[name] = args[++i];
                }
                else
                    files.Add(args[i]);
            }

            string Get(string key, string fallback = null) => values.TryGetValue(key, out var value) ? value : fallback;
            long Number(string key, long fallback) => values.TryGetValue(key, out var value) ? long.Parse(value, CultureInfo.InvariantCulture) : fallback;
            string output = Get("-o", Get("--output"));
            var overwrite = Get("--overwrite", "fail") switch
            {
                "fail" => OverwritePolicy.Fail,
                "skip" => OverwritePolicy.Skip,
                "replace" => OverwritePolicy.Replace,
                _ => throw new ArgumentException("Invalid overwrite policy.")};
            if (values.ContainsKey("--timeout"))
                cancel.CancelAfter(TimeSpan.FromSeconds(double.Parse(Get("--timeout"),CultureInfo.InvariantCulture)));
            if (Get("--schemes")is string schemes)
            {
                var imported = ArchiveService.LoadSchemes(schemes);
                if (imported.Failed.Count > 0)
                    throw new InvalidDataException("Cannot import schemes for: " + string.Join(", ", imported.Failed.Keys));
            }

            var provider = new JsonOptionsProvider(Get("--options")is string config ? File.ReadAllText(config) : null, Get("--scheme"));
            if(Get("--encoding") is string encoding) new DecoderContext(encoding).Apply();
            if (args[0] == "formats")
            {
                var formats = ArchiveService.Formats().Where(f => Get("--type") == null || f.Type == Get("--type")).ToArray();
                if (json)
                    Emit(formats);
                else
                    foreach (var format in formats)
                        Console.WriteLine($"{format.Type}\t{format.Tag}\t{format.Description}");
                return 0;
            }

            if (args[0] == "schemes")
            {
                if (files.Count != 1)
                    throw new ArgumentException("schemes requires one exported JSON file.");
                var report = ArchiveService.LoadSchemes(files[0]);
                if (json)
                    Emit(report);
                else
                    Console.WriteLine(text.Format("Cli.Applied", report.Applied.Length, report.Unported.Length, report.Failed.Count));
                return report.Failed.Count == 0 ? 0 : 3;
            }

            if (files.Count == 0)
                throw new ArgumentException("At least one input file is required.");
            if (args[0] == "convert")
            {
                if (files.Count != 1 || output == null)
                    throw new ArgumentException("convert requires one input and an output file.");
                var target = Get("--to", Path.GetExtension(output).TrimStart('.').ToUpperInvariant());
                if (target == "WAV")
                {
                    ArchiveService.ConvertAudio(files[0], output, overwrite, cancel.Token, Number("--max-entry", 2L << 30));
                    if (json)
                        Emit(new { input = files[0], output, format = "WAV" });
                    else
                        Console.WriteLine(output);
                }
                else
                {
                    var image = ArchiveService.ReadImage(files[0], Get("--format"), Number("--max-pixels", 100000000));
                    if (target == "JPG")
                        target = "JPEG";
                    if (target == "TIF")
                        target = "TIFF";
                    ArchiveService.WriteImage(image, output, target, overwrite);
                    if (json)
                        Emit(new { input = files[0], output, width = image.Width, height = image.Height, image.OffsetX, image.OffsetY });
                    else
                        Console.WriteLine(output);
                }

                return 0;
            }

            if(nested)
            {
                if(args[0]!="extract" || output==null) throw new ArgumentException("--nested requires extract and -o DIRECTORY.");
                var reports=new List<ResourceJobReport>();
                long remaining=Number("--max-total",16L<<30);
                int entriesRemaining=262144;
                var destinations=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(string source in files)
                {
                    string destination=Path.Combine(output,Path.GetFileName(Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar))+(Directory.Exists(source)?"":".unpacked"));
                    if(!destinations.Add(Path.GetFullPath(destination)))throw new IOException("Input roots have the same destination: "+destination);
                }
                foreach(string source in files)
                {
                    cancel.Token.ThrowIfCancellationRequested();
                    bool directory=Directory.Exists(source);
                    if(directory&&!recursive)throw new ArgumentException("A directory input requires --recursive.");
                    string root=Path.Combine(output,Path.GetFileName(Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar))+(directory?"":".unpacked"));
                    var report=ResourceService.Extract(new ResourceLocation(Path.GetFullPath(source)),root,
                        new ResourceExtractionOptions(true,checked((int)Number("--max-depth",8))),
                        new ExtractionLimits(Number("--max-entry",2L<<30),remaining,double.Parse(Get("--max-ratio","10000"),CultureInfo.InvariantCulture),entriesRemaining),
                        overwrite,null,provider,null);
                    remaining-=report.Bytes;entriesRemaining-=report.Entries;reports.Add(report);
                    if(!json)Console.WriteLine(text.Format("Cli.NestedResult", source, report.Files.Length, report.Errors.Length, root));
                }
                if(json)Emit(reports);
                return reports.Any(r=>r.Errors.Length>0)?3:0;
            }
            var inputs = new List<string>();
            foreach (var file in files)
            {
                if (Directory.Exists(file))
                {
                    if (!recursive)
                        throw new ArgumentException("A directory input requires --recursive.");
                    inputs.AddRange(Directory.EnumerateFiles(file, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = checked((int)Number("--max-depth", 8)), AttributesToSkip = FileAttributes.ReparsePoint }).OrderBy(x => x, StringComparer.Ordinal));
                }
                else
                    inputs.Add(file);
            }

            if (inputs.Count == 0)
                throw new ArgumentException("No input files were found.");
            var results = new List<object>();
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var limits = new ExtractionLimits(Number("--max-entry", 2L << 30), Number("--max-total", 16L << 30), double.Parse(Get("--max-ratio", "10000"), CultureInfo.InvariantCulture));
            foreach (var file in inputs)
            {
                cancel.Token.ThrowIfCancellationRequested();
                if (args[0] == "probe")
                {
                    try
                    {
                        using var probe = ArchiveService.Open(file, Get("--format"), provider);
                        var result = new
                        {
                            input = file,
                            type = "archive",
                            format = probe.Tag,
                            count = probe.Entries.Count
                        };
                        results.Add(result);
                        if (!json)
                            Console.WriteLine(text.Format("Cli.ProbeResult", file, probe.Tag, probe.Entries.Count));
                    }
                    catch (UnknownFormatException)
                    {
                        var result = ArchiveService.ProbeResource(file);
                        results.Add(result);
                        if (!json)
                            Console.WriteLine(JsonSerializer.Serialize(result, Json));
                    }

                    continue;
                }

                using var archive = ArchiveService.Open(file, Get("--format"), provider);
                switch (args[0])
                {
                    case "probe":
                        results.Add(new { input = file, type = "archive", format = archive.Tag, count = archive.Entries.Count });
                        if (!json)
                            Console.WriteLine(text.Format("Cli.ProbeResult", file, archive.Tag, archive.Entries.Count));
                        break;
                    case "list":
                        results.Add(new { input = file, format = archive.Tag, entries = archive.Entries });
                        if (!json)
                            foreach (var entry in archive.Entries)
                                Console.WriteLine($"{entry.Size, 12}\t{entry.Name}");
                        break;
                    case "extract":
                        if (output == null)
                            throw new ArgumentException("extract requires -o DIRECTORY.");
                        var root = inputs.Count == 1 ? output : Path.Combine(output, Path.GetFileName(file));
                        if (!roots.Add(Path.GetFullPath(root)))
                            throw new IOException("Batch output directories collide: " + root);
                        var extracted = await archive.ExtractAsync(root, overwrite, limits, cancellation: cancel.Token);
                        results.Add(new { input = file, format = archive.Tag, output = root, files = extracted });
                        if (!json)
                            Console.WriteLine(text.Format("Cli.ExtractResult", file, extracted.Count, root));
                        break;
                    default:
                        throw new ArgumentException("Unknown command: " + args[0]);
                }
            }

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
