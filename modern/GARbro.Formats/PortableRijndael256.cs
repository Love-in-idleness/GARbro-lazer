using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace GameRes.Formats.AttacheCase
{
    // Modern .NET supports AES's 128-bit block only. Side-B requires the
    // original Rijndael 256-bit block with CBC and zero padding.
    internal sealed class PortableRijndael256 : SymmetricAlgorithm
    {
        public PortableRijndael256()
        {
            LegalBlockSizesValue = new[] { new KeySizes(256,256,0) };
            LegalKeySizesValue = new[] { new KeySizes(128,256,64) };
            BlockSizeValue = KeySizeValue = 256;
            ModeValue = CipherMode.CBC;
            PaddingValue = PaddingMode.Zeros;
        }
        public override void GenerateIV() => IV = RandomNumberGenerator.GetBytes(32);
        public override void GenerateKey() => Key = RandomNumberGenerator.GetBytes(KeySize / 8);
        public override ICryptoTransform CreateDecryptor(byte[] key, byte[] iv) => Create(false,key,iv);
        public override ICryptoTransform CreateEncryptor(byte[] key, byte[] iv) => Create(true,key,iv);
        ICryptoTransform Create(bool encrypt, byte[] key, byte[] iv)
        {
            if (Mode != CipherMode.CBC || Padding != PaddingMode.Zeros || iv?.Length != 32
                || key == null || !ValidKeySize(key.Length * 8))
                throw new CryptographicException("Expected Rijndael-256 CBC with zero padding.");
            return new Transform(encrypt,key,iv);
        }

        sealed class Transform : ICryptoTransform
        {
            readonly CbcBlockCipher cipher = new(new RijndaelEngine(256));
            readonly bool encrypt;
            bool disposed;
            public Transform(bool encrypt, byte[] key, byte[] iv)
            {
                this.encrypt=encrypt;
                cipher.Init(encrypt,new ParametersWithIV(new KeyParameter(key),iv));
            }
            public bool CanReuseTransform => false;
            public bool CanTransformMultipleBlocks => true;
            public int InputBlockSize => 32;
            public int OutputBlockSize => 32;
            public int TransformBlock(byte[] input,int offset,int count,byte[] output,int destination)
            {
                ObjectDisposedException.ThrowIf(disposed,this);
                ArgumentNullException.ThrowIfNull(input);
                ArgumentNullException.ThrowIfNull(output);
                if (offset < 0 || count < 0 || offset > input.Length-count || count % 32 != 0
                    || destination < 0 || destination > output.Length-count)
                    throw new CryptographicException("Invalid Rijndael-256 block length or buffer.");
                for (int i=0;i<count;i+=32) cipher.ProcessBlock(input,offset+i,output,destination+i);
                return count;
            }
            public byte[] TransformFinalBlock(byte[] input,int offset,int count)
            {
                if (count < 0 || offset < 0 || offset > input.Length-count || (!encrypt && count % 32 != 0))
                    throw new CryptographicException("Incomplete Rijndael-256 block.");
                int length=encrypt ? checked((count+31)/32*32) : count;
                var padded=new byte[length];
                Buffer.BlockCopy(input,offset,padded,0,count);
                var output=new byte[length];
                TransformBlock(padded,0,length,output,0);
                return output; // Zero padding is retained on decryption, as in .NET Framework.
            }
            public void Dispose() { cipher.Reset(); disposed=true; }
        }
    }
}
