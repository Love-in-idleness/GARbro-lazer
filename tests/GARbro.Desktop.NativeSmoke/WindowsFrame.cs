using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

// Check the real HWND, rather than only the managed decoration preference.
static class WindowsFrame
{
    public static void Check(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        var hwnd=window.TryGetPlatformHandle()?.Handle??IntPtr.Zero;
        if(hwnd==IntPtr.Zero)throw new InvalidDataException("The desktop HWND is missing.");
        long style=GetWindowLongW(hwnd,-16);
        const long caption=0x00C00000,systemMenu=0x00080000,minimize=0x00020000,maximize=0x00010000;
        long expected=caption|systemMenu|minimize|maximize;
        if((style&expected)!=expected)throw new InvalidDataException("The native title bar is missing window controls.");
        Point origin=default;
        if(!GetWindowRect(hwnd,out var outer)||!ClientToScreen(hwnd,ref origin))throw new InvalidDataException("Cannot inspect the native window frame.");
        VerifyVisibleAt(window,new Avalonia.Point(window.Bounds.Width/2,window.Bounds.Height/2));
        var capture=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true};
        foreach(string argument in new[]{"-NoProfile","-NonInteractive","-File",Path.GetFullPath("tools/capture-window.ps1"),
            "-X",outer.Left.ToString(),"-Y",outer.Top.ToString(),"-Width",(outer.Right-outer.Left).ToString(),
            "-Height",(outer.Bottom-outer.Top).ToString(),"-Output",Path.Combine(Program.Output,"window-with-titlebar.png")})capture.ArgumentList.Add(argument);
        using var process=Process.Start(capture);
        if(!process.WaitForExit(10000)||process.ExitCode!=0)throw new InvalidDataException("Cannot capture the native window title bar.");
        var work=window.Screens.ScreenFromWindow(window).WorkingArea;
        Console.WriteLine($"Native window bounds: {outer.Left},{outer.Top}–{outer.Right},{outer.Bottom}; work area: {work}; scaling: {window.RenderScaling}.");
        if(outer.Top<work.Y||outer.Left<work.X||outer.Right>work.Right||outer.Bottom>work.Bottom)
            throw new InvalidDataException("The window frame or caption is outside the screen work area.");
        Console.WriteLine("Native Windows frame remains inside the desktop work area.");
    }
    public static void VerifyVisibleAt(Window window,Avalonia.Point point)
    {
        if(!OperatingSystem.IsWindows())return;
        var screen=window.PointToScreen(point);
        var visible=GetAncestor(WindowFromPoint(new Point {X=screen.X,Y=screen.Y}),2);
        if(visible!=window.TryGetPlatformHandle()?.Handle)
            throw new InvalidDataException("The native capture point is covered by another window.");
    }
    [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
    [DllImport("user32.dll")]static extern int GetWindowLongW(IntPtr window,int index);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr window,ref Point point);
    [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
}
