using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using GARbro.Application;

namespace GARbro.Desktop;

public sealed class AppSettings
{
    public int Version { get; set; } = 3;
    public string SeedColor { get; set; } = "#6750A4";
    public bool DynamicColors { get; set; } = true;
    public string Language { get; set; } = "auto";
    public string Theme { get; set; } = "system";
    public string TextEncoding { get; set; } = "auto";
    public double TimeoutSeconds { get; set; } = 300;
    public long MemoryMiB { get; set; } = 1024;
    public string OutputDirectory { get; set; } = "";
    public bool MenuVisible { get; set; } = true;
    public bool ToolbarVisible { get; set; } = true;
    public bool StatusVisible { get; set; } = true;
    public bool FitImage { get; set; } = true;
    public bool WrapText { get; set; } = true;
    public string Sort { get; set; } = "name";
    public bool SortDescending { get; set; }
    public double Width { get; set; } = 1180;
    public double Height { get; set; } = 780;
    public double Volume { get; set; } = 1;
    public List<string> Recent { get; set; } = new();
    public Dictionary<string, JsonElement> FormatSettings { get; set; } = new();
    public Dictionary<string, string[]> ShortcutBindings { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<string, JsonElement> SessionFormatSettings { get; set; } = new();
    public string SchemesFile { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public WorkerLimits Limits => new(TimeoutSeconds, checked(MemoryMiB * (1L << 20)));
    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveLanguage => GARbro.Domain.LanguageCatalog.Resolve(Language, CultureInfo.CurrentUICulture.Name);
    [System.Text.Json.Serialization.JsonIgnore]
    public DecoderContext Context => new(TextEncoding, EffectiveLanguage, FormatSettings.Concat(SessionFormatSettings).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value), SchemesFile);
    public void Validate()
    {
        if (OutputDirectory == null || Recent == null || FormatSettings == null) throw new ArgumentException("Invalid settings collection.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(SeedColor ?? "", "^#[0-9a-fA-F]{6}$")) throw new ArgumentException("Choose a color as #RRGGBB.");
        Limits.Validate();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _ = Context.ResolveEncoding();
        if (Theme is not ("system" or "light" or "dark")) throw new ArgumentException("Unknown theme.");
        if (!GARbro.Domain.LanguageCatalog.IsSupported(Language)) throw new ArgumentException("Unknown language.");
        if (OutputDirectory.Length > 0 && !Path.IsPathFullyQualified(OutputDirectory)) throw new ArgumentException("Choose an absolute output directory.");
        ShortcutCatalog.Validate(this);
    }
}

public sealed class SettingsStore
{
    public string Path { get; }
    public string LoadError { get; private set; }
    public SettingsStore(string path = null) => Path = path ?? UiPlatform.SettingsPath ?? Environment.GetEnvironmentVariable("GARBRO_SETTINGS_PATH") ??
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GARbro", "settings.json");
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new();
            if (new FileInfo(Path).Length > 1 << 20) throw new InvalidDataException("Settings are too large.");
            var result = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new();
            if (result.Version is not (1 or 2 or 3)) throw new InvalidDataException("Unsupported settings version.");
            result.Version = 3; result.MenuVisible = true;
            ShortcutCatalog.MigrateTabBindings(result);
            result.Validate();
            return result;
        }
        catch (Exception e) when (e is IOException or JsonException or ArgumentException or OverflowException)
        { LoadError = e.Message; return new(); }
    }
    public void Save(AppSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)));
        string temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            if (LoadError != null && File.Exists(Path))
            {
                string backup = Path + ".invalid-" + Guid.NewGuid().ToString("N");
                File.Copy(Path, backup);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(backup, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            File.Move(temporary, Path, true);
            LoadError = null;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed partial class DesktopText : INotifyPropertyChanged
{
    ResourceText resources;
    public string Language => resources.Language;
    public event PropertyChangedEventHandler PropertyChanged;
    public DesktopText(string language) => resources = new(language);
    public string this[string key] => resources[key];
    // Avalonia's reflection indexer observes the CLR property name, Item.
    public void Change(string language) { resources = new(language); PropertyChanged?.Invoke(this, new("Item")); }
    public string Format(string key, params object[] args) => resources.Format(key, args);
    public string Limitation(string message) => resources.Contains("Limitation." + message) ? resources["Limitation." + message] : message;
    public string ParameterName(string name, string fallback = null) => resources.Contains("Parameter." + name) ? this["Parameter." + name] : fallback ?? name;
    public IEnumerable<string> Keys => resources.Keys;
}
