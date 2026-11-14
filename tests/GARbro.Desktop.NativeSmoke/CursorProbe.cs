using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GARbro.Desktop;

// Test-only driver. Production controls continue requesting standard cursor
// types; this probe checks those requests in a real, isolated compositor.
static class CursorProbe
{
    public static async Task Check(MainWindow page)
    {
        var window=page.DesktopHost;window.WindowState=WindowState.Maximized;
        await Task.Delay(500);
        string command=Path.Combine(Program.Output,"cursor-command.json"),reply=Path.Combine(Program.Output,"cursor-reply.json");
        var cursors=new List<Cursor>();long sequence=0;
        Publish(reply,new {sequence,ready=true,targets=Targets(window),
            uiAssembly=typeof(MainWindow).Assembly.Location,backendAssembly=typeof(WaylandPlatformOptions).Assembly.Location,
            backendSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(WaylandPlatformOptions).Assembly.Location)))});
        var lifetime=(IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current.ApplicationLifetime;
        var deadline=DateTime.UtcNow.AddSeconds(120);
        try
        {
            while(DateTime.UtcNow<deadline)
            {
                await Task.Delay(20);
                if(!File.Exists(command))continue;
                using var request=JsonDocument.Parse(File.ReadAllText(command));var root=request.RootElement;
                long next=root.GetProperty("sequence").GetInt64();if(next<=sequence)continue;
                string mode=root.GetProperty("mode").GetString();
                switch(mode)
                {
                    case "cursor":
                        var type=Enum.Parse<StandardCursorType>(root.GetProperty("cursor").GetString());
                        var cursor=new Cursor(type);cursors.Add(cursor);window.Cursor=cursor;
                        break;
                    case "theme":
                        string language=root.GetProperty("language").GetString();
                        CaptionVisuals.Appearance(page,root.GetProperty("theme").GetString(),language);
                        break;
                    case "popup":
                        var menu=window.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="MenuFile");
                        menu.ContextMenu.Open(menu);
                        break;
                    case "close-popup":
                        window.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="MenuFile").ContextMenu.Close();
                        break;
                    case "settings":
                        // Exercise a second real native surface without making
                        // cursor tests depend on a complete format-catalog scan.
                        var settings=new SettingsWindow(new AppSettings(),UiPlatform.CurrentText,Array.Empty<GARbro.Application.ResourceFormatInfo>());
                        _=settings.ShowDialog<bool>(window);
                        settings.DesktopHost.WindowState=WindowState.Maximized;
                        break;
                    case "close-dialog":
                        foreach(var child in lifetime.Windows.Where(w=>w!=window).ToArray())child.Close();
                        break;
                    case "stop":
                        Publish(reply,new {sequence=next,stopped=true});
                        return;
                    default:throw new InvalidDataException("Unknown cursor-probe command.");
                }
                await Task.Delay(180);
                Window active=lifetime.Windows.LastOrDefault(w=>w.IsVisible&&w!=window)??window;
                sequence=next;
                Publish(reply,new {sequence,targets=Targets(active)});
            }
            throw new TimeoutException("Cursor compositor probe did not finish.");
        }
        finally {window.Cursor=null;foreach(var cursor in cursors)cursor.Dispose();}
    }
    static object Targets(Window window)
    {
        Point Center(Control control)=>control.TranslatePoint(new Point(control.Bounds.Width/2,control.Bounds.Height/2),window).Value;
        object Position(Point point)=>new {x=point.X,y=point.Y};
        var controls=window.GetVisualDescendants().OfType<Control>().ToArray();
        var captions=(window.GetVisualAncestors().LastOrDefault()??window).GetVisualDescendants().OfType<Button>()
            .Where(b=>b.Name is "PART_MinimizeButton" or "PART_MaximizeButton" or "PART_CloseButton")
            .ToDictionary(b=>b.Name,b=>Position(Center(b)));
        var search=controls.OfType<TextBox>().FirstOrDefault(c=>c.Name=="Search")??controls.OfType<TextBox>().FirstOrDefault(c=>c.IsEffectivelyVisible&&c.IsEnabled);
        var files=controls.OfType<ListBox>().FirstOrDefault(c=>c.Name=="Files");
        Point blank=files?.TranslatePoint(new Point(files.Bounds.Width/2,files.Bounds.Height-70),window)??new Point(window.Bounds.Width/2,window.Bounds.Height-30);
        var menu=controls.OfType<Button>().FirstOrDefault(c=>c.Name=="MenuFile");
        Point popup=menu?.TranslatePoint(new Point(15,menu.Bounds.Height+25),window)??blank;
        return new {width=window.Bounds.Width,height=window.Bounds.Height,blank=Position(blank),
            text=Position(search==null?blank:Center(search)),popup=Position(popup),captions};
    }
    static void Publish(string path,object value)
    {
        File.WriteAllText(path+".pending",JsonSerializer.Serialize(value));File.Move(path+".pending",path,true);
    }
}
