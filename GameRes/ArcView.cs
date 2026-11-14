//! \file       ArcView.cs
//! \date       Mon Jul 07 10:31:10 2014
//! \brief      Memory mapped view of gameres file.
//
// Copyright (C) 2014-2017 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace GameRes
{
    public static class Encodings
    {
        public static readonly Encoding cp932;
        // Set only inside an isolated decoding operation. Fixed protocol/key
        // encodings continue to use cp932 or an explicitly specified encoding.
        public static Encoding ResourceEncodingOverride { get; set; }
        public static readonly System.Collections.Generic.Dictionary<string, Encoding> ResourceEncodingSettings = new System.Collections.Generic.Dictionary<string, Encoding>();
        public static Encoding ResourceEncoding { get { return ResourceEncodingOverride ?? cp932; } }

        static Encodings ()
        {
#if NET8_0_OR_GREATER
            Encoding.RegisterProvider (CodePagesEncodingProvider.Instance);
#endif
            cp932 = Encoding.GetEncoding (932);
        }

        public static Encoding WithFatalFallback (this Encoding enc)
        {
            var encoding = enc.Clone() as Encoding;
            encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
            encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
            return encoding;
        }

        public static bool IsUtf16 (this Encoding enc)
        {
            // enc.WindowsCodePage property might throw an exception for some encodings, while
            // CodePage is just a direct field access.
            return (enc.CodePage == 1200 || enc.CodePage == 1201); // enc is UnicodeEncoding
        }
    }

    public static class StreamExtension
    {
        public static void ReadExactlyCompat (this Stream input, byte[] buffer)
        {
            input.ReadExactlyCompat (buffer, 0, buffer.Length);
        }

        public static void ReadExactlyCompat (this Stream input, Span<byte> buffer)
        {
#if NET8_0_OR_GREATER
            input.ReadExactly (buffer);
#else
            var temporary = new byte[Math.Min (8192, buffer.Length)];
            while (!buffer.IsEmpty)
            {
                int count = Math.Min (temporary.Length, buffer.Length);
                input.ReadExactlyCompat (temporary, 0, count);
                temporary.AsSpan (0, count).CopyTo (buffer);
                buffer = buffer.Slice (count);
            }
#endif
        }

        /// <summary>Fill a buffer, including streams that return partial reads.</summary>
        public static void ReadExactlyCompat (this Stream input, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = input.Read (buffer, offset, count);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
                count -= read;
            }
        }

        static public string ReadStringUntil (this Stream file, byte delim, Encoding enc)
        {
            byte[] buffer = new byte[16];
            int size = 0;
            for (;;)
            {
                int b = file.ReadByte ();
                if (-1 == b || delim == b)
                    break;
                if (buffer.Length == size)
                {
                    Array.Resize (ref buffer, checked(size/2*3));
                }
                buffer[size++] = (byte)b;
            }
            return enc.GetString (buffer, 0, size);
        }

        static public string ReadCString (this Stream file, Encoding enc)
        {
            return ReadStringUntil (file, 0, enc);
        }

        static public string ReadCString (this Stream file)
        {
            return ReadStringUntil (file, 0, Encodings.ResourceEncoding);
        }
    }

    public static class MappedViewExtension
    {
        unsafe public static byte* GetPointer (this MemoryMappedViewAccessor view, long offset)
        {
            // PointerOffset is the runtime's actual alignment adjustment on every OS.
            // The offset parameter remains for source/binary API compatibility.
            byte* ptr = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer (ref ptr);
            return ptr + view.PointerOffset;
        }
    }

    public class ArcView : IDisposable
    {
        private MemoryMappedFile    m_map;

        public const long           PageSize = 4096;
        public long                 MaxOffset { get; private set; }
        public Frame                View { get; private set; }
        public string               Name { get; private set; }

        public ArcView (string name)
        {
            using (var fs = new FileStream(name, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Name = name;
                MaxOffset = fs.Length;
                InitFromFileStream (fs, 0);
            }
        }

        public ArcView (Stream input, string name, uint length)
        {
            Name = name;
            MaxOffset = length;
            if (input is FileStream)
                InitFromFileStream (input as FileStream, length);
            else
                InitFromStream (input, length);
        }

        private void InitFromFileStream (FileStream fs, uint length)
        {
            if (MaxOffset == 0)
            {
                View = new Frame (this);
                return;
            }
#if NET8_0_OR_GREATER
            m_map = MemoryMappedFile.CreateFromFile (fs, null, length,
                MemoryMappedFileAccess.Read, HandleInheritability.None, true);
#else
            m_map = MemoryMappedFile.CreateFromFile (fs, null, length,
                MemoryMappedFileAccess.Read, null, HandleInheritability.None, true);
#endif
            try {
                View = new Frame (this);
            } catch {
                m_map.Dispose(); // dispose on error only
                throw;
            }
        }

        private void InitFromStream (Stream input, uint length)
        {
            if (length == 0)
            {
                View = new Frame (this);
                return;
            }
#if NET8_0_OR_GREATER
            m_map = MemoryMappedFile.CreateNew (null, length, MemoryMappedFileAccess.ReadWrite,
                MemoryMappedFileOptions.None, HandleInheritability.None);
#else
            m_map = MemoryMappedFile.CreateNew (null, length, MemoryMappedFileAccess.ReadWrite,
                MemoryMappedFileOptions.None, null, HandleInheritability.None);
#endif
            try
            {
                using (var view = m_map.CreateViewAccessor (0, length, MemoryMappedFileAccess.Write))
                {
                    var buffer = new byte[81920];
                    unsafe
                    {
                        byte* ptr = view.GetPointer (0);
                        try
                        {
                            uint total = 0;
                            while (total < length)
                            {
                                int read = input.Read (buffer, 0, (int)Math.Min ((uint)buffer.Length, length-total));
                                if (0 == read)
                                    throw new EndOfStreamException ("Input is shorter than the declared view length.");
                                read = (int)Math.Min (read, length-total);
                                Marshal.Copy (buffer, 0, (IntPtr)(ptr+total), read);
                                total += (uint)read;
                            }
                            MaxOffset = total;
                        }
                        finally
                        {
                            view.SafeMemoryMappedViewHandle.ReleasePointer();
                        }
                    }
                }
                View = new Frame (this);
            }
            catch
            {
                m_map.Dispose();
                throw;
            }
        }

        public Frame CreateFrame ()
        {
            return new Frame (View);
        }

        public ArcViewStream CreateStream ()
        {
            return new ArcViewStream (this);
        }

        public ArcViewStream CreateStream (long offset)
        {
            var size = this.MaxOffset - offset;
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Greater than archive size");
            }
            return new ArcViewStream (this, offset, size);
        }

        public ArcViewStream CreateStream (long offset, uint size, string name = null)
        {
            return new ArcViewStream (this, offset, size, name);
        }

        public MemoryMappedViewAccessor CreateViewAccessor (long offset, uint size)
        {
            if (disposed)
                throw new ObjectDisposedException (nameof(ArcView));
            if (offset < 0 || offset > MaxOffset || size > MaxOffset - offset)
                throw new ArgumentOutOfRangeException (nameof(offset));
            if (m_map == null)
                throw new ArgumentException ("Cannot map an empty file.");
            return m_map.CreateViewAccessor (offset, size, MemoryMappedFileAccess.Read);
        }

        #region IDisposable Members
        bool disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    View.Dispose();
                    if (m_map != null) m_map.Dispose();
                }
                disposed = true;
                m_map = null;
            }
        }
        #endregion

        public class Frame : IDisposable
        {
            private ArcView                     m_arc;
            private MemoryMappedViewAccessor    m_view;
            private long                        m_offset;
            private uint                        m_size;
            private unsafe byte*                m_mem;

            public long Offset      { get { return m_offset; } }
            public uint Reserved    { get { return m_size; } }

            public Frame (ArcView arc) : this (arc, 0, (uint)ArcView.PageSize)
            {
            }

            public Frame (Frame other) : this (other.m_arc)
            {
                if (other.disposed)
                    throw new ObjectDisposedException (nameof(Frame));
            }

            public Frame (ArcView arc, long offset, uint size)
            {
                if (arc == null)
                    throw new ArgumentNullException (nameof(arc));
                if (arc.disposed)
                    throw new ObjectDisposedException (nameof(ArcView));
                if (offset < 0 || offset > arc.MaxOffset)
                    throw new ArgumentOutOfRangeException (nameof(offset));
                m_arc = arc;
                m_offset = offset;
                m_size = (uint)Math.Min (size, arc.MaxOffset - offset);
                if (m_size != 0)
                {
                    m_view = arc.CreateViewAccessor (m_offset, m_size);
                    try { unsafe { m_mem = m_view.GetPointer (m_offset); } }
                    catch { m_view.Dispose(); throw; }
                }
            }

            public uint Reserve (long offset, uint size)
            {
                if (disposed)
                    throw new ObjectDisposedException (nameof(Frame));
                if (offset < 0 || offset > m_arc.MaxOffset)
                    throw new ArgumentOutOfRangeException (nameof(offset));
                if (offset == m_arc.MaxOffset)
                    return 0;
                // Subtractions avoid overflow for corrupt offsets near Int64.MaxValue.
                if (offset < m_offset || offset - m_offset > m_size
                    || size > m_size - (offset - m_offset))
                {
                    size = (uint)Math.Min (Math.Max ((long)size, ArcView.PageSize), m_arc.MaxOffset-offset);
                    var new_view = m_arc.CreateViewAccessor (offset, size);
                    unsafe
                    {
                        byte* new_mem;
                        try { new_mem = new_view.GetPointer (offset); }
                        catch { new_view.Dispose(); throw; }
                        if (m_view != null)
                        {
                            m_view.SafeMemoryMappedViewHandle.ReleasePointer();
                            m_view.Dispose();
                        }
                        m_view = new_view;
                        m_mem = new_mem;
                    }
                    m_offset = offset;
                    m_size = size;
                }
                return (uint)(m_size - (offset - m_offset));
            }

            public void StrictReserve (long offset, uint size)
            {
                if (Reserve (offset, size) < size)
                    throw new ArgumentException ("Not enough bytes to read in the memory mapped file view.", "offset");
            }

            public bool BytesEqual (long offset, byte[] data)
            {
                if (Reserve (offset, (uint)data.Length) < (uint)data.Length)
                    return false;
                unsafe
                {
                    byte* ptr = m_mem + (offset - m_offset);
                    for (int i = 0; i < data.Length; ++i)
                    {
                        if (ptr[i] != data[i])
                            return false;
                    }
                    return true;
                }
            }

            public bool AsciiEqual (long offset, string data)
            {
                if (Reserve (offset, (uint)data.Length) < (uint)data.Length)
                    return false;
                unsafe
                {
                    byte* ptr = m_mem + (offset - m_offset);
                    for (int i = 0; i < data.Length; ++i)
                    {
                        if (ptr[i] != data[i])
                            return false;
                    }
                    return true;
                }
            }

            public int Read (long offset, byte[] buf, int buf_offset, uint count)
            {
                // supposedly faster version of
                //Reserve (offset, count);
                //return m_view.ReadArray (offset-m_offset, buf, buf_offset, (int)count);

                if (buf == null)
                    throw new ArgumentNullException ("buf", "Buffer cannot be null.");
                if (buf_offset < 0)
                    throw new ArgumentOutOfRangeException ("buf_offset", "Buffer offset should be non-negative.");
                if (disposed)
                    throw new ObjectDisposedException (null);

                int total = checked((int)Math.Min (Reserve (offset, count), count));
                if (buf.Length - buf_offset < total)
                    throw new ArgumentException ("Buffer offset and length are out of bounds.");
                if (total != 0)
                    UnsafeCopy (offset, buf, buf_offset, total);
                return total;
            }

            private unsafe void UnsafeCopy (long offset, byte[] buf, int buf_offset, int count)
            {
                Marshal.Copy ((IntPtr)(m_mem + (offset-m_offset)), buf, buf_offset, count);
            }

            /// <summary>
            /// Read <paramref name="count"/> bytes starting from <paramref name="offset"/> into byte array and return that array.
            /// Returned array could be less than <paramref name="count"/> bytes length if end of the mapped file was reached.
            /// </summary>
            public byte[] ReadBytes (long offset, uint count)
            {
                count = Math.Min (count, Reserve (offset, count));
                var data = new byte[count];
                if (count != 0)
                    UnsafeCopy (offset, data, 0, data.Length);
                return data;
            }

            public byte ReadByte (long offset)
            {
                StrictReserve (offset, 1);
                unsafe { return m_mem[offset-m_offset]; }
            }

            public sbyte ReadSByte (long offset)
            {
                StrictReserve (offset, 1);
                unsafe { return (sbyte)m_mem[offset-m_offset]; }
            }

            public ushort ReadUInt16 (long offset)
            {
                StrictReserve (offset, 2);
                unsafe { return *(ushort*)(m_mem+offset-m_offset); }
            }

            public short ReadInt16 (long offset)
            {
                StrictReserve (offset, 2);
                unsafe { return *(short*)(m_mem+offset-m_offset); }
            }

            public uint ReadUInt32 (long offset)
            {
                StrictReserve (offset, 4);
                unsafe { return *(uint*)(m_mem+offset-m_offset); }
            }

            public int ReadInt32 (long offset)
            {
                StrictReserve (offset, 4);
                unsafe { return *(int*)(m_mem+offset-m_offset); }
            }

            public ulong ReadUInt64 (long offset)
            {
                StrictReserve (offset, 8);
                unsafe { return *(ulong*)(m_mem+offset-m_offset); }
            }

            public long ReadInt64 (long offset)
            {
                StrictReserve (offset, 8);
                unsafe { return *(long*)(m_mem+offset-m_offset); }
            }

            public string ReadString (long offset, uint size, Encoding enc)
            {
                size = Math.Min (size, Reserve (offset, size));
                if (0 == size)
                    return string.Empty;
                unsafe
                {
                    byte* s = m_mem + (offset - m_offset);
                    uint string_length = 0;
                    if (enc.IsUtf16()) // for UTF-16 encodings stop marker is 2-bytes long
                    {
                        ushort* u = (ushort*)s;
                        while (string_length + 1 < size && 0 != u[string_length >> 1])
                        {
                            string_length += 2;
                        }
                    }
                    else
                    {
                        while (string_length < size && 0 != s[string_length])
                        {
                            ++string_length;
                        }
                    }
                    return new string ((sbyte*)s, 0, (int)string_length, enc);
                }
            }

            public string ReadString (long offset, uint size)
            {
                return ReadString (offset, size, Encodings.ResourceEncoding);
            }

            internal unsafe ViewPointer GetPointer ()
            {
                if (disposed)
                    throw new ObjectDisposedException (nameof(Frame));
                if (m_view == null)
                    throw new EndOfStreamException();
                return new ViewPointer (m_view, m_offset);
            }

            #region IDisposable Members
            bool disposed = false;

            public void Dispose ()
            {
                Dispose (true);
                GC.SuppressFinalize (this);
            }

            protected virtual void Dispose (bool disposing)
            {
                if (!disposed)
                {
                    if (disposing)
                    {
                        unsafe
                        {
                            if (m_mem != null)
                            {
                                m_view.SafeMemoryMappedViewHandle.ReleasePointer();
                                m_mem = null;
                            }
                        }
                        if (m_view != null) m_view.Dispose();
                    }
                    m_arc = null;
                    m_view = null;
                    m_size = 0;
                    disposed = true;
                }
            }
            #endregion
        }

        public class Reader : System.IO.BinaryReader
        {
            public Reader (Stream stream) : base (stream, Encoding.ASCII, true)
            {
            }
        }
    }

    /// <summary>
    /// Unsafe wrapper around unmanaged memory mapped view pointer.
    /// </summary>
    public unsafe class ViewPointer : IDisposable
    {
        MemoryMappedViewAccessor    m_view;
        byte*                       m_ptr;

        public ViewPointer (MemoryMappedViewAccessor view, long offset)
        {
            m_view = view;
            m_ptr = m_view.GetPointer (offset);
        }

        public byte* Value
        {
            get
            {
                if (!_disposed)
                    return m_ptr;
                else
                    throw new ObjectDisposedException (null, "Access to disposed ViewPointer object failed.");
            }
        }

        #region IDisposable Members
        bool _disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    m_view.SafeMemoryMappedViewHandle.ReleasePointer();
                }
                _disposed = true;
            }
        }
        #endregion
    }
}
