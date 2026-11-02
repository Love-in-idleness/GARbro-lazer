using System;
using Avalonia;
using GARbro.Application;

namespace GARbro.Desktop
{
    public static class DesktopPlatform
    {
        public static string AppId => Environment.GetEnvironmentVariable("FLATPAK_ID") ?? "org.garbro.GARbro";

        public static void InitializeServices()
        {
            UiPlatform.Worker ??= new ManagedWorkerClient(GARbro.Worker.WorkerMarker.AssemblyPath);
            UiPlatform.Audio ??= path => new AudioPlayback(path);
            UiPlatform.Video ??= (artifact, frame, error, memory) => new VideoPlayback(artifact, frame, error, memory);
            UiPlatform.Storage ??= new DesktopStorageAccess();
            if (Environment.GetEnvironmentVariable("FLATPAK_ID") != null)
                UiPlatform.CanTrash ??= () => false;
        }

        public static AppBuilder Configure(AppBuilder builder)
        {
            InitializeServices();
            builder.UsePlatformDetect().WithInterFont()
                .With(new X11PlatformOptions {UseDBusFilePicker=true});
            if(OperatingSystem.IsLinux())
            {
                builder.With(new WaylandPlatformOptions {AppId=AppId});
                string backend=Environment.GetEnvironmentVariable("GARBRO_LINUX_BACKEND")??"auto";
                switch(backend.ToLowerInvariant())
                {
                    // The native backend binds zwp_text_input_v3 automatically
                    // when the compositor advertises its manager. TextBox supplies
                    // composition, surrounding text, selection and cursor geometry.
                    case "auto":builder.UseWaylandWithFallback();break;
                    case "wayland":builder.UseWayland();break;
                    case "x11":break;
                    default:throw new ArgumentException("GARBRO_LINUX_BACKEND must be auto, wayland or x11.");
                }
            }
            return builder;
        }
    }
}
