using System;
using System.IO;
using GameRes;
using GARbro.Application;
using Xunit;

public sealed class WrappedXp3Tests
{
    [Theory]
    [InlineData(16, true)]
    [InlineData(17, false)]
    public void ReportsKnownFateWrapperOnlyWhenItsFooterFits(byte footerOffset, bool wrapped)
    {
        var bytes = new byte[64];
        bytes[0] = footerOffset;
        new byte[] { 0x15, 0xd9, 0x30, 0x54, 0xdc, 0x3a, 0xd1, 0xcd, 4 }.CopyTo(bytes, 4);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xp3");
        File.WriteAllBytes(path, bytes);
        try
        {
            var error = Assert.ThrowsAny<UnknownFormatException>(() => ArchiveService.Open(path, "XP3"));
            Assert.Equal(wrapped, error.Message.Contains("demoleition VS", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }
}
