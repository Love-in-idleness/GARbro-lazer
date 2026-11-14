using System;
using System.IO;
using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
using Hjg.Pngcs;
using Hjg.Pngcs.Chunks;
using SkiaSharp;

namespace GARbro.Imaging;

/// <summary>Android-compatible raster backend with straight alpha and indexed PNG semantics.</summary>
public sealed class PortableRasterCodec : IRasterCodec
{
    static int Signature(Stream stream)
    {
        if (!stream.CanSeek) throw new ArgumentException("Raster input must support seeking.");
        long position = stream.Position;
        Span<byte> bytes = stackalloc byte[4]; stream.ReadExactly(bytes); stream.Position = position;
        return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }
    static void Validate(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 100_000_000)
            throw new InvalidDataException("Image dimensions exceed the decoding limit.");
    }
    public (int Width, int Height) Identify(Stream stream)
    {
        int signature = Signature(stream);
        if (signature is 0x002a4949 or 0x2a004d4d)
        {
            using var tif = Tiff.ClientOpen("resource", "r", stream, new OpenStream());
            if (tif == null) throw new InvalidDataException("Invalid TIFF image.");
            int width = tif.GetField(TiffTag.IMAGEWIDTH)[0].ToInt(), height = tif.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            Validate(width, height); return (width, height);
        }
        using var managed = new SKManagedStream(stream, false);
        using var codec = SKCodec.Create(managed) ?? throw new InvalidDataException("Unsupported raster image.");
        Validate(codec.Info.Width, codec.Info.Height); return (codec.Info.Width, codec.Info.Height);
    }
    public PixelBuffer Decode(Stream stream, bool preserveColorType = false)
    {
        int signature = Signature(stream);
        if (signature == 0x474e5089) return Png(stream, preserveColorType);
        if (signature is 0x002a4949 or 0x2a004d4d) return TiffImage(stream);
        using var managed = new SKManagedStream(stream, false);
        using var codec = SKCodec.Create(managed) ?? throw new InvalidDataException("Unsupported raster image.");
        int width = codec.Info.Width, height = codec.Info.Height; Validate(width, height);
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var bytes = new byte[checked(width * height * 4)];
        if (codec.GetPixels(info, bytes) != SKCodecResult.Success) throw new InvalidDataException("Truncated raster image.");
        if (preserveColorType && codec.Info.AlphaType == SKAlphaType.Opaque)
        {
            var rgb = new byte[checked(width * height * 3)];
            for (int i = 0, j = 0; i < bytes.Length; i += 4, j += 3) { rgb[j] = bytes[i]; rgb[j + 1] = bytes[i + 1]; rgb[j + 2] = bytes[i + 2]; }
            return PixelBuffer.Create(width, height, 96, 96, PixelFormats.Bgr24, null, rgb, width * 3);
        }
        return PixelBuffer.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, width * 4);
    }
    static PixelBuffer Png(Stream stream, bool preserve)
    {
        var reader = new PngReader(stream) { ShouldCloseStream = false, MaxBytesMetadata = 16L << 20, MaxTotalBytesRead = 512L << 20 };
        try
        {
            var info = reader.ImgInfo; int width = info.Cols, height = info.Rows; Validate(width, height);
            reader.SetUnpackedMode(true);
            int[] first = reader.ReadRowInt(0).Scanline;
            var plte = reader.GetChunksList().GetById1("PLTE") as PngChunkPLTE;
            var trns = reader.GetChunksList().GetById1("tRNS") as PngChunkTRNS;
            int[] alpha = trns?.GetPalletteAlpha();
            if (preserve && info.Indexed && plte != null)
            {
                var colors = new Color[plte.GetNentries()];
                for (int i = 0; i < colors.Length; i++)
                { int rgb = plte.GetEntry(i); colors[i] = Color.FromArgb((byte)(alpha != null && i < alpha.Length ? alpha[i] : 255), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); }
                var indices = new byte[checked(width * height)];
                for (int y = 0; y < height; y++)
                { int[] row = y == 0 ? first : reader.ReadRowInt(y).Scanline; for (int x = 0; x < width; x++) indices[y * width + x] = checked((byte)row[x]); }
                reader.End(); return PixelBuffer.Create(width, height, 96, 96, PixelFormats.Indexed8, new BitmapPalette(colors), indices, width);
            }
            var bytes = new byte[checked(width * height * 4)];
            int shift = Math.Max(0, info.BitDepth - 8), maximum = (1 << info.BitDepth) - 1;
            for (int y = 0; y < height; y++)
            {
                int[] row = y == 0 ? first : reader.ReadRowInt(y).Scanline;
                for (int x = 0; x < width; x++)
                {
                    int r, g, b, a = 255, at = x * info.Channels;
                    if (info.Indexed)
                    { int index = row[x], rgb = plte.GetEntry(index); r = (rgb >> 16) & 255; g = (rgb >> 8) & 255; b = rgb & 255; if (alpha != null && index < alpha.Length) a = alpha[index]; }
                    else if (info.Greyscale)
                    { r = g = b = info.BitDepth < 8 ? row[at] * 255 / maximum : row[at] >> shift; if (info.Alpha) a = row[at + 1] >> shift; else if (trns != null && row[at] == trns.GetGray()) a = 0; }
                    else
                    {
                        r = row[at] >> shift; g = row[at + 1] >> shift; b = row[at + 2] >> shift;
                        if (info.Alpha) a = row[at + 3] >> shift;
                        else if (trns != null) { var transparent = trns.GetRGB(); if (row[at] == transparent[0] && row[at + 1] == transparent[1] && row[at + 2] == transparent[2]) a = 0; }
                    }
                    int dest = (y * width + x) * 4; bytes[dest] = (byte)b; bytes[dest + 1] = (byte)g; bytes[dest + 2] = (byte)r; bytes[dest + 3] = (byte)a;
                }
            }
            reader.End();
            return PixelBuffer.Create(width, height, 96, 96, preserve && !info.Alpha && trns == null ? PixelFormats.Bgr32 : PixelFormats.Bgra32, null, bytes, width * 4);
        }
        finally { reader.End(); }
    }
    sealed class OpenStream : TiffStream { public override void Close(object clientData) { } }
    static PixelBuffer TiffImage(Stream stream)
    {
        using var tif = Tiff.ClientOpen("resource", "r", stream, new OpenStream()) ?? throw new InvalidDataException("Invalid TIFF image.");
        int width = tif.GetField(TiffTag.IMAGEWIDTH)[0].ToInt(), height = tif.GetField(TiffTag.IMAGELENGTH)[0].ToInt(); Validate(width, height);
        var bytes = new byte[checked(width * height * 4)];
        int bits = tif.GetFieldDefaulted(TiffTag.BITSPERSAMPLE)[0].ToInt(), samples = tif.GetFieldDefaulted(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
        bool rgb = tif.GetFieldDefaulted(TiffTag.PHOTOMETRIC)[0].ToInt() == (int)Photometric.RGB;
        bool contiguous = tif.GetFieldDefaulted(TiffTag.PLANARCONFIG)[0].ToInt() == (int)PlanarConfig.CONTIG;
        int orientation = tif.GetFieldDefaulted(TiffTag.ORIENTATION)[0].ToInt();
        if (rgb && contiguous && bits == 8 && samples is 3 or 4 && orientation == (int)Orientation.TOPLEFT)
        {
            var row = new byte[tif.ScanlineSize()];
            var extra = tif.GetField(TiffTag.EXTRASAMPLES);
            bool associated = extra != null && extra[0].ToInt() > 0 && extra[1].ToShortArray()[0] == (short)ExtraSample.ASSOCALPHA;
            for (int y = 0; y < height; y++)
            {
                if (!tif.ReadScanline(row, y)) throw new InvalidDataException("Truncated TIFF image.");
                for (int x = 0; x < width; x++)
                {
                    int at = x * samples, dest = (y * width + x) * 4, a = samples == 4 ? row[at + 3] : 255;
                    for (int c = 0; c < 3; c++) bytes[dest + c] = (byte)(associated && a != 0 ? Math.Min(255, row[at + 2 - c] * 255 / a) : row[at + 2 - c]);
                    bytes[dest + 3] = (byte)a;
                }
            }
        }
        else
        {
            var raster = new int[checked(width * height)];
            if (!tif.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT)) throw new InvalidDataException("Invalid TIFF pixels.");
            for (int i = 0; i < raster.Length; i++)
            { uint rgba = unchecked((uint)raster[i]); int a = (int)(rgba >> 24); bytes[i * 4 + 3] = (byte)a; for (int c = 0; c < 3; c++) { int value = (int)(rgba >> ((2 - c) * 8)) & 255; bytes[i * 4 + c] = (byte)(a == 0 ? 0 : Math.Min(255, value * 255 / a)); } }
        }
        return PixelBuffer.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, width * 4);
    }
    public void Encode(PixelBuffer buffer, Stream output, string format, int quality = 90)
    {
        int width = buffer.PixelWidth, height = buffer.PixelHeight; Validate(width, height);
        byte[] bytes = buffer.ToBgra32();
        if (format.Equals("TIFF", StringComparison.OrdinalIgnoreCase) || format.Equals("TIF", StringComparison.OrdinalIgnoreCase))
        {
            using var tif = Tiff.ClientOpen("output", "w", output, new OpenStream()) ?? throw new IOException("Cannot create TIFF.");
            tif.SetField(TiffTag.IMAGEWIDTH, width); tif.SetField(TiffTag.IMAGELENGTH, height); tif.SetField(TiffTag.SAMPLESPERPIXEL, 4);
            tif.SetField(TiffTag.BITSPERSAMPLE, 8); tif.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT); tif.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
            tif.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB); tif.SetField(TiffTag.COMPRESSION, Compression.LZW); tif.SetField(TiffTag.ROWSPERSTRIP, tif.DefaultStripSize(0));
            tif.SetField(TiffTag.EXTRASAMPLES, 1, new short[] { (short)ExtraSample.UNASSALPHA });
            var row = new byte[width * 4];
            for (int y = 0; y < height; y++)
            { for (int x = 0; x < width; x++) { int at = (y * width + x) * 4; row[x * 4] = bytes[at + 2]; row[x * 4 + 1] = bytes[at + 1]; row[x * 4 + 2] = bytes[at]; row[x * 4 + 3] = bytes[at + 3]; } if (!tif.WriteScanline(row, y)) throw new IOException("Cannot write TIFF pixels."); }
            return;
        }
        if (format.Equals("BMP", StringComparison.OrdinalIgnoreCase))
        {
            using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, true);
            writer.Write((ushort)0x4d42); writer.Write(checked(122 + bytes.Length)); writer.Write(0); writer.Write(122);
            writer.Write(108); writer.Write(width); writer.Write(-height); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(3); writer.Write(bytes.Length); writer.Write(3780); writer.Write(3780); writer.Write(0); writer.Write(0);
            writer.Write(0x00ff0000u); writer.Write(0x0000ff00u); writer.Write(0x000000ffu); writer.Write(0xff000000u); writer.Write(0x73524742u);
            for (int i = 0; i < 12; i++) writer.Write(0); writer.Write(bytes); return;
        }
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        if(format.Equals("WEBP",StringComparison.OrdinalIgnoreCase))
        {
            using var pixels=bitmap.PeekPixels();
            using var webp=pixels.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless,100));
            webp.SaveTo(output);return;
        }
        SKEncodedImageFormat encoded = format.ToUpperInvariant() switch { "PNG" => SKEncodedImageFormat.Png, "JPEG" or "JPG" => SKEncodedImageFormat.Jpeg, _ => throw new NotSupportedException("Unknown output image format: " + format) };
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(encoded, Math.Clamp(quality, 1, 100));
        data.SaveTo(output);
    }
}
