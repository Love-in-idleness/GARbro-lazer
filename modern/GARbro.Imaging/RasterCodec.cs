using System;
using System.IO;
namespace GARbro.Imaging;

public sealed class RasterDecoder
{
    public System.Collections.Generic.IReadOnlyList<PixelBuffer> Frames { get; }
    public RasterDecoder(Stream input) => Frames = Array.AsReadOnly(new[] { RasterCodec.Decode(input, true) });
}

public static class RasterCodec
{
    static IRasterCodec backend;
    static readonly object gate = new();
    public static void RegisterBackend(IRasterCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        lock (gate) backend = codec;
    }
    static IRasterCodec Backend
    {
        get
        {
            lock (gate)
                return backend ??= (IRasterCodec)Activator.CreateInstance(Type.GetType(
                    "GARbro.Imaging.MagickRasterCodec, GARbro.Imaging.Magick", throwOnError: true));
        }
    }
    public static (int Width, int Height) Identify(Stream stream) => Backend.Identify(stream);
    public static PixelBuffer Decode(Stream stream) => Backend.Decode(stream);
    internal static PixelBuffer Decode(Stream stream, bool preserveColorType) => Backend.Decode(stream, preserveColorType);
    public static void Encode(PixelBuffer buffer, Stream output, string format, int quality = 90) => Backend.Encode(buffer, output, format, quality);
}
