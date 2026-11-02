using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GARbro.Application;
namespace GARbro.Desktop;

public sealed partial class MainWindow
{
    bool seeking;
    void BuildPlayback()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(ActionButton("Play", PlayAudio));
        row.Children.Add(ActionButton("Pause", () => { Playback?.Pause(); return Task.CompletedTask; }));
        row.Children.Add(ActionButton("Stop", () => { Playback?.Stop(); return Task.CompletedTask; }));
        audioControls.Children.Add(row); audioControls.Children.Add(playbackInfo); audioControls.Children.Add(position); audioControls.Children.Add(Label("Volume")); audioControls.Children.Add(volume);
        volume.Value = Settings.Volume; volume.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) { Settings.Volume = volume.Value; if (Playback != null) Playback.Volume = volume.Value; } };
        position.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty && !changingPosition && Playback != null && !seeking) Playback.Position = position.Value; };
        position.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, (_, _) => seeking = true, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        position.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, (_, _) => { seeking = false; if (Playback != null) Playback.Position = position.Value; }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            var playback = Playback; if (playback == null) return;
            changingPosition = true; position.Maximum = Math.Max(1, playback.Duration); if (!seeking) position.Value = playback.Position; changingPosition = false;
            playbackInfo.Text = TimeSpan.FromSeconds(playback.Position).ToString(@"hh\:mm\:ss") + " / " + TimeSpan.FromSeconds(playback.Duration).ToString(@"hh\:mm\:ss");
            if (videoPlayer?.Warning is string warning) playbackInfo.Text += " · " + Text[warning];
        };
        timer.Start(); Closed += (_, _) => timer.Stop();
    }
    IAudioOutput Playback => (IAudioOutput)videoPlayer ?? player;
    async Task PlayAudio()
    {
        if (videoPlayer != null) { videoPlayer.Play(); return; }
        if (player != null) { player.Play(); return; }
        if (previewEntry == null || listing == null) return;
        audioDirectory = Path.Combine(DesktopServices.CacheDirectory, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(audioDirectory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(audioDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string output = Path.Combine(audioDirectory, "preview.wav");
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); active = operation; SetBusy(true); status.Text = Text["Preparing"];
        try
        {
            if (preview?.Kind == "video")
            {
                if (UiPlatform.Video == null) throw new NotSupportedException(Text["NoVideo"]);
                output = Path.Combine(audioDirectory, "preview.media");
                var artifact = await Request<VideoArtifact>(new("resource-video", null, Location: listing.Location, Entry: previewEntry.Name, Output: output), operation.Token);
                if (artifact.Path != output || new FileInfo(output).Length != artifact.Bytes) throw new InvalidDataException("Invalid preview artifact.");
                int ticket = generation;
                videoPlayer = UiPlatform.Video(artifact, (pixels, width, height) => Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (isClosed || ticket != generation) return;
                    if (bitmap is not WriteableBitmap writable || writable.PixelSize != new PixelSize(width, height))
                    { bitmap?.Dispose(); bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque); image.Source = bitmap; }
                    using var target = ((WriteableBitmap)bitmap).Lock();
                    for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, target.Address + y * target.RowBytes, width * 4);
                }).GetTask(), error => Dispatcher.UIThread.Post(() =>
                {
                    if (isClosed || ticket != generation) return;
                    previewInfo.Text = Text["Error"] + ": " + Text.ErrorMessage(error.Message); DesktopServices.Log(error); StopAudio();
                }), Settings.Limits.MaxMemoryBytes);
                videoPlayer.Volume = volume.Value; videoPlayer.Play();
            }
            else
            {
                var artifact = await Request<AudioArtifact>(new("resource-audio", null, Location: listing.Location, Entry: previewEntry.Name, Output: output), operation.Token);
                if (artifact.Path != output || new FileInfo(output).Length != artifact.Bytes) throw new InvalidDataException("Invalid preview artifact.");
                player = UiPlatform.Audio(output); player.Volume = volume.Value; player.Play();
            }
        }
        catch { StopAudio(); throw; }
        finally { active = null; SetBusy(false); status.Text = ""; }
    }
    void StopAudio()
    {
        videoPlayer?.Dispose(); videoPlayer = null;
        player?.Dispose(); player = null;
        playbackInfo.Text = "";
        if (audioDirectory != null)
        {
            string directory = audioDirectory;
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (IOException) { _ = Task.Run(async () => { for (int n = 0; n < 20; n++) { await Task.Delay(100); try { if (Directory.Exists(directory)) Directory.Delete(directory, true); return; } catch (IOException) { } } }); }
            audioDirectory = null;
        }
    }
}
