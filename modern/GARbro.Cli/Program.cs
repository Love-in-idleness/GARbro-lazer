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

internal static class Program
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
            string output = Get("-o", Get("--output"));
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
            if (Get("--schemes")is string schemes)
            {
                var imported = ArchiveService.LoadSchemes(schemes);
                if (imported.Failed.Count > 0)
                    throw new InvalidDataException("Cannot import schemes for: " + string.Join(", ", imported.Failed.Keys));
            }

            var provider = new JsonOptionsProvider(Get("--options")is string config ? File.ReadAllText(config) : null, Get("--scheme"));
            if(Get("--encoding") is string encoding) new DecoderContext(encoding,Language:text.Language).Apply();
            if (command == "formats")
            {
                if(Get("--type") is string type && type is not ("archive" or "image" or "audio" or "script"))throw new ArgumentException("Invalid resource type: "+type);
                var formats = ArchiveService.Formats().Where(f => Get("--type") == null || f.Type == Get("--type")).ToArray();
                if (json)
                    Emit(formats);
                else
                    foreach (var format in formats)
                        Console.WriteLine($"{format.Type}\t{format.Tag}\t{format.Description}");
                return 0;
            }

            if (command == "schemes")
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
            if(command=="extract" && output==null)throw new ArgumentException("extract requires -o DIRECTORY.");
            if (command == "convert")
            {
                if (files.Count != 1 || output == null)
                    throw new ArgumentException("convert requires one input and an output file.");
                var target=MediaOutputFormats.Normalize(Get("--to",Path.GetExtension(output).TrimStart('.')));
                string input=Path.GetFullPath(files[0]);output=Path.GetFullPath(output);
                string entry=Get("--entry");
                bool script=ScriptOutputFormats.Supports(target);
                if(entry!=null && !script)
                    throw new ArgumentException("--entry is only supported for script conversion.");
                if(input.Equals(output,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
                    throw new IOException("Cannot replace a source document. Choose another destination.");
                if (script || target is "WAV" or "FLAC" or "M4A" or "MP4" or "MKV" or "AUTO" or "AUDIO")
                {
                    if(Get("--format")!=null)throw new ArgumentException("Forced input formats are only supported for image conversion.");
                    using var resource=new ResourceSession(new(entry==null?Path.GetDirectoryName(input):input),provider);
                    var result=ResourceService.ConvertFile(resource,entry??Path.GetFileName(input),output,target,overwrite,maxEntry,cancellation:cancel.Token);
                    if (json)
                        Emit(new { input = files[0], output=result.Path, format=target });
                    else
                        Console.WriteLine(result.Path);
                }
                else
                {
                    ResourceService.ResolveImageOutput(target);
                    var image = ArchiveService.ReadImage(files[0], Get("--format"), maxPixels,provider);
                    ArchiveService.WriteImage(image, output, target, overwrite,maxEntry);
                    if (json)
                        Emit(new { input = files[0], output, width = image.Width, height = image.Height, image.OffsetX, image.OffsetY });
                    else
                        Console.WriteLine(output);
                }

                return 0;
            }

            if(command=="extract" && (nested || values.ContainsKey("--image-format") || values.ContainsKey("--audio-format") || values.ContainsKey("--video-format")))
            {
                if(output==null)throw new ArgumentException("extract requires -o DIRECTORY.");
                if(Get("--format")!=null)throw new ArgumentException("Forced input formats cannot be combined with nested or media extraction.");
                var reports=new List<ResourceJobReport>();
                long remaining=maxTotal;
                int entriesRemaining=262144;
                var extraction=new ResourceExtractionOptions(Recursive:nested,MaxDepth:maxDepth,
                    ImageFormat:Get("--image-format"),AudioFormat:Get("--audio-format"),VideoFormat:Get("--video-format"),RecurseDirectories:recursive,
                    DiscardChildArchives:invocation.Flags.Contains("--discard-child-archives"),DiscardOriginalMedia:invocation.Flags.Contains("--discard-original-media"));
                string Destination(string source)=>files.Count==1 && !nested?output:Path.Combine(output,Path.GetFileName(Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar))+(Directory.Exists(source)?"":".unpacked"));
                var destinations=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(string source in files)
                {
                    if(Directory.Exists(source)&&!recursive)throw new ArgumentException("A directory input requires --recursive.");
                    string destination=Destination(source);
                    if(!destinations.Add(Path.GetFullPath(destination)))throw new IOException("Input roots have the same destination: "+destination);
                }
                foreach(string source in files)
                {
                    cancel.Token.ThrowIfCancellationRequested();
                    if(entriesRemaining<=0)throw new InvalidDataException("Recursive extraction has too many entries.");
                    string root=Destination(source);
                    var report=ResourceService.Extract(new ResourceLocation(Path.GetFullPath(source)),root,
                        extraction,new ExtractionLimits(maxEntry,remaining,maxRatio,entriesRemaining),
                        overwrite,null,provider,null);
                    remaining-=report.Bytes;entriesRemaining-=report.Entries;reports.Add(report);
                    if(!json)
                    {
                        Console.WriteLine(report.Files.Length==0 && report.Errors.Length==0?text["NoExtractableFiles"]:text.Format("Cli.NestedResult",source,report.Files.Length,report.Errors.Length,root));
                        foreach(string failure in report.Errors)Console.Error.WriteLine(text.ErrorMessage(failure));
                    }
                }
                if(json)Emit(reports);
                return reports.Any(r=>r.Errors.Length>0)?3:0;
            }
            var inputs = new List<string>();
            var discovered=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                if (Directory.Exists(file))
                {
                    if (!recursive)
                        throw new ArgumentException("A directory input requires --recursive.");
                    string sourceRoot=Path.GetFullPath(file),outputRoot=output==null?null:Path.GetFullPath(output);
                    foreach(string found in Directory.EnumerateFiles(file,"*",new EnumerationOptions {RecurseSubdirectories=true,MaxRecursionDepth=maxDepth,AttributesToSkip=FileAttributes.ReparsePoint}).OrderBy(x=>x,StringComparer.Ordinal))
                    {
                        if(outputRoot!=null && outputRoot!=sourceRoot && Path.GetFullPath(found).StartsWith(outputRoot+Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))continue;
                        string relative=Path.GetRelativePath(sourceRoot,Path.GetFullPath(found));
                        discovered[found]=files.Count==1?relative:Path.Combine(new DirectoryInfo(sourceRoot).Name,relative);
                        inputs.Add(found);
                    }
                }
                else
                    inputs.Add(file);
            }

            if (inputs.Count == 0)
                throw new ArgumentException("No input files were found.");
            var results = new List<object>();
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var limits = new ExtractionLimits(maxEntry,maxTotal,maxRatio);
            long remainingBytes=maxTotal;int remainingEntries=limits.MaxEntries;
            ArchiveSession OpenOrSkip(string file)
            {
                try {return ArchiveService.Open(file,Get("--format"),provider);}
                catch(UnknownFormatException)when(discovered.ContainsKey(file))
                {if(!json)Console.Error.WriteLine(text.Format("Cli.SkippedUnrecognized",file));return null;}
            }
            foreach (var file in inputs)
            {
                cancel.Token.ThrowIfCancellationRequested();
                if (command == "probe")
                {
                    if(GARbro.Media.MediaInfo.IsVideoName(file))
                    {
                        var video=ArchiveService.ProbeResource(file,provider);results.Add(video);
                        if(!json)Console.WriteLine(JsonSerializer.Serialize(video,Json));
                        continue;
                    }
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
                        try
                        {
                            var result=ArchiveService.ProbeResource(file,provider);results.Add(result);
                            if(!json)Console.WriteLine(JsonSerializer.Serialize(result,Json));
                        }
                        catch(UnknownFormatException)when(discovered.ContainsKey(file))
                        {if(!json)Console.Error.WriteLine(text.Format("Cli.SkippedUnrecognized",file));}
                    }

                    continue;
                }

                using var archive=OpenOrSkip(file);
                if(archive==null)continue;
                switch (command)
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
                        var root=discovered.TryGetValue(file,out string relativeOutput)?ExtractionPath.Resolve(output,relativeOutput+".unpacked"):
                            inputs.Count==1?output:Path.Combine(output,Path.GetFileName(file));
                        if (!roots.Add(Path.GetFullPath(root)))
                            throw new IOException("Batch output directories collide: " + root);
                        if(remainingEntries<archive.Entries.Count)throw new InvalidDataException("Recursive extraction has too many entries.");
                        var extracted = await archive.ExtractAsync(root, overwrite, limits with {MaxTotalBytes=remainingBytes,MaxEntries=remainingEntries}, cancellation: cancel.Token);
                        remainingBytes-=extracted.Where(f=>f.Status=="extracted").Sum(f=>f.Size);remainingEntries-=archive.Entries.Count;
                        results.Add(new { input = file, format = archive.Tag, output = root, files = extracted });
                        if (!json)
                            Console.WriteLine(text.Format("Cli.ExtractResult", file, extracted.Count, root));
                        break;
                    default:
                        throw new ArgumentException("Unknown command: " + command);
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
