using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using GARbro.Desktop;

/// <summary>Read actual native drawable pixels; labels and playback controls are outside the sampled region.</summary>
static class NativeVideo
{
    public static async Task Check(MainWindow page)
    {
        if(OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("GARBRO_NATIVE_VIDEO")!="1")return;
        if(!OperatingSystem.IsWindows() && page.DesktopHost.TryGetPlatformHandle()?.HandleDescriptor!="XID")return;
        string examples=Path.Combine(Program.Output,"Example resources");Directory.CreateDirectory(examples);
        foreach(var pair in new[]{("movie.mpg","Movie_end01.mpg"),("colors-rgb.png","Background.png"),("tone.ogg","Theme.ogg"),("silent.mp4","Opening.mp4")})
            File.Copy(Path.Combine(Path.GetDirectoryName(Program.Fixture),pair.Item1),Path.Combine(examples,pair.Item2));
        File.WriteAllText(Path.Combine(examples,"Scene.txt"),"Welcome to GARbro!");Directory.CreateDirectory(Path.Combine(examples,"Scripts"));
        await page.LoadArchiveAsync(Path.Combine(examples,"Movie_end01.mpg"));
        var image=page.GetLogicalDescendants().OfType<Image>().Single(i=>i.Name=="PreviewImage");
        for(int i=0;i<200&&image.Source==null;i++)await Task.Delay(25);
        if(image.Source==null)throw new InvalidDataException("Video poster did not appear.");
        var play=page.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string==UiPlatform.CurrentText["Play"]);
        play.Command.Execute(null);
        var ready=System.Diagnostics.Stopwatch.StartNew();
        while(image.Source is not Avalonia.Media.Imaging.WriteableBitmap && ready.Elapsed.TotalSeconds<15)await Task.Delay(25);
        if(image.Source is not Avalonia.Media.Imaging.WriteableBitmap)throw new InvalidDataException("Video playback did not produce a first frame.");
        double firstFrameMs=ready.Elapsed.TotalMilliseconds;
        await Task.Delay(100);
        var frames=new HashSet<ulong>();var decoded=new HashSet<ulong>();
        for(int i=0;i<24;i++)
        {
            await Task.Delay(70);
            var origin=image.TranslatePoint(default,page.DesktopHost)??throw new InvalidDataException("Video has no rendered position.");
            frames.Add(Sample(page.DesktopHost,new Point(origin.X+image.Bounds.Width/2,origin.Y+image.Bounds.Height/2)));
            if(image.Source is Avalonia.Media.Imaging.Bitmap pixels)
            {
                int stride=pixels.PixelSize.Width*4;var bytes=new byte[stride*pixels.PixelSize.Height];var pinned=GCHandle.Alloc(bytes,GCHandleType.Pinned);
                try{pixels.CopyPixels(new PixelRect(pixels.PixelSize),pinned.AddrOfPinnedObject(),bytes.Length,stride);}finally{pinned.Free();}
                ulong hash=14695981039346656037;foreach(byte value in bytes)hash=unchecked((hash^value)*1099511628211);decoded.Add(hash);
            }
        }
        File.WriteAllText(Path.Combine(Program.Output,"video-rendering.json"),System.Text.Json.JsonSerializer.Serialize(new {distinctRegions=frames.Count,decodedFrames=decoded.Count,firstFrameMs,captures=24,source="native drawable",excluded="all controls and labels",imageBounds=image.Bounds.ToString()}));
        var root=page.DesktopHost.GetVisualAncestors().LastOrDefault()??page.DesktopHost;
        using(var snapshot=new Avalonia.Media.Imaging.RenderTargetBitmap(PixelSize.FromSize(root.Bounds.Size,page.DesktopHost.RenderScaling),new Vector(96*page.DesktopHost.RenderScaling,96*page.DesktopHost.RenderScaling)))
        {snapshot.Render(root);snapshot.Save(Path.Combine(Program.Output,"video-window.png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());}
        if(frames.Count<4)throw new InvalidDataException($"Native video did not animate: {frames.Count} distinct rendered regions; {decoded.Count} decoded frames.");
        page.StopPreviewAudio();
        await page.LoadArchiveAsync(Program.Fixture);
        Console.WriteLine($"Continuous video: {frames.Count} distinct native pixel regions.");
    }
    static ulong Sample(Window window,Point center)
    {
        double scale=window.RenderScaling;int cx=(int)(center.X*scale),cy=(int)(center.Y*scale);ulong hash=14695981039346656037;
        var handle=window.TryGetPlatformHandle().Handle;
        if(OperatingSystem.IsWindows())
        {
            WindowsFrame.VerifyVisibleAt(window,center);
            // Desktop DC includes the compositor's presented pixels, including
            // hardware-accelerated/layered windows. The window DC can be stale.
            var screen=window.PointToScreen(center);cx=screen.X;cy=screen.Y;
            var dc=GetDC(IntPtr.Zero);var copy=CreateCompatibleDC(dc);var bitmap=CreateCompatibleBitmap(dc,64,32);IntPtr previous=IntPtr.Zero;
            try
            {
                previous=SelectObject(copy,bitmap);
                if(!BitBlt(copy,0,0,64,32,dc,cx-32,cy-16,0x40CC0020))throw new IOException("Cannot capture the desktop video area.");
                SelectObject(copy,previous);previous=IntPtr.Zero;
                var info=new BitmapInfo {Size=40,Width=64,Height=-32,Planes=1,Bits=32};var pixels=new byte[64*32*4];
                if(GetDIBits(dc,bitmap,0,32,pixels,ref info,0)!=32)throw new IOException("Cannot read the desktop video area.");
                for(int i=0;i<pixels.Length;i+=4)hash=unchecked((hash^(uint)(pixels[i]|pixels[i+1]<<8|pixels[i+2]<<16))*1099511628211);
            }
            finally {if(previous!=IntPtr.Zero)SelectObject(copy,previous);DeleteObject(bitmap);DeleteDC(copy);ReleaseDC(IntPtr.Zero,dc);}
        }
        else
        {
            var display=XOpenDisplay(IntPtr.Zero);if(display==IntPtr.Zero)throw new IOException("Cannot capture X11 video.");
            try
            {
                var pixels=XGetImage(display,handle,cx-32,cy-16,64,32,nuint.MaxValue,2);
                if(pixels==IntPtr.Zero)throw new IOException("Cannot capture the native video drawable.");
                try {for(int y=0;y<32;y+=2)for(int x=0;x<64;x+=2)hash=unchecked((hash^(ulong)XGetPixel(pixels,x,y))*1099511628211);}
                finally{XDestroyImage(pixels);}
            }
            finally{XCloseDisplay(display);}
        }
        return hash;
    }
    [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [StructLayout(LayoutKind.Sequential)]struct BitmapInfo {public uint Size;public int Width,Height;public ushort Planes,Bits;public uint Compression,ImageSize;public int Xppm,Yppm;public uint Used,Important,Color;}
    [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleBitmap(IntPtr dc,int width,int height);
    [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr value);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")]static extern bool BitBlt(IntPtr target,int x,int y,int width,int height,IntPtr source,int sx,int sy,uint operation);
    [DllImport("gdi32.dll")]static extern int GetDIBits(IntPtr dc,IntPtr bitmap,uint start,uint count,byte[] pixels,ref BitmapInfo info,uint usage);
    [DllImport("libX11.so.6")]static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")]static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")]static extern IntPtr XGetImage(IntPtr display,IntPtr drawable,int x,int y,uint width,uint height,nuint planeMask,int format);
    [DllImport("libX11.so.6")]static extern nuint XGetPixel(IntPtr image,int x,int y);
    [DllImport("libX11.so.6")]static extern int XDestroyImage(IntPtr image);
}
