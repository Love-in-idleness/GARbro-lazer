using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GARbro.Application
{
    /// <summary>A metadata-only session; decoding and extraction occur in supervised processes.</summary>
    public sealed class WorkerArchiveSession : IDisposable
    {
        readonly string host,options,scheme;
        WorkerLimits limits;
        readonly ArchiveFingerprint fingerprint;
        readonly CancellationTokenSource lifetime=new();
        readonly SemaphoreSlim gate=new(1,1);
        bool disposed;
        public string Path {get;}
        public string Tag {get;}
        public IReadOnlyList<ArchiveEntryInfo> Entries {get;}
        public WorkerLimits Limits {get=>limits;set {value.Validate();limits=value;} }

        WorkerArchiveSession(string host,WorkerArchiveData archive,string options,string scheme,WorkerLimits limits)
        {
            this.host=host;this.options=options;this.scheme=scheme;this.limits=limits;
            Path=archive.Path;Tag=archive.Tag;Entries=archive.Entries;fingerprint=archive.Fingerprint;
        }

        public static async Task<WorkerArchiveSession> OpenAsync(string host,string path,string optionsJson=null,
            string scheme=null,WorkerLimits limits=null,CancellationToken cancellation=default)
        {
            limits??=new();
            var request=new WorkerRequest("open",System.IO.Path.GetFullPath(path),optionsJson,scheme);
            var archive=await RequestAsync<WorkerArchiveData>(host,request,limits,null,cancellation);
            return new WorkerArchiveSession(host,archive,optionsJson,scheme,limits);
        }

        async Task<T> SendAsync<T>(WorkerRequest request,IProgress<ExtractionProgress> progress,CancellationToken cancellation)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellation,lifetime.Token);
            await gate.WaitAsync(linked.Token);
            try { return await RequestAsync<T>(host,request,limits,progress,linked.Token); }
            finally { gate.Release(); }
        }

        public Task<PreviewResult> PreviewAsync(string name,CancellationToken cancellation=default)
            =>SendAsync<PreviewResult>(new WorkerRequest("preview",Path,options,scheme,Entry:name,Fingerprint:fingerprint),null,cancellation);

        public async Task<IReadOnlyList<ExtractedFile>> ExtractAsync(string root,OverwritePolicy overwrite=OverwritePolicy.Fail,
            ExtractionLimits limits=null,IProgress<ExtractionProgress> progress=null,CancellationToken cancellation=default,
            ISet<string> selection=null)
        {
            return await SendAsync<ExtractedFile[]>(new WorkerRequest("extract",Path,options,scheme,
                System.IO.Path.GetFullPath(root),Overwrite:overwrite,Extraction:limits,
                Selection:selection==null?null:new List<string>(selection).ToArray(),Fingerprint:fingerprint),progress,cancellation);
        }

        public static async Task<T> RequestAsync<T>(string host,WorkerRequest request,WorkerLimits limits,
            IProgress<ExtractionProgress> progress,CancellationToken cancellation)
        {
            string input=await Task.Run(()=>WorkerProtocol.Serialize(request),cancellation).ConfigureAwait(false);
            if(input.Length>32<<20) throw new ArgumentException("Worker request exceeds the size limit.");
            WorkerReply result=null;
            var run=await WorkerSupervisor.RunManagedAsync(host,Array.Empty<string>(),limits,input,cancellation,line=>
            {
                var reply=WorkerProtocol.Deserialize<WorkerReply>(line)
                    ?? throw new InvalidDataException("Invalid decoder worker response.");
                if(reply.Kind=="progress") progress?.Report(reply.Progress);
                else if(reply.Kind is "result" or "error")
                {
                    if(result!=null) throw new InvalidDataException("Duplicate decoder worker result.");
                    result=reply;
                }
                else throw new InvalidDataException("Unknown decoder worker response.");
            }).ConfigureAwait(false);
            if(result?.Error is WorkerError error) throw new WorkerOperationException(error.Type,error.Message) { FormatId=error.FormatId, OptionsJson=error.OptionsJson, CompletedPaths=error.CompletedPaths, ContextKey=error.ContextKey };
            if(run.ExitCode!=0 || result?.Data==null)
                throw new WorkerOperationException("WorkerExited",string.IsNullOrWhiteSpace(run.StandardError)
                    ?"Decoder worker exited before returning a result.":run.StandardError.Trim());
            return result.Data.Value.Deserialize<T>(WorkerProtocol.Json);
        }

        public void Dispose()
        {
            if(!disposed) {disposed=true;lifetime.Cancel();}
        }
    }
}
