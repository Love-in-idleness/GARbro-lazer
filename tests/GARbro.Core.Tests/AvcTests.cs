using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using GARbro.Application;
using GameRes;
using GameRes.Formats.AVC;
using Xunit;

[Collection("Decoder state")]
public sealed class AvcTests
{
    const int RecordSize = 0x114;
    const int IndexOffset = 0x39;

    [Fact]
    public void MeteorArchivesExtractDistinctPayloadsWithCollidingNames()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dat");
        var data = MakeArchive();
        Encrypt(data);
        // A plausible Circus footer points to an out-of-bounds entry.
        int footerIndex = data.Length;
        Array.Resize(ref data, footerIndex + 36);
        data[footerIndex] = data[footerIndex + 14] = 1;
        data[footerIndex + 1] = (byte)'a';
        data[footerIndex + 15] = (byte)'b';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(footerIndex + 2), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(footerIndex + 10), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(data.Length - 8), (uint)footerIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(data.Length - 4), 2);
        File.WriteAllBytes(path, data);
        try
        {
            ArchiveService.Formats();
            using (var view = new ArcView(path))
                Assert.Null(new GameRes.Formats.Circus.DatOpener().TryOpenFromFooter(view));
            using var archive = ArchiveService.Open(path);
            Assert.Equal("AVC", archive.Tag);
            Assert.Equal(new[] { "音声.ogg", "音声#3.OGG", "音声#2.ogg" }, archive.Entries.Select(entry => entry.Name));
            Assert.Equal("first"u8.ToArray(), archive.ReadEntry("音声.ogg"));
            Assert.Equal("two"u8.ToArray(), archive.ReadEntry("音声#3.OGG"));
            Assert.Equal("third"u8.ToArray(), archive.ReadEntry("音声#2.ogg"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0, 0, 0)] // Wrong key marker.
    [InlineData(0x28, 0xffff, 0)] // Directory exceeds the archive.
    [InlineData(IndexOffset + 0x108, 0x23, 0)] // Entry begins inside the header.
    [InlineData(IndexOffset + 0x10c, 14, 0)] // Entry overlaps the directory.
    [InlineData(-1, 0, 1)] // Last directory record is truncated.
    public void InvalidMeteorArchivesAreRejected(int field, uint value, int trim)
    {
        ArchiveService.Formats();
        var data = MakeArchive();
        if (field >= 0)
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(field), value);
        Encrypt(data);
        using var stream = new MemoryStream(data, 0, data.Length - trim);
        using var view = new ArcView(stream, "bad.dat", (uint)stream.Length);
        Assert.Null(new DatOpener().TryOpen(view));
    }

    static byte[] MakeArchive()
    {
        var data = new byte[IndexOffset + 3 * RecordSize];
        "METEOR01ARCHIVE\0"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x14), 0x24);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x18), IndexOffset - 8);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x1c), RecordSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x28), 3);
        "firsttwothird"u8.CopyTo(data.AsSpan(0x2c));
        WriteEntry(0, "音声.ogg", 0x2c, 5);
        WriteEntry(1, "音声.OGG", 0x31, 3);
        WriteEntry(2, "音声#2.ogg", 0x34, 5);
        return data;

        void WriteEntry(int index, string name, int offset, uint size)
        {
            var record = data.AsSpan(IndexOffset + index * RecordSize, RecordSize);
            Encodings.cp932.GetBytes(name, record[1..]);
            BinaryPrimitives.WriteUInt32LittleEndian(record[0x108..], (uint)(offset - 8));
            BinaryPrimitives.WriteUInt32LittleEndian(record[0x10c..], size);
        }
    }

    static void Encrypt(byte[] data)
    {
        ReadOnlySpan<byte> key = [0x4d, 0x70, 0xab, 0x11, 0x61, 0x19, 0x03, 0xd2];
        for (int i = 0; i < data.Length; ++i)
            data[i] ^= key[i % key.Length];
    }
}
