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
        row.Children.Add(Button("Play"));
        row.Children.Add(Button("Pause"));
        row.Children.Add(Button("Stop"));
        playbackInfo.FontSize=12;playbackInfo.Classes.Add("muted");
        position.MinHeight=0;position.Height=24;volume.MinHeight=0;volume.Height=24;
        var volumeRow=new Grid {ColumnDefinitions=new("Auto,*"),ColumnSpacing=12};
        var volumeLabel=Label("Volume");volumeLabel.VerticalAlignment=VerticalAlignment.Center;volumeLabel.FontSize=12;
        volumeRow.Children.Add(volumeLabel);Grid.SetColumn(volume,1);volumeRow.Children.Add(volume);
        audioControls.Spacing=4;
        audioControls.Children.Add(row);audioControls.Children.Add(playbackInfo);audioControls.Children.Add(position);audioControls.Children.Add(volumeRow);
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
        if(mediaPreparation!=null)return;
        int requestedGeneration=generation;
        await mediaCleanup;
        if(requestedGeneration!=generation || mediaPreparation!=null)return;
        if (videoPlayer != null) { videoPlayer.Play(); return; }
        if (player != null) { player.Play(); return; }
        if (previewEntry == null || listing == null) return;
        audioDirectory = Path.Combine(DesktopServices.CacheDirectory, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(audioDirectory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(audioDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string output = Path.Combine(audioDirectory, "preview.wav");
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);mediaPreparation=operation;
        var completed=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);preparationFinished=completed.Task;
        int ticket=generation;var sourceLocation=listing.Location;string entryName=previewEntry.Name;bool direct=listing.Physical;
        audioControls.IsEnabled=false;UpdateCommands();
        try
        {
            if (preview?.Kind == "video")
            {
                if (UiPlatform.Video == null) throw new NotSupportedException(Text["NoVideo"]);
                output = Path.Combine(audioDirectory, "preview.media");
                var artifact = await Request<VideoArtifact>(new("resource-video", null, Location: sourceLocation, Entry: entryName, Output: output, PreferDirectSource: direct), operation.Token);
                if(ticket!=generation || isClosed || operation.IsCancellationRequested)return;
                string expected=direct?Path.GetFullPath(Path.Combine(sourceLocation.RootPath,entryName)):output;
                if (artifact.Path != expected || new FileInfo(artifact.Path).Length != artifact.Bytes) throw new InvalidDataException("Invalid preview artifact.");
                videoPlayer = UiPlatform.Video(artifact, (pixels, width, height) => Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (isClosed || ticket != generation) return;
                    if (bitmap is not WriteableBitmap writable || writable.PixelSize != new PixelSize(width, height))
                    { bitmap?.Dispose(); bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque); image.Source = bitmap; }
                    using (var target = ((WriteableBitmap)bitmap).Lock())
                        for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, target.Address + y * target.RowBytes, width * 4);
                    // Pixel writes do not change Image.Source. Schedule a new scene/render pass.
                    image.InvalidateVisual();
                }).GetTask(), error => Dispatcher.UIThread.Post(() =>
                {
                    if (isClosed || ticket != generation) return;
                    previewInfo.Text = Text["Error"] + ": " + Text.ErrorMessage(error.Message); DesktopServices.Log(error); StopAudio();
                }), Settings.Limits.MaxMemoryBytes);
                videoPlayer.Volume = volume.Value; videoPlayer.Play();
            }
            else
            {
                var artifact = await Request<AudioArtifact>(new("resource-audio", null, Location: sourceLocation, Entry: entryName, Output: output), operation.Token);
                if (artifact.Path != output || new FileInfo(output).Length != artifact.Bytes) throw new InvalidDataException("Invalid preview artifact.");
                if(ticket!=generation || isClosed || operation.IsCancellationRequested)return;
                var prepared=await Task.Run(()=>UiPlatform.Audio(output),operation.Token);
                if(ticket!=generation || isClosed || operation.IsCancellationRequested){_ = Task.Run(prepared.Dispose);return;}
                player=prepared;player.Volume=volume.Value;player.Play();
            }
        }
        catch(OperationCanceledException)when(operation.IsCancellationRequested){}
        catch { StopAudio(); throw; }
        finally { if(ReferenceEquals(mediaPreparation,operation))mediaPreparation=null;completed.TrySetResult(true);audioControls.IsEnabled=true;UpdateCommands(); }
    }
    CancellationTokenSource mediaPreparation;
    Task mediaCleanup = Task.CompletedTask, preparationFinished=Task.CompletedTask;
    void StopAudio()
    {
        if(mediaPreparation==null && videoPlayer==null && player==null && audioDirectory==null){playbackInfo.Text="";return;}
        mediaPreparation?.Cancel();
        var preparing=preparationFinished;
        var video=videoPlayer; var audio=player; string directory=audioDirectory;
        videoPlayer=null;player=null;audioDirectory=null;playbackInfo.Text="";
        var previous=mediaCleanup;
        mediaCleanup=Task.Run(async()=>
        {
            try {await previous;}catch(Exception e){DesktopServices.Log(e);}
            await preparing;
            video?.Dispose();audio?.Dispose();
            if(directory==null)return;
            for(int n=0;n<20;n++)
            {
                try {if(Directory.Exists(directory))Directory.Delete(directory,true);return;}
                catch(IOException){await Task.Delay(100);}
                catch(UnauthorizedAccessException){return;}
            }
        });
    }
}
