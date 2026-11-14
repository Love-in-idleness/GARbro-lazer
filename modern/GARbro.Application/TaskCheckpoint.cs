using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GARbro.Application;

/// <summary>Only committed paths and source fingerprints. Decoder options never reach this file.</summary>
public sealed class TaskCheckpoint
{
    public sealed record Data(string Key, string State, Dictionary<string, ArchiveFingerprint> Sources, string[] CompletedPaths);
    readonly string path;
    readonly Data data;
    readonly HashSet<string> completed;
    public string[] CompletedPaths => completed.ToArray();
    TaskCheckpoint(string path, Data data) { this.path = path; this.data = data; completed = new(data.CompletedPaths, StringComparer.Ordinal); }
    public static TaskCheckpoint Begin(string path, WorkerRequest request)
    {
        string root = request.Location?.RootPath ?? request.Path;
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            request.Operation, root, request.Location?.Archives, request.Location?.Directory, request.Output,
            request.Selection, request.ResourceOptions, request.FormatId, request.Inputs
        }))));
        Data previous = null;
        if (File.Exists(path) && new FileInfo(path).Length <= 64L << 20)
            try { previous = JsonSerializer.Deserialize<Data>(File.ReadAllText(path)); } catch (JsonException) { }
        if (previous?.Key == key && previous.State != "completed")
        {
            foreach (var source in previous.Sources)
                if (!File.Exists(source.Key) || ArchiveFingerprint.Read(source.Key) != source.Value)
                    throw new IOException("A source changed after the interrupted task. Choose a different output directory.");
            var resumed = new TaskCheckpoint(path, previous with { State = "running" });
            string committed = path + ".committed";
            if (File.Exists(committed))
            {
                if (new FileInfo(committed).Length > 64L << 20) throw new InvalidDataException("Task checkpoint is too large.");
                foreach (string line in File.ReadLines(committed))
                    try { if (JsonSerializer.Deserialize<string>(line) is string file) resumed.completed.Add(file); } catch (JsonException) { }
            }
            resumed.Save(); File.Delete(committed); return resumed;
        }
        var sources = new Dictionary<string, ArchiveFingerprint>(StringComparer.Ordinal);
        if (root != null && File.Exists(root)) sources[root] = ArchiveFingerprint.Read(root);
        else if (root != null && Directory.Exists(root))
            foreach (string file in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                if (request.Output != null && Path.GetFullPath(request.Output) != Path.GetFullPath(root) && Inside(request.Output, file)) continue;
                if (sources.Count >= (request.Extraction?.MaxEntries ?? 262144)) throw new InvalidDataException("Source entry limit exceeded.");
                sources[file] = ArchiveFingerprint.Read(file);
            }
        var checkpoint = new TaskCheckpoint(path, new(key, "running", sources, request.CompletedPaths ?? Array.Empty<string>())); checkpoint.Save(); File.Delete(path + ".committed"); return checkpoint;
    }
    static bool Inside(string root, string target)
    { string relative = Path.GetRelativePath(root, target); return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar) && !Path.IsPathRooted(relative); }
    public void Commit(string output) { if (output != null && completed.Add(output)) File.AppendAllText(path + ".committed", JsonSerializer.Serialize(output) + "\n"); }
    public void SetState(string state) { Save(state); File.Delete(path + ".committed"); }
    void Save(string state = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(data with { State = state ?? "running", CompletedPaths = completed.ToArray() })); File.Move(temporary, path, true);
    }
}
