using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GARbro.Application;
using GARbro.Worker;
using Xunit;

[Collection("Decoder state")]
public sealed class FormatSettingsTests
{
    static Task<T> Request<T>(WorkerRequest request) => WorkerArchiveSession.RequestAsync<T>(
        WorkerMarker.AssemblyPath, request, new WorkerLimits(), null, default);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyQuotedUnsignedSettingDoesNotPreventOpeningResources(bool archive)
    {
        var formats = await Request<ResourceFormatInfo[]>(new("resource-catalog", null));
        var format = formats.First(f => f.Settings.Any(s => s.Name == "ODNAudioSampleRate"));
        string root = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var context = new DecoderContext(FormatSettings: new Dictionary<string, JsonElement>
        { [format.Id + "/ODNAudioSampleRate"] = JsonSerializer.SerializeToElement("44100") });
        var listing = await Request<BrowserListing>(new("resource-list", null,
            Location: new(archive ? Path.Combine(root, "standard.xfl") : root), Context: context));
        Assert.NotEmpty(listing.Entries);
        Assert.Equal(archive ? "XFL" : "directory", listing.Tag);
    }

    [Fact]
    public async Task CatalogRetainsUnsignedNumericSettingType()
    {
        var formats = await Request<ResourceFormatInfo[]>(new("resource-catalog", null));
        var setting = formats.SelectMany(f => f.Settings).First(s => s.Name == "ODNAudioSampleRate");
        Assert.Equal("uint", setting.Kind);
        Assert.Equal(JsonValueKind.Number, setting.Value.ValueKind);
    }

    [Fact]
    public async Task InvalidSavedOverrideCannotPreventOpeningTheSettingsCatalog()
    {
        var formats = await Request<ResourceFormatInfo[]>(new("resource-catalog", null));
        var format = formats.First(f => f.Settings.Any(s => s.Name == "ODNAudioSampleRate"));
        var context = new DecoderContext(FormatSettings: new Dictionary<string, JsonElement>
        { [format.Id + "/ODNAudioSampleRate"] = JsonSerializer.SerializeToElement("invalid") });
        var reopened = await Request<ResourceFormatInfo[]>(new("resource-catalog", null, Context: context));
        Assert.NotEmpty(reopened);
    }
}
