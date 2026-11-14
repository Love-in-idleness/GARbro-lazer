using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using GARbro.Application;

namespace GARbro.Desktop;

public sealed class DesktopStorageAccess : IStorageAccess
{
    public async Task<ResourceSource[]> PickInputsAsync(bool folders, CancellationToken cancellation = default)
    {
        var storage = UiPlatform.ActivePage.StorageProvider;
        var items = folders ? (await storage.OpenFolderPickerAsync(new() { AllowMultiple = true, Title = UiPlatform.CurrentText["OpenFolder"] })).Cast<IStorageItem>() :
            await storage.OpenFilePickerAsync(new() { AllowMultiple = true, Title = UiPlatform.CurrentText["Open"] });
        return items.Select(i => i.TryGetLocalPath()).Where(p => p != null).Select(Source).ToArray();
    }
    public async Task<ResourceSource> PickOutputAsync(bool folder, string suggestedName = null, CancellationToken cancellation = default)
    {
        var storage = UiPlatform.ActivePage.StorageProvider;
        string path = folder ? (await storage.OpenFolderPickerAsync(new() { Title = UiPlatform.CurrentText["SelectOutput"] })).FirstOrDefault()?.TryGetLocalPath() :
            (await storage.SaveFilePickerAsync(new() { SuggestedFileName = suggestedName, Title = UiPlatform.CurrentText["Save"] }))?.TryGetLocalPath();
        return path == null ? null : Source(path);
    }
    static ResourceSource Source(string path) => new(Path.GetFullPath(path), Path.GetFileName(path), Path.GetFullPath(path));
    public Task<ResourceSource> RestoreAsync(string id, CancellationToken cancellation = default) => Task.FromResult(Source(id));
    public Task PrepareAsync(ResourceSource source, CancellationToken cancellation = default) => Task.CompletedTask;
    public Task CommitAsync(string localPath, OverwritePolicy overwrite, CancellationToken cancellation = default) => Task.CompletedTask;
    public string DisplayPath(string localPath) => localPath;
    public string PersistentId(string localPath) => localPath;
}
