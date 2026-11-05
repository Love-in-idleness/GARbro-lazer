using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
namespace GARbro.Application;
public static class WorkerExecutor
{
    sealed class ImmediateProgress<T>(Action<T> callback) : IProgress<T>
    { public void Report(T value) => callback(value); }
    public static async Task<int> ExecuteAsync(WorkerRequest request, Action<WorkerReply> reply)
    {
        try
        {
                if(request.Operation.StartsWith("resource-",StringComparison.Ordinal))
                {
                    if(request.Operation=="resource-release"){BrowserDecoderSession.Reset();reply(WorkerProtocol.Result(true));return 0;}
                    var measurement=System.Diagnostics.Stopwatch.StartNew();
                    if (LightweightResources.TryExecute(request, out var quick))
                    { reply(WorkerProtocol.Result(quick) with {Timing=new(OperationMs:measurement.Elapsed.TotalMilliseconds)}); return 0; }
                    if(request.Refresh)BrowserDecoderSession.Reset();
                    var context = request.Context ?? new DecoderContext();
                    // Metadata must remain available to repair a malformed saved
                    // override, and describes defaults rather than applied values.
                    if (request.Operation is "resource-catalog" or "resource-validate-schemes")
                        context = context with { FormatSettings = null };
                    BrowserDecoderSession.Apply(context);
                    double initialization=measurement.Elapsed.TotalMilliseconds;measurement.Restart();
                    var provider=new ScopedOptionsProvider(request);
                    var updates=new ImmediateProgress<ExtractionProgress>(p=>reply(new WorkerReply("progress",Progress:p)));
                    object value;
                    switch(request.Operation)
                    {
                        case "resource-warm":value=GameRes.FormatCatalog.Instance.RegisteredExtensions.ToDictionary(e=>e,e=>GameRes.FormatCatalog.Instance.GetTypeFromName("file."+e),StringComparer.OrdinalIgnoreCase);break;
                        case "resource-catalog": value=ResourceService.Catalog();break;
                        case "resource-validate-schemes":
                            using(var file=File.OpenRead(request.Path))
                            using(var gzip=new System.IO.Compression.GZipStream(file,System.IO.Compression.CompressionMode.Decompress))
                            using(var bounded=new MemoryStream())
                            {
                                byte[] buffer=new byte[81920];int n;
                                while((n=gzip.Read(buffer))>0)
                                {if(bounded.Length>(64L<<20)-n)throw new InvalidDataException("Format database exceeds the size limit.");bounded.Write(buffer,0,n);}
                                bounded.Position=0;
                                var report=GameRes.FormatCatalog.Instance.DeserializePortableSchemeJson(bounded);
                                if(report.Failed.Count>0)throw new InvalidDataException("Format database contains incompatible schemes.");
                            }
                            value=true;break;
                        case "resource-list":value=ResourceService.List(request.Location,provider);break;
                        case "resource-preview":value=ResourceService.Preview(request.Location,request.Entry,request.Context??new(),request.ForcedType,request.Offset,provider,request.PreviewMode,request.PreviewEncoding,request.Output,request.PreviewWidth,request.PreviewHeight);break;
                        case "resource-audio":value=ResourceService.PrepareAudio(request.Location,request.Entry,request.Output,provider);break;
                        case "resource-video":value=ResourceService.PrepareVideo(request.Location,request.Entry,request.Output,provider,request.PreferDirectSource);break;
                        case "resource-extract":value=ResourceService.Extract(request.Location,request.Output,request.ResourceOptions,request.Extraction,
                            request.Overwrite,request.Selection,provider,updates,request.CompletedPaths);break;
                        case "resource-create":
                            ResourceService.CreateArchive(request.Path,request.Inputs,request.Output,request.FormatId,request.OptionsJson,request.Scheme,request.Overwrite,updates);
                            value=request.Output;break;
                        case "resource-convert":
                            using(var resource=new ResourceSession(request.Location,provider))
                            {
                                var target=request.FormatId=="MEDIA"?ResourceService.MediaConversionTarget(resource,request.Entry,request.Output,request.ResourceOptions??new()):(request.FormatId,request.Output);
                                if(target.Item1==null){value=new ResourceConversion(null,0,false);break;}
                                var converted=ResourceService.ConvertFile(resource,request.Entry,target.Item2,target.Item1,request.Overwrite);
                                if(converted.Written && request.ResourceOptions?.DiscardOriginalMedia==false)
                                    ResourceService.CopyOriginalMedia(resource,request.Entry,converted.Path,request.Overwrite);
                                value=converted;
                            }
                            break;
                        default:throw new ArgumentException("Unknown resource operation.");
                    }
                    reply(WorkerProtocol.Result(value) with {Timing=new(initialization,measurement.Elapsed.TotalMilliseconds)});return 0;
                }
                var fingerprint=ArchiveFingerprint.Read(request.Path);
                if(request.Fingerprint!=null && request.Fingerprint!=fingerprint)
                    throw new IOException("Archive changed; reopen it before continuing.");
                var options=new JsonOptionsProvider(request.OptionsJson,request.Scheme);
                WorkerReply result;
                using(var archive=ArchiveService.Open(request.Path,options:options))
                {
                  switch(request.Operation)
                  {
                    case "open":
                        result=WorkerProtocol.Result(new WorkerArchiveData(archive.Path,archive.Tag,archive.Entries.ToArray(),fingerprint));
                        break;
                    case "preview":result=WorkerProtocol.Result(archive.Preview(request.Entry));break;
                    case "extract":
                        var progress=new ImmediateProgress<ExtractionProgress>(p=>reply(new WorkerReply("progress",Progress:p)));
                        var files=await archive.ExtractAsync(request.Output,request.Overwrite,request.Extraction,progress,
                            selection:request.Selection==null?null:new HashSet<string>(request.Selection));
                        result=WorkerProtocol.Result(files);
                        break;
                    default:throw new ArgumentException("Unknown worker operation.");
                  }
                }
                reply(result);
                return 0;
        }
            catch(Exception error)
            {
                var missing=error as MissingOptionsException;
                reply(new WorkerReply("error",Error:new WorkerError(error.GetType().Name,error.Message,missing?.FormatId,missing?.OptionsJson,missing?.CompletedPaths,missing?.ContextKey)));
                return 1;
            }
    }
}
