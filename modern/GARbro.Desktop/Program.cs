using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Platform.Storage;
using System.Linq;

namespace GARbro.Desktop
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            App.StartupArguments = (string[])args.Clone();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
                void Activated(object sender, ActivatedEventArgs args)
                {
                    if (args is FileActivatedEventArgs documents) files.Open(documents.Files.Select(item => item.TryGetLocalPath()));
                    if (args.Kind is ActivationKind.File or ActivationKind.Reopen) { window.DesktopHost.Show(); window.DesktopHost.Activate(); }
                }
                if (activation != null) activation.Activated += Activated;
                files.Open(StartupArguments ?? desktop.Args ?? Array.Empty<string>(), startup: true);
                window.Opened += (_, _) => files.Ready();
                window.Closed += (_, _) => { files.Stop(); if (activation != null) activation.Activated -= Activated; };
                desktop.MainWindow = window.DesktopHost;
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
