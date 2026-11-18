using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GARbro.Scripting;
using GameRes;
using GameRes.Formats.Luca;
using Avc = GameRes.Formats.AVC;

namespace GARbro.SchemeTool;

internal static class Program
{
    static int Main(string[] args)
    {
        try
        {
            var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i += 2)
            {
                if (i + 1 == args.Length || args[i] is not ("--input" or "--luca" or "--avc" or "--game-map" or "--output" or "--report" or "--version") || !arguments.TryAdd(args[i], args[i + 1]))
                    throw new ArgumentException("SchemeTool --input DATABASE[.gz] [--luca Luca.json] [--avc Avc.json] [--game-map Games.json] --output EXPORT.json --report REPORT.json --version NUMBER");
            }
            foreach (string key in new[] { "--input", "--output", "--report", "--version" })
                if (!arguments.ContainsKey(key)) throw new ArgumentException("Missing " + key);
            if (!arguments.ContainsKey("--luca") && !arguments.ContainsKey("--avc") && !arguments.ContainsKey("--game-map"))
                throw new ArgumentException("Select --luca, --avc or --game-map scheme data.");
            var catalog = FormatCatalog.Instance;
            JsonNode source;
            using (var file = File.OpenRead(arguments["--input"]))
            using (Stream input = arguments["--input"].EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? new GZipStream(file, CompressionMode.Decompress) : file)
            {
                source = JsonNode.Parse(input);
                using var serialized = new MemoryStream(Encoding.UTF8.GetBytes(source.ToJsonString()));
                var imported = catalog.DeserializePortableSchemeJson(serialized);
                if (imported.Failed.Count != 0 || imported.Unported.Length != 0)
                    throw new InvalidDataException("Resolve every existing scheme import before updating the database.");
            }
            int version = int.Parse(arguments["--version"], CultureInfo.InvariantCulture);
            if (version <= catalog.CurrentSchemeVersion) throw new ArgumentException("The new database version must be greater than the loaded version.");
            var options = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
            var changed = new List<string>();
            if (arguments.TryGetValue("--luca", out string luca))
            {
                var schemes = JsonSerializer.Deserialize<Dictionary<string, LucaProfile>>(File.ReadAllBytes(luca), options);
                if (schemes == null || schemes.Count == 0 || schemes.Count > 1024) throw new InvalidDataException("Invalid Luca scheme database.");
                foreach (var scheme in schemes)
                {
                    if (string.IsNullOrWhiteSpace(scheme.Key)) throw new InvalidDataException("Scheme names cannot be empty.");
                    LucaScript.ValidateProfile(JsonSerializer.Serialize(scheme.Value, options));
                }
                catalog.Formats.OfType<LucaScriptFormat>().Single().Scheme = new LucaScheme { KnownSchemes = schemes };
                changed.Add("LUCA-SCRIPT");
            }
            if (arguments.TryGetValue("--avc", out string avc))
            {
                var records = JsonSerializer.Deserialize<Avc.ArchiveScheme[]>(File.ReadAllBytes(avc), options);
                if (records == null || records.Length == 0 || records.Length > 1024) throw new InvalidDataException("Invalid AVC scheme data.");
                foreach (var record in records)
                    if (record?.Password == null || record.Password.Length != 8 || record.Password.Any(c => c < 32 || c > 126) ||
                        record.KeyOffset < 0 || record.KeyOffset > 0x78 || record.HeaderOffset < 0 || record.HeaderOffset > 0x5c)
                        throw new InvalidDataException("Invalid AVC password or header offsets.");
                var provider = catalog.ArcFormats.OfType<Avc.DatOpener>().Single();
                var existing = (Avc.AvcScheme)provider.Scheme;
                provider.Scheme = new Avc.AvcScheme { KnownSchemes = (existing.KnownSchemes ?? Array.Empty<Avc.ArchiveScheme>())
                    .Concat(records).DistinctBy(s => (s.Password, s.KeyOffset, s.HeaderOffset)).ToArray() };
                changed.Add("AVC");
            }
            using var exported = new MemoryStream();
            catalog.SerializePortableSchemeJson(exported);
            var updated = JsonNode.Parse(exported.ToArray());
            var document = source.DeepClone();
            foreach (string tag in changed) document["SchemeMap"][tag] = updated["SchemeMap"][tag].DeepClone();
            if (arguments.TryGetValue("--game-map", out string games))
            {
                var aliases = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(games));
                if (aliases == null || aliases.Count == 0) throw new InvalidDataException("The game map is empty.");
                var map = document["GameMap"].AsObject();
                foreach (var alias in aliases)
                {
                    if (string.IsNullOrWhiteSpace(alias.Key) || alias.Key.IndexOfAny(new[] { '/', '\\' }) >= 0 || string.IsNullOrWhiteSpace(alias.Value))
                        throw new InvalidDataException("Game aliases require a filename and a title.");
                    map[alias.Key] = alias.Value;
                }
                changed.Add("GameMap");
            }
            document["Version"] = version;
            byte[] encoded = Encoding.UTF8.GetBytes(document.ToJsonString(options) + "\n");
            using var check = new MemoryStream(encoded, false);
            var report = catalog.DeserializePortableSchemeJson(check);
            if (report.Failed.Count != 0 || report.Unported.Length != 0) throw new InvalidDataException("The updated database failed its read-back validation.");
            WriteAtomic(arguments["--output"], encoded);
            WriteAtomic(arguments["--report"], JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
            Console.WriteLine("Database {0}: {1} scheme groups; updated {2}.", version, report.Applied.Length, string.Join(", ", changed));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    static void WriteAtomic(string path, byte[] data)
    {
        string target = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, data); File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
