using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GARbro.Application;

/// <summary>The same bounded decoder protocol over desktop processes or Android IPC.</summary>
public interface IWorkerClient
{
    Task<T> ExecuteAsync<T>(WorkerRequest request, WorkerLimits limits,
        IProgress<ExtractionProgress> progress = null, CancellationToken cancellation = default);
}

public sealed class ManagedWorkerClient(string assembly) : IWorkerClient, IDisposable, IAsyncDisposable
{
    readonly BrowsingWorker browser = new(assembly);
    public WorkerTiming LastTiming=>browser.LastTiming;
    public Task<T> ExecuteAsync<T>(WorkerRequest request, WorkerLimits limits,
        IProgress<ExtractionProgress> progress = null, CancellationToken cancellation = default) =>
        request.Operation is "resource-list" or "resource-preview" or "resource-warm" or "resource-release" or "resource-audio" or "resource-video"
            ? browser.ExecuteAsync<T>(request, limits, cancellation)
            : WorkerArchiveSession.RequestAsync<T>(assembly, request, limits, progress, cancellation);
    public void Dispose() => browser.Dispose();
    public ValueTask DisposeAsync() => browser.DisposeAsync();
}

public interface IAudioOutput : IDisposable
{
    double Duration { get; }
    double Position { get; set; }
    double Volume { get; set; }
    bool Playing { get; }
    void Play();
    void Pause();
    void Stop();
}

public interface IVideoOutput : IAudioOutput
{
    string Warning { get; }
}

public sealed record ResourceSource(string Id, string DisplayName, string LocalPath);

/// <summary>External identifiers remain outside the path-based decoding engine.</summary>
public interface IStorageAccess
{
    Task<ResourceSource[]> PickInputsAsync(bool folders, CancellationToken cancellation = default);
    Task<ResourceSource> PickOutputAsync(bool folder, string suggestedName = null, CancellationToken cancellation = default);
    Task<ResourceSource> RestoreAsync(string id, CancellationToken cancellation = default);
    Task PrepareAsync(ResourceSource source, CancellationToken cancellation = default);
    Task CommitAsync(string localPath, OverwritePolicy overwrite, CancellationToken cancellation = default);
    string DisplayPath(string localPath);
    string PersistentId(string localPath);
    string ParentPath(string localPath) => System.IO.Directory.GetParent(localPath)?.FullName;
}
