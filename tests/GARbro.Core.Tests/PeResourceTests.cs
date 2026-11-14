using System;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Formats;
using Xunit;

public sealed class PeResourceTests
{
    [Theory]
    [InlineData("resources32.exe")]
    [InlineData("resources64.exe")]
    public void ManagedPeResourcesExposeIdsUnicodeNamesEmptyDataAndFixedVersions(string fixture)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        using var resources = new ExeFile.ResourceAccessor(path);
        Assert.Equal(new[] { "CUSTOM", "#10", "#16" }, resources.EnumTypes());
        Assert.Equal(new[] { "日本語" }, resources.EnumNames("CUSTOM"));
        Assert.Equal("Unicode resource\n", Encoding.UTF8.GetString(resources.GetResource("日本語", "CUSTOM")));
        Assert.Equal("Hello, PE resources!\n", Encoding.UTF8.GetString(resources.GetResource("#1", "#10")));
        Assert.Empty(resources.GetResource("#2", "#10"));
        Assert.Null(resources.GetResource("#99", "#10"));
        var buffer = Enumerable.Repeat((byte)99, 8).ToArray();
        Assert.Equal(6, resources.ReadResource("#1", "#10", buffer, 2));
        Assert.Equal(new byte[] { 99, 99, 72, 101, 108, 108, 111, 44 }, buffer);
        var versions = new ExeFile.VersionAccessor(path);
        Assert.Equal(new ushort[] { 1, 2, 3, 4 }, versions.FileVersions);
        Assert.Equal(new ushort[] { 5, 6, 7, 8 }, versions.ProductVersions);
        resources.Dispose();
        Assert.Throws<ObjectDisposedException>(() => resources.EnumTypes());
    }

    [Fact]
    public void TruncatedPeResourceTablesAreRejected()
    {
        string path = Path.Combine(Path.GetTempPath(), "garbro-pe-" + Guid.NewGuid() + ".exe");
        var original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "resources32.exe"));
        File.WriteAllBytes(path, original[..0x210]);
        try { Assert.Throws<BadImageFormatException>(() => new ExeFile.ResourceAccessor(path)); }
        finally { File.Delete(path); }
    }
}
