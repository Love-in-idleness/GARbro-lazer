using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using GARbro.Desktop;

public static class Program
{
    internal static string Fixture, Output;
    internal static bool TestIme,TestCursor;
    [STAThread]
    public static int Main(string[] args)
    {
        System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(Console.Error));
        System.Diagnostics.Trace.AutoFlush = true;
        if (args.Length is <2 or >3) throw new ArgumentException("Expected fixture path, output directory and optional --wayland-ime or --wayland-cursor.");
        Fixture=Path.GetFullPath(args[0]); Output=Path.GetFullPath(args[1]);
        TestIme=args.Length==3 && args[2]=="--wayland-ime";
        TestCursor=args.Length==3 && args[2]=="--wayland-cursor";
        return DesktopPlatform.Configure(AppBuilder.Configure<NativeSmokeApp>())
            .LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}

public sealed class NativeSmokeApp : Avalonia.Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime=(IClassicDesktopStyleApplicationLifetime)ApplicationLifetime;
        lifetime.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        Directory.CreateDirectory(Program.Output);
        var store=new SettingsStore(Path.Combine(Program.Output,"legacy-settings.json"));
        var settingType=typeof(GameRes.Formats.Valkyria.OdnOpener);
        store.Save(new AppSettings { Language=Environment.GetEnvironmentVariable("GARBRO_SMOKE_LANGUAGE")??"auto", Theme=Environment.GetEnvironmentVariable("GARBRO_SMOKE_THEME")??"system", FormatSettings=new()
        {
            [settingType.Assembly.GetName().Name+":"+settingType.FullName+"/ODNAudioSampleRate"]=JsonSerializer.SerializeToElement("44100")
        }});
        var window=new MainWindow(store);
        // Hosted Windows ARM images can display an always-on-top OOBE overlay.
        // Native pixel tests need the test window visible on the actual desktop.
        if(OperatingSystem.IsWindows())window.DesktopHost.Topmost=true;
        lifetime.MainWindow=window.DesktopHost;
        window.Opened+=async (_,_)=>
        {
            int code=1;
            try
            {
                window.DesktopHost.Activate();
                await window.LoadInputsAsync(new[]{Path.GetDirectoryName(Program.Fixture)});
                if (window.ArchiveTag!="directory")
                    throw new InvalidDataException("Saved format settings blocked folder browsing.");
                await window.LoadArchiveAsync(Program.Fixture);
                if (window.ArchiveTag!="XFL" || window.VisibleEntryCount!=3)
                    throw new InvalidDataException("Native window did not list the fixture.");
                if(Program.TestCursor)
                {
                    await CursorProbe.Check(window);
                    code=0;
                    return;
                }
                // Let the native window complete its layout/render pass.
                await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
                await Task.Delay(150);
                SharedFrame.Check(window);
                WindowsFrame.Check(window.DesktopHost);
                await CaptionVisuals.Check(window,store.Load());
                await LocalizationSnapshots.Check(window);
                Directory.CreateDirectory(Program.Output);
                if(Program.TestIme)
                {
                    await CheckImeAsync(window);
                    await Dispatcher.UIThread.InvokeAsync(()=>window.UpdateLayout(),DispatcherPriority.Background);
                }
                using (var image=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height)))
                {
                    image.Render(window);
                    image.Save(Path.Combine(Program.Output,"window.png"),new PngBitmapEncoderOptions());
                }
                string extracted=Path.Combine(Program.Output,"extracted");
                await window.ExtractToAsync(extracted);
                if (File.ReadAllText(Path.Combine(extracted,"hello.txt"))!="Hello, GARbro!\n"
                    || !File.Exists(Path.Combine(extracted,"sub","日本語.bin")))
                    throw new InvalidDataException("Native window extraction differs.");
                Console.WriteLine("Native desktop window rendered, listed and extracted the fixture.");
                // Sway intentionally ignores maximize requests. Its harness checks
                // actual protocol requests; Openbox/Windows check native states too.
                await NativePerformance.Check(window);
                await NativeVideo.Check(window);
                await SharedFrame.CheckActions(window,!Program.TestIme);
                code=0;
            }
            catch(Exception error) { Console.Error.WriteLine(error); }
            finally { lifetime.Shutdown(code); }
        };
        base.OnFrameworkInitializationCompleted();
    }

    static async Task CheckImeAsync(MainWindow window)
    {
        var controls=window.GetLogicalDescendants().ToArray();
        controls.OfType<Expander>().Single().IsExpanded=true;
        await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
        var search=controls.OfType<TextBox>().Single(c=>c.Name=="Search");
        var scheme=controls.OfType<TextBox>().Single(c=>c.Name=="Scheme");
        search.Text="";scheme.Text="";search.Focus();
        int phase=0;
        var finished=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(30)};
        void Mark(string name)=>File.WriteAllText(Path.Combine(Program.Output,name),"ok\n");
        timer.Tick+=(_,_)=>
        {
            try
            {
                string preedit=search.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault()?.PreeditText;
                if(phase==0 && preedit=="ni")
                {
                    if(!string.IsNullOrEmpty(search.Text))throw new InvalidDataException("Preedit was committed prematurely.");
                    Mark("preedit-ok");phase=1;
                }
                else if(phase==1 && search.Text=="你好") {Mark("commit-ok");phase=2;}
                else if(phase==2 && search.Text=="你世界")
                {
                    Mark("delete-ok");scheme.Focus();phase=3;
                }
                else if(phase==3 && scheme.Text=="かな")
                {
                    if(search.Text!="你世界")throw new InvalidDataException("IME commit reached the previous field.");
                    Mark("focus-ok");finished.TrySetResult(true);
                }
            }
            catch(Exception error){finished.TrySetException(error);}
        };
        timer.Start();
        try {await finished.Task.WaitAsync(TimeSpan.FromSeconds(25));}
        catch(TimeoutException) {throw new InvalidDataException($"Wayland IME timed out at phase {phase}: search={search.Text}, scheme={scheme.Text}.");}
        finally {timer.Stop();}
        search.Text="";scheme.Text="none";
        Console.WriteLine("Wayland text-input-v3 composition, commit, surrounding deletion and focus passed.");
    }
}
