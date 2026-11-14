using System;
using System.Collections.Generic;
using System.Linq;

namespace GARbro.Imaging
{
    public enum PixelKind
    {
        Bgra32,
        Bgr32,
        Pbgra32,
        Bgr24,
        Rgb24,
        Bgr565,
        Bgr555,
        Gray16,
        Gray8,
        Gray4,
        BlackWhite,
        Indexed1,
        Indexed2,
        Indexed4,
        Indexed8
    }

    public readonly record struct PixelFormat(PixelKind Kind, int BitsPerPixel);
    public static class PixelFormats
    {
        public static PixelFormat Bgra32 => new(PixelKind.Bgra32, 32);
        public static PixelFormat Bgr32 => new(PixelKind.Bgr32, 32);
        public static PixelFormat Pbgra32 => new(PixelKind.Pbgra32, 32);
        public static PixelFormat Bgr24 => new(PixelKind.Bgr24, 24);
        public static PixelFormat Rgb24 => new(PixelKind.Rgb24, 24);
        public static PixelFormat Bgr565 => new(PixelKind.Bgr565, 16);
        public static PixelFormat Bgr555 => new(PixelKind.Bgr555, 16);
        public static PixelFormat Gray16 => new(PixelKind.Gray16, 16);
        public static PixelFormat Gray8 => new(PixelKind.Gray8, 8);
        public static PixelFormat Gray4 => new(PixelKind.Gray4, 4);
        public static PixelFormat BlackWhite => new(PixelKind.BlackWhite, 1);
        public static PixelFormat Indexed1 => new(PixelKind.Indexed1, 1);
        public static PixelFormat Indexed2 => new(PixelKind.Indexed2, 2);
        public static PixelFormat Indexed4 => new(PixelKind.Indexed4, 4);
        public static PixelFormat Indexed8 => new(PixelKind.Indexed8, 8);
    }

    public record struct Color(byte A, byte R, byte G, byte B)
    {
        public static Color FromRgb(byte r, byte g, byte b) => new(255, r, g, b);
        public static Color FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);

