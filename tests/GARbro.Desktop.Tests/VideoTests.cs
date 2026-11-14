using System;
using System.IO;
using System.Threading.Tasks;
using GARbro.Application;
using GARbro.Desktop;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Xunit;

[CollectionDefinition("Native audio", DisableParallelization = true)]
public sealed class NativeAudioCollection { }

[Collection("Native audio")]
public sealed class VideoTests
{
    [AvaloniaTheory]
    [InlineData("en-US","light")]
    [InlineData("zh-CN","dark")]
    public async Task VideoUsesTheParentFolderAndDoesNotReenterItsSyntheticArchive(string language,string theme)
    {
        string root=Path.Combine(Path.GetTempPath(),"GARbro media "+Guid.NewGuid());Directory.CreateDirectory(root);
        var store=new SettingsStore(Path.Combine(root,"settings.json"));store.Save(new AppSettings{Language=language,Theme=theme});
        var window=new MainWindow(store);window.Width=1440;window.Height=810;window.Show();
        try
        {
            string media=Path.Combine(root,"Game resources");Directory.CreateDirectory(media);
            foreach(var item in new[]{("movie.mpg","Movie_end01.mpg"),("colors-rgb.png","Background.png"),("tone.ogg","Theme.ogg"),("silent.mp4","Opening.mp4")})
                File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures",item.Item1),Path.Combine(media,item.Item2));
            File.WriteAllText(Path.Combine(media,"Scene.txt"),"Welcome to GARbro!");Directory.CreateDirectory(Path.Combine(media,"Scripts"));
            await window.LoadInputsAsync(new[]{Path.Combine(media,"Movie_end01.mpg")});
            Assert.Equal("directory",window.ArchiveTag);Assert.Empty(window.CurrentLocation.Chain);
            var preview=window.GetLogicalDescendants().OfType<Image>().Single(i=>i.Name=="PreviewImage");
            for(int i=0;i<200&&preview.Source==null;i++)await Task.Delay(50,TestContext.Current.CancellationToken);
            Assert.NotNull(preview.Source);
            var play=window.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string==new DesktopText(language)["Play"]);
            Assert.True(play.IsVisible);Assert.True(play.IsEnabled);
            var all=window.GetVisualDescendants().OfType<TextBlock>().Select(t=>t.Text??"").ToArray();
            Assert.DoesNotContain(all,s=>s.Contains("DAT/BOGUS")||s.Contains("Offset and length"));
            if(Environment.GetEnvironmentVariable("GARBRO_MEDIA_SNAPSHOTS")is string snapshots)
            {
                window.UpdateLayout();await Task.Delay(100,TestContext.Current.CancellationToken);
                Directory.CreateDirectory(snapshots);using var image=new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height));
                image.Render(window);image.Save(Path.Combine(snapshots,"video-"+language+".png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            var before=Directory.Exists(DesktopServices.CacheDirectory)?Directory.GetDirectories(DesktopServices.CacheDirectory):Array.Empty<string>();
            play.Command.Execute(null);
            for(int i=0;i<400&&preview.Source is not Avalonia.Media.Imaging.WriteableBitmap;i++)await Task.Delay(50,TestContext.Current.CancellationToken);
            Assert.IsType<Avalonia.Media.Imaging.WriteableBitmap>(preview.Source);
            var files=window.GetVisualDescendants().OfType<ListBox>().Single(l=>l.Name=="Files");
            files.SelectedItem=files.Items.Cast<BrowserEntry>().Single(e=>e.Name=="Scene.txt");
            for(int i=0;i<50&&Directory.GetDirectories(DesktopServices.CacheDirectory).Except(before).Any();i++)await Task.Delay(50,TestContext.Current.CancellationToken);
            Assert.Empty(Directory.GetDirectories(DesktopServices.CacheDirectory).Except(before));
        }
        finally {window.Close();Directory.Delete(root,true);}
    }
    [AvaloniaFact]
    public async Task PresentedVideoPixelsChangeWithoutRenderingTheControlManually()
    {
        using var sdl=AudioPlayback.LoadApi();sdl.SetHint("SDL_AUDIODRIVER","dummy");
        var window=new MainWindow();window.Width=1440;window.Height=900;window.Show();
        try
        {
            await window.LoadArchiveAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","movie.mpg"));
            var image=window.GetLogicalDescendants().OfType<Image>().Single(i=>i.Name=="PreviewImage");
            for(int n=0;n<200&&image.Source==null;n++)await Task.Delay(20,TestContext.Current.CancellationToken);
            var play=window.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string==UiPlatform.CurrentText["Play"]);
            play.Command.Execute(null);
            var hashes=new HashSet<string>();
            for(int n=0;n<150 && hashes.Count<6;n++)
            {
                await Task.Delay(60,TestContext.Current.CancellationToken);
                using var actual=window.DesktopHost.CaptureRenderedFrame();
                var origin=image.TranslatePoint(default,window.DesktopHost);
                if(actual==null || origin==null || image.Source is not Avalonia.Media.Imaging.WriteableBitmap)continue;
                // Sample only the central video pixels, excluding labels, controls and the seek slider.
                int x=(int)(origin.Value.X+image.Bounds.Width*.3),y=(int)(origin.Value.Y+image.Bounds.Height*.3);
                int w=Math.Max(1,(int)(image.Bounds.Width*.4)),h=Math.Max(1,(int)(image.Bounds.Height*.4));
                var region=new PixelRect(x,y,w,h);byte[] pixels=new byte[w*h*4];var pinned=GCHandle.Alloc(pixels,GCHandleType.Pinned);
                try{actual.CopyPixels(region,pinned.AddrOfPinnedObject(),pixels.Length,w*4);}finally{pinned.Free();}
                hashes.Add(Convert.ToHexString(SHA256.HashData(pixels)));
            }
            Assert.True(hashes.Count>=4,$"Expected moving presented pixels; captured {hashes.Count} distinct video regions.");
        }
        finally{window.Close();}
    }
    [Theory]
    [InlineData("movie.mpg")]
    [InlineData("silent.mp4")]
    public async Task VideoFramesAndAudioSupportManualPlaybackPauseSeekAndStop(string name)
    {
        using var sdl=AudioPlayback.LoadApi();sdl.SetHint("SDL_AUDIODRIVER","dummy");
        string path=Path.Combine(AppContext.BaseDirectory,"Fixtures",name);
        var artifact=new VideoArtifact(path,new FileInfo(path).Length,MediaTools.Probe(path));
        var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception failure=null;int frames=0;
        using var output=new VideoPlayback(artifact,(pixels,w,h)=>
        {
            Assert.Equal(w*h*4,pixels.Length);Assert.Equal(160,w);Assert.Equal(90,h);
            System.Threading.Interlocked.Increment(ref frames);ready.TrySetResult(true);return Task.CompletedTask;
        },error=>{failure=error;ready.TrySetException(error);},1L<<30);
        Assert.False(output.Playing);Assert.Equal(0,output.Position);Assert.Equal(0,frames);
        output.Play();await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
        output.Position=.1;Assert.Null(output.Warning);
        await Task.Delay(150,TestContext.Current.CancellationToken);output.Pause();double paused=output.Position;
        await Task.Delay(100,TestContext.Current.CancellationToken);Assert.InRange(output.Position,paused-.03,paused+.03);
        output.Position=.5;Assert.InRange(output.Position,.49,.51);output.Volume=.25;Assert.Equal(.25,output.Volume);
        output.Play();await Task.Delay(150,TestContext.Current.CancellationToken);Assert.True(output.Playing);
        output.Stop();Assert.False(output.Playing);Assert.Equal(0,output.Position);Assert.Null(failure);
    }
    [Fact]
    public async Task PlaybackClockWaitsForTheFirstVideoFrameBeforeStartingAudio()
    {
        using var sdl=AudioPlayback.LoadApi();sdl.SetHint("SDL_AUDIODRIVER","dummy");
        string path=Path.Combine(AppContext.BaseDirectory,"Fixtures","movie.mpg");
        var entered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);int frames=0;
        using var output=new VideoPlayback(new(path,new FileInfo(path).Length,MediaTools.Probe(path)),async(_,_,_)=>
        {if(System.Threading.Interlocked.Increment(ref frames)==1){entered.TrySetResult(true);await release.Task;}},error=>entered.TrySetException(error),1L<<30);
        try
        {
            output.Play();await entered.Task.WaitAsync(TimeSpan.FromSeconds(15),TestContext.Current.CancellationToken);
            await Task.Delay(400,TestContext.Current.CancellationToken);
            Assert.InRange(output.Position,0,.05);
            release.TrySetResult(true);
            // SDL's dummy device can start its next callback after 200ms on a
            // busy Windows runner. Observe resumed playback rather than rely
            // on a fixed scheduling delay; the blocked-frame assertion above
            // still verifies that audio cannot start prematurely.
            var resumed=System.Diagnostics.Stopwatch.StartNew();
            while(resumed.Elapsed<TimeSpan.FromSeconds(3) && (output.Position<=.05 || System.Threading.Volatile.Read(ref frames)<3))
                await Task.Delay(20,TestContext.Current.CancellationToken);
            Assert.True(output.Position>.05,$"Playback clock did not advance after the first frame: {output.Position:F3}s.");
            Assert.True(System.Threading.Volatile.Read(ref frames)>=3,$"Playback did not resume continuous frames: {frames}.");
        }
        finally{release.TrySetResult(true);}
    }
    [Fact]
    public async Task UnavailableAudioStreamFallsBackToMutedVideo()
    {
        using var sdl=AudioPlayback.LoadApi();sdl.SetHint("SDL_AUDIODRIVER","dummy");
        string path=Path.Combine(AppContext.BaseDirectory,"Fixtures","silent.mp4");
        var info=MediaTools.Probe(path);info.HasAudio=true;
        int frames=0;Exception failure=null;
        using var output=new VideoPlayback(new(path,new FileInfo(path).Length,info),(_,_,_)=>{frames++;return Task.CompletedTask;},error=>failure=error,1L<<30);
        output.Play();
        for(int n=0;n<150&&(output.Warning==null||frames<2);n++)await Task.Delay(50,TestContext.Current.CancellationToken);
        Assert.Equal("VideoAudioUnavailable",output.Warning);Assert.True(frames>=2);Assert.Null(failure);
    }
    [Fact]
    public async Task InvalidVideoStopsAndReportsFailureWithoutHanging()
    {
        var ready=new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var artifact=new VideoArtifact(Path.Combine(AppContext.BaseDirectory,"Fixtures","standard.xfl"),0,
            new GARbro.Media.VideoInfo{Width=160,Height=90,Duration=1,FrameRate=25});
        using var output=new VideoPlayback(artifact,(_,_,_)=>Task.CompletedTask,error=>ready.TrySetResult(error),1L<<30);
        output.Play();Assert.NotNull(await ready.Task.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.False(output.Playing);
    }
}
