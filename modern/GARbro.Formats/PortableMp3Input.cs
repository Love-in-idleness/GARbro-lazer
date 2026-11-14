using System;
using System.IO;
using NLayer;

namespace GameRes.Formats
{
    /// <summary>Managed MPEG decoding with a byte-addressable PCM stream.</summary>
    public sealed class Mp3Input : SoundInput
    {
        readonly MpegFile reader;
        readonly byte[] pending = new byte[8192];
        readonly int bitrate;
        int pendingOffset, pendingCount;
        long position;
        bool disposed;

        public Mp3Input(Stream source) : base(source)
        {
            reader = new MpegFile(source);
            PcmSize = reader.Length;
            if (PcmSize < 0)
                throw new InvalidDataException("MPEG decoded length is unavailable.");
            Format = new WaveFormat
            {
                FormatTag = 3,
                Channels = checked((ushort)reader.Channels),
                SamplesPerSecond = checked((uint)reader.SampleRate),
                BitsPerSample = 32,
                BlockAlign = checked((ushort)(reader.Channels * 4)),
                AverageBytesPerSecond = checked((uint)(reader.SampleRate * reader.Channels * 4))
            };
            bitrate = reader.Duration.TotalSeconds > 0
                ? (int)Math.Min(int.MaxValue, source.Length * 8d / reader.Duration.TotalSeconds) : 0;
        }

        public override int SourceBitrate => bitrate;
        public override string SourceFormat => "mp3";
        public override bool CanSeek => !disposed && reader.CanSeek;
        public override long Position
        {
            get => position;
            set
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (value < 0 || value > PcmSize)
                    throw new ArgumentOutOfRangeException(nameof(value));
                reader.Position = value - value % Format.BlockAlign;
                position = reader.Position;
                pendingOffset = pendingCount = 0;
                if (position > value)
                {
                    reader.Position = 0;
                    position = 0;
                }
                var discard = new byte[8192];
                while (position < value)
                    if (Read(discard, 0, (int)Math.Min(discard.Length, value - position)) == 0)
                        throw new EndOfStreamException();
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentNullException.ThrowIfNull(buffer);
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0)
                return 0;
            if (pendingOffset == pendingCount)
            {
                pendingCount = reader.ReadSamples(pending, 0, pending.Length);
                pendingOffset = 0;
            }
            int copied = Math.Min(count, pendingCount - pendingOffset);
            Buffer.BlockCopy(pending, pendingOffset, buffer, offset, copied);
            pendingOffset += copied;
            position += copied;
            return copied;
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                    reader.Dispose();
                disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
