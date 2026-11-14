using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GARbro.GUI;

static class WpfBench
{
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    static readonly Stopwatch startup=Stopwatch.StartNew();
    [STAThread] static int Main(string[] args)
    {
        string root=Path.GetFullPath(args[1]),scenario=args[2],output=Path.GetFullPath(args[3]);
        var app=new GARbro.GUI.App();app.InitializeComponent();app.StartupUri=new Uri("/GARbro.GUI;component/MainWindow.xaml",UriKind.Relative);app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        bool attached=false;
        EventManager.RegisterClassHandler(typeof(GARbro.GUI.MainWindow),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,ev)=>
        {
            if(attached)return;attached=true;
            var window=(GARbro.GUI.MainWindow)sender;window.Width=1280;window.Height=840;window.Left=40;window.Top=40;
            bool started=false;
            window.ContentRendered+=async delegate
            {
                if(started)return;started=true;
                int code=1;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};
                try
                {
                    await Present(window);double startupMs=startup.Elapsed.TotalMilliseconds;
                    var list=(ListView)window.FindName("CurrentDirectory");var image=(Image)window.FindName("ImageCanvas");
                    var open=typeof(GARbro.GUI.MainWindow).GetMethod("OpenFileOrDir",BindingFlags.NonPublic|BindingFlags.Instance);
                    var lag=new List<double>();var beat=Stopwatch.StartNew();timer.Tick+=delegate{lag.Add(Math.Max(0,beat.Elapsed.TotalMilliseconds-10));beat.Restart();};timer.Start();
                    if(scenario=="image"){open.Invoke(window,new object[]{Path.Combine(root,"images")});await Present(window);}
                    Func<string,Task> action=async suffix=>
                    {
                        if(scenario=="image")
                        {
                            object previous=image.Source;
                            list.SelectedItem=list.Items.Cast<EntryViewModel>().Single(e=>e.Name=="sample-"+suffix+".png");
                            var waiting=Stopwatch.StartNew();
                            while(image.Source==null || ReferenceEquals(image.Source,previous))
                            {if(waiting.Elapsed.TotalSeconds>30)throw new TimeoutException("Image did not appear.");await Task.Delay(1);}
                        }
                        else
                        {
                            string path=scenario=="archive"?Path.Combine(root,"sample-"+suffix+".xfl"):Path.Combine(root,"directory-"+suffix);
                            open.Invoke(window,new object[]{path});
                            if(scenario=="archive"&&!list.Items.Cast<EntryViewModel>().Any(e=>e.Name=="hello.txt"))throw new InvalidDataException("Archive result differs.");
                            if(scenario=="directory"&&list.Items.Cast<EntryViewModel>().Count(e=>!e.IsDirectory)!=10000)throw new InvalidDataException("Directory result differs.");
                        }
                        await Present(window);
                    };
                    var clock=Stopwatch.StartNew();await action("a");double first=clock.Elapsed.TotalMilliseconds;
                    var warm=new List<double>();
                    for(int i=0;i<7;i++){await action("b");clock.Restart();await action("a");warm.Add(clock.Elapsed.TotalMilliseconds);}
                    timer.Stop();lag.Sort();
                    var report=new{framework="WPF",version=typeof(GARbro.GUI.MainWindow).Assembly.GetName().Version.ToString(),scenario=scenario,startupMs=startupMs,firstMs=first,warmMs=warm,uiDelayP95Ms=lag.Count==0?0:lag[(int)((lag.Count-1)*.95)],uiDelayMaxMs=lag.Count==0?0:lag[lag.Count-1]};
                    File.WriteAllText(output,Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));code=0;
                }
                catch(Exception error){File.WriteAllText(output+".error.txt",error.ToString());Console.Error.WriteLine(error);}
                finally{timer.Stop();window.Close();app.Shutdown(code);}
            };
        }));
        return app.Run();
    }
    static async Task Present(Window window)
    {
        await window.Dispatcher.InvokeAsync(()=>window.UpdateLayout(),DispatcherPriority.ApplicationIdle);
        await Task.Delay(17);DwmFlush();
    }
}
