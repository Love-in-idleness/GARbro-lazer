using System;
using System.Runtime.InteropServices;

namespace GARbro.Imaging
{
    /// <summary>Decoder-owned canvas; freeze it before sharing the completed image.</summary>
    public sealed class MutablePixelBuffer : PixelBuffer
    {
        GCHandle pinned;
        bool frozen;
        int locks;
        public int BackBufferStride => Stride;
        public IntPtr BackBuffer => locks > 0 ? pinned.AddrOfPinnedObject()
            : throw new InvalidOperationException("Lock the canvas before accessing its buffer.");

        public MutablePixelBuffer(int width, int height, double dpiX, double dpiY, PixelFormat format, BitmapPalette palette)
            : base(width, height, dpiX, dpiY, format, palette,
                new byte[checked(RowBytes(width, format) * height)], RowBytes(width, format)) { }

        public MutablePixelBuffer(PixelBuffer source)
            : base(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY,
                source.Format, source.Palette, source.Pixels.ToArray(), source.Stride) { }

        void EnsureMutable()
        {
            if (frozen) throw new InvalidOperationException("The canvas is frozen.");
        }

        public void Lock()
        {
            EnsureMutable();
            if (locks++ == 0) pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        }

        public void Unlock()
        {
            if (locks == 0) throw new InvalidOperationException("The canvas is not locked.");
            if (--locks == 0) pinned.Free();
        }

        public override void Freeze()
        {
            if (locks != 0) throw new InvalidOperationException("Unlock the canvas before freezing it.");
            frozen = true;
        }

        public void AddDirtyRect(PixelRect rect) => Validate(rect);

        void Validate(PixelRect rect)
        {
            EnsureMutable();
            if (rect.X < 0 || rect.Y < 0 || rect.Width < 0 || rect.Height < 0
                || rect.X > PixelWidth - rect.Width || rect.Y > PixelHeight - rect.Height)
                throw new ArgumentOutOfRangeException(nameof(rect));
        }

        public void WritePixels(PixelRect destination, Array source, int stride, int offset)
        {
            Validate(destination);
            int row = RowBytes(destination.Width, Format);
            if (!destination.HasArea) return;
            if (Format.BitsPerPixel < 8 || stride < row || offset < 0
                || Buffer.ByteLength(source) < checked(offset + (destination.Height - 1) * stride + row))
                throw new ArgumentException("Invalid source pixel region.");
            for (int y = 0; y < destination.Height; y++)
                Buffer.BlockCopy(source, offset + y * stride, pixels,
                    (destination.Y + y) * Stride + destination.X * Format.BitsPerPixel / 8, row);
        }

        public void WritePixels(PixelRect sourceRect, Array source, int stride, int x, int y)
        {
            if (sourceRect.X < 0 || sourceRect.Y < 0
                || RowBytes(checked(sourceRect.X + sourceRect.Width), Format) > stride)
                throw new ArgumentException("Invalid source pixel region.");
            WritePixels(new(x, y, sourceRect.Width, sourceRect.Height), source, stride,
                checked(sourceRect.Y * stride + sourceRect.X * Format.BitsPerPixel / 8));
        }

        ~MutablePixelBuffer()
        {
            if (pinned.IsAllocated) pinned.Free();
        }
    }
}
