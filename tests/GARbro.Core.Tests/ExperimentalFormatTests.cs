using System;
using System.IO;
using System.Linq;
using GARbro.Application;
using GameRes;
using Xunit;

[Collection("Decoder state")]
public sealed class ExperimentalFormatTests
{
    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void ExperimentalWebpProviderDecodesTheIndependentLosslessFixture()
    {
        ArchiveService.Formats();
        var formats = FormatCatalog.Instance.ImageFormats.Where(f => f.Tag == "WEBP").ToArray();
        Assert.Single(formats);
        foreach (var format in formats)
        {
            using var input = BinaryStream.FromFile(Fixture("slice.webp"));
            var info = format.ReadMetaData(input);
            Assert.NotNull(info);
            var image = format.Read(input, info);
            Assert.Equal(2u, image.Width);
            Assert.Equal(1u, image.Height);
            Assert.Equal(new byte[] { 0, 0, 255, 128, 0, 255, 0, 255 }, image.Bitmap.ToBgra32());
        }
    }

    [Fact]
    public void IptTilesClipNegativeCoordinatesInsideTheOwnedCanvas()
    {
        ArchiveService.Formats();
        var format = new GameRes.Formats.Artemis.IptFormat();
        var canvas = new GARbro.Imaging.MutablePixelBuffer(2, 1, 96, 96, GARbro.Imaging.PixelFormats.Bgra32, null);
        canvas.Lock();
        try
        {
            // Exercise clipping directly: the inherited text grammar accepts unsigned numbers only.
            var read = typeof(GameRes.Formats.Artemis.IptFormat).GetMethod("ReadIntoCanvas",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            read.Invoke(format, new object[] { Fixture("colors-rgba.png"), canvas, -1, 0, true });
        }
        finally { canvas.Unlock(); }
        canvas.Freeze();
        Assert.Equal(new byte[] { 0, 255, 0, 255, 0, 0, 0, 0 }, canvas.ToBgra32());
    }

    [Fact]
    public void RpgMakerFindsEachGamesOwnImageKeyBeforeUsingTheCachedKey()
    {
        foreach (string game in new[] { "rpg-a", "rpg-b", "rpg-a" })
        {
            var image = ArchiveService.ReadImage(Fixture(game + "/img/pictures/colors.rpgmvp"));
            Assert.Equal(new byte[] { 0, 0, 255, 128, 0, 255, 0, 255 }, image.Bitmap.ToBgra32());
        }
    }

    [Fact]
    public void RpgMakerAudioFindsTheCurrentGamesKeyAndRetainsVorbisSamples()
    {
        ArchiveService.Formats();
        using var referenceInput = BinaryStream.FromFile(Fixture("tone.ogg"));
        using var reference = AudioFormat.Read(referenceInput);
        using var expected = new MemoryStream();
        reference.CopyTo(expected);
        foreach (string game in new[] { "rpg-a", "rpg-b", "rpg-a" })
        {
            using var input = BinaryStream.FromFile(Fixture(game + "/audio/bgm/tone.rpgmvo"));
            using var sound = AudioFormat.Read(input);
            Assert.NotNull(sound);
            using var samples = new MemoryStream();
            sound.CopyTo(samples);
            Assert.Equal(expected.ToArray(), samples.ToArray());
        }
    }

    [Fact]
    public void ManagedOpusDecodesTheIndependentEncodedTone()
    {
        ArchiveService.Formats();
        using var input = BinaryStream.FromFile(Fixture("tone.opus"));
        using var sound = AudioFormat.Read(input);
        Assert.NotNull(sound);
        Assert.Equal(48000u, sound.Format.SamplesPerSecond);
        Assert.Equal((ushort)1, sound.Format.Channels);
        Assert.Equal((ushort)16, sound.Format.BitsPerSample);
        using var pcm = new MemoryStream();
        sound.CopyTo(pcm);
        Assert.Equal(sound.Length, pcm.Length);
        Assert.InRange(pcm.Length / 2, 4800, 6000);
        byte[] bytes = pcm.ToArray();
        double power = 0;
        for (int i = 0; i < bytes.Length; i += 2)
        {
            double sample = BitConverter.ToInt16(bytes, i) / 32768d;
            power += sample * sample;
        }
        Assert.InRange(Math.Sqrt(power / (bytes.Length / 2)), 0.10, 0.25);
    }
}
