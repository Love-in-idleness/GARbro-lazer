using System;
using System.IO;
using System.Security.Cryptography;
using GARbro.Application;
using GameRes;
using Xunit;

[Collection("Decoder state")]
public sealed class SideBTests
{
    [Fact]
    public void Rijndael256RetainsZeroPaddingAndRejectsPartialCipherBlocks()
    {
        var type=typeof(GameRes.Formats.AttacheCase.DatOpener).Assembly.GetType("GameRes.Formats.AttacheCase.PortableRijndael256",true);
        using var algorithm=(SymmetricAlgorithm)Activator.CreateInstance(type);
        var key=new byte[32]; var iv=new byte[32];
        using var encryptor=algorithm.CreateEncryptor(key,iv);
        var encrypted=encryptor.TransformFinalBlock(new byte[] {1,2,3},0,3);
        Assert.Equal(32,encrypted.Length);
        using var decryptor=algorithm.CreateDecryptor(key,iv);
        var decrypted=decryptor.TransformFinalBlock(encrypted,0,encrypted.Length);
        Assert.Equal(32,decrypted.Length);
        Assert.Equal(new byte[] {1,2,3},decrypted[..3]);
        Assert.All(decrypted[3..],value=>Assert.Equal((byte)0,value));
        using var invalid=algorithm.CreateDecryptor(key,iv);
        Assert.Throws<CryptographicException>(()=>invalid.TransformFinalBlock(encrypted,0,31));
    }

    [Fact]
    public void OversizedSideBHeadersAreRejectedBeforeAllocation()
    {
        ArchiveService.Formats();
        byte[] bytes=new byte[128];
        BitConverter.GetBytes(uint.MaxValue).CopyTo(bytes,0x1c);
        using var memory=new MemoryStream(bytes);
        using var view=new ArcView(memory,"bad.dat",(uint)bytes.Length);
        Assert.Null(new GameRes.Formats.AttacheCase.DatOpener().TryOpen(view));
    }
}
