using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace GARbro.Desktop;

/// Startup arguments and native document-open events share one window loading path.
public sealed class FileActivationRouter
{
    readonly MainWindow window;
    readonly Queue<(string Key, string[] Files)> pending = new();
    readonly HashSet<string> queued = new(StringComparer.Ordinal);
    bool ready, draining, stopped;
    string startupKey;
    DateTime startupTime;
    public FileActivationRouter(MainWindow window) => this.window = window;
    public void Open(IEnumerable<string> inputs, bool startup = false)
    {
        var values = inputs.ToArray();
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Open(values, startup)); return; }
        if (stopped) return;
        string[] files = Normalize(values);
        DesktopServices.Activity("activation-request", $"arguments={values.Length}; resources={files.Length}; startup={startup}");
        if (files.Length == 0) return;
        string key = string.Join('\0', files);
        if (startup) { startupKey = key; startupTime = DateTime.UtcNow; }
        // macOS may deliver the same cold-start document in argv and an open event.
        else if (key == startupKey && DateTime.UtcNow - startupTime < TimeSpan.FromSeconds(2)) return;
        if (!queued.Add(key)) return;
        pending.Enqueue((key, files));
        _ = Drain();
    }
    public static string[] Normalize(IEnumerable<string> inputs) => inputs.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s =>
    {
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.IsFile) s = uri.LocalPath;
        try { return Path.GetFullPath(s); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }).Where(s => s != null && (File.Exists(s) || Directory.Exists(s)))
      .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
    public void Ready() { ready = true; _ = Drain(); }
    public void Stop() { stopped = true; pending.Clear(); queued.Clear(); }
    async Task Drain()
    {
        if (!ready || draining || stopped) return;
        draining = true;
        try
        {
            while (!stopped && pending.TryDequeue(out var request))
            {
                bool success = false;
                await window.Guard(async () => { await window.LoadInputsAsync(request.Files); success = true; });
                queued.Remove(request.Key);
                DesktopServices.Activity("file-activation", $"resources={request.Files.Length}; success={success}; archive={window.ArchiveTag}; entries={window.VisibleEntryCount}");
            }
        }
        finally { draining = false; }
    }
}
