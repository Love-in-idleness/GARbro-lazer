using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameRes;
using GARbro.Media;
using GARbro.Domain;
using Newtonsoft.Json;
using NAudio.Wave;

namespace GARbro.Application;

public static partial class ResourceService
{
    sealed record ExtractionNode(ResourceLocation Location,string Output,int Depth,string[] Selection,bool Required,
        string Copy=null,bool FromDirectory=false,bool DefiniteArchive=false);

    public static ResourceJobReport Extract(ResourceLocation location, string output, ResourceExtractionOptions options,
        ExtractionLimits limits, OverwritePolicy overwrite, string[] selection, IOptionsProvider provider,
        IProgress<ExtractionProgress> progress, string[] completedPaths = null)
    {
        options ??= new(); limits ??= new();
        if (options.MaxDepth is < 0 or > 64 || limits.MaxEntries < 1 || limits.MaxEntryBytes < 0 || limits.MaxTotalBytes < 0
            || !double.IsFinite(limits.MaxExpansionRatio) || limits.MaxExpansionRatio <= 0) throw new ArgumentException("Invalid extraction limits.");
        if(options.Types is {Length:0})throw new ArgumentException("Select at least one resource type.");
        string audioFormat=options.AudioFormat ?? (options.ConvertAudio?"AUTO":null);
        if(audioFormat!=null && !MediaOutputFormats.Audio.Contains(audioFormat))throw new ArgumentException("Unsupported media output format.");
        if(options.VideoFormat!=null && !MediaOutputFormats.Video.Contains(options.VideoFormat))throw new ArgumentException("Unsupported media output format.");
        var files=new List<ExtractedFile>();var errors=new List<string>();var queue=new Queue<ExtractionNode>();
        var removals=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claimed=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var directories=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        output=Path.GetFullPath(output);
        static bool Inside(string root,string path)
        {
            string relative=Path.GetRelativePath(root,path);
            return !Path.IsPathRooted(relative) && relative!=".." && !relative.StartsWith(".."+Path.DirectorySeparatorChar);
        }
        bool inPlace=System.IO.Directory.Exists(location.RootPath) && Path.GetRelativePath(location.RootPath,output)==".";
        bool outputWithinInput=System.IO.Directory.Exists(location.RootPath) && !inPlace && Inside(location.RootPath,output);
        var completed=new HashSet<string>(completedPaths??Array.Empty<string>(),StringComparer.OrdinalIgnoreCase);
        queue.Enqueue(new(location,output,0,selection,true));
        long total=0;int count=0;
        foreach(string path in completed)
        {
            if(!Inside(output,path))throw new IOException("Invalid retry output.");
            ExtractionPath.RejectLinks(output,path);if(File.Exists(path))total=checked(total+new FileInfo(path).Length);
        }
        if(total>limits.MaxTotalBytes)throw new InvalidDataException("Previous outputs exceed the job limit.");
        void Claim(string path,string source)
        {
            if(claimed.TryGetValue(path,out var previous) && previous!=source)throw new IOException("Colliding archive paths: "+path);
            claimed[path]=source;
        }
        while(queue.TryDequeue(out var task))
        {
            try
            {
                bool forceFile=task.FromDirectory && Path.GetDirectoryName(task.Location.RootPath)==location.RootPath && options.TypeOverrides?.ContainsKey(Path.GetFileName(task.Location.RootPath))==true;
                using var session=new ResourceSession(task.Location,provider,allowPlainFile:task.Copy==null,forcePlainFile:forceFile);
                if((session.Physical && task.Depth>0) || (!session.Physical && !session.Synthetic && (task.FromDirectory || task.Copy!=null)))
                {
                    string identity=task.Location.RootPath+"\0"+string.Join("\0",task.Location.Chain);
                    if(directories.TryGetValue(task.Output,out var owner) && owner!=identity)throw new IOException("Nested archive output collides with an existing entry: "+task.Output);
                    directories[task.Output]=identity;
                }
                if(session.Physical)
                {
                    foreach(var item in new DirectoryInfo(task.Location.RootPath).EnumerateFileSystemInfos().OrderBy(f=>f.Name,StringComparer.Ordinal))
                    {
                        if(task.Selection!=null && !task.Selection.Contains(item.Name,StringComparer.Ordinal))continue;
                        if((item.Attributes&FileAttributes.ReparsePoint)!=0)continue;
                        if(item is DirectoryInfo)
                        {
                            if(outputWithinInput && Inside(output,item.FullName) || inPlace && item.Name.EndsWith(".unpacked",StringComparison.OrdinalIgnoreCase))continue;
                            if((options.Recursive || task.Selection!=null) && task.Depth<options.MaxDepth)
                                queue.Enqueue(new(new(item.FullName),Path.Combine(task.Output,item.Name),task.Depth+1,null,false));
                            else if(options.Recursive)errors.Add("Depth limit: "+item.Name);
                        }
                        else queue.Enqueue(new(new(item.FullName),Path.Combine(task.Output,item.Name+".unpacked"),task.Depth,null,false,FromDirectory:true));
                    }
                    continue;
                }
                if(session.Synthetic && task.Copy!=null)
                {
                    if(task.DefiniteArchive)errors.Add(task.Location.DisplayPath+": The archive could not be recognized.");continue;
                }
                if(session.Synthetic && task.FromDirectory)task=task with {Output=Path.GetDirectoryName(task.Output)};
                var entries=session.Archive.Dir.Where(e=>ExtractionScope.Includes(task.Location.Directory,task.Selection,e.Name)).ToArray();
                bool current=task.Location.RootPath==location.RootPath && task.Location.Chain.SequenceEqual(location.Chain);
                bool direct=task.FromDirectory && session.Synthetic && Path.GetDirectoryName(task.Location.RootPath)==location.RootPath;
                string OverrideKey(Entry entry)=>direct?Path.GetFileName(task.Location.RootPath):entry.Name;
                bool Forced(Entry entry)=>(current||direct) && options.TypeOverrides?.ContainsKey(OverrideKey(entry))==true;
                string OutName(Entry entry)=>ExtractionScope.RelativeName(task.Location.Directory,entry.Name);
                string RawTarget(Entry entry)=>ExtractionPath.Resolve(task.Output,OutName(entry));
                string SourceKey(Entry entry)=>task.Location.RootPath+"\0"+string.Join("\0",task.Location.Chain)+"\0"+entry.Name;
                foreach(var entry in entries)
                {
                    if(Forced(entry)){entry.Type=options.TypeOverrides[OverrideKey(entry)];if(entry.Type=="binary")entry.Type="";continue;}
                    entry.Type=MediaKind(session,entry.Name);
                }
                if(session.Synthetic && (task.FromDirectory || options.Recursive) && entries.Any(e=>!Forced(e) && e.Type is not ("audio" or "video" or "image" or "script") && FormatCatalog.Instance.GetTypeFromName(e.Name)=="archive" && Path.GetExtension(e.Name).ToLowerInvariant() is not (".dat" or ".bin")))
                    throw new InvalidDataException("The archive could not be recognized.");
                string Conversion(Entry entry)=>entry.Type switch
                {
                    "image"=>options.ImageFormat,
                    "audio"=>audioFormat,
                    "video"=>options.VideoFormat,
                    _=>null
                };
                string Extension(Entry entry,string format)=>entry.Type=="image"?format.ToLowerInvariant():MediaOutputFormats.Extension(format);
                var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var entry in entries)
                {
                    string target=RawTarget(entry);
                    if(!targets.Add(target))throw new InvalidDataException("Colliding archive paths: "+entry.Name);
                    Claim(target,SourceKey(entry));
                }
                var originalTargets=new HashSet<string>(targets,StringComparer.OrdinalIgnoreCase);
                foreach(var entry in entries)
                {
                    string format=Conversion(entry);if(format==null || format=="AUTO")continue;
                    string raw=RawTarget(entry),converted=Path.ChangeExtension(raw,Extension(entry,format));
                    if(!converted.Equals(raw,StringComparison.OrdinalIgnoreCase) && !targets.Add(converted))throw new IOException("Conversion output collides with another resource: "+converted);
                    Claim(converted,SourceKey(entry));
                }
                var children=new List<ExtractionNode>();
                foreach(var entry in entries)
                {
                    bool archiveCandidate=!session.Synthetic && entry.Type!="video" && entry.Type!="audio" &&
                        (entry.Type=="archive" || !Forced(entry) && FormatCatalog.Instance.GetTypeFromName(entry.Name)=="archive");
                    if(options.Recursive && !session.Synthetic && !Forced(entry) && !archiveCandidate && entry.Type is not ("audio" or "video") && entry.Size>=4)
                    {
                        using var signature=session.Archive.OpenBinaryEntry(entry);
                        archiveCandidate=signature.Signature!=0 && FormatCatalog.Instance.ArcFormats.Any(f=>f.Signatures.Contains(signature.Signature));
                    }
                    string category=entry.Type is "image" or "audio" or "script" or "archive" or "video"?entry.Type:"";
                    if(options.Types is {Length:>0} && !options.Types.Contains(category) && !(options.Recursive && archiveCandidate))continue;
                    if(++count>limits.MaxEntries)throw new InvalidDataException("Recursive extraction has too many entries.");
                    string target=RawTarget(entry);
                    if(Path.GetFullPath(target).Equals(Path.GetFullPath(task.Location.RootPath),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
                        throw new IOException("Extraction cannot replace its input archive.");
                    ExtractionPath.RejectLinks(output,target);
                    void Notify(string path)=>progress?.Report(new(files.Count,Math.Max(files.Count,entries.Length),entry.Name,total,path));
                    void Original()
                    {
                        if(completed.Contains(target) && File.Exists(target)){files.Add(new(target,new FileInfo(target).Length,"retained"));Notify(target);return;}
                        if(File.Exists(target))
                        {
                            if(overwrite==OverwritePolicy.Skip){files.Add(new(target,0,"skipped"));Notify(target);return;}
                            if(overwrite==OverwritePolicy.Fail)throw new IOException("Output exists: "+target);
                        }
                        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target));string temporary=WorkerTemporaryFiles.Create(Path.GetDirectoryName(target));
                        try
                        {
                            long bytes;
                            using(var input=session.Archive.OpenEntry(entry))using(var destination=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write))
                            {
                                long ratio=(long)Math.Min(long.MaxValue,Math.Max(1L,entry.Size)*limits.MaxExpansionRatio);
                                bytes=CopyBounded(input,destination,Math.Min(Math.Min(limits.MaxEntryBytes,limits.MaxTotalBytes-total),ratio));
                            }
                            ExtractionPath.RejectLinks(output,target);File.Move(temporary,target,overwrite==OverwritePolicy.Replace);
                            total+=bytes;files.Add(new(target,bytes,"extracted"));completed.Add(target);Notify(target);
                        }
                        finally {File.Delete(temporary);}
                    }
                    string format=Conversion(entry);
                    if(format!=null)
                    {
                        ResourceConversion result=null;string source=SourceKey(entry);
                        try
                        {
                            string converted=Path.ChangeExtension(target,Extension(entry,format));
                            var prohibited=new HashSet<string>(originalTargets.Where(p=>!p.Equals(target,StringComparison.OrdinalIgnoreCase)),StringComparer.OrdinalIgnoreCase);
                            prohibited.UnionWith(claimed.Where(p=>p.Value!=source).Select(p=>p.Key));
                            result=ConvertFile(session,entry.Name,converted,format,overwrite,Math.Min(limits.MaxEntryBytes,limits.MaxTotalBytes-total),completed,options.AdjustImageOffset,prohibited);
                        }
                        catch(MissingOptionsException){throw;}
                        catch(Exception error)when(error is IOException or InvalidDataException or UnknownFormatException or NotSupportedException)
                        {errors.Add(entry.Name+": "+error.Message);Original();}
                        if(result!=null)
                        {
                            Claim(result.Path,source);total+=result.Bytes;
                            bool owned=result.Written || completed.Contains(result.Path);
                            files.Add(new(result.Path,result.Written?result.Bytes:File.Exists(result.Path)?new FileInfo(result.Path).Length:0,result.Written?"converted":owned?"retained":"skipped"));
                            if(result.Written)completed.Add(result.Path);Notify(result.Path);
                            if(!result.Path.Equals(target,StringComparison.OrdinalIgnoreCase))
                            {
                                if(!options.DiscardOriginalMedia || !owned)Original();
                                else if(completed.Contains(target) && File.Exists(target))removals.Add(target);
                            }
                        }
                    }
                    else Original();
                    if(options.Recursive && task.Depth<options.MaxDepth && archiveCandidate)
                    {
                        string childOutput=target+".unpacked";
                        if(targets.Any(p=>p.Equals(childOutput,StringComparison.OrdinalIgnoreCase)||p.StartsWith(childOutput+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))
                            throw new IOException("Nested archive output collides with an existing entry: "+childOutput);
                        children.Add(new(session.Child(entry.Name),childOutput,task.Depth+1,null,false,target,DefiniteArchive:Path.GetExtension(entry.Name).ToLowerInvariant() is not (".bin" or ".dat") && FormatCatalog.Instance.GetTypeFromName(entry.Name)=="archive"));
                    }
                    else if(options.Recursive && archiveCandidate)errors.Add("Depth limit: "+entry.Name);
                }
                foreach(var child in children)queue.Enqueue(child);
                if(options.DiscardChildArchives && task.Copy!=null)removals.Add(task.Copy);
            }
            catch(MissingOptionsException error){error.CompletedPaths=completed.ToArray();throw;}
            catch(UnknownFormatException)when(!task.Required && task.Copy!=null && !task.DefiniteArchive) { }
            catch(Exception error)when(!task.Required && error is not ResourceParametersRequiredException)
            {errors.Add(task.Location.DisplayPath+": "+error.Message);}
        }
        // A failed conversion or child archive must keep every recoverable output copy.
        if(errors.Count==0)foreach(string path in removals)
        {
            ExtractionPath.RejectLinks(output,path);
            try {File.Delete(path);files.RemoveAll(f=>f.Path.Equals(path,StringComparison.OrdinalIgnoreCase));}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException){errors.Add(path+": "+error.Message);}
        }
        return new(files.ToArray(),errors.ToArray(),total,Math.Min(count,limits.MaxEntries));
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

    public static string MediaKind(ResourceSession session,string name,string forced=null)
    {
        if(forced!=null)return forced=="binary"?"":forced;
        string declared=session.Physical?FormatCatalog.Instance.GetTypeFromName(name):session.Find(name).Type;
        if(MediaInfo.IsAudioName(name))return "audio";
        if(MediaInfo.IsVideoName(name))return "video";
        if(declared is "image" or "audio" or "script")return declared;
        using var data=session.OpenEntry(name);var bytes=new byte[256];int length=data.ReadAtLeast(bytes,256,false);var header=bytes[..length];
        if(MediaInfo.IsAudioName(name) || MediaInfo.HasAudioHeader(header))return "audio";
        if(MediaInfo.IsVideoName(name) || MediaInfo.HasVideoHeader(name,header))return "video";
        if(declared is "image" or "audio" or "video" or "script")return declared;
        uint signature=length>=4?System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header):0;
        if(signature!=0 && FormatCatalog.Instance.ImageFormats.Any(f=>f.Signatures.Contains(signature)))return "image";
        if(signature!=0 && FormatCatalog.Instance.AudioFormats.Any(f=>f.Signatures.Contains(signature)))return "audio";
        return declared??"";
    }
    public static (string Format,string Output) MediaConversionTarget(ResourceSession session,string name,string output,ResourceExtractionOptions options)
    {
        string kind=MediaKind(session,name,options.TypeOverrides?.GetValueOrDefault(name));
        string format=kind switch {"image"=>options.ImageFormat,"audio"=>options.AudioFormat,"video"=>options.VideoFormat,_=>null};
        if(format==null)return (null,null);
        string extension=kind=="image"?FormatCatalog.Instance.ImageFormats.Single(f=>FormatIdentity.Of(f)==format || f.Tag==format).Extensions.FirstOrDefault()??"png":MediaOutputFormats.Extension(format);
        return (format,Path.ChangeExtension(output,extension));
    }

    public static ResourceConversion ConvertFile(ResourceSession session, string name, string output, string formatId, OverwritePolicy overwrite, long maxBytes = 2L << 30, ISet<string> completed = null, bool adjustImageOffset = false, ISet<string> prohibited = null)
    {
        if (formatId == "AUTO") formatId = "AUDIO";
        if (formatId is "FLAC" or "M4A" or "MP4" or "MKV")
            return ConvertStandardMedia(session,name,output,formatId,overwrite,maxBytes,completed,prohibited);
        string scratch = WorkerTemporaryFiles.CreateDirectory(Path.GetTempPath());
        string temporary = null;
        try
        {
            string path = Path.Combine(scratch, Path.GetFileName(name));
            using (var sourceInput = session.OpenEntry(name))
            using (var target = File.Create(path)) CopyBounded(sourceInput, target, 2L << 30);
            using var input = new BinaryStream(File.OpenRead(path), name);
            using var audio = formatId is "WAV" or "AUDIO" ? AudioFormat.Read(input) : null;
            if(formatId is "WAV" or "AUDIO" && audio==null)
                return ConvertStandardMedia(session,name,output,"WAV",overwrite,maxBytes,completed,prohibited);
            bool passthrough = audio != null && formatId == "AUDIO" && new[] { "wav", "mp3", "ogg" }.Contains(audio.SourceFormat);
            if (passthrough) output = Path.ChangeExtension(output, audio.SourceFormat);
            string sourcePath = session.Physical ? session.PhysicalPath(name) : session.Location.RootPath;
            if (Path.GetFullPath(output).Equals(Path.GetFullPath(sourcePath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("Cannot replace a source document. Choose another destination.");
            if(File.Exists(output) && completed?.Contains(output)!=true &&
                Path.GetDirectoryName(Path.GetFullPath(output)).Equals(Path.GetDirectoryName(Path.GetFullPath(sourcePath)),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
                throw new IOException("Cannot overwrite existing files in the source directory. Choose another destination.");
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

    static ResourceConversion ConvertStandardMedia(ResourceSession session,string name,string output,string format,
        OverwritePolicy overwrite,long maxBytes,ISet<string> completed,ISet<string> prohibited)
    {
        string sourcePath=session.Physical?session.PhysicalPath(name):session.Location.RootPath;
        if(Path.GetFullPath(output).Equals(Path.GetFullPath(sourcePath),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
            throw new IOException("Cannot replace a source document. Choose another destination.");
        if(File.Exists(output) && completed?.Contains(output)!=true &&
            Path.GetDirectoryName(Path.GetFullPath(output)).Equals(Path.GetDirectoryName(Path.GetFullPath(sourcePath)),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
            throw new IOException("Cannot overwrite existing files in the source directory. Choose another destination.");
        ExtractionPath.RejectLinks(Path.GetDirectoryName(Path.GetFullPath(output)),output);
        if(prohibited?.Contains(output)==true)throw new IOException("Conversion output collides with another resource: "+output);
        if(completed?.Contains(output)==true && File.Exists(output))return new(output,0,false);
        if(File.Exists(output))
        {
            if(overwrite==OverwritePolicy.Skip)return new(output,0,false);
            if(overwrite==OverwritePolicy.Fail)throw new IOException("Output exists: "+output);
        }
        string scratch=WorkerTemporaryFiles.CreateDirectory(Path.GetTempPath()),temporary=null;
        try
        {
            string input;
            if(format is "FLAC" or "M4A")
            {
                input=Path.Combine(scratch,"audio.wav");
                ConvertFile(session,name,input,"WAV",OverwritePolicy.Fail,maxBytes);
            }
            else
            {
                input=session.Physical?session.PhysicalPath(name):Path.Combine(scratch,"source.media");
                if(!session.Physical)
                {
                    using var source=session.OpenEntry(name);using var copy=File.Create(input);CopyBounded(source,copy,2L<<30);
                }
                if(format!="WAV")MediaTools.Probe(input);
            }
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            temporary=WorkerTemporaryFiles.Create(Path.GetDirectoryName(Path.GetFullPath(output)));
            long bytes=MediaTools.Convert(input,temporary,format,maxBytes);
            ExtractionPath.RejectLinks(Path.GetDirectoryName(Path.GetFullPath(output)),output);
            File.Move(temporary,output,overwrite==OverwritePolicy.Replace);return new(output,bytes);
        }
        finally {if(temporary!=null)File.Delete(temporary);System.IO.Directory.Delete(scratch,true);}
    }

    public static void CopyOriginalMedia(ResourceSession session, string name, string converted, OverwritePolicy overwrite)
    {
        string copy = Path.ChangeExtension(converted, Path.GetExtension(name));
        if (Path.GetFullPath(copy).Equals(Path.GetFullPath(converted), StringComparison.OrdinalIgnoreCase)) return;
        string sourcePath=session.Physical?session.PhysicalPath(name):session.Location.RootPath;
        if(Path.GetFullPath(copy).Equals(Path.GetFullPath(sourcePath),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))return;
        if(File.Exists(copy) && Path.GetDirectoryName(Path.GetFullPath(copy)).Equals(Path.GetDirectoryName(Path.GetFullPath(sourcePath)),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
            throw new IOException("Cannot overwrite existing files in the source directory. Choose another destination.");
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
            using var audio = AudioFormat.Read(binary);
            if(audio==null)
            {
                var converted=ConvertFile(session,name,output,"WAV",OverwritePolicy.Fail,512L<<20);
                var metadata=MediaTools.ProbeAudio(converted.Path);
                return new(converted.Path,converted.Bytes,metadata.SampleRate,metadata.Channels,metadata.Duration);
            }
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
