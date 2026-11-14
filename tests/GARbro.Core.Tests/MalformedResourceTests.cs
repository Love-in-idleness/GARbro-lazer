using System;
using System.IO;
using GARbro.Application;
using GameRes;
using Xunit;

[Collection("Decoder state")]
public sealed class MalformedResourceTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "garbro-boundary-" + Guid.NewGuid());
    public MalformedResourceTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    [Fact]
    public void SerializedArchiveIndexesRejectUnknownTypesAndExcessiveArrayCounts()
    {
        foreach (string name in new[] { "gx4-wrong-type.nrbf", "gx4-large-array.nrbf" })
        {
            using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
            Assert.Throws<InvalidDataException>(() => new GameRes.Gx4Lib.PackageFile().Deserialize(input));
        }
    }

    [Fact]
    public void HugeImageMetadataIsRejectedBeforeAllocatingPixels()
    {
        string path = Path.Combine(root, "huge.lim");
        var data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "raw565.lim"));
        BitConverter.GetBytes(uint.MaxValue).CopyTo(data, 8);
        BitConverter.GetBytes(uint.MaxValue).CopyTo(data, 12);
        File.WriteAllBytes(path, data);
        Assert.Throws<InvalidDataException>(() => ArchiveService.ReadImage(path));
    }

    [Fact]
    public void TopDownBmpHasPositiveMetadataAndCorrectOrientation()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "top-down.bmp");
        var image = ArchiveService.ReadImage(path);
        Assert.Equal(2u, image.Height);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255 }, image.Bitmap.ToBgra32());
    }

    [Fact]
    public void NpaEntryCountsAreBoundedBeforeListAllocation()
    {
        var bytes = new byte[41];
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 17);
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 25);
        string path = Path.Combine(root, "count.npa");
        File.WriteAllBytes(path, bytes);
        using var view = new ArcView(path);
        Assert.Null(new GameRes.Formats.NitroPlus.NpaOpener().TryOpen(view));
    }
}
