using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GARbro.Desktop;

public static class DesktopServices
{
    internal const string RepositoryPath = "Love-in-idleness/GARbro-lazer";
    public const string Repository = "https://github.com/" + RepositoryPath;
    public static string Version => typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.3.0-preview.1";
    public static string DataDirectory => UiPlatform.DataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GARbro");
    public static string CacheDirectory => Path.Combine(DataDirectory, "cache");
    static readonly object logGate = new();
    public static string Redact(string text)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0) text = text.Replace(home, "<home>", StringComparison.OrdinalIgnoreCase);
        text = Regex.Replace(text, """(?i)(["']?(?:password|passphrase|secret|contentkey|encryptionkey|key)["']?\s*[:=]\s*)(?:"(?:\\.|[^"\\])*"|'[^']*'|[^\s,;}]+)""", "$1<redacted>");
        text = Regex.Replace(text, """[A-Za-z]:[\\/][^\r\n"<>]+""", "<path>");
        return Regex.Replace(text, """(?<![\w:])/(?!/)[^\r\n"<>]+""", "<path>");
    }
    public static void Log(Exception error)
        => WriteLog(new { timestamp = DateTimeOffset.UtcNow, type = error.GetType().Name,
            message = Redact(error.Message), stack = Redact(error.StackTrace ?? "") });
    public static void Activity(string action, string detail)
        => WriteLog(new { timestamp = DateTimeOffset.UtcNow, action, detail = Redact(detail) });
    static void WriteLog(object record)
    {
        try
        {
            lock (logGate)
            {
                Directory.CreateDirectory(DataDirectory); string path = Path.Combine(DataDirectory, "desktop.log");
                if (File.Exists(path) && new FileInfo(path).Length > 5 << 20) File.Move(path, path + ".previous", true);
                // Do not include option JSON, key material or source contents.
                File.AppendAllText(path, JsonSerializer.Serialize(record) + "\n");
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    public static void ExportLogs(string output, AppSettings settings)
    {
        using var file = new FileStream(output, FileMode.CreateNew); using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        void Add(string name, string value) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(value); }
        Add("environment.json", JsonSerializer.Serialize(new { version = Version, os = RuntimeInformation.OSDescription,
            runtime = RuntimeInformation.FrameworkDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(), settings.Language, settings.Theme,
            settings.TextEncoding, settings.TimeoutSeconds, settings.MemoryMiB }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (string name in new[] { "desktop.log", "desktop.log.previous" })
        { string path = Path.Combine(DataDirectory, name); if (File.Exists(path)) Add(name, Redact(File.ReadAllText(path))); }
    }
    public static string ReadResource(string suffix)
    {
        var assembly = typeof(MainWindow).Assembly;
        string name = Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith(suffix, StringComparison.Ordinal));
        if (name == null) return "";
        using var stream = assembly.GetManifestResourceStream(name); using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    internal const string PythonDownloads = "https://www.python.org/downloads/";
    internal const string MoleBoxToolDownload = "https://lifeinhex.com/download/demoleition-vs/";

    public static void OpenUrl(string url)
    {
        var uri = new Uri(url);
        if (uri.Scheme != "https" || (uri.Host != "github.com" && url is not (PythonDownloads or MoleBoxToolDownload)))
            throw new ArgumentException("Unsupported help URL.");
        if (UiPlatform.OpenUrl != null) { UiPlatform.OpenUrl(url); return; }
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    public static void Explore(string path)
    {
        if (UiPlatform.Explore != null) { UiPlatform.Explore(path); return; }
        if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", Directory.Exists(path) ? path : "/select,\"" + path + "\"") { UseShellExecute = true });
        else if (OperatingSystem.IsMacOS()) Start("/usr/bin/open", "-R", path);
        else Start("xdg-open", Directory.Exists(path) ? path : Path.GetDirectoryName(path));
    }
    static Process Start(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument); return Process.Start(info);
    }
    public static async Task Trash(string path)
    {
        if (UiPlatform.Trash != null) { await UiPlatform.Trash(path); return; }
        if (OperatingSystem.IsWindows())
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        else if (OperatingSystem.IsMacOS())
        {
            // Finder handles external volumes and records restoration metadata.
            string escaped = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
            using var process = Start("/usr/bin/osascript", "-e", "tell application \"Finder\" to delete POSIX file \"" + escaped + "\"");
            await process.WaitForExitAsync(); if (process.ExitCode != 0) throw new IOException("Cannot move the file to Trash.");
        }
        else
        {
            using var process = Start("gio", "trash", "--", path); await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new IOException("Cannot move the file to Trash.");
        }
    }
}
