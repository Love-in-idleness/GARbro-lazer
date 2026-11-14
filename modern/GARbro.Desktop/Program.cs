using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Platform.Storage;
using System.Linq;
using Avalonia.Threading;

namespace GARbro.Desktop
{
    internal static class Program
    {
        internal static DesktopInstance Instance;
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                using var instance=new DesktopInstance();
                if(!instance.IsPrimary)
                {
                    instance.ForwardAsync(FileActivationRouter.Normalize(args)).GetAwaiter().GetResult();
                    return 0;
                }
                Instance=instance;
                App.StartupArguments = (string[])args.Clone();
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch(Exception error){DesktopServices.Log(error);return 1;}
            finally {Instance=null;}
        }
        public static AppBuilder BuildAvaloniaApp() => DesktopPlatform.Configure(AppBuilder.Configure<App>()).LogToTrace();
    }

    public sealed class App : Avalonia.Application
    {
        internal static string[] StartupArguments;
        public override void Initialize() { DesktopPlatform.InitializeServices(); Styles.Add(new FluentTheme()); }
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var window = new MainWindow();
                var files = new FileActivationRouter(window);
                var activation = this.TryGetFeature<IActivatableLifetime>();
                bool closed=false;
                void ShowBrowser()
                {
                    if(closed)return;
                    window.DesktopHost.Show();
                    if(window.DesktopHost.WindowState==Avalonia.Controls.WindowState.Minimized)window.DesktopHost.WindowState=Avalonia.Controls.WindowState.Normal;
                    window.DesktopHost.Activate();
                }
                void Activated(object sender, ActivatedEventArgs args)
                {
                    if (args is FileActivatedEventArgs documents) files.Open(documents.Files.Select(item => item.TryGetLocalPath()));
                    if (args.Kind is ActivationKind.File or ActivationKind.Reopen) ShowBrowser();
                }
                if (activation != null) activation.Activated += Activated;
                Program.Instance?.SetReceiver(values=>Dispatcher.UIThread.Post(()=>
                {
                    if(closed)return;
                    ShowBrowser();files.Open(values);
                }));
                files.Open(StartupArguments ?? desktop.Args ?? Array.Empty<string>(), startup: true);
                window.Opened += (_, _) => files.Ready();
                window.Closed += (_, _) => { closed=true; files.Stop(); if (activation != null) activation.Activated -= Activated; };
                desktop.MainWindow = window.DesktopHost;
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
