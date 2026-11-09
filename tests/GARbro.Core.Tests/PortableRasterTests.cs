using System;
using System.IO;
using GARbro.Imaging;
using Xunit;

public sealed class PortableRasterTests
{
    readonly PortableRasterCodec codec = new();
    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    [Theory]
    [InlineData("PNG")]
    [InlineData("BMP")]
    [InlineData("TIFF")]
    [InlineData("WEBP")]
    public void MobileLosslessEncodersPreserveStraightAlpha(string format)
    {
        byte[] bytes = { 10,20,30,255,40,50,60,80,70,80,90,255,100,110,120,160 };
        var input = PixelBuffer.Create(2,2,96,96,PixelFormats.Bgra32,null,bytes,8);
        using var stream = new MemoryStream(); codec.Encode(input,stream,format); stream.Position=0;
        Assert.Equal((2,2),codec.Identify(stream)); stream.Position=0;
        Assert.Equal(bytes,codec.Decode(stream).ToBgra32());
    }
    [Theory]
    [InlineData("rgb")]
    [InlineData("rgba")]
    [InlineData("indexed")]
    public void MobileEmbeddedPngMatchesDesktopPaletteAndAlphaSemantics(string kind)
    {
        using var file=File.OpenRead(Fixture("colors-"+kind+".png"));
        var expected=new MagickRasterCodec().Decode(file,true);file.Position=0;
        var actual=codec.Decode(file,true);
        Assert.Equal(expected.Format,actual.Format);Assert.Equal(expected.ToBgra32(),actual.ToBgra32());
        if(expected.Palette!=null) Assert.Equal(expected.Palette.Colors,actual.Palette.Colors);
    }
    [Fact]
    public void MobileJpegRetainsDimensionsAndCloseColors()
    {
        byte[] bytes={25,100,200,255,25,100,200,255};
        var input=PixelBuffer.Create(2,1,96,96,PixelFormats.Bgra32,null,bytes,8);
        using var stream=new MemoryStream();codec.Encode(input,stream,"JPEG",100);stream.Position=0;
        var actual=codec.Decode(stream);Assert.Equal(2,actual.PixelWidth);Assert.Equal(1,actual.PixelHeight);
        var pixels=actual.ToBgra32();for(int i=0;i<pixels.Length;i++)Assert.InRange(Math.Abs(pixels[i]-bytes[i]),0,3);
    }
}
