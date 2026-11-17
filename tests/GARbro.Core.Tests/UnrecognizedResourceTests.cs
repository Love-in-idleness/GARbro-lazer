using System;
using System.IO;
using GameRes;
using GARbro.Application;
using Xunit;

[Collection("Decoder state")]
public sealed class UnrecognizedResourceTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(17)]
    public void UnrecognizedArchiveUsesNormalErrorAndCanStillBeBrowsedAsAFile(byte footerOffset)
    {
        var bytes = new byte[64];
        bytes[0] = footerOffset;
        new byte[] { 0x15, 0xd9, 0x30, 0x54, 0xdc, 0x3a, 0xd1, 0xcd, 4 }.CopyTo(bytes, 4);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xp3");
        File.WriteAllBytes(path, bytes);
        try
        {
            Assert.IsType<UnknownFormatException>(Assert.ThrowsAny<UnknownFormatException>(() => ArchiveService.Open(path, "XP3")));
            using var session = new ResourceSession(new(path), allowPlainFile: true);
            var entry = Assert.Single(session.List().Entries);
            using var stream = session.OpenEntry(entry.Name);
            using var copy = new MemoryStream(); stream.CopyTo(copy);
            Assert.Equal(bytes, copy.ToArray());
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }
}
