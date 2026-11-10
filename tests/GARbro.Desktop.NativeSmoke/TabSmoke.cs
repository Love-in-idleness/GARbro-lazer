using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GARbro.Desktop;

static class TabSmoke
{
    public static async Task Check(MainWindow window,IClassicDesktopStyleApplicationLifetime lifetime)
    {
        string resources=Path.Combine(Program.Output,"游戏资源"),runtime=Path.Combine(Program.Output,"runtime");Directory.CreateDirectory(resources);Directory.CreateDirectory(runtime);
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR",runtime);
        string picture=Path.Combine(resources,"背景_日本語.png");File.Copy(Path.Combine(Path.GetDirectoryName(Program.Fixture),"colors-rgba.png"),picture,true);
        using var instance=new DesktopInstance();
        if(!instance.IsPrimary)throw new InvalidOperationException("Tab smoke needs its own primary instance.");
        var router=new FileActivationRouter(window);router.Ready();
        instance.SetReceiver(paths=>Dispatcher.UIThread.Post(()=>router.Open(paths)));
        await window.LoadInputsAsync(new[]{Program.Fixture,resources});
        var tabs=window.GetVisualDescendants().OfType<ListBox>().Single(c=>c.Name=="ResourceTabs");
        if(tabs.ItemCount!=2)throw new InvalidDataException("Multiple inputs did not create tabs.");
        var start=new ProcessStartInfo("dotnet") {UseShellExecute=false};start.ArgumentList.Add(typeof(GARbro.Desktop.App).Assembly.Location);start.ArgumentList.Add(picture);
        using var secondary=Process.Start(start);await secondary.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        if(secondary.ExitCode!=0)throw new InvalidDataException("Secondary GUI did not forward its file.");
        var files=window.GetVisualDescendants().OfType<ListBox>().Single(c=>c.Name=="Files");
        for(int i=0;i<200 && (tabs.ItemCount!=3 || (files.SelectedItem as GARbro.Application.BrowserEntry)?.Name!=Path.GetFileName(picture));i++)await Task.Delay(25);
        if(tabs.ItemCount!=3 || lifetime.Windows.Count!=1)throw new InvalidDataException("Activation created another window or lost the file.");
        var menu=window.GetVisualDescendants().OfType<Button>().Single(c=>c.Name=="MenuFile").ContextMenu;
        menu.Items.OfType<MenuItem>().Single(c=>c.Header as string==UiPlatform.CurrentText["NewTab"]).Command.Execute(null);
        for(int i=0;i<100 && tabs.ItemCount!=4;i++)await Task.Delay(10);
        tabs.SelectedIndex=0;
        for(int i=0;i<200 && (window.ArchiveTag!="XFL" || window.VisibleEntryCount!=3);i++)await Task.Delay(25);
        if(window.ArchiveTag!="XFL" || window.VisibleEntryCount!=3)throw new InvalidDataException("Switching tabs lost the archive listing.");
        await Dispatcher.UIThread.InvokeAsync(()=>window.DesktopHost.UpdateLayout(),DispatcherPriority.Background);await Task.Delay(200);
        foreach(var (language,shortName,theme) in new[]{("zh-CN","zh","dark"),("en-US","en","light"),("ru-RU","ru","light"),("ja-JP","ja","dark")})
        {
            CaptionVisuals.Appearance(window,theme,language);
            await Dispatcher.UIThread.InvokeAsync(()=>window.DesktopHost.UpdateLayout(),DispatcherPriority.Background);await Task.Delay(150);
            var root=window.DesktopHost.GetVisualAncestors().LastOrDefault()??window.DesktopHost;
            using var bitmap=new RenderTargetBitmap(PixelSize.FromSize(root.Bounds.Size,window.DesktopHost.RenderScaling),new Vector(96*window.DesktopHost.RenderScaling,96*window.DesktopHost.RenderScaling));bitmap.Render(root);
            bitmap.Save(Path.Combine(Program.Output,$"tabs-{shortName}-{theme}.png"),new PngBitmapEncoderOptions());
        }
        File.WriteAllText(Path.Combine(Program.Output,"tabs.json"),JsonSerializer.Serialize(new {Tabs=tabs.ItemCount,Windows=lifetime.Windows.Count,ForwardedExitCode=secondary.ExitCode,SelectedArchive=window.ArchiveTag}));
        router.Stop();Console.WriteLine("Tabs and secondary-process forwarding passed: four tabs, one native window.");
    }
}
