using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using GameRes;
using Xunit;

public sealed class ArcViewTests : IDisposable
{
    readonly string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
    readonly byte[] bytes = Enumerable.Range(0, 200003).Select(x => (byte)(x * 17 + x / 251)).ToArray();
    public ArcViewTests() => File.WriteAllBytes(path, bytes);
    public void Dispose() => File.Delete(path);
    [Fact]
    public void UnalignedMappingUsesRuntimePointerOffset()
    {
        using var map = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        using var view = map.CreateViewAccessor(65539, 27, MemoryMappedFileAccess.Read);
        unsafe
        {
            byte* pointer = view.GetPointer(65539);
            try
            {
                for (int n = 0; n < 27; n++)
                    Assert.Equal(bytes[65539 + n], pointer[n]);
            }
            finally
            {
                view.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
    }

    [Fact]
    public void FramesRemapAcrossPageAndAllocationBoundaries()
    {
        using var arc = new ArcView(path);
        using var frame = arc.CreateFrame();
        foreach (long offset in new long[]
        {
            0,
            4093,
            65533,
            131075,
            7,
            bytes.Length - 8
        }

        )
        {
            Assert.Equal(BitConverter.ToUInt64(bytes, (int)offset), frame.ReadUInt64(offset));
            Assert.Equal(bytes.Skip((int)offset).Take(8).ToArray(), frame.ReadBytes(offset, 8));
        }
    }

    [Fact]
    public void EmptyFileHasReadableEmptyStream()
    {
        File.WriteAllBytes(path, Array.Empty<byte>());
        using var arc = new ArcView(path);
        using var input = arc.CreateStream();
        Assert.Equal(0, input.Length);
        Assert.Equal(0u, input.Signature);
        Assert.Equal(-1, input.ReadByte());
        Assert.Equal(0, input.Read(new byte[3], 0, 3));
        Assert.Empty(arc.View.ReadBytes(0, 99));
        Assert.Throws<ArgumentException>(() => arc.View.ReadByte(0));
    }

    [Fact]
    public void CorruptOffsetsAndTruncatedReadsAreRejected()
    {
        using var arc = new ArcView(path);
        Assert.Throws<ArgumentOutOfRangeException>(() => arc.View.ReadByte(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => arc.View.Reserve(long.MaxValue, uint.MaxValue));
        Assert.Throws<ArgumentException>(() => arc.View.ReadUInt64(bytes.Length - 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => arc.CreateStream(1, uint.MaxValue));
        Assert.Empty(arc.View.ReadBytes(bytes.Length, 16));
        Assert.Equal(1u, arc.View.Reserve(bytes.Length - 1, uint.MaxValue));
    }

    [Fact]
    public void SubstreamCopiesOnlyItsOwnBytes()
    {
        using var arc = new ArcView(path);
        using var input = arc.CreateStream(65537, 70001);
        using var output = new MemoryStream();
        input.CopyTo(output);
        Assert.Equal(bytes.Skip(65537).Take(70001).ToArray(), output.ToArray());
        input.Seek(17, SeekOrigin.End);
        Assert.Equal(-1, input.ReadByte());
        Assert.Equal(0, input.Read(new byte[20], 0, 20));
    }

    [Fact]
    public void StreamInputDoesNotConsumePastDeclaredLength()
    {
        using var input = new MemoryStream(bytes);
        using var arc = new ArcView(input, "memory", 31);
        Assert.Equal(31, input.Position);
        Assert.Equal(bytes.Take(31).ToArray(), arc.View.ReadBytes(0, 31));
        using var shortInput = new MemoryStream(new byte[3]);
        Assert.Throws<EndOfStreamException>(() => new ArcView(shortInput, "truncated", 4));
    }

    [Fact]
    public void DisposedFrameAndStreamRejectAccess()
    {
        using var arc = new ArcView(path);
        var frame = arc.CreateFrame();
        frame.Dispose();
        frame.Dispose();
        Assert.Throws<ObjectDisposedException>(() => frame.ReadByte(0));
        var input = arc.CreateStream();
        input.Dispose();
        Assert.Throws<ObjectDisposedException>(() => input.ReadByte());
        Assert.Throws<ObjectDisposedException>(() => input.Read(new byte[1], 0, 1));
    }

    [Fact]
    public void LegacyCodePagesRoundTrip()
    {
        Assert.Equal("日本語", Encodings.cp932.GetString(Encodings.cp932.GetBytes("日本語")));
        var cp936 = Encoding.GetEncoding(936);
        Assert.Equal("中文", cp936.GetString(cp936.GetBytes("中文")));
    }

    [Fact]
    public void SparseFileSupportsOffsetsAboveFourGiB()
    {
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            file.Position = (1L << 32) + 123;
            file.WriteByte(0xA7);
        }

        using var arc = new ArcView(path);
        Assert.Equal(0xA7, arc.View.ReadByte((1L << 32) + 123));
    }
}
