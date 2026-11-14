using System;
using System.IO;
using System.Linq;
using System.Threading;
using GARbro.Application;
using GameRes;
using Xunit;

[Collection("Decoder state")]
public sealed class AudioTests
{
    [Theory]
    [InlineData("mp3")]
    [InlineData("ogg")]
    [InlineData("aiff")]
    public void ManagedAudioDecodesAndConvertsSyntheticTone(string suffix)
    {
        ArchiveService.Formats();
        string source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone." + suffix);
        using var input = BinaryStream.FromFile(source);
        using var sound = AudioFormat.Read(input);
        Assert.NotNull(sound);
        Assert.Equal(suffix, sound.SourceFormat);
        Assert.Equal(22050u, sound.Format.SamplesPerSecond);
        Assert.Equal((ushort)1, sound.Format.Channels);
        Assert.True(sound.SourceBitrate > 0);
        using var pcm = new MemoryStream();
        sound.CopyTo(pcm);
        Assert.Equal(sound.Length, pcm.Length);
        Assert.InRange(pcm.Length / sound.Format.BlockAlign, 1600, 4096);
        byte[] samples = pcm.ToArray();
        if (suffix == "aiff")
        {
            using var original = new WaveInput(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone.wav")));
            using var originalPcm = new MemoryStream();
            original.CopyTo(originalPcm);
            Assert.Equal(originalPcm.ToArray(), samples);
        }
        double power = 0;
        int count = samples.Length / (sound.Format.BitsPerSample / 8);
        for (int i = 0; i < count; i++)
        {
            double value = sound.Format.FormatTag == 3
                ? BitConverter.ToSingle(samples, i * 4)
                : BitConverter.ToInt16(samples, i * 2) / 32768d;
            Assert.True(double.IsFinite(value));
            power += value * value;
        }
        Assert.InRange(Math.Sqrt(power / count), 0.10, 0.25);
        string output = Path.Combine(Path.GetTempPath(), "garbro-audio-" + Guid.NewGuid() + ".wav");
        try
        {
            ArchiveService.ConvertAudio(source, output);
            using var converted = new WaveInput(File.OpenRead(output));
            using var actual = new MemoryStream();
            converted.CopyTo(actual);
            Assert.Equal(samples, actual.ToArray());
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void ManagedMp3PreservesSmallReadsAndByteSeeks()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone.mp3");
        using var sound = new GameRes.Formats.Mp3Input(File.OpenRead(source));
        using var all = new MemoryStream();
        sound.CopyTo(all);
        var expected = all.ToArray();
        sound.Position = 0;
        for (int i = 0; i < 33; i++)
        {
            var one = new byte[1];
            Assert.Equal(1, sound.Read(one, 0, 1));
            Assert.Equal(expected[i], one[0]);
        }
        sound.Position = 37;
        var next = new byte[19];
        Assert.Equal(next.Length, sound.Read(next, 0, next.Length));
        Assert.Equal(expected.Skip(37).Take(19), next);
        sound.Position = sound.Length;
        Assert.Equal(0, sound.Read(next, 0, next.Length));
    }

    [Fact]
    public void WaveConversionRetainsPcmSamples()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone.wav");
        string root = Path.Combine(Path.GetTempPath(), "garbro-audio-" + Guid.NewGuid());
        try
        {
            string output = Path.Combine(root, "tone.wav");
            ArchiveService.ConvertAudio(source, output);
            using var original = new WaveInput(File.OpenRead(source));
            using var converted = new WaveInput(File.OpenRead(output));
            Assert.Equal(original.Format.SamplesPerSecond, converted.Format.SamplesPerSecond);
            using var a = new MemoryStream();
            using var b = new MemoryStream();
            original.CopyTo(a);
            converted.CopyTo(b);
            Assert.Equal(a.ToArray(), b.ToArray());
            Assert.Throws<IOException>(() => ArchiveService.ConvertAudio(source, output));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
