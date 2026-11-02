using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using GARbro.Desktop;

static class SharedFrame
{
    static bool Applies => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();
    static Visual Root(Window window) => window.GetVisualAncestors().LastOrDefault() ?? window;
    static Button Caption(Window window,string part) => Root(window).GetVisualDescendants().OfType<Button>().Single(b=>b.Name==part);
    public static void Check(MainWindow page)
    {
        if(!Applies)return;
        var window=page.DesktopHost;var root=Root(window);
        double previous=-1,bottom=0;
        foreach(string name in new[]{"PART_MinimizeButton","PART_MaximizeButton","PART_CloseButton"})
        {
            var button=Caption(window,name);
            var origin=button.TranslatePoint(default,root)??throw new InvalidDataException("Caption button has no position.");
            if(!button.IsEffectivelyVisible||button.Bounds.Width<20||button.Bounds.Height<20||origin.X<=previous||origin.Y<0)
                throw new InvalidDataException("Shared caption buttons are hidden, clipped or out of order: "+name);
            previous=origin.X;bottom=Math.Max(bottom,origin.Y+button.Bounds.Height);
            if(origin.X+button.Bounds.Width>root.Bounds.Width+1)throw new InvalidDataException("Caption button is outside the window.");
        }
        var menus=page.GetLogicalDescendants().OfType<StackPanel>().Single(c=>c.Name=="IconMenus");
        var menuPosition=menus.TranslatePoint(default,root)??throw new InvalidDataException("Menus have no position.");
        if(menuPosition.Y<bottom)throw new InvalidDataException("The application menus overlap the caption buttons.");
        var text=UiPlatform.CurrentText;string language=text.Language;
        try
        {
            foreach(string locale in new[]{"en-US","zh-CN"})
            {
                text.Change(locale);
                foreach(var pair in new[]{("PART_MinimizeButton","Minimize"),("PART_MaximizeButton","Maximize"),("PART_CloseButton","Close")})
                    if(ToolTip.GetTip(Caption(window,pair.Item1))as string!=text[pair.Item2])throw new InvalidDataException("Caption translation did not refresh: "+pair.Item2);
            }
        }
        finally{text.Change(language);}
        using var image=new RenderTargetBitmap(PixelSize.FromSize(root.Bounds.Size,window.RenderScaling),new Vector(96*window.RenderScaling,96*window.RenderScaling));
        image.Render(root);image.Save(Path.Combine(Program.Output,"window-with-titlebar.png"),new PngBitmapEncoderOptions());
        Console.WriteLine("Shared minimize/maximize/close controls are visible above the application menus.");
    }
    public static async Task CheckActions(MainWindow page,bool verifyCompositorState)
    {
        if(!Applies)return;
        var window=page.DesktopHost;
        var maximize=Caption(window,"PART_MaximizeButton");
        if(maximize.IsEnabled)
        {
            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(verifyCompositorState)
            {
                await WaitForState(WindowState.Maximized);
                maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForState(WindowState.Normal);
            }
            else window.WindowState=WindowState.Normal;
        }
        var minimize=Caption(window,"PART_MinimizeButton");
        if(minimize.IsEnabled)
        {
            minimize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(verifyCompositorState)await WaitForState(WindowState.Minimized);
            window.WindowState=WindowState.Normal;window.Activate();
            if(verifyCompositorState)await WaitForState(WindowState.Normal);
        }
        await Task.Delay(100);
        bool closed=false;window.Closed+=(_,_)=>closed=true;
        Caption(window,"PART_CloseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(!closed)throw new InvalidDataException("Close caption action failed.");
        Console.WriteLine(verifyCompositorState ? "Shared caption actions and native state transitions passed." : "Shared caption action requests passed; Wayland protocol is verified by the compositor harness.");
        async Task WaitForState(WindowState expected)
        {
            for(int i=0;i<120&&window.WindowState!=expected;i++)await Task.Delay(25);
            if(window.WindowState!=expected)throw new InvalidDataException($"Caption action expected {expected}; compositor reported {window.WindowState}.");
        }
    }
}
