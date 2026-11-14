using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Utility;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace GameRes.Formats.Microsoft
{
    // CFHEADER/CFFOLDER/CFFILE/CFDATA layout and checksum follow Microsoft's
    // Cabinet Format specification. Folder decoding retains at most 32 KiB of
    // dictionary history and one 32 KiB output block, regardless of entry size.
    [Export(typeof(ArchiveFormat))]
    public sealed class CabOpener : ArchiveFormat
    {
        public override string Tag => "CAB";
        public override string Description => "Microsoft cabinet archive";
        public override uint Signature => 0x4643534D;
        public override bool IsHierarchic => true;
        public override bool CanWrite => false;

        internal sealed record Block(long Header, long Data, ushort Packed, ushort Unpacked, uint Checksum);
        internal sealed class Folder
        {
            public int Method, Reserve;
            public long Size;
            public readonly List<Block> Blocks = new();
        }
        sealed class CabEntry : Entry { public Folder Folder; }

        public override ArcFile TryOpen(ArcView file)
        {
            if (file.MaxOffset < 36 || !file.View.AsciiEqual(0, "MSCF")) return null;
            long size = file.View.ReadUInt32(8), filesOffset = file.View.ReadUInt32(16);
            int folderCount = file.View.ReadUInt16(26), count = file.View.ReadUInt16(28), flags = file.View.ReadUInt16(30);
            if (size < 36 || size > file.MaxOffset || !IsSaneCount(folderCount) || !IsSaneCount(count)
                || file.View.ReadUInt16(24) != 0x0103 || (flags & ~7) != 0) return null;
            if ((flags & 3) != 0) throw new NotSupportedException("Multi-cabinet sets are not supported by the portable CAB decoder.");
            long position = 36;
            int folderReserve = 0, dataReserve = 0;
            if ((flags & 4) != 0)
            {
                Require(position, 4, size);
                int headerReserve = file.View.ReadUInt16(position);
                folderReserve = file.View.ReadByte(position + 2);
                dataReserve = file.View.ReadByte(position + 3);
                position += 4 + headerReserve;
            }
            Require(position, (long)folderCount * (8 + folderReserve), size);
            if (filesOffset < position + (long)folderCount * (8 + folderReserve) || filesOffset >= size) return null;
            var folders = new Folder[folderCount];
            var starts = new long[folderCount];
            var counts = new int[folderCount];
            int totalBlocks = 0;
            for (int i = 0; i < folderCount; i++, position += 8 + folderReserve)
            {
                starts[i] = file.View.ReadUInt32(position);
                counts[i] = file.View.ReadUInt16(position + 4);
                int compression = file.View.ReadUInt16(position + 6);
                int method = compression & 15;
                if (method > 3 || (method < 2 && compression != method)) return null;
                totalBlocks = checked(totalBlocks + counts[i]);
                if (totalBlocks >= 0x40000) throw new InvalidDataException("CAB block count exceeds the limit.");
                folders[i] = new Folder { Method = method, Reserve = dataReserve };
            }
            position = filesOffset;
            var entries = new List<Entry>(count);
            for (int i = 0; i < count; i++)
            {
                Require(position, 16, size);
                uint length = file.View.ReadUInt32(position), offset = file.View.ReadUInt32(position + 4);
                int folder = file.View.ReadUInt16(position + 8), attributes = file.View.ReadUInt16(position + 14);
                if (folder >= 0xFFFD) throw new NotSupportedException("Split CAB entries are not supported.");
                if (folder >= folders.Length) return null;
                position += 16;
                var name = new List<byte>();
                for (;;)
                {
                    Require(position, 1, size);
                    byte value = file.View.ReadByte(position++);
                    if (value == 0) break;
                    if (name.Count >= 32768) throw new InvalidDataException("CAB entry name exceeds the limit.");
                    name.Add(value);
                }
                var encoding = (attributes & 0x80) != 0 ? new UTF8Encoding(false, true)
                    : Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
                string text = encoding.GetString(name.ToArray());
                if (string.IsNullOrWhiteSpace(text)) return null;
                entries.Add(new CabEntry { Name = text, Type = FormatCatalog.Instance.GetTypeFromName(text),
                    Offset = offset, Size = length, Folder = folders[folder] });
            }
            long indexEnd = position;
            for (int i = 0; i < folders.Length; i++)
            {
                var folder = folders[i];
                position = starts[i];
                if (position < indexEnd) return null;
                for (int block = 0; block < counts[i]; block++)
                {
                    Require(position, 8 + dataReserve, size);
                    uint checksum = file.View.ReadUInt32(position);
                    ushort packed = file.View.ReadUInt16(position + 4), unpacked = file.View.ReadUInt16(position + 6);
                    if (unpacked == 0) throw new NotSupportedException("Split CAB data blocks are not supported.");
                    if (unpacked > 32768 || (folder.Method == 0 && packed != unpacked)
                        || (folder.Method == 1 && (packed < 2 || packed > 32780))) return null;
                    long data = position + 8 + dataReserve;
                    Require(data, packed, size);
                    folder.Blocks.Add(new Block(position, data, packed, unpacked, checksum));
                    folder.Size += unpacked;
                    position = data + packed;
                }
            }
            foreach (CabEntry entry in entries)
                if (entry.Offset > entry.Folder.Size || entry.Size > entry.Folder.Size - entry.Offset) return null;
            return new ArcFile(file, this, entries);
        }

        static void Require(long position, long length, long size)
        {
            if (position < 0 || length < 0 || position > size || length > size - position)
                throw new InvalidDataException("CAB data extends beyond the cabinet.");
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var item = (CabEntry)entry;
            if (item.Folder.Method >= 2)
                throw new NotSupportedException("The portable CAB decoder supports stored and MSZIP folders; Quantum and LZX require another backend.");
            var input = new FolderStream(arc.File, item.Folder);
            try
            {
                var buffer = new byte[32768];
                long remaining = item.Offset;
                while (remaining > 0)
                {
                    int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read == 0) throw new EndOfStreamException("Truncated CAB folder.");
                    remaining -= read;
                }
                return new LimitStream(input, item.Size);
            }
            catch { input.Dispose(); throw; }
        }

        sealed class FolderStream(ArcView file, Folder folder) : Stream
        {
            byte[] output = Array.Empty<byte>(), history = Array.Empty<byte>();
            int block, cursor;
            bool disposed;
            public override bool CanRead => !disposed;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => folder.Size;
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { disposed = true; base.Dispose(disposing); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                ArgumentNullException.ThrowIfNull(buffer);
                if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException();
                int copied = 0;
                while (copied < count)
                {
                    if (cursor == output.Length && !ReadBlock()) break;
                    int length = Math.Min(count - copied, output.Length - cursor);
                    Buffer.BlockCopy(output, cursor, buffer, offset + copied, length);
                    cursor += length; copied += length;
                }
                return copied;
            }

            bool ReadBlock()
            {
                if (block == folder.Blocks.Count) return false;
                var info = folder.Blocks[block++];
                var packed = file.View.ReadBytes(info.Data, info.Packed);
                if (info.Checksum != 0)
                {
                    var header = file.View.ReadBytes(info.Header + 4, (uint)(4 + folder.Reserve));
                    if (Checksum(header, Checksum(packed, 0)) != info.Checksum)
                        throw new InvalidDataException("CAB block checksum mismatch.");
                }
                if (folder.Method == 0) output = packed;
                else
                {
                    if (packed[0] != 'C' || packed[1] != 'K') throw new InvalidDataException("Missing MSZIP block signature.");
                    output = Inflate(packed, info.Unpacked, history);
                }
                // Non-final blocks normally fill the 32 KiB window. Retain the
                // trailing window across shorter blocks as well.
                history = history.Concat(output).TakeLast(32768).ToArray();
                cursor = 0;
                return true;
            }
        }

        static uint Checksum(byte[] data, uint seed)
        {
            int i = 0;
            while (i + 4 <= data.Length) { seed ^= LittleEndian.ToUInt32(data, i); i += 4; }
            uint tail = 0;
            for (; i < data.Length; i++) tail = tail << 8 | data[i];
            return seed ^ tail;
        }

        static byte[] Inflate(byte[] packed, int size, byte[] history)
        {
            var output = new byte[size];
            int header = history.Length == 0 ? 2 : 6;
            var input = new byte[header + packed.Length - 2 + 4];
            input[0] = 0x78; input[1] = history.Length == 0 ? (byte)0x01 : (byte)0x20;
            static void BigEndian(byte[] bytes, int position, uint value)
            {
                for (int i = 3; i >= 0; i--) { bytes[position + i] = (byte)value; value >>= 8; }
            }
            if (history.Length != 0) BigEndian(input, 2, Adler32.Compute(history, 0, history.Length));
            Buffer.BlockCopy(packed, 2, input, header, packed.Length - 2);
            // MSZIP stores raw deflate. Add zlib framing for the inflater's preset
            // dictionary support, then supply its synthetic Adler trailer after
            // the declared output is filled. The CAB checksum verifies file data.
            var inflater = new Inflater(false);
            inflater.SetInput(input, 0, input.Length - 4);
            int written = 0;
            bool dictionary = false, footer = false;
            var extra = new byte[1];
            while (!inflater.IsFinished)
            {
                int read = written < size ? inflater.Inflate(output, written, size - written) : inflater.Inflate(extra);
                if (written == size && read != 0) throw new InvalidDataException("MSZIP block exceeds its declared size.");
                written += read;
                if (inflater.IsNeedingDictionary && !dictionary && history.Length != 0)
                { inflater.SetDictionary(history); dictionary = true; }
                else if (inflater.IsNeedingInput && !footer && written == size)
                {
                    BigEndian(input, input.Length - 4, Adler32.Compute(output, 0, output.Length));
                    inflater.SetInput(input, input.Length - 4, 4);
                    footer = true;
                }
                else if (read == 0 && !inflater.IsFinished)
                    throw new InvalidDataException("Incomplete MSZIP block.");
            }
            if (written != size || inflater.RemainingInput != 0 || !footer)
                throw new InvalidDataException("Invalid MSZIP block length.");
            return output;
        }
    }
}
