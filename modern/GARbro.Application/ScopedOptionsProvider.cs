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
    public void SetLocation(ResourceLocation location) => locationKey = JsonSerializer.Serialize(new[] { string.IsNullOrEmpty(location.RootPath) ? "" : Path.GetFullPath(location.RootPath) }.Concat(location.Chain));
    public ScopedOptionsProvider(WorkerRequest request)
    {
        context = request.Context ?? new(); formatId = request.OptionsFormatId;
        fallback = new JsonOptionsProvider(request.OptionsJson, request.Scheme);
        var location = request.Location ?? new ResourceLocation(request.Path ?? "");
        if (request.Entry != null && Directory.Exists(location.RootPath)) location = new ResourceLocation(Path.GetFullPath(Path.Combine(location.RootPath, request.Entry)));
        SetLocation(location); initialLocationKey = locationKey;
    }
    public ResourceOptions GetOptions(IResource format, string notice)
    {
        string id = FormatIdentity.Of(format);
        string key = locationKey + "::" + id;
        try
        {
            if (context.OptionsByFormat?.TryGetValue(key, out string local) == true)
                return new JsonOptionsProvider(local).GetOptions(format, notice);
            if (context.OptionsByFormat?.TryGetValue(id, out string json) == true)
                return new JsonOptionsProvider(json).GetOptions(format, notice);
            if (locationKey == initialLocationKey && (formatId == null || formatId == id)) return fallback.GetOptions(format, notice);
            return new JsonOptionsProvider().GetOptions(format, notice);
        }
        catch (JsonException error)
        {
            throw new MissingOptionsException(format.Tag + ": " + error.Message)
            { FormatId = id, ContextKey = key, OptionsJson = JsonConvert.SerializeObject(format.GetDefaultOptions(), Formatting.Indented) };
        }
        catch (MissingOptionsException error) { error.ContextKey = key; throw; }
    }
}
