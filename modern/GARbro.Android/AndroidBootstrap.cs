using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using global::Android.Content;
using global::Android.OS;
using GARbro.Desktop;
using GARbro.Imaging;
using GameRes;

namespace GARbro.Mobile;

public static class AndroidBootstrap
{
    static bool initialized;
    public static AndroidStorageAccess Storage { get; private set; }
    public static void Initialize(Context context)
    {
        if (initialized) return;
        string files = context.FilesDir.AbsolutePath;
        string database = Path.Combine(files, "GameData"); Directory.CreateDirectory(database);
        string stamp = Path.Combine(database, ".package-version");
        string version = context.PackageManager.GetPackageInfo(context.PackageName, 0).LastUpdateTime.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!File.Exists(stamp) || File.ReadAllText(stamp) != version)
        {
            foreach (string name in context.Assets.List("GameData"))
            {
                string target = Path.Combine(database, name);
                using var input = context.Assets.Open("GameData/" + name);
                string temporary = target + "." + Guid.NewGuid().ToString("N");
                try
                {
                    using (var output = File.Create(temporary)) input.CopyTo(output);
                    File.Move(temporary, target, true);
                }
                finally { File.Delete(temporary); }
            }
            string temporaryStamp = stamp + ".tmp"; File.WriteAllText(temporaryStamp, version); File.Move(temporaryStamp, stamp, true);
        }
        RasterCodec.RegisterBackend(new PortableRasterCodec());
        FormatCatalog.ConfigureProviders(new[] { Assembly.Load("ArcFormats.Portable"), Assembly.Load("ArcLegacy.Portable"), Assembly.Load("ArcExtra.Portable") }, database);
        UiPlatform.IsMobile = true; UiPlatform.DataDirectory = files; UiPlatform.SettingsPath = Path.Combine(files, "settings.json");
        Storage = new AndroidStorageAccess(context.ApplicationContext);
        UiPlatform.Storage = Storage; UiPlatform.Worker = new AndroidWorkerClient(context.ApplicationContext, Storage);
        UiPlatform.Audio = path => new AndroidAudioOutput(context.ApplicationContext, path);
        UiPlatform.OpenUrl = url => MainActivity.Current.StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url)));
        UiPlatform.Explore = path => Storage.Explore(path);
        UiPlatform.CanExplore = path => Storage.CanExplore(path);
        UiPlatform.CanTrash = () => false;
        UiPlatform.RequestAllFilesAccess = () => MainActivity.Current.StartActivity(new Intent(global::Android.Provider.Settings.ActionManageAppAllFilesAccessPermission,
            global::Android.Net.Uri.Parse("package:" + context.PackageName)));
        UiPlatform.RequestNotificationPermission = () => { if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu) MainActivity.Current.RequestPermissions(new[] { "android.permission.POST_NOTIFICATIONS" }, 80); };
        UiPlatform.ClearStorageCache = () => Storage.ClearSourceCacheAsync();
        UiPlatform.DynamicColor = role => DynamicColor(context, role);
        initialized = true;
    }
    static uint? DynamicColor(Context context, string role)
    {
        bool dark = role.StartsWith("dark/"); string name = role[(role.IndexOf('/') + 1)..];
        var mapping = new Dictionary<string, string>
        {
            ["Primary"] = "accent1_" + (dark ? "200" : "600"), ["OnPrimary"] = "accent1_" + (dark ? "800" : "0"),
            ["PrimaryContainer"] = "accent1_" + (dark ? "700" : "100"), ["OnPrimaryContainer"] = "accent1_" + (dark ? "100" : "900"),
            ["SecondaryContainer"] = "accent2_" + (dark ? "700" : "100"), ["OnSecondaryContainer"] = "accent2_" + (dark ? "100" : "900"),
            ["Surface"] = "neutral1_" + (dark ? "900" : "10"), ["OnSurface"] = "neutral1_" + (dark ? "100" : "900"),
            ["SurfaceContainer"] = "neutral1_" + (dark ? "900" : "50"), ["SurfaceContainerLow"] = "neutral1_" + (dark ? "900" : "10"),
            ["SurfaceContainerHigh"] = "neutral1_" + (dark ? "800" : "100"), ["OnSurfaceVariant"] = "neutral2_" + (dark ? "200" : "700"),
            ["Outline"] = "neutral2_" + (dark ? "400" : "500"), ["OutlineVariant"] = "neutral2_" + (dark ? "700" : "200")
        };
        if (!mapping.TryGetValue(name, out string resource)) return null;
        int id = context.Resources.GetIdentifier("system_" + resource, "color", "android");
        return id == 0 ? null : unchecked((uint)context.GetColor(id));
    }
}
