using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace GARbro.Application;

/// <summary>A supervised, single-flight browsing process. Heavy jobs remain isolated one-shot requests.</summary>
public sealed class BrowsingWorker : IDisposable, IAsyncDisposable
{
    readonly string assembly;
    readonly object disposeGate=new();
    Task cleanup=Task.CompletedTask;
    readonly SemaphoreSlim gate=new(1,1);
    readonly CancellationTokenSource lifetime=new();
    readonly Timer idle;
    readonly TimeSpan idleTimeout;
    public int? ProcessId {get {try{return process is {HasExited:false}?process.Id:null;}catch(InvalidOperationException){return null;}}}
    Process process;
    Channel<WorkerReply> replies;
    Task stdout,stderr,watchdog;
    CancellationTokenSource monitor;
    string registry,scope,key,cachedArchive;
    long sequence,expiresAt=long.MaxValue;
    public WorkerTiming LastTiming {get;private set;}
    bool disposed;
    public BrowsingWorker(string assembly, TimeSpan? idleTimeout=null)
    {
        this.assembly=assembly;this.idleTimeout=idleTimeout??TimeSpan.FromSeconds(60);
        idle=new Timer(_=>_ = Expire(),null,Timeout.Infinite,Timeout.Infinite);
        AppDomain.CurrentDomain.ProcessExit+=OnExit;
    }
    void OnExit(object sender,EventArgs e)
    {
        var owned=process;
        try {if(owned is {HasExited:false})owned.Kill(true);if(owned!=null && owned.WaitForExit(2000))WorkerTemporaryFiles.Cleanup(registry,scope);}catch(InvalidOperationException){}
    }
    async Task Expire(bool force=false)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            long deadline=Interlocked.Read(ref expiresAt),now=Stopwatch.GetTimestamp();
            if(force || now>=deadline)await Recycle().ConfigureAwait(false);
            else if(deadline!=long.MaxValue && !disposed)
                ScheduleIdle(TimeSpan.FromMilliseconds(Math.Max(1,Math.Ceiling((deadline-now)*1000d/Stopwatch.Frequency))));
        }
        finally {gate.Release();}
    }
    void ScheduleIdle(TimeSpan delay)
    {
        try {idle.Change(delay,Timeout.InfiniteTimeSpan);}
        catch(ObjectDisposedException)when(disposed){}
    }
    public Task<T> ExecuteAsync<T>(WorkerRequest request,WorkerLimits limits,CancellationToken cancellation=default) =>
        Task.Run(()=>ExecuteCore<T>(request,limits,cancellation),CancellationToken.None);
    async Task<T> ExecuteCore<T>(WorkerRequest request,WorkerLimits limits,CancellationToken cancellation)
    {
        limits.Validate();
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellation,lifetime.Token);
        var token=linked.Token;
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed,this);
        var watch=Stopwatch.StartNew();
        if(request.Operation=="resource-release" && process==null)return (T)(object)true;
        if(LightweightResources.TryExecute(request,out object quick,token))
        {
            token.ThrowIfCancellationRequested();
            if(cachedArchive!=null)await ExecuteCore<bool>(request with {Operation="resource-release"},limits,token).ConfigureAwait(false);
            LastTiming=new(OperationMs:watch.Elapsed.TotalMilliseconds,RoundTripMs:watch.Elapsed.TotalMilliseconds);return (T)quick;
        }
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Interlocked.Exchange(ref expiresAt,long.MaxValue);idle.Change(Timeout.Infinite,Timeout.Infinite);
            token.ThrowIfCancellationRequested();
            var file=request.Location==null?null:new FileInfo(request.Location.RootPath);
            var database=request.Context?.SchemesFile==null?null:new FileInfo(request.Context.SchemesFile);
            string next=WorkerProtocol.Serialize(new {request.Context,request.OptionsJson,Scheme=request.Scheme is "none"?null:request.Scheme,DatabaseWritten=database?.Exists==true?database.LastWriteTimeUtc.Ticks:0,limits.MaxMemoryBytes});
            if(request.Operation=="resource-release")
            {
                cachedArchive=null;
                if(process==null || process.HasExited){await Recycle().ConfigureAwait(false);return (T)(object)true;}
                next=key;
            }
            if(process==null || process.HasExited || key!=next) { await Recycle().ConfigureAwait(false); Start(limits); key=next; }
            request=request with {RequestId=Interlocked.Increment(ref sequence)};
            string input=WorkerProtocol.Serialize(request);
            if(input.Length>32<<20)throw new ArgumentException("Worker request exceeds the size limit.");
            await process.StandardInput.WriteLineAsync(input.AsMemory(),token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(token).ConfigureAwait(false);
            var read=replies.Reader.ReadAsync(token).AsTask();
            while(!read.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                if(watch.Elapsed.TotalSeconds>limits.TimeoutSeconds)throw new WorkerLimitException($"Operation exceeded {limits.TimeoutSeconds:g} seconds.","timeout");
                if(WorkerSupervisor.ResidentBytes(process)>limits.MaxMemoryBytes)throw new WorkerLimitException($"Operation exceeded the {limits.MaxMemoryBytes/(1<<20)} MiB memory limit.","memory");
                await Task.WhenAny(read,Task.Delay(25,token)).ConfigureAwait(false);
            }
            WorkerReply result=await read.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if(result.RequestId!=request.RequestId)throw new InvalidDataException("Unexpected browsing request number.");
            if(result.Error is WorkerError error)throw new WorkerOperationException(error.Type,error.Message) {FormatId=error.FormatId,OptionsJson=error.OptionsJson,CompletedPaths=error.CompletedPaths,ContextKey=error.ContextKey};
            if(result.Kind!="result" || result.Data==null)throw new InvalidDataException("Invalid decoder worker response.");
            var decode=Stopwatch.StartNew();
            var value=result.Data.Value.Deserialize<T>(WorkerProtocol.Json);
            if(request.Location!=null && !Directory.Exists(request.Location.RootPath))cachedArchive=request.Location.RootPath;
            LastTiming=(result.Timing??new()) with {PayloadDecodeMs=decode.Elapsed.TotalMilliseconds,RoundTripMs=watch.Elapsed.TotalMilliseconds};
            return value;
        }
        catch(ChannelClosedException closed)
        {
            await Recycle().ConfigureAwait(false);
            if(closed.InnerException is WorkerLimitException limit)throw limit;
            throw new WorkerOperationException("WorkerExited","Decoder worker exited before returning a result.");
        }
        catch(WorkerOperationException) {throw;}
        catch { await Recycle().ConfigureAwait(false);throw; }
        finally {if(!disposed){Interlocked.Exchange(ref expiresAt,Stopwatch.GetTimestamp()+(long)(idleTimeout.TotalSeconds*Stopwatch.Frequency));ScheduleIdle(idleTimeout);}gate.Release();}
    }
    void Start(WorkerLimits limits)
    {
        scope=Guid.NewGuid().ToString("N");registry=Path.Combine(Path.GetTempPath(),"garbro-worker-"+scope+".jsonl");
        var permissions=new FileStreamOptions {Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.Read};
        if(!OperatingSystem.IsWindows())permissions.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
        using(new FileStream(registry,permissions)){}
        var start=WorkerSupervisor.StartInfo(assembly);start.ArgumentList.Add("--browse");
        start.Environment[WorkerTemporaryFiles.ScopeVariable]=scope;start.Environment[WorkerTemporaryFiles.RegistryVariable]=registry;
        start.Environment[MediaTools.MemoryVariable]=limits.MaxMemoryBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        process=new Process {StartInfo=start};
        if(!process.Start())throw new IOException("Cannot start the decoder worker.");
        var channel=Channel.CreateBounded<WorkerReply>(new BoundedChannelOptions(2) {SingleReader=true,SingleWriter=true});replies=channel;
        var owned=process;
        stdout=Task.Run(async()=>
        {
            Exception failure=null;
            try {await WorkerSupervisor.ReadLinesAsync(owned.StandardOutput,line=> {if(!channel.Writer.TryWrite(WorkerProtocol.Deserialize<WorkerReply>(line)))throw new InvalidDataException("Unexpected decoder worker response.");}).ConfigureAwait(false);}
            catch(Exception e) {failure=e;}
            finally {channel.Writer.TryComplete(failure??new IOException("Decoder worker exited before returning a result."));}
        });
        stderr=Task.Run(()=>WorkerSupervisor.ReadLinesAsync(owned.StandardError,_=>{},1<<20));
        monitor=new CancellationTokenSource();var monitoring=monitor.Token;
        watchdog=Task.Run(async()=>
        {
            try
            {
                while(!monitoring.IsCancellationRequested && !owned.HasExited)
                {
                    await Task.Delay(100,monitoring).ConfigureAwait(false);
                    if(WorkerSupervisor.ResidentBytes(owned)>limits.MaxMemoryBytes)
                    {
                        channel.Writer.TryComplete(new WorkerLimitException($"Operation exceeded the {limits.MaxMemoryBytes/(1<<20)} MiB memory limit.","memory"));
                        owned.Kill(true);return;
                    }
                }
            }
            catch(OperationCanceledException)when(monitoring.IsCancellationRequested){}
            catch(InvalidOperationException){}
        });
    }
    async Task Recycle()
    {
        if(process==null)return;
        var owned=process;process=null;key=null;cachedArchive=null;monitor?.Cancel();
        try
        {
            if(!owned.HasExited)owned.Kill(true);
            await owned.WaitForExitAsync().ConfigureAwait(false);
            if(watchdog!=null)await watchdog.ConfigureAwait(false);
            if(stdout!=null) {try {await stdout.ConfigureAwait(false);}catch(IOException){}}
            if(stderr!=null) {try {await stderr.ConfigureAwait(false);}catch(Exception){}}
        }
        catch(InvalidOperationException){}
        finally {owned.Dispose();monitor?.Dispose();monitor=null;WorkerTemporaryFiles.Cleanup(registry,scope);stdout=stderr=watchdog=null;}
    }
    public async ValueTask DisposeAsync()
    {
        Dispose();Task pending;lock(disposeGate)pending=cleanup;await pending.ConfigureAwait(false);
    }
    public void Dispose()
    {
        lock(disposeGate)
        {
            if(disposed)return;disposed=true;lifetime.Cancel();idle.Dispose();
            cleanup=Task.Run(async()=>{try{await Expire(true).ConfigureAwait(false);}finally{AppDomain.CurrentDomain.ProcessExit-=OnExit;}});
        }
    }
}
