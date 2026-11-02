using System;
using System.IO;
using System.Threading.Tasks;
using GARbro.Application;
using GARbro.Desktop;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Xunit;

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
            play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for(int i=0;i<400&&preview.Source is not Avalonia.Media.Imaging.WriteableBitmap;i++)await Task.Delay(50,TestContext.Current.CancellationToken);
            Assert.IsType<Avalonia.Media.Imaging.WriteableBitmap>(preview.Source);
            var files=window.GetVisualDescendants().OfType<ListBox>().Single(l=>l.Name=="Files");
            files.SelectedItem=files.Items.Cast<BrowserEntry>().Single(e=>e.Name=="Scene.txt");
            for(int i=0;i<50&&Directory.GetDirectories(DesktopServices.CacheDirectory).Except(before).Any();i++)await Task.Delay(50,TestContext.Current.CancellationToken);
            Assert.Empty(Directory.GetDirectories(DesktopServices.CacheDirectory).Except(before));
        }
        finally {window.Close();Directory.Delete(root,true);}
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
        await Task.Delay(150,TestContext.Current.CancellationToken);output.Pause();double paused=output.Position;
        await Task.Delay(100,TestContext.Current.CancellationToken);Assert.InRange(output.Position,paused-.03,paused+.03);
        output.Position=.5;Assert.InRange(output.Position,.49,.51);output.Volume=.25;Assert.Equal(.25,output.Volume);
        output.Play();await Task.Delay(150,TestContext.Current.CancellationToken);Assert.True(output.Playing);
        output.Stop();Assert.False(output.Playing);Assert.Equal(0,output.Position);Assert.Null(failure);
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
