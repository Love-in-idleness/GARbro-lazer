using System;
using System.Runtime.InteropServices;
using GARbro.Imaging;
using Xunit;

public sealed class CompositorTests
{
    [Fact]
    public void CanvasWritesRespectSourceCroppingDestinationOffsetsAndFreezing()
    {
        var canvas = new MutablePixelBuffer(3, 2, 96, 96, PixelFormats.Gray8, null);
        byte[] rows = { 1, 2, 3, 0, 4, 5, 6, 0 };
        canvas.WritePixels(new PixelRect(1, 0, 1, 2), rows, 4, 2, 0);
        canvas.WritePixels(new PixelRect(0, 1, 2, 1), new byte[] { 7, 8 }, 2, 0);
        Assert.Equal(new byte[] { 0, 0, 2, 7, 8, 5 }, canvas.Pixels.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.WritePixels(new PixelRect(3, 0, 1, 1), rows, 4, 0));
        Assert.Throws<ArgumentException>(() => canvas.WritePixels(new PixelRect(1, 0, 2, 1), rows, 2, 0, 0));
        Assert.Throws<InvalidOperationException>(() => { _ = canvas.BackBuffer; });
        canvas.Lock();
        Marshal.WriteByte(canvas.BackBuffer, 0, 9);
        Assert.Throws<InvalidOperationException>(() => canvas.Freeze());
        canvas.Unlock();
        canvas.Freeze();
        Assert.Equal(9, canvas.Pixels.Span[0]);
        Assert.Throws<InvalidOperationException>(() => canvas.Lock());
        Assert.Throws<InvalidOperationException>(() => canvas.WritePixels(new PixelRect(0, 0, 1, 1), rows, 4, 0));
    }

    [Fact]
    public void PointerCopiesCheckTheLastRowAndRetainGuardBytes()
    {
        var source = PixelBuffer.Create(2, 2, 96, 96, PixelFormats.Gray8, null, new byte[] { 1, 2, 3, 4 }, 2);
        var output = Marshal.AllocHGlobal(7);
        try
        {
            Marshal.Copy(new byte[] { 99, 99, 99, 99, 99, 99, 99 }, 0, output, 7);
            Assert.Throws<ArgumentException>(() => source.CopyPixels(new PixelRect(0, 0, 1, 2), output + 1, 4, 4));
            source.CopyPixels(new PixelRect(0, 0, 1, 2), output + 1, 5, 4);
            var actual = new byte[7];
            Marshal.Copy(output, actual, 0, 7);
            Assert.Equal(new byte[] { 99, 1, 99, 99, 99, 3, 99 }, actual);
        }
        finally { Marshal.FreeHGlobal(output); }
    }

    [Fact]
    public void SourceOverUsesPremultipliedAlphaAndClipsToTheCanvas()
    {
        var background = PixelBuffer.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 80, 60, 40, 128 }, 4);
        var foreground = PixelBuffer.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 50, 100, 200, 128 }, 4);
        var result = PixelCompositor.Over(background, foreground, 0, 0);
        Assert.Equal(PixelFormats.Pbgra32, result.Format);
        Assert.Equal(new byte[] { 45, 65, 110, 192 }, result.Pixels.ToArray());
        Assert.Equal(new byte[] { 59, 86, 146, 192 }, result.ToBgra32());
        var wide = PixelBuffer.Create(2, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 0, 0, 255, 255, 0, 255, 0, 255 }, 8);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, PixelCompositor.Over(background, wide, -1, 0).ToBgra32());
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, wide.Crop(new PixelRect(1, 0, 1, 1)).ToBgra32());
    }
}
