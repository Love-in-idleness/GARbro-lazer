using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using GARbro.Scripting;

namespace GameRes.Formats.Luca
{
    [Serializable]
    public sealed class LucaProfile
    {
        public string ExpressionEncoding = "utf-8";
        public string TextEncoding = "utf-16";
        public string[] Opcodes = Array.Empty<string>();
        public Dictionary<string, string[]> Parameters = new Dictionary<string, string[]>(StringComparer.Ordinal);
    }

    [Serializable]
    public sealed class LucaScheme : ResourceScheme
    {
        public Dictionary<string, LucaProfile> KnownSchemes = new Dictionary<string, LucaProfile>(StringComparer.Ordinal);
    }

    public sealed class LucaOptions : ResourceOptions
    {
        public string Scheme;
    }

    [Export(typeof(ScriptFormat))]
    public sealed class LucaScriptFormat : GenericScriptFormat
    {
        LucaScheme scheme = new LucaScheme();
        public override string Tag => "LUCA-SCRIPT";
        public override string Description => "Luca System script archive";
        public override uint Signature => 0;
        public override bool CanWrite => true;
        public LucaScriptFormat() { Extensions = new[] { "pak" }; }
        public override ResourceScheme Scheme { get => scheme; set => scheme = (LucaScheme)value; }
        public override ResourceOptions GetDefaultOptions() => new LucaOptions();
        public (string Name, string Json) SelectProfile(string requested = null)
        {
            string selected = requested;
            if (string.IsNullOrEmpty(selected)) selected = Query<LucaOptions>("Select a Luca System game scheme.").Scheme;
            if (string.IsNullOrEmpty(selected) || !scheme.KnownSchemes.TryGetValue(selected, out var profile))
                throw new ResourceParametersRequiredException("Unknown Luca System game scheme.");
            return (selected, JsonSerializer.Serialize(profile, new JsonSerializerOptions { IncludeFields = true }));
        }
        public override bool IsScript(IBinaryStream file) => Path.GetFileName(file.Name).Equals("SCRIPT.PAK", StringComparison.OrdinalIgnoreCase) &&
            LucaScript.IsArchive(file.AsStream);
        public override Stream ConvertFrom(IBinaryStream file)
        {
            if (file.Length > 64L << 20) throw new InvalidDataException("Resource exceeds the size limit.");
            var profile = SelectProfile();
            file.Position = 0;
            return new MemoryStream(Encoding.UTF8.GetBytes(LucaScript.Export(file.ReadBytes((int)file.Length), profile.Name, profile.Json, null, CancellationToken.None)), false);
        }
        public override Stream ConvertBack(IBinaryStream file)
        {
            if (file.Length > 64L << 20) throw new InvalidDataException("Resource exceeds the size limit.");
            file.Position = 0;
            string text = new UTF8Encoding(false, true).GetString(file.ReadBytes((int)file.Length));
            var profile = SelectProfile(LucaScript.SchemeName(text));
            return new MemoryStream(LucaScript.Compile(text, profile.Json, null, CancellationToken.None), false);
        }
    }
}
