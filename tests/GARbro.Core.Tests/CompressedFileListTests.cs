using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using GARbro.Application;
using GameRes;
using Xunit;

[Collection("Decoder state")]
public sealed class CompressedFileListTests
{
    [Fact]
    public void CompressedListsRetainUnicodeAndAcceptPlainFileOverrides()
    {
        ArchiveService.Formats();
        var catalog = FormatCatalog.Instance;
        string name = "test-compressed-" + Guid.NewGuid() + ".lst";
        string path = Path.Combine(catalog.DataDirectory, name);
        string[] expected = { "中文/背景.png", "Русский/音声.ogg", "日本語/画像.webp" };
        try
        {
            using (var compressed = new GZipStream(File.Create(path + ".gz"), CompressionLevel.SmallestSize))
            using (var writer = new StreamWriter(compressed, new UTF8Encoding(true)))
                writer.Write(string.Join("\r\n\r\n", expected));
            var lines = new List<string>(); catalog.ReadFileList(name, lines.Add); Assert.Equal(expected, lines);
            File.WriteAllText(path, "override.png\n");
            lines.Clear(); catalog.ReadFileList(name, lines.Add); Assert.Equal(new[] { "override.png" }, lines);
            File.Delete(path); File.WriteAllBytes(path + ".gz", new byte[] { 1, 2, 3, 4, 5 });
            Assert.Throws<InvalidDataException>(() => catalog.ReadFileList(name, _ => { }));
            File.Delete(path + ".gz");
            catalog.ReadFileList(name, _ => Assert.Fail("Missing lists must stay optional."));
        }
        finally { File.Delete(path); File.Delete(path + ".gz"); }
    }
}
