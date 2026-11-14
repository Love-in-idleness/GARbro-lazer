using System;
using System.IO;
using GameRes;
using Xunit;

public sealed class BinaryStreamTests
{
    [Fact]
    public void NumericReadsAndFixedStringsFillAcrossShortReads()
    {
        using var input = new BinaryStream(new ShortStream(new byte[] { 1, 2, 3, 4, 5, 6, 65, 66, 0, 67 }), "short");
        Assert.Equal(0x04030201u, input.ReadUInt32());
        Assert.Equal(0x0605, input.ReadUInt16());
        Assert.Equal("AB", input.ReadCString(3));
        Assert.Equal(67, input.ReadByte());
        Assert.Throws<EndOfStreamException>(() => input.ReadInt16());
    }

    [Fact]
    public void HeaderGrowthAndReadBytesDistinguishShortReadsFromEndOfStream()
    {
        using var input = new BinaryStream(new ShortStream(new byte[] { 1, 2, 3, 4, 5, 6 }), "short");
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, input.ReadHeader(4).ToArray());
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, input.ReadHeader(6).ToArray());
        Assert.Throws<EndOfStreamException>(() => input.ReadHeader(7));
        Assert.Equal(6, input.Position);
        input.Position = 0;
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, input.ReadBytes(10));
        Assert.Empty(input.ReadBytes(1));
    }

    [Fact]
    public void UnseekableSignaturePreservesAllPrefetchedBytes()
    {
        using var input = new BinaryStream(new ShortStream(new byte[] { 1, 2, 3, 4, 5 }, false), "short");
        Assert.Equal(0x04030201u, input.Signature);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, input.ReadBytes(5));
        Assert.Equal(-1, input.ReadByte());
    }

    sealed class ShortStream(byte[] bytes, bool seekable = true) : MemoryStream(bytes, false)
    {
        public override bool CanSeek => seekable;
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 1));
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, 1)]);
    }
}
