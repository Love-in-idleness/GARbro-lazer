using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using GARbro.Application;
using GARbro.Desktop;

static class Program
{
    internal static string Root,Scenario,Output;
    internal static readonly Stopwatch Startup=Stopwatch.StartNew();
    [STAThread] public static int Main(string[] args)
    {
        Root=Path.GetFullPath(args[0]);Scenario=args[1];Output=Path.GetFullPath(args[2]);
        return DesktopPlatform.Configure(AppBuilder.Configure<BenchApp>()).StartWithClassicDesktopLifetime(args);
    }
}
sealed class BenchApp : Avalonia.Application
{
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    public override void Initialize()=>Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var desktop=(IClassicDesktopStyleApplicationLifetime)ApplicationLifetime;desktop.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var window=new MainWindow(new SettingsStore(Program.Output+".settings.json"));window.Width=1280;window.Height=840;desktop.MainWindow=window.DesktopHost;
        window.Opened+=async(_,_)=>
        {
            int code=1;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};
            try
            {
                await Present();double startup=Program.Startup.Elapsed.TotalMilliseconds;
                var lag=new List<double>();var beat=Stopwatch.StartNew();timer.Tick+=(_,_)=>{lag.Add(Math.Max(0,beat.Elapsed.TotalMilliseconds-10));beat.Restart();};timer.Start();
                var list=window.GetLogicalDescendants().OfType<ListBox>().Single(x=>x.Name=="Files");
                var image=window.GetLogicalDescendants().OfType<Image>().Single(x=>x.Name=="PreviewImage");
                if(Program.Scenario=="image")await window.LoadArchiveAsync(Path.Combine(Program.Root,"images"));
                async Task Action(string suffix)
                {
                    if(Program.Scenario=="image")
                    {
                        object previous=image.Source;
                        list.SelectedItem=list.Items.OfType<BrowserEntry>().Single(e=>e.Name=="sample-"+suffix+".png");
                        var waiting=Stopwatch.StartNew();
                        while(image.Source==null || ReferenceEquals(image.Source,previous))
                        {if(waiting.Elapsed.TotalSeconds>30)throw new TimeoutException("Image did not appear.");await Task.Delay(1);}
                    }
                    else
                    {
                        string path=Program.Scenario=="archive"?Path.Combine(Program.Root,"sample-"+suffix+".xfl"):Path.Combine(Program.Root,"directory-"+suffix);
                        await window.LoadArchiveAsync(path);
                        if(Program.Scenario=="archive" && !list.Items.OfType<BrowserEntry>().Any(e=>e.Name=="hello.txt"))throw new InvalidDataException("Archive result differs.");
                        if(Program.Scenario=="directory" && list.Items.OfType<BrowserEntry>().Count(e=>!e.IsDirectory)!=10000)throw new InvalidDataException("Directory result differs.");
                    }
                    await Present();
                }
                var clock=Stopwatch.StartNew();await Action("a");double first=clock.Elapsed.TotalMilliseconds;
                var warm=new List<double>();
                for(int i=0;i<7;i++){await Action("b");clock.Restart();await Action("a");warm.Add(clock.Elapsed.TotalMilliseconds);}
                timer.Stop();lag.Sort();
                var report=new{framework="Avalonia",version=DesktopServices.Version,scenario=Program.Scenario,startupMs=startup,firstMs=first,warmMs=warm,uiDelayP95Ms=lag.Count==0?0:lag[(int)((lag.Count-1)*.95)],uiDelayMaxMs=lag.Count==0?0:lag[^1]};
                File.WriteAllText(Program.Output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));code=0;
            }
            catch(Exception error){File.WriteAllText(Program.Output+".error.txt",error.ToString());Console.Error.WriteLine(error);}
            finally{timer.Stop();window.Close();desktop.Shutdown(code);}
            async Task Present()
            {
                await Dispatcher.UIThread.InvokeAsync(()=>window.UpdateLayout(),DispatcherPriority.Background);
                await Task.Delay(17);if(OperatingSystem.IsWindows())DwmFlush();
            }
        };
        base.OnFrameworkInitializationCompleted();
    }
}