        // WPF performs color arithmetic in linear scRGB, then encodes the result as sRGB.
        public static Color Subtract(Color left, Color right)
        {
            static float Linear(byte value) => value <= 10 ? value / 255f / 12.92f
                : (float)Math.Pow((value / 255d + 0.055) / 1.055, 2.4);
            static byte Encode(float value)
            {
                double linear = Math.Clamp(value, 0, 1);
                double encoded = linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
                return (byte)Math.Clamp((int)(encoded * 255 + 0.5), 0, 255);
            }
            return new((byte)Math.Max(0, left.A - right.A),
                Encode(Linear(left.R) - Linear(right.R)), Encode(Linear(left.G) - Linear(right.G)),
                Encode(Linear(left.B) - Linear(right.B)));
        }
    }

    public sealed class BitmapPalette
    {
        public IList<Color> Colors { get; }

        public BitmapPalette(IEnumerable<Color> colors)
        {
            var array = colors.ToArray();
            if (array.Length < 1 || array.Length > 256)
                throw new ArgumentException("Palette requires 1–256 colors.");
            Colors = Array.AsReadOnly(array);
        }
    }

    public static class BitmapPalettes
    {
        public static BitmapPalette Gray16 { get; } = new(Enumerable.Range(0, 16).Select(x => Color.FromRgb((byte)(x * 17), (byte)(x * 17), (byte)(x * 17))));
    }

    public struct PixelRect
    {
        public int X, Y, Width, Height;
        public PixelRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public static PixelRect Empty => default;
        public bool HasArea => Width > 0 && Height > 0;
    }

    /// <summary>Owned, immutable pixel bytes. Packed indexed rows use most-significant bits first.</summary>
    public class PixelBuffer
    {
        private protected readonly byte[] pixels;
        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public int Stride { get; }
        public double DpiX { get; }
        public double DpiY { get; }
        public PixelFormat Format { get; }
        public BitmapPalette Palette { get; }
        public ReadOnlyMemory<byte> Pixels => pixels;

        public static int RowBytes(int width, PixelFormat format) => checked((int)((checked((long)width * format.BitsPerPixel) + 7) / 8));
        private protected PixelBuffer(int width, int height, double dpiX, double dpiY, PixelFormat format, BitmapPalette palette, Array data, int stride)
        {
            if (width <= 0 || height <= 0 || format.BitsPerPixel <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (stride < RowBytes(width, format))
                throw new ArgumentOutOfRangeException(nameof(stride));
            long length = checked((long)stride * height);
            if (length > int.MaxValue || Buffer.ByteLength(data) < length)
                throw new ArgumentException("Pixel buffer is too short or too large.");
            PixelWidth = width;
            PixelHeight = height;
            Stride = stride;
            DpiX = dpiX;
            DpiY = dpiY;
            Format = format;
            Palette = palette;
            pixels = new byte[(int)length];
            Buffer.BlockCopy(data, 0, pixels, 0, pixels.Length);
        }

        public static PixelBuffer Create(int width, int height, double dpiX, double dpiY, PixelFormat format, BitmapPalette palette, Array data, int stride) => new(width, height, dpiX, dpiY, format, palette, data, stride);
        // Retained for decoder source compatibility; buffers are immutable from construction.
        public virtual void Freeze()
        {
        }

        public PixelBuffer FlipVertical()
        {
            var flipped = new byte[pixels.Length];
            for (int y = 0; y < PixelHeight; y++)
                Buffer.BlockCopy(pixels, y * Stride, flipped, (PixelHeight - y - 1) * Stride, Stride);
            return Create(PixelWidth, PixelHeight, DpiX, DpiY, Format, Palette, flipped, Stride);
        }

        /// <summary>Replace a rectangular region, retaining source pixels without alpha blending.</summary>
        public PixelBuffer WithRegion(PixelBuffer source, PixelRect rect, int x, int y)
        {
            if (rect.Width == 0 && rect.Height == 0)
                rect = new(0, 0, source.PixelWidth, source.PixelHeight);
            if (x < 0 || y < 0 || x > PixelWidth - rect.Width || y > PixelHeight - rect.Height)
                throw new ArgumentOutOfRangeException(nameof(x));
            if (Format.BitsPerPixel < 8)
                throw new NotSupportedException("Region replacement requires byte-aligned pixels.");
            source = source.Convert(Format);
            var result = (byte[])pixels.Clone();
            source.CopyPixels(rect, result, Stride, checked(y * Stride + x * Format.BitsPerPixel / 8));
            return Create(PixelWidth, PixelHeight, DpiX, DpiY, Format, Palette, result, Stride);
        }

        public PixelBuffer Crop(PixelRect rect)
        {
            int stride = RowBytes(rect.Width, Format);
            var result = new byte[checked(stride * rect.Height)];
            CopyPixels(rect, result, stride, 0);
            return Create(rect.Width, rect.Height, DpiX, DpiY, Format, Palette, result, stride);
        }

        public void CopyPixels(PixelRect rect, IntPtr output, int bufferSize, int stride)
        {
            if (output == IntPtr.Zero)
                throw new ArgumentNullException(nameof(output));
            if (rect.Width == 0 && rect.Height == 0)
                rect = new(0, 0, PixelWidth, PixelHeight);
            if (rect.X < 0 || rect.Y < 0 || !rect.HasArea
                || rect.X > PixelWidth - rect.Width || rect.Y > PixelHeight - rect.Height)
                throw new ArgumentOutOfRangeException(nameof(rect));
            if ((rect.X * Format.BitsPerPixel) % 8 != 0)
                throw new NotSupportedException("Packed crop must start on a byte boundary.");
            int row = RowBytes(rect.Width, Format);
            if (stride < row || bufferSize < checked((rect.Height - 1) * stride + row))
                throw new ArgumentException("Output is too small.");
            for (int y = 0; y < rect.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(pixels,
                    checked((rect.Y + y) * Stride + rect.X * Format.BitsPerPixel / 8),
                    IntPtr.Add(output, checked(y * stride)), row);
        }

        public byte[] ToBgra32()
        {
            var output = new byte[checked(PixelWidth * PixelHeight * 4)];
            for (int y = 0; y < PixelHeight; y++)
                for (int x = 0; x < PixelWidth; x++)
                {
                    int p = y * Stride + x * Format.BitsPerPixel / 8, d = (y * PixelWidth + x) * 4;
                    byte r = 0, g = 0, b = 0, a = 255;
                    switch (Format.Kind)
                    {
                        case PixelKind.Bgra32:
                        case PixelKind.Bgr32:
                        case PixelKind.Pbgra32:
                            b = pixels[p];
                            g = pixels[p + 1];
                            r = pixels[p + 2];
                            if (Format.Kind != PixelKind.Bgr32)
                                a = pixels[p + 3];
                            if (Format.Kind == PixelKind.Pbgra32)
                            {
                                b = Unpremultiply(b, a);
                                g = Unpremultiply(g, a);
                                r = Unpremultiply(r, a);
                            }

                            break;
                        case PixelKind.Bgr24:
                            b = pixels[p];
                            g = pixels[p + 1];
                            r = pixels[p + 2];
                            break;
                        case PixelKind.Rgb24:
                            r = pixels[p];
                            g = pixels[p + 1];
                            b = pixels[p + 2];
                            break;
                        case PixelKind.Bgr565:
                        case PixelKind.Bgr555:
                            int v = pixels[p] | pixels[p + 1] << 8;
                            bool six = Format.Kind == PixelKind.Bgr565;
                            b = Expand(v & 31, 31);
                            g = Expand((v >> 5) & (six ? 63 : 31), six ? 63 : 31);
                            r = Expand((v >> (six ? 11 : 10)) & 31, 31);
                            break;
                        case PixelKind.Gray16:
                            b = g = r = Gray16Quantizer.ToByte(pixels[p] | pixels[p + 1] << 8);
                            break;
                        case PixelKind.Gray8:
                            b = g = r = pixels[p];
                            break;
                        default:
                            int bits = Format.BitsPerPixel;
                            int value = (pixels[p] >> (8 - bits - (x * bits % 8))) & ((1 << bits) - 1);
                            if (Format.Kind == PixelKind.Gray4 || Format.Kind == PixelKind.BlackWhite)
                                b = g = r = Expand(value, (1 << bits) - 1);
                            else
                            {
                                if (Palette == null || value >= Palette.Colors.Count)
                                    throw new ArgumentException("Missing palette color.");
                                var c = Palette.Colors[value];
                                b = c.B;
                                g = c.G;
                                r = c.R;
                                a = c.A;
                            }

                            break;
                        case PixelKind.Indexed8:
                            if (Palette == null || pixels[p] >= Palette.Colors.Count)
                                throw new ArgumentException("Missing palette color.");
                            var color = Palette.Colors[pixels[p]];
                            b = color.B;
                            g = color.G;
                            r = color.R;
                            a = color.A;
                            break;
                    }

                    output[d] = b;
                    output[d + 1] = g;
                    output[d + 2] = r;
                    output[d + 3] = a;
                }

            return output;
        }

        // WPF/WIC replicates packed color bits rather than rounding a normalized fraction.
        static byte Expand(int value, int maximum) => (byte)(maximum switch
        {
            31 => (value << 3) | (value >> 2),
            63 => (value << 2) | (value >> 4),
            _ => (value * 255 + maximum / 2) / maximum
        });
        static byte Unpremultiply(byte value, byte alpha) => alpha == 0 ? (byte)0 : (byte)Math.Min(255, value * 255 / alpha);
        public PixelBuffer Convert(PixelFormat target)
        {
            if (target == Format)
                return this;
            var bgra = ToBgra32();
            int stride = RowBytes(PixelWidth, target);
            var result = new byte[checked(stride * PixelHeight)];
            for (int y = 0; y < PixelHeight; y++)
                for (int x = 0; x < PixelWidth; x++)
                {
                    int s = (y * PixelWidth + x) * 4, d = y * stride + x * target.BitsPerPixel / 8;
                    byte b = bgra[s], g = bgra[s + 1], r = bgra[s + 2], a = bgra[s + 3];
                    switch (target.Kind)
                    {
                        case PixelKind.Bgra32:
                        case PixelKind.Bgr32:
                        case PixelKind.Pbgra32:
                            if (target.Kind == PixelKind.Pbgra32)
                            {
                                b = (byte)((b * a + 127) / 255);
                                g = (byte)((g * a + 127) / 255);
                                r = (byte)((r * a + 127) / 255);
                            }

                            result[d] = b;
                            result[d + 1] = g;
                            result[d + 2] = r;
                            result[d + 3] = target.Kind == PixelKind.Bgr32 ? (byte)255 : a;
                            break;
                        case PixelKind.Bgr24:
                            result[d] = b;
                            result[d + 1] = g;
                            result[d + 2] = r;
                            break;
                        case PixelKind.Rgb24:
                            result[d] = r;
                            result[d + 1] = g;
                            result[d + 2] = b;
                            break;
                        case PixelKind.Gray8:
                            result[d] = (byte)((r * 299 + g * 587 + b * 114 + 500) / 1000);
                            break;
                        default:
                            throw new NotSupportedException("Conversion to " + target.Kind + " requires an explicit quantizer.");
                    }
                }

            return Create(PixelWidth, PixelHeight, DpiX, DpiY, target, null, result, stride);
        }

        public void CopyPixels(Array output, int stride, int offset) => CopyPixels(PixelRect.Empty, output, stride, offset);
        public void CopyPixels(PixelRect rect, Array output, int stride, int offset)
        {
            if (rect.Width == 0 && rect.Height == 0)
                rect = new(0, 0, PixelWidth, PixelHeight);
            if (rect.X < 0 || rect.Y < 0 || rect.Width < 0 || rect.Height < 0 || rect.X > PixelWidth - rect.Width || rect.Y > PixelHeight - rect.Height)
                throw new ArgumentOutOfRangeException(nameof(rect));
            if ((rect.X * Format.BitsPerPixel) % 8 != 0)
                throw new NotSupportedException("Packed crop must start on a byte boundary.");
            int row = RowBytes(rect.Width, Format);
            if (stride < row || offset < 0 || Buffer.ByteLength(output) < checked(offset + (rect.Height - 1) * stride + row))
                throw new ArgumentException("Output is too small.");
            for (int y = 0; y < rect.Height; y++)
                Buffer.BlockCopy(pixels, (rect.Y + y) * Stride + rect.X * Format.BitsPerPixel / 8, output, offset + y * stride, row);
        }
    }
}
