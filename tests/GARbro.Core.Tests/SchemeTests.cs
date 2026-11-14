using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GARbro.Application;
using GameRes;
using GameRes.Formats.KiriKiri;
using GameRes.Formats.DxLib;
using Xunit;

[Collection("Decoder state")]
public sealed class SchemeTests
{
    [Fact]
    public void BundledHistoricalDatabaseLoadsAllMigratedSchemeGroups()
    {
        var report = ArchiveService.BundledSchemes;
        Assert.NotNull(report);
        Assert.Equal(148, report.Version);
        Assert.Empty(report.Failed);
        Assert.Contains("XP3", report.Applied);
        Assert.Equal(77, report.Applied.Length);
        Assert.Empty(report.Unported);
        Assert.Contains("DXA", report.Applied);
        Assert.Contains("DXA8", report.Applied);
        Assert.Contains("RCT", report.Applied);
        Assert.Contains("PCK/TAMAMO", report.Applied);
        Assert.True(Xp3Opener.KnownSchemes.Count >= 500);
    }

    [Fact]
    public void PolymorphicSchemesRoundTripWithoutBinaryFormatter()
    {
        ArchiveService.Formats();
        var catalog = FormatCatalog.Instance;
        Xp3Opener.KnownSchemes["synthetic-unit"] = new XorCrypt(0xA7);
        Xp3Opener.KnownSchemes["constructor-only"] = new CxEncryption(new CxScheme { Mask = 7, Offset = 11, ControlBlock = new uint[] { 1, 2, 3 } });
        try
        {
            using var stream = new MemoryStream();
            catalog.SerializePortableSchemeJson(stream);
            Xp3Opener.KnownSchemes.Remove("synthetic-unit");
            stream.Position = 0;
            var report = catalog.DeserializePortableSchemeJson(stream);
            Assert.True(report.Failed.Count == 0, string.Join("\n", report.Failed.Select(pair => pair.Key + ": " + pair.Value)));
            Assert.Contains("XP3", report.Applied);
            Assert.Equal(0xA7, Assert.IsType<XorCrypt>(Xp3Opener.KnownSchemes["synthetic-unit"]).Key);
            var cx = Assert.IsType<CxEncryption>(Xp3Opener.KnownSchemes["constructor-only"]);
            Assert.Equal(7u, typeof(CxEncryption).GetField("m_mask", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(cx));
            Assert.Equal(11u, typeof(CxEncryption).GetField("m_offset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(cx));
            var cache = typeof(CxEncryption).GetField("m_program_list", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(cx);
            Assert.Equal(128, Assert.IsAssignableFrom<System.Array>(cache).Length);
        }
        finally
        {
            Xp3Opener.KnownSchemes.Remove("synthetic-unit");
            Xp3Opener.KnownSchemes.Remove("constructor-only");
        }
    }

    [Fact]
    public void HashedDxKeysRetainPrivatePasswordAndCodePageFields()
    {
        ArchiveService.Formats();
        var catalog = FormatCatalog.Instance;
        var dxa = catalog.Formats.Single(f => f.Tag == "DXA");
        var dxa8 = catalog.Formats.Single(f => f.Tag == "DXA8");
        var original7 = dxa.Scheme;
        var original8 = dxa8.Scheme;
        var key7 = new DxKey7("synthetic password");
        var key8 = new DxKey8("synthetic password", 932);
        try
        {
            dxa.Scheme = new DxScheme { KnownKeys = new List<IDxKey> { key7 } };
            dxa8.Scheme = new Dx8Scheme { KnownKeys = new Dictionary<string, IDxKey> { ["synthetic"] = key8 } };
            using var stream = new MemoryStream();
            catalog.SerializePortableSchemeJson(stream);
            stream.Position = 0;
            var report = catalog.DeserializePortableSchemeJson(stream);
            Assert.Empty(report.Failed);
            var restored7 = ((DxScheme)dxa.Scheme).KnownKeys.Single();
            var restored8 = ((Dx8Scheme)dxa8.Scheme).KnownKeys["synthetic"];
            Assert.Equal(key7.GetEntryKey("sub/日本語.png"), restored7.GetEntryKey("sub/日本語.png"));
            Assert.Equal(key8.GetEntryKey("sub/日本語.png"), restored8.GetEntryKey("sub/日本語.png"));
        }
        finally
        {
            dxa.Scheme = original7;
            dxa8.Scheme = original8;
        }
    }

    [Fact]
    public void SchemeTypeAllowlistRejectsFrameworkObjects()
    {
        ArchiveService.Formats();
        const string data = "{\"Version\":1,\"SchemeMap\":{\"XP3\":{\"$type\":\"System.IO.FileInfo, mscorlib\",\"OriginalPath\":\"unsafe\"}}}";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(data));
        var result = FormatCatalog.Instance.DeserializePortableSchemeJson(stream);
        Assert.Contains("XP3", result.Failed.Keys);
        Assert.Empty(result.Applied);
    }
}
