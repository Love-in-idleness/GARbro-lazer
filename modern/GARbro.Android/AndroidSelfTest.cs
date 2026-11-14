#if GARBRO_ANDROID_TEST
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GARbro.Application;
using GARbro.Desktop;

namespace GARbro.Mobile;

/// <summary>Compiled exclusively into the internal test APK, never the public application.</summary>
public static class AndroidSelfTest
{
    static Task<T> Request<T>(WorkerRequest request, CancellationToken cancellation = default) => UiPlatform.Worker.ExecuteAsync<T>(request,
        new(120, 1L << 30), cancellation: cancellation);
    static void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    public static async Task RunAsync(MainActivity activity, MainWindow browser)
    {
        var checks = new List<string>(); string root = Path.Combine(activity.FilesDir.AbsolutePath, "self-test"); Directory.CreateDirectory(root);
        string result = Path.Combine(activity.FilesDir.AbsolutePath, "test-results.json"); File.Delete(result);
        try
        {
            await Task.Run(() => CopyAssets(activity, "TestFixtures", root));
            string archive = Path.Combine(root, "standard.xfl");
            await browser.LoadArchiveAsync(archive); Check(browser.ArchiveTag == "XFL" && browser.VisibleEntryCount == 3, "Shared UI failed to list XFL."); checks.Add("ui-list");
            string output = Path.Combine(root, "extracted-" + Guid.NewGuid().ToString("N")); await browser.ExtractToAsync(output);
            Check(File.ReadAllText(Path.Combine(output, "hello.txt")) == "Hello, GARbro!\n", "IPC extraction changed the payload."); checks.Add("ipc-extract");
            var preview = await Request<ResourcePreview>(new("resource-preview", null, Location: new(archive), Entry: "hello.txt", ForcedType: "script"));
            Check(preview.Text.Contains("Hello, GARbro!"), "Text preview failed."); checks.Add("text");
            var png = await Request<ResourcePreview>(new("resource-preview", null, Location: new(root), Entry: "colors-indexed.png", ForcedType: "image"));
            Check(png.Image?.Length > 0, "Portable PNG preview failed."); checks.Add("png");
            string wav = Path.Combine(root, "preview.wav"); var audio = await Request<AudioArtifact>(new("resource-audio", null, Location: new(root), Entry: "tone.wav", Output: wav));
            Check(audio.Bytes > 44 && File.Exists(wav), "PCM audio artifact failed.");
            using (var player = UiPlatform.Audio(wav)) { Check(!player.Playing, "Audio started automatically."); player.Play(); await Task.Delay(100); player.Pause(); player.Position = player.Duration / 2; player.Volume = .25; player.Stop(); }
            checks.Add("audio");
            using (var cancellation = new CancellationTokenSource()) { cancellation.Cancel(); try { await Request<BrowserListing>(new("resource-list", null, Location: new(archive)), cancellation.Token); throw new Exception("Cancellation was ignored."); } catch (OperationCanceledException) { } }
            checks.Add("cancel");
            await CheckDocuments(activity, archive); checks.Add("saf-list-extract-conflict-revocation");
            string createRoot = Path.Combine(root, "writer-inputs"); Directory.CreateDirectory(createRoot);
            File.WriteAllText(Path.Combine(createRoot, "hello.txt"), "Hello writer!\n"); File.WriteAllBytes(Path.Combine(createRoot, "00000001.scr"), new byte[] { 0x53, 0x43, 0x52, 0, 1, 2, 3, 4 });
            var catalog = await Request<ResourceFormatInfo[]>(new("resource-catalog", null)); Check(catalog.Length == 1247, "Provider catalog changed.");
            var writers = catalog.Where(f => f.Type == "archive" && f.CanWrite).ToArray(); Check(writers.Length == 22, "Writer catalog changed.");
            var exports = new List<object>(); string archives = Path.Combine(root, "created-archives"); Directory.CreateDirectory(archives);
            for (int i = 0; i < writers.Length; i++)
            {
                var writer = writers[i]; string file = "archive-" + i + "." + writer.Extensions.FirstOrDefault(); string target = Path.Combine(archives, file);
                string options = writer.Tag == "NPK" ? "{\"Key\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\"}" : writer.Tag == "YPF" ? "{\"Key\":90,\"Version\":256,\"Scheme\":\"none\"}" : null;
                string entry = writer.Tag == "AMI" ? "00000001.scr" : "hello.txt";
                await Request<string>(new("resource-create", createRoot, Output: target, Inputs: new[] { entry }, FormatId: writer.Id, OptionsJson: options, Overwrite: OverwritePolicy.Replace));
                var listing = await Request<BrowserListing>(new("resource-list", null, Location: new(target), OptionsJson: options ?? (writer.OptionsJson == "null" ? null : writer.OptionsJson)));
                Check(listing.Entries.Any(e => e.Name == entry), "Cannot read the created " + writer.Tag + " archive.");
                byte[] data = File.ReadAllBytes(Path.Combine(createRoot, entry)); exports.Add(new { file, tag = writer.Tag, options = options ?? writer.OptionsJson, entry, size = data.Length, sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant() });
            }
            File.WriteAllText(Path.Combine(archives, "created-archives.json"), JsonSerializer.Serialize(exports)); checks.Add("22-writers");
            File.WriteAllText(result, JsonSerializer.Serialize(new { success = true, architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), checks }));
        }
        catch (Exception error)
        {
            DesktopServices.Log(error); File.WriteAllText(result, JsonSerializer.Serialize(new { success = false, checks, error = error.ToString() }));
        }
    }
    static async Task CheckDocuments(MainActivity activity, string archive)
    {
        string root = Path.Combine(activity.FilesDir.AbsolutePath, "document-tests");
        string input = Path.Combine(root, "in"), output = Path.Combine(root, "out"); Directory.CreateDirectory(input); Directory.CreateDirectory(output);
        File.Copy(archive, Path.Combine(input, "中文 sample.xfl"), true);
        var storage = AndroidBootstrap.Storage;
        var source = await storage.ImportAsync(global::Android.Provider.DocumentsContract.BuildTreeDocumentUri(TestDocumentsProvider.Authority, "in"), true, 0);
        var target = await storage.ImportAsync(global::Android.Provider.DocumentsContract.BuildTreeDocumentUri(TestDocumentsProvider.Authority, "out"), true, 0, true);
        var listing = await Request<BrowserListing>(new("resource-list", null, Location: new(source.LocalPath)));
        Check(listing.Entries.Single().Size == new FileInfo(archive).Length, "SAF metadata changed.");
        string localArchive = Path.Combine(source.LocalPath, "中文 sample.xfl");
        await Request<ResourceJobReport>(new("resource-extract", null, Location: new(localArchive), Output: target.LocalPath, Overwrite: OverwritePolicy.Replace));
        Check(File.ReadAllText(Path.Combine(output, "hello.txt")) == "Hello, GARbro!\n", "SAF output changed.");
        TestDocumentsProvider.RenameSupported = false;
        try
        {
            try { await storage.CommitAsync(target.LocalPath, OverwritePolicy.Replace); throw new Exception("Unsafe provider replacement was accepted."); }
            catch (IOException) { Check(File.ReadAllText(Path.Combine(output, "hello.txt")) == "Hello, GARbro!\n", "Conflict erased the original output."); }
        }
        finally { TestDocumentsProvider.RenameSupported = true; }
        TestDocumentsProvider.Available = false;
        try
        {
            try { storage.TryList(new(source.LocalPath)); throw new Exception("Revoked source access was accepted."); }
            catch (Java.Lang.SecurityException) { }
        }
        finally { TestDocumentsProvider.Available = true; }
    }
    static void CopyAssets(MainActivity activity, string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string child in activity.Assets.List(source))
        {
            string remote = source + "/" + child, local = Path.Combine(target, child);
            if (activity.Assets.List(remote).Length > 0) CopyAssets(activity, remote, local);
            else { using var input = activity.Assets.Open(remote); using var output = File.Create(local); input.CopyTo(output); }
        }
    }
}
#endif
