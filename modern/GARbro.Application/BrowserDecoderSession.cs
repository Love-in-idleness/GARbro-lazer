using System;
using System.Collections.Generic;
using System.IO;
using GameRes;

namespace GARbro.Application;

/// <summary>Owned exclusively by the persistent worker's serial decoder thread.</summary>
public static class BrowserDecoderSession
{
    [ThreadStatic] static bool enabled;
    [ThreadStatic] static string applied, key, directory;
    [ThreadStatic] static ResourceSession current;
    [ThreadStatic] static ForwardOptions forward;
    [ThreadStatic] static Dictionary<string,(string Path,long Bytes)> materialized;
    [ThreadStatic] static long materializedBytes;
    const long FileCacheLimit=512L<<20;
    public static void Enable() => enabled = true;
    public static void Apply(DecoderContext context)
    {
        string next = WorkerProtocol.Serialize(context);
        if (!enabled || applied != next) { Reset(); context.Apply(); applied = next; }
    }
    public static ResourceSession Open(ResourceLocation location, IOptionsProvider options)
    {
        // Filesystem previews need a scoped working directory only while decoding.
        // Retaining that scope would lock the directory on Windows.
        if (!enabled || Directory.Exists(location.RootPath))
        {
            if(enabled)Release();
            return new(location,options);
        }
        var file = new FileInfo(location.RootPath);
        string next = WorkerProtocol.Serialize(new { location.RootPath, Chain=location.Chain, Length=file.Exists?file.Length:0, Written=file.Exists?file.LastWriteTimeUtc.Ticks:0 });
        if (key != next)
        {
            Release(); forward = new(); forward.Target = options;
            current = new ResourceSession(location,forward) { Retained=true }; key=next;
        }
        forward.Target=options; current.Location=location; forward.SetLocation(location);
        return current;
    }
    public static string Materialize(ResourceSession session,string name,string scratch)
    {
        if(enabled && materialized?.TryGetValue(name,out var found)==true)
        {
            if(File.Exists(found.Path))return found.Path;
            materialized.Remove(name);materializedBytes-=found.Bytes;
        }
        string transient=scratch;
        if(enabled) { directory ??= WorkerTemporaryFiles.CreateDirectory(Path.GetTempPath()); scratch=directory; }
        string path=Path.Combine(scratch,Guid.NewGuid().ToString("N")+Path.GetExtension(name));
        try
        {
            long bytes;
            using(var input=session.OpenEntry(name))using(var output=File.Create(path))bytes=ResourceService.CopyBounded(input,output,GARbro.Media.MediaInfo.IsVideoName(name)?2L<<30:512L<<20);
            if(enabled)
            {
                // Oversized media live only for this operation. Reusable scratch files
                // share a disk budget as well as the parent's separate memory LRU.
                if(bytes>FileCacheLimit)
                {
                    string temporary=Path.Combine(transient,Path.GetFileName(path));File.Move(path,temporary);return temporary;
                }
                if(materializedBytes+bytes>FileCacheLimit || materialized?.Count>=32)
                {
                    foreach(var entry in materialized.Values)File.Delete(entry.Path);
                    materialized.Clear();materializedBytes=0;
                }
                (materialized??=new(StringComparer.Ordinal))[name]=(path,bytes);materializedBytes+=bytes;
            }
            return path;
        }
        catch {File.Delete(path);throw;}
    }

    static void Release()
    {
        if(current!=null) {current.Retained=false;current.Dispose();current=null;}
        if(directory!=null) {try {Directory.Delete(directory,true);}catch(IOException){}directory=null;}
        materialized=null;materializedBytes=0;key=null;
    }
    public static void Reset() {Release();applied=null;}
    sealed class ForwardOptions : ILocatedOptionsProvider
    {
        public IOptionsProvider Target;
        public void SetLocation(ResourceLocation location) => (Target as ILocatedOptionsProvider)?.SetLocation(location);
        public ResourceOptions GetOptions(IResource format,string notice)=>Target.GetOptions(format,notice);
    }
}
