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
                if (i + 1 == args.Length || args[i] is not ("--input" or "--luca" or "--output" or "--report" or "--version") || !arguments.TryAdd(args[i], args[i + 1]))
                    throw new ArgumentException("SchemeTool --input DATABASE[.gz] --luca Luca.json --output EXPORT.json --report REPORT.json --version NUMBER");
            }
            foreach (string key in new[] { "--input", "--luca", "--output", "--report", "--version" })
                if (!arguments.ContainsKey(key)) throw new ArgumentException("Missing " + key);
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
            var schemes = JsonSerializer.Deserialize<Dictionary<string, LucaProfile>>(File.ReadAllBytes(arguments["--luca"]), options);
            if (schemes == null || schemes.Count == 0 || schemes.Count > 1024) throw new InvalidDataException("Invalid Luca scheme database.");
            foreach (var scheme in schemes)
            {
                if (string.IsNullOrWhiteSpace(scheme.Key)) throw new InvalidDataException("Scheme names cannot be empty.");
                LucaScript.ValidateProfile(JsonSerializer.Serialize(scheme.Value, options));
            }
            var format = catalog.Formats.OfType<LucaScriptFormat>().Single();
            format.Scheme = new LucaScheme { KnownSchemes = schemes };
            using var exported = new MemoryStream();
            catalog.SerializePortableSchemeJson(exported);
            var updated = JsonNode.Parse(exported.ToArray());
            var document = source.DeepClone();
            document["SchemeMap"]["LUCA-SCRIPT"] = updated["SchemeMap"]["LUCA-SCRIPT"].DeepClone();
            document["Version"] = version;
            byte[] encoded = Encoding.UTF8.GetBytes(document.ToJsonString(options) + "\n");
            using var check = new MemoryStream(encoded, false);
            var report = catalog.DeserializePortableSchemeJson(check);
            if (report.Failed.Count != 0 || report.Unported.Length != 0) throw new InvalidDataException("The updated database failed its read-back validation.");
            WriteAtomic(arguments["--output"], encoded);
            WriteAtomic(arguments["--report"], JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
            Console.WriteLine("Database {0}: {1} scheme groups, {2} Luca game profiles.", version, report.Applied.Length, schemes.Count);
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
