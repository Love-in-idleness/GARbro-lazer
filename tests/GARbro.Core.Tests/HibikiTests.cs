using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GARbro.Application;
using GameRes;
using GameRes.Formats.YaneSDK;
using Xunit;

[Collection("Decoder state")]
public sealed class HibikiTests
{
    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void PlainJsonSchemeDecryptsBoundedHibikiEntriesAndLeavesTheTailClear()
    {
        ArchiveService.Formats();
        using var json = File.OpenRead(Fixture("hibiki-scheme.json"));
        var format = new HDatOpener { Scheme = HibikiScheme.ReadJson(json) };
        using var view = new ArcView(Fixture("hibiki-init.dat"));
        using var archive = format.TryOpen(view);
        Assert.NotNull(archive);
        Assert.Equal(new[] { "hello.txt", "sub/日本語.bin", "empty.txt" }, archive.Dir.Select(e => e.Name));
        var expected = new[] { Encoding.UTF8.GetBytes("Hello, GARbro!\n"), Enumerable.Range(0,32).Select(i => (byte)i).ToArray(), Array.Empty<byte>() };
        int index = 0;
        foreach (var entry in archive.Dir)
        {
            using var input = archive.OpenEntry(entry);
            using var output = new MemoryStream();
            input.CopyTo(output);
            Assert.Equal(expected[index++], output.ToArray());
        }
    }

    [Theory]
    [InlineData("{\"$type\":\"System.IO.FileInfo\",\"KnownSchemes\":{}}")]
    [InlineData("{\"KnownSchemes\":{\"bad\":{\"ContentKey\":null}}}")]
    [InlineData("{\"KnownSchemes\":null}")]
    public void InvalidOrTypedHibikiJsonIsRejected(string data)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(data));
        Assert.Throws<InvalidDataException>(() => HibikiScheme.ReadJson(input));
    }

    [Fact]
    public void HibikiIndexEntriesCannotPointBeyondTheArchive()
    {
        ArchiveService.Formats();
        byte[] bytes = File.ReadAllBytes(Fixture("hibiki-init.dat"));
        // First entry's obfuscated offset points one byte beyond the archive.
        byte[] offset = BitConverter.GetBytes((uint)bytes.Length + 1);
        for (int i = 0; i < 4; i++) bytes[264+i] = (byte)(offset[i] ^ 0x80);
        using var data = new MemoryStream(bytes);
        using var view = new ArcView(data, "hibiki-bad.dat", (uint)bytes.Length);
        var format = new HDatOpener { Scheme = new HibikiScheme { KnownSchemes = new Dictionary<string,HibikiDatScheme> {
            ["Synthetic"] = new() { ContentKey = Enumerable.Range(0,16).Select(i => (byte)i).ToArray() }
        } } };
        Assert.Null(format.TryOpen(view));
    }
}
