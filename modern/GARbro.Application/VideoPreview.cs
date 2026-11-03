using System;
using System.IO;
using GARbro.Media;

namespace GARbro.Application;

public static partial class ResourceService
{
    public static VideoArtifact PrepareVideo(ResourceLocation location, string name, string output, IOptionsProvider options, bool preferDirectSource=false)
    {
        using var session = BrowserDecoderSession.Open(location, options);
        if(preferDirectSource && session.Physical)
        {
            string path=session.PhysicalPath(name);
            return new(path,new FileInfo(path).Length,MediaTools.Probe(path));
        }
        string root = Path.GetDirectoryName(Path.GetFullPath(output)); Directory.CreateDirectory(root);
        string temporary = WorkerTemporaryFiles.Create(root);
        try
        {
            using (var source = session.OpenEntry(name))
            using (var destination = File.Create(temporary)) CopyBounded(source, destination, 2L << 30);
            var info = MediaTools.Probe(temporary);
            ExtractionPath.RejectLinks(root, output);
            File.Move(temporary, output, true);
            return new(output, new FileInfo(output).Length, info);
        }
        finally { File.Delete(temporary); }
    }
}
