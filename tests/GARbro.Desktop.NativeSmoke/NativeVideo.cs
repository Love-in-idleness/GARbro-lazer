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
        play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var frames=new HashSet<ulong>();
        for(int i=0;i<24;i++)
        {
            await Task.Delay(70);
            var origin=image.TranslatePoint(default,page.DesktopHost)??throw new InvalidDataException("Video has no rendered position.");
            frames.Add(Sample(page.DesktopHost,new Point(origin.X+image.Bounds.Width/2,origin.Y+image.Bounds.Height/2)));
        }
        if(frames.Count<4)throw new InvalidDataException($"Native video did not animate: {frames.Count} distinct rendered regions.");
        File.WriteAllText(Path.Combine(Program.Output,"video-rendering.json"),System.Text.Json.JsonSerializer.Serialize(new {distinctRegions=frames.Count,captures=24,source="native drawable",excluded="all controls and labels"}));
        var root=page.DesktopHost.GetVisualAncestors().LastOrDefault()??page.DesktopHost;
        using(var snapshot=new Avalonia.Media.Imaging.RenderTargetBitmap(PixelSize.FromSize(root.Bounds.Size,page.DesktopHost.RenderScaling),new Vector(96*page.DesktopHost.RenderScaling,96*page.DesktopHost.RenderScaling)))
        {snapshot.Render(root);snapshot.Save(Path.Combine(Program.Output,"video-window.png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());}
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
            var dc=GetDC(handle);
            try {for(int y=-16;y<16;y+=2)for(int x=-32;x<32;x+=2)hash=unchecked((hash^GetPixel(dc,cx+x,cy+y))*1099511628211);}
            finally{ReleaseDC(handle,dc);}
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
    [DllImport("gdi32.dll")]static extern uint GetPixel(IntPtr dc,int x,int y);
    [DllImport("libX11.so.6")]static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")]static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")]static extern IntPtr XGetImage(IntPtr display,IntPtr drawable,int x,int y,uint width,uint height,nuint planeMask,int format);
    [DllImport("libX11.so.6")]static extern nuint XGetPixel(IntPtr image,int x,int y);
    [DllImport("libX11.so.6")]static extern int XDestroyImage(IntPtr image);
}
