using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using GARbro.Application;
using GameRes;
using GameRes.Formats.ExHibit;
using Xunit;

[Collection("Decoder state")]
public sealed class GyuCompositeTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "garbro-gyu-" + Guid.NewGuid());
    public GyuCompositeTests() { Directory.CreateDirectory(root); ArchiveService.Formats(); }
    public void Dispose() => Directory.Delete(root, true);

    static byte[] Gyu(int width, int height, string reference = null, byte alpha = 0)
    {
        int stride = (width * 3 + 3) & ~3;
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, Encoding.ASCII, true);
        writer.Write(0x1A555947u); writer.Write((ushort)(reference == null ? 0 : 0x80));
        writer.Write((ushort)0x100); writer.Write(uint.MaxValue); writer.Write(24);
        writer.Write(width); writer.Write(height); writer.Write(stride * height);
        writer.Write(alpha == 0 ? 0 : 4 * height); writer.Write(0);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++) writer.Write(reference == null ? new byte[] { 0, 0, 255 } : new byte[] { 255, 0, 0 });
            writer.Write(new byte[stride - width * 3]);
        }
        if (alpha != 0) for (int y = 0; y < height; y++) writer.Write(new byte[] { alpha, 0, 0, 0 });
        if (reference != null)
        {
            var trailer = new byte[1008]; Encoding.ASCII.GetBytes(reference).CopyTo(trailer, 0); writer.Write(trailer);
        }
        return bytes.ToArray();
    }
    static ImageData Read(ResourceLocation location, string name)
    {
        using var session = new ResourceSession(location);
        using var input = VFS.OpenBinaryStream(name);
        var format = new GyuFormat(); var metadata = format.ReadMetaData(input); metadata.FileName = name;
        return format.Read(input, metadata);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComposesSiblingBaseFromDirectoryOrArchive(bool archived)
    {
        byte[] background = Gyu(2, 2), overlay = Gyu(1, 1, "1,1,1", 8);
        ResourceLocation location;
        if (archived)
        {
            string path = Path.Combine(root, "images.zip");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (var entry = zip.CreateEntry("sub/0001.gyu").Open()) entry.Write(background);
                using (var entry = zip.CreateEntry("sub/0002.gyu").Open()) entry.Write(overlay);
            }
            location = new(path);
        }
        else
        {
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllBytes(Path.Combine(root, "sub/0001.gyu"), background);
            File.WriteAllBytes(Path.Combine(root, "sub/0002.gyu"), overlay); location = new(root);
        }
        var image = Read(location, "sub/0002.gyu");
        Assert.Equal(2u, image.Width); Assert.Equal(2u, image.Height);
        Assert.Equal(0, image.OffsetX); Assert.Equal(0, image.OffsetY);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 255, 0, 0, 128 }, image.Bitmap.ToBgra32());
    }
    [Fact]
    public void MissingBaseKeepsTheStandaloneDifference()
    {
        File.WriteAllBytes(Path.Combine(root, "0002.gyu"), Gyu(1, 1, "1,1,1"));
        var image = Read(new(root), "0002.gyu");
        Assert.Equal(1u, image.Width); Assert.Equal(1, image.OffsetX);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, image.Bitmap.ToBgra32());
    }
    [Theory]
    [InlineData("1,-1,0")]
    [InlineData("1,2,0")]
    [InlineData("../1,0,0")]
    [InlineData("1,no,0")]
    public void RejectsInvalidReferencesOrOverlayBounds(string reference)
    {
        File.WriteAllBytes(Path.Combine(root, "0001.gyu"), Gyu(2, 2));
        File.WriteAllBytes(Path.Combine(root, "0002.gyu"), Gyu(1, 1, reference));
        Assert.Throws<InvalidFormatException>(() => Read(new(root), "0002.gyu"));
    }
    [Fact]
    public void CyclicBaseReferencesAreBounded()
    {
        File.WriteAllBytes(Path.Combine(root, "0001.gyu"), Gyu(1, 1, "2,0,0"));
        File.WriteAllBytes(Path.Combine(root, "0002.gyu"), Gyu(1, 1, "1,0,0"));
        Assert.Throws<InvalidFormatException>(() => Read(new(root), "0002.gyu"));
    }
}
