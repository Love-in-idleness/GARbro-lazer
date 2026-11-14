using System;
using System.IO;

namespace GARbro.Imaging;

public interface IRasterCodec
{
    (int Width, int Height) Identify(Stream stream);
    PixelBuffer Decode(Stream stream, bool preserveColorType = false);
    void Encode(PixelBuffer buffer, Stream output, string format, int quality = 90);
}
