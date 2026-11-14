using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GARbro.Imaging;
using Xunit;

public sealed class PixelVectorTests
{
    static JsonDocument Load() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pixel-vectors.json")));
    public static IEnumerable<object[]> Cases()
    {
        using var doc = Load();
        foreach (var item in doc.RootElement.EnumerateArray())
            yield return new object[]
            {
                item.GetProperty("name").GetString()
            };
    }

    [Theory, MemberData(nameof(Cases))]
    public void PixelModelMatchesFrozenVectors(string name)
    {
        using var doc = Load();
        var item = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("name").GetString() == name);
        var format = (PixelFormat)typeof(PixelFormats).GetProperty(item.GetProperty("format").GetString()).GetValue(null);
        BitmapPalette palette = null;
        if (item.GetProperty("palette").ValueKind != JsonValueKind.Null)
            palette = new BitmapPalette(item.GetProperty("palette").EnumerateArray().Select(c => Color.FromArgb(c.GetProperty("a").GetByte(), c.GetProperty("r").GetByte(), c.GetProperty("g").GetByte(), c.GetProperty("b").GetByte())));
        var image = PixelBuffer.Create(item.GetProperty("width").GetInt32(), item.GetProperty("height").GetInt32(), 96, 96, format, palette, Convert.FromBase64String(item.GetProperty("pixels").GetString()), item.GetProperty("stride").GetInt32());
        Assert.Equal(Convert.FromBase64String(item.GetProperty("bgra32").GetString()), image.ToBgra32());
    }

    [Fact]
    public void EveryGray16ValueMatchesObservedWpfQuantization()
    {
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gray16-quantization.json")));
        var bounds = reference.RootElement.GetProperty("lowerBounds").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var input = new byte[65536 * 2];
        var expected = new byte[65536 * 4];
        int level = 0;
        for (int value = 0; value <= ushort.MaxValue; value++)
        {
            input[value * 2] = (byte)value;
            input[value * 2 + 1] = (byte)(value >> 8);
            while (level < bounds.Length && value >= bounds[level])
                level++;
            expected[value * 4] = expected[value * 4 + 1] = expected[value * 4 + 2] = (byte)level;
            expected[value * 4 + 3] = 255;
        }
        var image = PixelBuffer.Create(256, 256, 96, 96, PixelFormats.Gray16, null, input, 512);
        Assert.Equal(expected, image.ToBgra32());
    }
}
