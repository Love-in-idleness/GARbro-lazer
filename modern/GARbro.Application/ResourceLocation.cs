using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameRes;

namespace GARbro.Application;

public sealed record ResourceLocation(string RootPath, string[] Archives = null, string Directory = "", ArchiveFingerprint Fingerprint = null)
{
    public string[] Chain => Archives ?? Array.Empty<string>();
    public string DisplayPath => RootPath + string.Concat(Chain.Select(p => " → " + p)) + (string.IsNullOrEmpty(Directory) ? "" : " → " + Directory);
    public ResourceLocation EnterArchive(string name) => this with { Archives = Chain.Append(name).ToArray(), Directory = "" };
    public ResourceLocation EnterDirectory(string name) => this with { Directory = name.TrimEnd('/', '\\') + "/" };
}

public sealed record DecoderContext(string TextEncoding = "auto", string Language = "en-US",
    Dictionary<string, JsonElement> FormatSettings = null, string SchemesFile = null,
    Dictionary<string, string> OptionsByFormat = null)
{
    static readonly JsonSerializerOptions compatibleSettings = new()
    { NumberHandling = JsonNumberHandling.AllowReadingFromString };
    public Encoding ResolveEncoding() => string.IsNullOrWhiteSpace(TextEncoding) || TextEncoding == "auto" ? null :
        int.TryParse(TextEncoding, out int cp) ? Encoding.GetEncoding(cp) : Encoding.GetEncoding(TextEncoding.ToLowerInvariant() switch { "gbk" => "gbk", "shift-jis" or "shift_jis" => "shift_jis", _ => TextEncoding });

    public void Apply()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encodings.ResourceEncodingOverride = ResolveEncoding();
        Encodings.ResourceEncodingSettings.Clear();
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(Language);
        ArchiveService.Formats();
        if (!string.IsNullOrEmpty(SchemesFile))
        {
            try
            {
                var imported = ArchiveService.LoadSchemes(SchemesFile);
                if (imported.Failed.Count != 0) throw new InvalidDataException("The downloaded database is incompatible.");
            }
            catch (Exception error) { throw new InvalidSchemeDatabaseException(error.Message); }
        }
        if (FormatSettings == null) return;
        foreach (var format in FormatCatalog.Instance.Formats)
            foreach (var setting in format.Settings ?? Array.Empty<IResourceSetting>())
                if (FormatSettings.TryGetValue(FormatIdentity.Of(format) + "/" + setting.Name, out var value))
                {
                    try
                    {
                        var current = setting.Value;
                        if (current is Encoding)
                        {
                            var selected = Encoding.GetEncoding(value.Deserialize<int>(compatibleSettings));
                            Encodings.ResourceEncodingSettings[setting.Name] = selected;
                            setting.Value = selected;
                        }
                        else setting.Value = current == null ? value.GetString() : value.Deserialize(current.GetType(), compatibleSettings);
                    }
                    catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or OverflowException)
                    { throw new InvalidDataException("Invalid format setting: " + setting.Name, error); }
                }
    }
}

public static class FormatIdentity
{
    public static string Of(IResource format) => format.GetType().Assembly.GetName().Name + ":" + format.GetType().FullName;
}

public sealed record BrowserEntry(string Name, string DisplayName, string Type, long Size, bool IsDirectory, int Index = -1);
public sealed record BrowserListing(ResourceLocation Location, string Tag, bool Physical, BrowserEntry[] Entries, string ProviderId = null);
public sealed record FormatSettingInfo(string Name, string Label, string Description, string Kind, JsonElement Value, int[] Choices = null);
public sealed record ResourceFormatInfo(string Id, string Tag, string Type, string Description, string[] Extensions, bool CanWrite,
    FormatSettingInfo[] Settings, string OptionsJson, string[] Schemes = null);
public sealed record AudioArtifact(string Path, long Bytes, int SampleRate, int Channels, double Duration);
public sealed record ResourcePreview(string Kind, string Title, string Text, byte[] Image = null, AudioArtifact Audio = null, long Offset = 0, bool More = false, ImageArtifact Pixels = null);
public sealed record VideoArtifact(string Path, long Bytes, GARbro.Media.VideoInfo Info);
public sealed record ResourceExtractionOptions(bool Recursive = false, int MaxDepth = 8, string[] Types = null,
    string ImageFormat = null, bool ConvertAudio = false, bool AdjustImageOffset = false, Dictionary<string, string> TypeOverrides = null,
    bool DiscardChildArchives = false, bool DiscardOriginalMedia = false, string AudioFormat = null, string VideoFormat = null);
public sealed record ResourceJobReport(ExtractedFile[] Files, string[] Errors, long Bytes, int Entries = 0);
public sealed record ResourceConversion(string Path, long Bytes, bool Written = true);
public sealed class InvalidSchemeDatabaseException(string message) : IOException(message);

public sealed record ImageArtifact(string Path, long Bytes, int Width, int Height);
