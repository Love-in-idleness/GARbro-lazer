using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using GARbro.Application;
using GameRes;

internal static class SchemeDatabase
{
    static string DirectoryPath
    {
        get
        {
            string settings = Environment.GetEnvironmentVariable("GARBRO_SETTINGS_PATH") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GARbro", "settings.json");
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settings)), "cli-databases");
        }
    }
    static string ActiveFile => Path.Combine(DirectoryPath, "active");
    public static string Active()
    {
        if (!File.Exists(ActiveFile)) return null;
        if (new FileInfo(ActiveFile).Length > 128) throw new InvalidDataException("Invalid saved scheme database.");
        string file = File.ReadAllText(ActiveFile);
        if (Path.GetFileName(file) != file || !(file.EndsWith(".json") || file.EndsWith(".json.gz")))
            throw new InvalidDataException("Invalid saved scheme database.");
        return Path.Combine(DirectoryPath, file);
    }
    public static void Reset() => File.Delete(ActiveFile);

    public static SchemeImportReport Install(string source, CancellationToken cancellation)
    {
        bool compressed = source.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
        long limit = (compressed ? 32L : 64L) << 20;
        Directory.CreateDirectory(DirectoryPath);
        string staging = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + (compressed ? ".json.gz" : ".json"));
        string pointer = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            string name;
            using (var input = File.OpenRead(source))
            using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                if (input.Length > limit) throw new InvalidDataException("Format database exceeds the size limit.");
                byte[] buffer = new byte[81920];
                int count;
                while ((count = input.Read(buffer)) != 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (output.Length > limit - count) throw new InvalidDataException("Format database exceeds the size limit.");
                    output.Write(buffer, 0, count);
                }
                output.Position = 0;
                name = Convert.ToHexString(SHA256.HashData(output)).ToLowerInvariant() + (compressed ? ".json.gz" : ".json");
            }
            var report = ArchiveService.LoadSchemes(staging);
            if (report.Failed.Count > 0) return report;
            cancellation.ThrowIfCancellationRequested();
            File.Move(staging, Path.Combine(DirectoryPath, name), true);
            File.WriteAllText(pointer, name);
            File.Move(pointer, ActiveFile, true);
            return report;
        }
        finally { File.Delete(staging); File.Delete(pointer); }
    }
}
