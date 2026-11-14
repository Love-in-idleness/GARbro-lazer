using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using global::Android.Content;
using global::Android.OS;
using global::Android.Provider;
using GARbro.Application;
using GARbro.Desktop;
using Uri = global::Android.Net.Uri;
using Document = global::Android.Provider.DocumentsContract.Document;

namespace GARbro.Mobile;

public sealed class AndroidStorageAccess : IStorageAccess
{
    public sealed record Mapping(string Id, string Uri, string Name, string Path, bool Folder, bool Output, bool CacheOnly = false);
    sealed record DocumentInfo(string Id, string Name, string Mime, long Size, long Modified, int Flags)
    { public bool Folder => Mime == Document.MimeTypeDir; }
    readonly Context context;
    readonly string root, registry;
    readonly Dictionary<string, Mapping> mappings = new();
    readonly object stateGate = new();
    readonly SemaphoreSlim ioGate = new(1, 1);
    readonly HashSet<string> protectedSources = new(StringComparer.Ordinal);
    public AndroidStorageAccess(Context context)
    {
        this.context = context; root = Path.Combine(context.FilesDir.AbsolutePath, "worksets"); registry = Path.Combine(context.FilesDir.AbsolutePath, "sources.json");
        Directory.CreateDirectory(root);
        if (File.Exists(registry))
            try { foreach (var m in JsonSerializer.Deserialize<Mapping[]>(File.ReadAllText(registry)) ?? Array.Empty<Mapping>()) mappings[m.Id] = m; }
            catch (JsonException e) { DesktopServices.Log(e); File.Move(registry, registry + ".invalid-" + Guid.NewGuid().ToString("N")); }
        RecoverPendingDocuments();
    }
    static ResourceSource Source(Mapping m) => new(m.Id, m.Name, m.Path);
    void Save()
    {
        string temporary = registry + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(mappings.Values.ToArray())); File.Move(temporary, registry, true);
    }
    public async Task<ResourceSource[]> PickInputsAsync(bool folders, CancellationToken cancellation = default)
    {
        var intent = new Intent(folders ? Intent.ActionOpenDocumentTree : Intent.ActionOpenDocument);
        if (!folders) { intent.SetType("*/*"); intent.AddCategory(Intent.CategoryOpenable); intent.PutExtra(Intent.ExtraAllowMultiple, true); }
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);
        var result = await MainActivity.Current.PickAsync(intent); if (result == null) return Array.Empty<ResourceSource>();
        var uris = new List<Uri>();
        if (result.ClipData is { } clip) for (int i = 0; i < clip.ItemCount; i++) uris.Add(clip.GetItemAt(i).Uri);
        else if (result.Data != null) uris.Add(result.Data);
        var sources = new List<ResourceSource>();
        foreach (var uri in uris) sources.Add(await ImportAsync(uri, folders, result.Flags));
        return sources.ToArray();
    }
    public async Task<ResourceSource> PickOutputAsync(bool folder, string suggestedName = null, CancellationToken cancellation = default)
    {
        var intent = new Intent(folder ? Intent.ActionOpenDocumentTree : Intent.ActionCreateDocument);
        if (!folder) { intent.SetType("application/octet-stream"); intent.AddCategory(Intent.CategoryOpenable); intent.PutExtra(Intent.ExtraTitle, suggestedName ?? "archive.bin"); }
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);
        var result = await MainActivity.Current.PickAsync(intent); if (result?.Data == null) return null;
        return await ImportAsync(result.Data, folder, result.Flags, true);
    }
    public async Task<ResourceSource> ImportAsync(Uri uri, bool folder, ActivityFlags flags, bool output = false)
    {
        bool persistent = (flags & ActivityFlags.GrantPersistableUriPermission) != 0;
        if (persistent)
            try { context.ContentResolver.TakePersistableUriPermission(uri, flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission)); }
            catch (Java.Lang.SecurityException) { persistent = false; }
        string physical = PhysicalPath(uri, folder);
        if (physical != null) return new(physical, Path.GetFileName(physical), physical);
        string id = (output ? "output:" : "input:") + uri;
        Mapping mapping;
        lock (stateGate)
        {
            var doc = Info(DocumentUri(uri, folder));
            string name = doc.Name; ValidateName(name);
            string directory = Path.Combine(root, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id))).ToLowerInvariant());
            Directory.CreateDirectory(directory);
            mapping = new(id, uri.ToString(), name, folder ? Path.Combine(directory, "files") : Path.Combine(directory, name), folder, output, !persistent && !output);
            if (folder) Directory.CreateDirectory(mapping.Path);
            mappings[id] = mapping; Save();
        }
        if (!persistent && !output && !folder) await DownloadAsync(mapping.Path, uri, CancellationToken.None);
        return Source(mapping);
    }
    static string PhysicalPath(Uri uri, bool folder)
    {
        if (!global::Android.OS.Environment.IsExternalStorageManager || uri.Authority != "com.android.externalstorage.documents") return null;
        string id = folder ? DocumentsContract.GetTreeDocumentId(uri) : DocumentsContract.GetDocumentId(uri);
        string[] parts = id.Split(':', 2); if (parts.Length != 2) return null;
        string volume = parts[0] == "primary" ? global::Android.OS.Environment.ExternalStorageDirectory.AbsolutePath :
            System.Text.RegularExpressions.Regex.IsMatch(parts[0], "^[A-Za-z0-9-]+$") ? "/storage/" + parts[0] : null;
        if (volume == null) return null;
        string path = Path.GetFullPath(Path.Combine(volume, parts[1]));
        return Inside(volume, path) && (File.Exists(path) || Directory.Exists(path) || Directory.Exists(Path.GetDirectoryName(path))) ? path : null;
    }
    public Task<ResourceSource> RestoreAsync(string id, CancellationToken cancellation = default)
    {
        lock (stateGate)
        {
            if (mappings.TryGetValue(id, out var m)) return Task.FromResult(Source(m));
            if (Find(id) is { } existing) return Task.FromResult(new ResourceSource(existing.Id, existing.Name, id));
        }
        if (!Path.IsPathFullyQualified(id)) throw new IOException("Reopen this resource to grant access again.");
        return Task.FromResult(new ResourceSource(id, Path.GetFileName(id), Path.GetFullPath(id)));
    }
    static bool Inside(string root, string path) { string relative = Path.GetRelativePath(root, path); return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar) && !Path.IsPathRooted(relative); }
    Mapping Find(string path) { lock (stateGate) return mappings.Values.Where(m => m.Folder ? Inside(m.Path, path) : m.Path == path).OrderByDescending(m => m.Path.Length).FirstOrDefault(); }
    static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(new[] { '/', '\\', '\0' }) >= 0)
            throw new InvalidDataException("The document provider returned an invalid filename.");
    }
    static Uri DocumentUri(Uri uri, bool folder) => folder ? DocumentsContract.BuildDocumentUriUsingTree(uri, DocumentsContract.GetTreeDocumentId(uri)) : uri;
    DocumentInfo Info(Uri uri)
    {
        if (!DocumentsContract.IsDocumentUri(context, uri))
        {
            using var shared = context.ContentResolver.Query(uri, new[] { OpenableColumns.DisplayName, OpenableColumns.Size }, null, null, null);
            if (shared == null || !shared.MoveToFirst()) throw new IOException("Resource access expired. Select its folder again.");
            return new(uri.ToString(), shared.GetString(0), context.ContentResolver.GetType(uri) ?? "application/octet-stream", shared.IsNull(1) ? -1 : shared.GetLong(1), 0, 0);
        }
        using var cursor = context.ContentResolver.Query(uri, new[] { Document.ColumnDocumentId, Document.ColumnDisplayName, Document.ColumnMimeType, Document.ColumnSize, Document.ColumnLastModified, Document.ColumnFlags }, null, null, null);
        if (cursor == null || !cursor.MoveToFirst()) throw new IOException("Resource access expired. Select its folder again.");
        return new(cursor.GetString(0), cursor.GetString(1), cursor.GetString(2), cursor.IsNull(3) ? -1 : cursor.GetLong(3), cursor.IsNull(4) ? 0 : cursor.GetLong(4), cursor.GetInt(5));
    }
    List<DocumentInfo> Children(Uri tree, Uri directory)
    {
        var children = new List<DocumentInfo>();
        using var cursor = context.ContentResolver.Query(DocumentsContract.BuildChildDocumentsUriUsingTree(tree, DocumentsContract.GetDocumentId(directory)),
            new[] { Document.ColumnDocumentId, Document.ColumnDisplayName, Document.ColumnMimeType, Document.ColumnSize, Document.ColumnLastModified, Document.ColumnFlags }, null, null, null);
        if (cursor == null) throw new IOException("Cannot read the selected directory. Grant access again.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (cursor.MoveToNext())
        {
            string name = cursor.GetString(1); ValidateName(name);
            if (!names.Add(name)) throw new IOException("The directory contains conflicting filenames: " + name);
            children.Add(new(cursor.GetString(0), name, cursor.GetString(2), cursor.IsNull(3) ? -1 : cursor.GetLong(3), cursor.IsNull(4) ? 0 : cursor.GetLong(4), cursor.GetInt(5)));
        }
        return children;
    }
    Uri Resolve(Mapping mapping, string local)
    {
        var tree = Uri.Parse(mapping.Uri); Uri current = DocumentUri(tree, mapping.Folder);
        if (!mapping.Folder) return current;
        string relative = Path.GetRelativePath(mapping.Path, local); if (relative == ".") return current;
        if (!Inside(mapping.Path, local)) throw new IOException("Invalid resource path.");
        foreach (string part in relative.Split(Path.DirectorySeparatorChar))
        { var child = Children(tree, current).SingleOrDefault(c => c.Name == part) ?? throw new FileNotFoundException("The resource changed. Refresh the folder.", part); current = DocumentsContract.BuildDocumentUriUsingTree(tree, child.Id); }
        return current;
    }
    public BrowserListing TryList(ResourceLocation location)
    {
        var mapping = Find(location.RootPath);
        if (mapping is not { Folder: true, Output: false } || location.Chain.Length > 0) return null;
        var tree = Uri.Parse(mapping.Uri); Uri uri = Resolve(mapping, location.RootPath); if (!Info(uri).Folder) return null;
        Directory.CreateDirectory(location.RootPath);
        var entries = Children(tree, uri).Select(c => new BrowserEntry(c.Name, c.Name, c.Folder ? "" : GuessType(c.Name), Math.Max(0, c.Size), c.Folder)).ToArray();
        foreach (var entry in entries.Where(e => e.IsDirectory)) Directory.CreateDirectory(Path.Combine(location.RootPath, entry.Name));
        return new(location, "DIR", true, entries);
    }
    static string GuessType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    { ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff" or ".tga" => "image", ".wav" or ".mp3" or ".ogg" or ".opus" or ".aiff" => "audio", ".txt" or ".ks" or ".scr" => "script", _ => "" };
    public async Task PrepareAsync(ResourceSource source, CancellationToken cancellation = default)
    {
        var mapping = Find(source.LocalPath); if (mapping == null || mapping.Output || mapping.CacheOnly) return;
        await ioGate.WaitAsync(cancellation);
        try
        {
            Uri uri = Resolve(mapping, source.LocalPath); var info = Info(uri);
            if (info.Folder) await Hydrate(mapping, source.LocalPath, uri, false, 0, cancellation);
            else if (mapping.Folder) await Hydrate(mapping, Path.GetDirectoryName(source.LocalPath), Resolve(mapping, Path.GetDirectoryName(source.LocalPath)), false, 0, cancellation);
            else await DownloadAsync(source.LocalPath, uri, cancellation);
        }
        finally { ioGate.Release(); }
    }
    public async Task PrepareRequestAsync(WorkerRequest request, CancellationToken cancellation)
    {
        protectedSources.Clear();
        string path = request.Location?.RootPath ?? request.Path; if (path == null) return;
        var mapping = Find(path); if (mapping == null || mapping.Output) return;
        if (mapping.CacheOnly && File.Exists(path)) { protectedSources.Add(DocumentIdentity(Uri.Parse(mapping.Uri))); return; }
        await ioGate.WaitAsync(cancellation);
        try
        {
            Uri uri = Resolve(mapping, path); var info = Info(uri);
            if (info.Folder)
            {
                if (request.Operation == "resource-preview" || request.Operation == "resource-audio")
                    await Hydrate(mapping, path, uri, false, 0, cancellation);
                else if (request.Operation is "resource-extract" or "resource-create")
                    await Hydrate(mapping, path, uri, request.Operation == "resource-create" || request.ResourceOptions?.Recursive == true, 0, cancellation);
            }
            else if (mapping.Folder) await Hydrate(mapping, Path.GetDirectoryName(path), Resolve(mapping, Path.GetDirectoryName(path)), false, 0, cancellation);
            else await DownloadAsync(path, uri, cancellation);
        }
        finally { ioGate.Release(); }
    }
    async Task Hydrate(Mapping mapping, string local, Uri uri, bool recursive, int depth, CancellationToken cancellation)
    {
        if (depth > 64) throw new IOException("Source directory nesting exceeds 64 levels.");
        Directory.CreateDirectory(local);
        var children = Children(Uri.Parse(mapping.Uri), uri);
        var names = children.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        foreach (string cached in Directory.EnumerateFileSystemEntries(local))
            if (!names.Contains(Path.GetFileName(cached)))
            { if (Directory.Exists(cached)) Directory.Delete(cached, true); else File.Delete(cached); }
        foreach (var child in children)
        {
            cancellation.ThrowIfCancellationRequested(); string target = Path.Combine(local, child.Name); var remote = DocumentsContract.BuildDocumentUriUsingTree(Uri.Parse(mapping.Uri), child.Id);
            if (child.Folder) { Directory.CreateDirectory(target); if (recursive) await Hydrate(mapping, target, remote, true, depth + 1, cancellation); }
            else await DownloadAsync(target, remote, cancellation);
        }
    }
    async Task DownloadAsync(string path, Uri uri, CancellationToken cancellation)
    {
        protectedSources.Add(DocumentIdentity(uri));
        var info = Info(uri); string stampDirectory = Path.Combine(root, "stamps"); Directory.CreateDirectory(stampDirectory);
        string stamp = Path.Combine(stampDirectory, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)))); string identity = info.Size + ":" + info.Modified;
        if (File.Exists(path) && File.Exists(stamp) && info.Modified > 0 && File.ReadAllText(stamp) == identity) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        if (info.Size > new StatFs(root).AvailableBytes - (16L << 20)) throw new IOException("Not enough temporary storage. Choose local access or free some space.");
        string temporary = path + ".download-" + Guid.NewGuid().ToString("N");
        try
        {
            using var input = context.ContentResolver.OpenInputStream(uri) ?? throw new IOException("Cannot read the selected document.");
            using (var output = File.Create(temporary))
            {
                byte[] buffer = new byte[81920]; int count; long total = 0;
                while ((count = await input.ReadAsync(buffer, cancellation)) != 0)
                {
                    if (new StatFs(root).AvailableBytes - (16L << 20) < count) throw new IOException("Not enough temporary storage. Choose local access or free some space.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellation); total += count;
                }
                if (info.Size >= 0 && total != info.Size) throw new IOException("The resource changed. Refresh the folder.");
            }
            cancellation.ThrowIfCancellationRequested(); File.Move(temporary, path, true); File.WriteAllText(stamp, identity);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async Task CommitAsync(string localPath, OverwritePolicy overwrite, CancellationToken cancellation = default)
    {
        var mapping = Find(localPath); if (mapping is not { Output: true }) return;
        await ioGate.WaitAsync(cancellation);
        try
        {
            if (!mapping.Folder)
            {
                if (protectedSources.Contains(DocumentIdentity(Uri.Parse(mapping.Uri)))) throw new IOException("Cannot replace a source document. Choose another destination.");
                await WriteDocument(localPath, Uri.Parse(mapping.Uri), cancellation); return;
            }
            foreach (string file in Directory.Exists(localPath) ? Directory.EnumerateFiles(localPath, "*", SearchOption.AllDirectories) : new[] { localPath })
            {
                cancellation.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(mapping.Path, file); string[] parts = relative.Split(Path.DirectorySeparatorChar); Uri parent = DocumentUri(Uri.Parse(mapping.Uri), true);
                foreach (string directory in parts[..^1])
                {
                    var existing = Children(Uri.Parse(mapping.Uri), parent).SingleOrDefault(c => c.Name == directory);
                    parent = existing != null ? existing.Folder ? DocumentsContract.BuildDocumentUriUsingTree(Uri.Parse(mapping.Uri), existing.Id) : throw new IOException("Output path conflicts with a file.") :
                        DocumentsContract.CreateDocument(context.ContentResolver, parent, Document.MimeTypeDir, directory) ?? throw new IOException("Cannot create output folder.");
                }
                await PublishFile(mapping, parent, file, parts[^1], overwrite, cancellation);
            }
        }
        finally { ioGate.Release(); }
    }
    async Task PublishFile(Mapping mapping, Uri parent, string file, string name, OverwritePolicy overwrite, CancellationToken cancellation)
    {
        var existing = Children(Uri.Parse(mapping.Uri), parent).SingleOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null && overwrite == OverwritePolicy.Skip) return;
        if (existing != null && overwrite == OverwritePolicy.Fail) throw new IOException("Output exists: " + name);
        if (existing != null && protectedSources.Contains(DocumentIdentity(DocumentsContract.BuildDocumentUriUsingTree(Uri.Parse(mapping.Uri), existing.Id))))
            throw new IOException("Cannot replace a source document. Choose another destination.");
        if (existing != null && (existing.Folder || (existing.Flags & (int)DocumentContractFlags.SupportsRename) == 0))
            throw new IOException("This storage cannot safely replace the existing file. Choose Skip or another destination.");
        string temporaryName = ".garbro-" + Guid.NewGuid().ToString("N");
        Uri temporary = DocumentsContract.CreateDocument(context.ContentResolver, parent, "application/octet-stream", existing == null ? name : temporaryName)
            ?? throw new IOException("Cannot create the output document.");
        string pending = Path.Combine(root, "pending-" + Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(pending, temporary.ToString());
        Uri backup = null; bool committed = false;
        try
        {
            await WriteDocument(file, temporary, cancellation);
            if (existing != null)
            {
                backup = DocumentsContract.RenameDocument(context.ContentResolver, DocumentsContract.BuildDocumentUriUsingTree(Uri.Parse(mapping.Uri), existing.Id), temporaryName + ".backup")
                    ?? throw new IOException("Cannot preserve the previous output.");
                try { temporary = DocumentsContract.RenameDocument(context.ContentResolver, temporary, name) ?? throw new IOException("Cannot commit output."); }
                catch { DocumentsContract.RenameDocument(context.ContentResolver, backup, existing.Name); backup = null; throw; }
            }
            committed = true; File.Delete(pending); if (backup != null) DocumentsContract.DeleteDocument(context.ContentResolver, backup);
        }
        finally { if (!committed) { try { DocumentsContract.DeleteDocument(context.ContentResolver, temporary); File.Delete(pending); } catch (Exception e) { DesktopServices.Log(e); } } }
    }
    async Task WriteDocument(string path, Uri uri, CancellationToken cancellation)
    {
        using var input = File.OpenRead(path); using var output = context.ContentResolver.OpenOutputStream(uri, "wt") ?? throw new IOException("Cannot write output.");
        await input.CopyToAsync(output, cancellation); await output.FlushAsync(cancellation);
    }
    void RecoverPendingDocuments()
    {
        foreach (string pending in Directory.EnumerateFiles(root, "pending-*.json"))
            try { DocumentsContract.DeleteDocument(context.ContentResolver, Uri.Parse(File.ReadAllText(pending))); File.Delete(pending); }
            catch (Exception e) { DesktopServices.Log(e); }
        foreach (string temporary in Directory.EnumerateFiles(root, "*.download-*", SearchOption.AllDirectories)) File.Delete(temporary);
    }
    public string DisplayPath(string localPath)
    {
        lock (stateGate)
        {
            if (mappings.TryGetValue(localPath, out var byId)) return byId.Name;
            var mapping = Find(localPath); if (mapping == null) return localPath;
            string relative = Path.GetRelativePath(mapping.Path, localPath); return mapping.Name + (relative == "." ? "" : "/" + relative);
        }
    }
    public async Task ClearSourceCacheAsync(CancellationToken cancellation = default)
    {
        await ioGate.WaitAsync(cancellation);
        try
        {
            Mapping[] sources; lock (stateGate) sources = mappings.Values.Where(m => !m.Output).ToArray();
            foreach (var source in sources)
            {
                cancellation.ThrowIfCancellationRequested();
                if (Directory.Exists(source.Path)) Directory.Delete(source.Path, true);
                else File.Delete(source.Path);
                if (source.Folder) Directory.CreateDirectory(source.Path);
            }
            string stamps = Path.Combine(root, "stamps"); if (Directory.Exists(stamps)) Directory.Delete(stamps, true);
        }
        finally { ioGate.Release(); }
    }
    public string PersistentId(string localPath) { lock (stateGate) return Find(localPath) is { } mapping && mapping.Path == localPath ? mapping.Id : localPath; }
    static string DocumentIdentity(Uri uri)
    {
        try { return uri.Authority + ":" + DocumentsContract.GetDocumentId(uri); }
        catch (Java.Lang.IllegalArgumentException) { return uri.ToString(); }
    }
    public string ParentPath(string localPath)
    {
        var mapping = Find(localPath); if (mapping != null)
            return mapping.Folder && localPath != mapping.Path ? Path.GetDirectoryName(localPath) : null;
        string primary = global::Android.OS.Environment.ExternalStorageDirectory.AbsolutePath;
        string parent = Path.GetDirectoryName(localPath);
        return parent != null && Inside(primary, parent) ? parent : null;
    }
    public bool CanExplore(string path) => Find(path) is { Folder: true };
    public void Explore(string localPath)
    {
        var mapping = Find(localPath);
        if (mapping is { Folder: true }) MainActivity.Current.StartActivity(new Intent(Intent.ActionView).SetDataAndType(Resolve(mapping,
            Directory.Exists(localPath) ? localPath : Path.GetDirectoryName(localPath)), Document.MimeTypeDir).AddFlags(ActivityFlags.GrantReadUriPermission));
    }
}
