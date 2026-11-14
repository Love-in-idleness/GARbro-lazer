using System;
using global::Android.App;
using global::Android.Content;
using global::Android.Content.PM;
using global::Android.OS;
using GARbro.Application;
using GARbro.Desktop;

namespace GARbro.Mobile;

[Service(Name = "org.garbro.garbro.JobCoordinatorService", Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class JobCoordinatorService : Service
{
    public static event Action CancelRequested;
    static JobCoordinatorService current;
    const int NotificationId = 1;
    DesktopText text;
    Notification.Builder Builder(ExtractionProgress progress = null)
    {
        var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var cancel = PendingIntent.GetService(this, 1, new Intent(this, typeof(JobCoordinatorService)).SetAction("cancel"), PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        return new Notification.Builder(this, "tasks").SetSmallIcon(Resource.Drawable.ic_notification).SetContentTitle("GARbro")
            .SetContentText(progress == null ? text["Working"] : text["Working"] + " · " + progress.Completed + "/" + progress.Total)
            .SetContentIntent(open).SetOngoing(true).SetOnlyAlertOnce(true).SetProgress(progress?.Total ?? 0, progress?.Completed ?? 0, progress == null)
            .AddAction(new Notification.Action.Builder(null, text["Cancel"], cancel).Build());
    }
    public override void OnCreate()
    {
        base.OnCreate(); current = this; text = new(new SettingsStore().Load().EffectiveLanguage);
        using var manager = (NotificationManager)GetSystemService(NotificationService);
        manager.CreateNotificationChannel(new NotificationChannel("tasks", text["Process"], NotificationImportance.Low));
    }
    public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == "cancel") { CancelRequested?.Invoke(); StopSelf(); return StartCommandResult.NotSticky; }
        StartForeground(NotificationId, Builder().Build(), ForegroundService.TypeDataSync); return StartCommandResult.NotSticky;
    }
    public static void Update(ExtractionProgress progress)
    {
        var service = current; if (service == null) return;
        using var manager = (NotificationManager)service.GetSystemService(NotificationService); manager.Notify(NotificationId, service.Builder(progress).Build());
    }
    public override void OnTimeout(int startId, ForegroundService foregroundServiceType) { CancelRequested?.Invoke(); StopSelf(); }
    public override void OnDestroy() { current = null; StopForeground(StopForegroundFlags.Remove); base.OnDestroy(); }
    public override IBinder OnBind(Intent intent) => null;
}
