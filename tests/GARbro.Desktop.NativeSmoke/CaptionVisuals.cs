using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using GARbro.Desktop;

// The native cursor is moved only on the disposable Windows CI desktop.
// KDE captures the same shared controls through its compositor test harness.
static class CaptionVisuals
{
    public static async Task Check(MainWindow page, AppSettings original)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GARBRO_CAPTION_SNAPSHOTS")!="1") return;
        var window=page.DesktopHost;
        var directory=Path.Combine(Program.Output,"captions");Directory.CreateDirectory(directory);
        try
        {
            foreach(var (theme,language) in new[]{("dark","zh-CN"),("light","en-US")})
            {
                Appearance(page,theme,language);
                window.WindowState=WindowState.Normal;
                await Task.Delay(150);
                foreach(string name in new[]{"PART_MinimizeButton","PART_MaximizeButton","PART_CloseButton"})
                {
                    var root=window.GetVisualAncestors().LastOrDefault()??window;
                    var button=root.GetVisualDescendants().OfType<Button>().Single(b=>b.Name==name);
                    var center=button.TranslatePoint(new Point(button.Bounds.Width/2,button.Bounds.Height/2),window).Value;
                    WindowsFrame.VerifyVisibleAt(window,center);
                    var screen=window.PointToScreen(center);SetCursorPos(screen.X,screen.Y);
                    await Task.Delay(120);
                    using var bitmap=new RenderTargetBitmap(PixelSize.FromSize(root.Bounds.Size,window.RenderScaling),new Vector(96*window.RenderScaling,96*window.RenderScaling));
                    bitmap.Render(root);
                    bitmap.Save(Path.Combine(directory,$"{theme}-{language}-{name}-hover.png"),new PngBitmapEncoderOptions());
                }
            }
        }
        finally {Appearance(page,original.Theme,original.Language,original.DynamicColors);}
        Console.WriteLine("Shared caption hover screenshots saved on the native Windows backend.");
    }
    internal static void Appearance(MainWindow page,string theme,string language,bool dynamicColors=false)
    {
        var settings=(AppSettings)typeof(MainWindow).GetField("Settings",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(page);
        settings.Theme=theme;settings.Language=language;settings.DynamicColors=dynamicColors;
        settings.Validate();
        typeof(MainWindow).GetMethod("ApplySettings",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(page,new object[]{false});
    }
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
}
