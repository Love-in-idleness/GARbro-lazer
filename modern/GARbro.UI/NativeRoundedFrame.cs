using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace GARbro.Desktop;

/// <summary>Match the shared alpha clip on systems without composited window transparency.</summary>
internal static class NativeRoundedFrame
{
    public static void Apply(Window window,double logicalRadius)
    {
        if(OperatingSystem.IsMacOS())return;
        var handle=window.TryGetPlatformHandle();
        if(handle==null || handle.Handle==IntPtr.Zero)return;
        int width=(int)Math.Ceiling(window.ClientSize.Width*window.RenderScaling),height=(int)Math.Ceiling(window.ClientSize.Height*window.RenderScaling);
        int radius=(int)Math.Round(logicalRadius*window.RenderScaling);
        if(width<=0||height<=0)return;
        if(OperatingSystem.IsWindows())
        {
            if(radius==0) {SetWindowRgn(handle.Handle,IntPtr.Zero,true);return;}
            if(!GetWindowRect(handle.Handle,out var rect))return;
            var region=CreateRoundRectRgn(0,0,rect.Right-rect.Left+1,rect.Bottom-rect.Top+1,radius*2,radius*2);
            if(region!=IntPtr.Zero && SetWindowRgn(handle.Handle,region,true)==0)DeleteObject(region);
        }
        else if(OperatingSystem.IsLinux() && handle.HandleDescriptor=="XID")
        {
            var display=XOpenDisplay(IntPtr.Zero);if(display==IntPtr.Zero)return;
            try
            {
                var region=XCreateRegion();
                try
                {
                    // The same circle as RectangleGeometry, in device pixels.
                    for(int y=0;y<height;y++)
                    {
                        double dy=y<radius?radius-y-.5:y>=height-radius?y-(height-radius)+.5:0;
                        int inset=dy>0?(int)Math.Ceiling(radius-Math.Sqrt(Math.Max(0,radius*radius-dy*dy))):0;
                        var row=new XRectangle {X=(short)inset,Y=(short)y,Width=(ushort)Math.Max(0,width-2*inset),Height=1};
                        XUnionRectWithRegion(ref row,region,region);
                    }
                    XShapeCombineRegion(display,handle.Handle,0,0,0,region,0);XFlush(display);
                }
                finally{XDestroyRegion(region);}
            }
            finally{XCloseDisplay(display);}
        }
    }
    [StructLayout(LayoutKind.Sequential)]struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct XRectangle {public short X,Y;public ushort Width,Height;}
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("gdi32.dll")]static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
    [DllImport("user32.dll")]static extern int SetWindowRgn(IntPtr window,IntPtr region,bool redraw);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr value);
    [DllImport("libX11.so.6")]static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")]static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")]static extern IntPtr XCreateRegion();
    [DllImport("libX11.so.6")]static extern int XDestroyRegion(IntPtr region);
    [DllImport("libX11.so.6")]static extern int XUnionRectWithRegion(ref XRectangle rectangle,IntPtr source,IntPtr target);
    [DllImport("libX11.so.6")]static extern int XFlush(IntPtr display);
    [DllImport("libXext.so.6")]static extern void XShapeCombineRegion(IntPtr display,IntPtr window,int kind,int x,int y,IntPtr region,int operation);
}
