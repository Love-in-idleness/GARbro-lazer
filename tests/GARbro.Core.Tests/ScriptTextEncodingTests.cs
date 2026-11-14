using System;
using System.IO;
using System.Linq;
using System.Text;
using GameRes;
using GameRes.Formats.NScripter;
using Xunit;

public sealed class ScriptTextEncodingTests
{
    [Theory]
    [InlineData(932, "日本語の台詞")]
    [InlineData(936, "简体中文对白")]
    public void ExportsNscripterWithItsSelectedEncoding(int codePage, string dialogue)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(codePage);
        string script = "*define\r\n*start\r\n" + dialogue + "\r\n";
        var encrypted = encoding.GetBytes(script).Select(value => (byte)(value ^ 0x84)).ToArray();
        using var input = new BinaryStream(new MemoryStream(encrypted), @"scenarios\NSCRIPT.DAT");
        var opener = new NSOpener();
        Assert.True(opener.IsScript(input));
        using var decoded = opener.ConvertFrom(input);
        using var output = new MemoryStream();
        ScriptTextEncoding.WriteUtf8(decoded, output, codePage == 932 ? null : encoding);
        Assert.Equal(Encoding.UTF8.GetBytes(script), output.ToArray());
    }

    [Theory]
    [InlineData(65001)]
    [InlineData(1200)]
    [InlineData(1201)]
    [InlineData(12000)]
    [InlineData(12001)]
    public void StripsUnicodeBomAcrossPartialReads(int codePage)
    {
        var encoding = Encoding.GetEncoding(codePage);
        const string script = "台詞😀\r\n";
        byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes(script)).ToArray();
        using var input = new PartialReads(bytes);
        using var output = new MemoryStream();
        ScriptTextEncoding.WriteUtf8(input, output, null);
        Assert.Equal(Encoding.UTF8.GetBytes(script), output.ToArray());
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public void KeepsUtf8WithoutABomAcrossReadBoundaries()
    {
        string script = "abcd" + new string('x', 8191) + "中文😀";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(script));
        using var output = new MemoryStream();
        ScriptTextEncoding.WriteUtf8(input, output, null);
        Assert.Equal(script, Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public void RejectsTruncatedUnicodeEvenWithABom()
    {
        using var input = new PartialReads(new byte[] { 0xef, 0xbb, 0xbf, 0xe3, 0x81 });
        using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => ScriptTextEncoding.WriteUtf8(input, output, null));
    }

    sealed class PartialReads(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, Math.Min(count, 1));
    }
}
