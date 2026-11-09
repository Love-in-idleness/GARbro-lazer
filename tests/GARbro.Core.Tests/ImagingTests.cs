using System;
using System.IO;
using System.Linq;
using GARbro.Imaging;
using GameRes;
using Xunit;

public sealed class ImagingTests
{
    [Fact]
    public void ColorSubtractionUsesLinearChannels()
    {
        Assert.Equal(Color.FromArgb(0, 229, 229, 229),
            Color.Subtract(Color.FromRgb(255, 255, 255), Color.FromRgb(128, 128, 128)));
        Assert.Equal(Color.FromArgb(127, 255, 249, 0),
            Color.Subtract(Color.FromRgb(255, 255, 255), Color.FromArgb(128, 0, 64, 255)));
    }

    [Fact]
    public void RegionReplacementChecksBoundsAndRetainsUnchangedPixels()
    {
        var canvas = PixelBuffer.Create(3, 2, 96, 96, PixelFormats.Gray8, null,
            new byte[] { 1, 2, 3, 4, 5, 6 }, 3);
        var source = PixelBuffer.Create(2, 1, 96, 96, PixelFormats.Gray8, null, new byte[] { 7, 8 }, 2);
        var result = canvas.WithRegion(source, PixelRect.Empty, 1, 1);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 7, 8 }, result.Pixels.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, canvas.Pixels.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.WithRegion(source, PixelRect.Empty, 2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.WithRegion(source, new PixelRect(1, 0, 2, 1), 0, 0));
    }

    [Fact]
    public void OddWidthPackedPaletteAndPaddingArePreserved()
    {
        var colors = new BitmapPalette(new[] { Color.FromRgb(0, 0, 0), Color.FromArgb(80, 255, 0, 0), Color.FromRgb(0, 255, 0), Color.FromRgb(0, 0, 255) });
        var buffer = PixelBuffer.Create(3, 1, 96, 96, PixelFormats.Indexed4, colors, new byte[] { 0x12, 0x30, 0xAA, 0xAA }, 4);
        Assert.Equal(new byte[] { 0, 0, 255, 80, 0, 255, 0, 255, 255, 0, 0, 255 }, buffer.ToBgra32());
        var image = ImageData.Create(new ImageMetaData { Width = 3, Height = 1 }, PixelFormats.Indexed4, colors, new byte[] { 0x12, 0x30 });
        Assert.Equal(2, image.Bitmap.Stride);
    }

    [Fact]
    public void PackedBgrAndPremultipliedAlphaDecodeCorrectly()
    {
        var buffer = PixelBuffer.Create(3, 1, 96, 96, PixelFormats.Bgr565, null, new ushort[] { 0xF800, 0x07E0, 0x001F }, 6);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255 }, buffer.ToBgra32());
        var premultiplied = PixelBuffer.Create(1, 1, 96, 96, PixelFormats.Pbgra32, null, new byte[] { 32, 64, 128, 128 }, 4);
        Assert.Equal(new byte[] { 63, 127, 255, 128 }, premultiplied.ToBgra32());
    }

    [Fact]
    public void VerticalFlipAndBufferOwnershipAreIndependent()
    {
        var source = new byte[]
        {
            1,
            2,
            3,
            4
        };
        var buffer = PixelBuffer.Create(2, 2, 96, 96, PixelFormats.Gray8, null, source, 2);
        source[0] = 99;
        Assert.Equal(new byte[] { 3, 4, 1, 2 }, buffer.FlipVertical().Pixels.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer.Pixels.ToArray());
    }

    [Theory]
    [InlineData("PNG")]
    [InlineData("BMP")]
    [InlineData("TIFF")]
    [InlineData("WEBP")]
    public void LosslessCodecsRetainNormalizedPixels(string format)
    {
        var bytes = new byte[]
        {
            10,
            20,
            30,
            255,
            40,
            50,
            60,
            80,
            70,
            80,
            90,
            255,
            100,
            110,
            120,
            160
        };
        var buffer = PixelBuffer.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, bytes, 8);
        using var stream = new MemoryStream();
        RasterCodec.Encode(buffer, stream, format);
        stream.Position = 0;
        var actual = RasterCodec.Decode(stream);
        Assert.Equal(2, actual.PixelWidth);
        Assert.Equal(2, actual.PixelHeight);
        Assert.Equal(bytes, actual.ToBgra32());
    }

    [Fact]
    public void PngOffsetsRoundTripThroughGameRes()
    {
        var image = ImageData.Create(new ImageMetaData { Width = 1, Height = 1, OffsetX = -17, OffsetY = 39 }, PixelFormats.Bgra32, null, new byte[] { 1, 2, 3, 255 });
        using var stream = new MemoryStream();
        var png = new PngFormat();
        png.Write(stream, image);
        stream.Position = 0;
        using var input = new BinaryStream(stream, "offset.png");
        var meta = png.ReadMetaData(input);
        Assert.Equal(-17, meta.OffsetX);
        Assert.Equal(39, meta.OffsetY);
        input.Position = 0;
        var decoded = png.Read(input, meta);
        Assert.Equal(image.Bitmap.ToBgra32(), decoded.Bitmap.ToBgra32());
    }

    [Fact]
    public void CoreAndImagingAssembliesHaveNoUiReferences()
    {
        foreach (var assembly in new[]
        {
            typeof(ImageData).Assembly,
            typeof(PixelBuffer).Assembly
        }

        )
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), x => x.Name is "PresentationCore" or "PresentationFramework" or "WindowsBase" or "System.Windows.Forms" || x.Name.StartsWith("Avalonia"));
    }

    [Fact]
    public void InvalidPixelDimensionsAndStrideFailBeforeCopy()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PixelBuffer.Create(0, 1, 96, 96, PixelFormats.Bgr24, null, new byte[3], 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => PixelBuffer.Create(3, 1, 96, 96, PixelFormats.Bgr24, null, new byte[3], 3));
        Assert.Throws<ArgumentException>(() => PixelBuffer.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[8], 8));
    }
}
