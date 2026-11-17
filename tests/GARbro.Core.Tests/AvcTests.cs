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
    public void MeteorArchivesUseDatabaseSchemeAndPreserveStoredNames()
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
            var provider = FormatCatalog.Instance.ArcFormats.OfType<DatOpener>().Single();
            var schemes = Assert.IsType<AvcScheme>(provider.Scheme).KnownSchemes;
            Assert.Contains(schemes, s => s.Password == "METEOR01" && s.KeyOffset == 0 && s.HeaderOffset == 8);
            provider.Scheme = new AvcScheme { KnownSchemes = schemes.Where(s => s.Password != "METEOR01").ToArray() };
            try
            {
                using var withoutScheme = new ArcView(path);
                Assert.Null(provider.TryOpen(withoutScheme));
            }
            finally { provider.Scheme = new AvcScheme { KnownSchemes = schemes }; }
            using (var view = new ArcView(path))
                Assert.Null(new GameRes.Formats.Circus.DatOpener().TryOpenFromFooter(view));
            using var archive = ArcFile.TryOpen(path);
            Assert.NotNull(archive); Assert.Equal("AVC", archive.Tag);
            Assert.Equal(new[] { "音声.ogg", "音声.OGG", "音声#2.ogg" }, archive.Dir.Select(entry => entry.Name));
            var expected = new[] { "first", "two", "third" };
            int index = 0;
            foreach (var entry in archive.Dir)
            {
                using var input = archive.OpenEntry(entry); using var output = new MemoryStream(); input.CopyTo(output);
                Assert.Equal(Encoding.ASCII.GetBytes(expected[index++]), output.ToArray());
            }
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

    [Fact]
    public void ExistingSchemeStillReadsPayloadAfterTheIndex()
    {
        ArchiveService.Formats();
        const int header = 81, keyOffset = 53, directory = header + 0x24, payload = directory + RecordSize;
        byte[] data = new byte[payload + 5];
        "CHOKOPAI"u8.CopyTo(data.AsSpan(keyOffset));
        "ARCHIVE\0"u8.CopyTo(data.AsSpan(header));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(header + 0x10), 0x24);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(header + 0x14), RecordSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(header + 0x20), 1);
        "original.txt"u8.CopyTo(data.AsSpan(directory + 1));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(directory + 0x108), payload - header);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(directory + 0x10c), 5);
        "first"u8.CopyTo(data.AsSpan(payload));
        ReadOnlySpan<byte> key = [0x4d, 0x70, 0xab, 0x11, 0x61, 0x19, 0x03, 0xd2];
        for (int i = header; i < data.Length; ++i) data[i] ^= key[(i - header) & 7];
        for (int i = 0; i < 8; ++i) data[keyOffset + i] ^= key[i];
        using var input = new MemoryStream(data); using var view = new ArcView(input, "original.dat", (uint)data.Length);
        using var archive = new DatOpener().TryOpen(view);
        Assert.NotNull(archive);
        var entry = Assert.Single(archive.Dir); Assert.Equal("original.txt", entry.Name);
        using var extracted = archive.OpenEntry(entry); using var output = new MemoryStream(); extracted.CopyTo(output);
        Assert.Equal("first"u8.ToArray(), output.ToArray());
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
