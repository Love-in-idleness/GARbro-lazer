using System;
using System.Threading.Tasks;
using GARbro.Application;

namespace GARbro.Desktop;

public static class UiPlatform
{
    public static bool IsMobile { get; set; }
    public static IWorkerClient Worker { get; set; }
    public static IStorageAccess Storage { get; set; }
    public static Func<string, IAudioOutput> Audio { get; set; }
    public static Func<VideoArtifact, Func<byte[], int, int, Task>, Action<Exception>, long, IVideoOutput> Video { get; set; }
    public static Action<string> OpenUrl { get; set; }
    public static Action<string> Explore { get; set; }
    public static Func<string, Task> Trash { get; set; }
    public static Func<bool> CanTrash { get; set; }
    public static Func<string, bool> CanExplore { get; set; }
    public static Action RequestAllFilesAccess { get; set; }
    public static Action RequestNotificationPermission { get; set; }
    public static Func<Task> ClearStorageCache { get; set; }
    public static Func<string, uint?> DynamicColor { get; set; }
    public static PageNavigation Navigation { get; set; }
    public static AppWindow ActivePage { get; set; }
    public static string DataDirectory { get; set; }
    public static string SettingsPath { get; set; }
    public static DesktopText CurrentText { get; set; } = new("en-US");
}
