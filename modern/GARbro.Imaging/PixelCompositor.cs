using System;

namespace GARbro.Imaging
{
    public static class PixelCompositor
    {
        /// <summary>Integer-aligned source-over composition with clipping in premultiplied color space.</summary>
        public static PixelBuffer Over(PixelBuffer background, PixelBuffer foreground, int x, int y)
        {
            var target = new byte[checked(background.PixelWidth * background.PixelHeight * 4)];
            background.Convert(PixelFormats.Pbgra32).CopyPixels(target, background.PixelWidth * 4, 0);
            var sourceBuffer = foreground.Convert(PixelFormats.Pbgra32);
            var source = sourceBuffer.Pixels.Span;
            int left = Math.Max(0, x), top = Math.Max(0, y);
            int right = (int)Math.Min(background.PixelWidth, (long)x + foreground.PixelWidth);
            int bottom = (int)Math.Min(background.PixelHeight, (long)y + foreground.PixelHeight);
            for (int row = top; row < bottom; row++)
                for (int col = left; col < right; col++)
                {
                    int s = checked((row - y) * sourceBuffer.Stride + (col - x) * 4);
                    int d = checked((row * background.PixelWidth + col) * 4);
                    int inverse = 255 - source[s + 3];
                    for (int channel = 0; channel < 4; channel++)
                        target[d + channel] = (byte)Math.Min(255, source[s + channel]
                            + (target[d + channel] * inverse + 127) / 255);
                }
            return PixelBuffer.Create(background.PixelWidth, background.PixelHeight, background.DpiX,
                background.DpiY, PixelFormats.Pbgra32, null, target, checked(background.PixelWidth * 4));
        }
    }
}
