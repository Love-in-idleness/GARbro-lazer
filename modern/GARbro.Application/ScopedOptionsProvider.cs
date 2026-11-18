using System;
using System.Linq;
using System.IO;
using JsonSerializer = System.Text.Json.JsonSerializer;
using GameRes;
using Newtonsoft.Json;

namespace GARbro.Application;

/// <summary>Keys belong to the selected provider, not to every nested container.</summary>
public interface ILocatedOptionsProvider : IOptionsProvider { void SetLocation(ResourceLocation location); }

public sealed class ScopedOptionsProvider : ILocatedOptionsProvider
{
    readonly DecoderContext context;
    readonly JsonOptionsProvider fallback;
    readonly string formatId;
    string locationKey;
    readonly string initialLocationKey;
    static string LocationKey(ResourceLocation location) => JsonSerializer.Serialize(new[] { string.IsNullOrEmpty(location.RootPath) ? "" : Path.GetFullPath(location.RootPath) }.Concat(location.Chain));
    public static string FormatKey(ResourceLocation location, string formatId) => LocationKey(location) + "::" + formatId;
    public void SetLocation(ResourceLocation location) => locationKey = LocationKey(location);
    public ScopedOptionsProvider(WorkerRequest request)
    {
        context = request.Context ?? new(); formatId = request.OptionsFormatId;
        fallback = new JsonOptionsProvider(request.OptionsJson, request.Scheme);
        var location = request.Location ?? new ResourceLocation(request.Path ?? "");
        if (request.Entry != null && Directory.Exists(location.RootPath)) location = new ResourceLocation(Path.GetFullPath(Path.Combine(location.RootPath, request.Entry)));
        SetLocation(location); initialLocationKey = locationKey;
    }
    public ResourceOptions GetOptions(IResource format, string notice) => Resolve(format, notice, false);
    public ResourceOptions GetExplicitOptions(IResource format) => Resolve(format, null, true);
    ResourceOptions Resolve(IResource format, string notice, bool explicitOnly)
    {
        string id = FormatIdentity.Of(format);
        string key = locationKey + "::" + id;
        try
        {
            JsonOptionsProvider provider;
            if (context.OptionsByFormat?.TryGetValue(key, out string json) == true || context.OptionsByFormat?.TryGetValue(id, out json) == true)
                provider = new JsonOptionsProvider(json);
            else
                provider = locationKey == initialLocationKey && (formatId == null || formatId == id) ? fallback : new JsonOptionsProvider();
            return explicitOnly ? provider.GetExplicitOptions(format) : provider.GetOptions(format, notice);
        }
        catch (JsonException error)
        {
            throw new MissingOptionsException(format.Tag + ": " + error.Message)
            { FormatId = id, ContextKey = key, OptionsJson = JsonConvert.SerializeObject(format.GetDefaultOptions(), Formatting.Indented) };
        }
        catch (MissingOptionsException error) { error.ContextKey = key; throw; }
    }
}
