using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using global::Android.App;
using global::Android.Content;
using global::Android.Content.PM;
using global::Android.OS;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using GARbro.Desktop;

namespace GARbro.Mobile;

[Activity(Name = "org.garbro.garbro.MainActivity", Label = "GARbro", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.KeyboardHidden | ConfigChanges.Density)]
[IntentFilter(new[] { Intent.ActionSend, Intent.ActionSendMultiple }, Categories = new[] { Intent.CategoryDefault }, DataMimeType = "*/*")]
public sealed class MainActivity : AvaloniaMainActivity
{
    public static MainActivity Current { get; private set; }
    public static MainWindow Browser { get; internal set; }
    readonly Dictionary<int, TaskCompletionSource<Intent>> pickers = new();
    int nextRequest = 100;
    protected override void OnCreate(Bundle state)
    {
        Current = this; AndroidBootstrap.Initialize(this); base.OnCreate(state);
        HandleInput(Intent);
    }
    protected override void OnNewIntent(Intent intent) { base.OnNewIntent(intent); Intent = intent; HandleInput(intent); }
    async void HandleInput(Intent intent)
    {
        if (Browser == null || intent == null) return;
#if GARBRO_ANDROID_TEST
        if (intent.GetBooleanExtra("garbro.test", false)) { await AndroidSelfTest.RunAsync(this, Browser); return; }
#endif
        var inputs = new List<global::Android.Net.Uri>();
        if (intent.ClipData is { } clips)
            for (int i = 0; i < clips.ItemCount; i++) if (clips.GetItemAt(i)?.Uri is { } uri) inputs.Add(uri);
        else if (intent.GetParcelableExtra(Intent.ExtraStream) is global::Android.Net.Uri stream) inputs.Add(stream);
        if (intent.Data is { } data) inputs.Add(data);
        foreach (var uri in inputs.DistinctBy(u => u.ToString()))
            await Browser.Guard(async () =>
            {
                var source = await AndroidBootstrap.Storage.ImportAsync(uri, false, intent.Flags);
                await Browser.LoadInputsAsync(new[] { source.Id });
            });
    }
    public Task<Intent> PickAsync(Intent intent)
    {
        int id = ++nextRequest; var completion = new TaskCompletionSource<Intent>(TaskCreationOptions.RunContinuationsAsynchronously);
        pickers.Add(id, completion); StartActivityForResult(intent, id); return completion.Task;
    }
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (pickers.Remove(requestCode, out var completion)) completion.TrySetResult(resultCode == Result.Ok ? data : null);
    }
    public override void OnBackPressed()
    {
        if (UiPlatform.Navigation?.Back() == true) return;
        if (Browser?.HandleBack() == true) return;
        MoveTaskToBack(true);
    }
    protected override void OnStop() { Browser?.StopPreviewAudio(); Browser?.SaveState(); base.OnStop(); }
    protected override void OnDestroy()
    {
        foreach (var task in pickers.Values) task.TrySetResult(null); pickers.Clear();
        if (Current == this) Current = null; base.OnDestroy();
    }
}

public sealed class MobileApp : Avalonia.Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime lifetime)
        {
            lifetime.MainViewFactory = () =>
            {
                MainActivity.Browser = new MainWindow();
                return UiPlatform.Navigation = new PageNavigation(MainActivity.Browser);
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}

[Application]
public sealed class MobileApplication : AvaloniaAndroidApplication<MobileApp>
{
    public MobileApplication(nint javaReference, global::Android.Runtime.JniHandleOwnership transfer) : base(javaReference, transfer) { }
    public override void OnCreate()
    {
        // A decoder has no UI. Its catalog and assets are initialized on its
        // dedicated request thread, never in Android's service lifecycle callback.
        if (!global::Android.App.Application.ProcessName.EndsWith(":decoder", StringComparison.Ordinal))
        {
            AndroidBootstrap.Initialize(this);
            base.OnCreate();
        }
    }
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder).WithInterFont();
}
