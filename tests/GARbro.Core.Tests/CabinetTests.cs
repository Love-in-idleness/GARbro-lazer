using System;
using System.IO;
using System.Linq;
using GARbro.Application;
using GameRes;
using Xunit;

[Collection("Decoder state")]
public sealed class CabinetTests
{
    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Theory]
    [InlineData("cab-stored.cab")]
    [InlineData("cab-mszip.cab")]
    public void CabinetRejectsCorruptedBlockChecksums(string fixture)
    {
        ArchiveService.Formats();
        byte[] bytes = File.ReadAllBytes(Fixture(fixture));
        int data = BitConverter.ToInt32(bytes, 45); // folder start, after header reserves
        bytes[data + 11] ^= 1; // first payload byte, checksum remains unchanged
        using var memory = new MemoryStream(bytes);
        using var view = new ArcView(memory, fixture, (uint)bytes.Length);
        var format = new GameRes.Formats.Microsoft.CabOpener();
        using var archive = format.TryOpen(view);
        Assert.NotNull(archive);
        using var input = archive.OpenEntry(archive.Dir.First());
        Assert.Throws<InvalidDataException>(() => input.CopyTo(Stream.Null));
    }

    [Fact]
    public void UnsupportedCabinetCompressionIsReportedBeforeDecoding()
    {
        ArchiveService.Formats();
        byte[] bytes = File.ReadAllBytes(Fixture("cab-mszip.cab"));
        bytes[51] = 3; // CFFOLDER compression: LZX
        using var memory = new MemoryStream(bytes);
        using var view = new ArcView(memory, "unsupported.cab", (uint)bytes.Length);
        var format = new GameRes.Formats.Microsoft.CabOpener();
        using var archive = format.TryOpen(view);
        Assert.NotNull(archive);
        Assert.Throws<NotSupportedException>(() => archive.OpenEntry(archive.Dir.First()));
    }
}
