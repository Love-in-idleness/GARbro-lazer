using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.Interactivity;
using GARbro.Desktop;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(DesktopTests))]
// UI tests share one Avalonia application, theme and desktop/mobile services.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
public sealed class DesktopTests
{
    [AvaloniaFact]
    public async Task CancelButtonStopsAnOpeningWorker()
    {
        var window=new MainWindow();window.Show();
        try
        {
            var opening=window.LoadArchiveAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","standard.xfl"));
            window.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="CancelOperation")
                .Command.Execute(null);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>opening);
            Assert.Null(window.ArchiveTag);
        }
        finally {window.Close();}
    }
    [AvaloniaFact]
    public async Task SearchCanRemoveAndRestoreAllRowsWithoutBreakingRecycledTemplates()
    {
        var window=new MainWindow();window.Show();
        try
        {
            await window.LoadArchiveAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","standard.xfl"));
            var search=window.GetVisualDescendants().OfType<TextBox>().Single(c=>c.Name=="Search");
            search.Text="no matching file";
            for(int i=0;i<100 && window.VisibleEntryCount!=0;i++)await Task.Delay(10,TestContext.Current.CancellationToken);
            await Dispatcher.UIThread.InvokeAsync(()=>window.UpdateLayout(),DispatcherPriority.Background);
            Assert.Equal(0,window.VisibleEntryCount);
            search.Text="";
            for(int i=0;i<100 && window.VisibleEntryCount!=3;i++)await Task.Delay(10,TestContext.Current.CancellationToken);
            await Dispatcher.UIThread.InvokeAsync(()=>window.UpdateLayout(),DispatcherPriority.Background);
            Assert.Equal(3,window.VisibleEntryCount);
        }
        finally {window.Close();}
    }
    [AvaloniaFact]
    public async Task OriginalSizeReloadsTheImageAfterAViewportPreview()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-image-size-"+Guid.NewGuid());Directory.CreateDirectory(root);
        string source=Path.Combine(root,"large.png");
        using(var output=File.Create(source))
            GameRes.ImageFormat.Png.Write(output,GameRes.ImageData.Create(new GameRes.ImageMetaData {Width=2000,Height=800,BPP=32},GARbro.Imaging.PixelFormats.Bgra32,null,new byte[2000*800*4]));
        var window=new MainWindow(new SettingsStore(Path.Combine(root,"settings.json")));window.Show();
        try
        {
            await window.LoadArchiveAsync(source);
            var image=window.GetLogicalDescendants().OfType<Image>().Single(i=>i.Name=="PreviewImage");
            for(int i=0;i<200&&image.Source==null;i++)await Task.Delay(25,TestContext.Current.CancellationToken);
            var fitted=Assert.IsType<Avalonia.Media.Imaging.Bitmap>(image.Source);Assert.True(fitted.PixelSize.Width<2000);
            var view=window.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="MenuView");
            var fit=view.ContextMenu.Items.OfType<MenuItem>().Single(m=>m.Header as string==UiPlatform.CurrentText["FitImage"]);
            fit.Command.Execute(null);
            for(int i=0;i<200&&(image.Source as Avalonia.Media.Imaging.Bitmap)?.PixelSize.Width!=2000;i++)await Task.Delay(25,TestContext.Current.CancellationToken);
            Assert.Equal(2000,((Avalonia.Media.Imaging.Bitmap)image.Source).PixelSize.Width);
        }
        finally {window.Close();Directory.Delete(root,true);}
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().WithInterFont().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    [AvaloniaFact]
    public async Task InvalidOptionsRestoreTheOpenButton()
    {
        var window=new MainWindow();
        window.Show();
        try
        {
            var options=window.GetLogicalDescendants().OfType<TextBox>().Single(c=>c.Name=="Options");
            options.Text="{invalid";
            await Assert.ThrowsAsync<GARbro.Application.WorkerOperationException>(()=>window.LoadArchiveAsync(
                Path.Combine(AppContext.BaseDirectory,"Fixtures","standard.xfl")));
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(c=>c.Name=="OpenArchive").IsEnabled);
        }
        finally {window.Close();}
    }

    [AvaloniaFact]
    public async Task ClosingDuringOpenDoesNotRetainTheArchiveSession()
    {
        var window=new MainWindow();
        window.Show();
        var loading=window.LoadArchiveAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","standard.xfl"));
        window.Close();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>loading);
        Assert.Null(window.ArchiveTag);
        await Assert.ThrowsAsync<ObjectDisposedException>(()=>window.LoadArchiveAsync("unused"));
    }
    [AvaloniaFact]
    public async Task WindowOpensListsAndExtractsThroughApplicationService()
    {
        string root = Path.Combine(Path.GetTempPath(), "garbro-desktop-" + Guid.NewGuid());
        var window = new MainWindow();
        window.Show();
        try
        {
            await window.LoadArchiveAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "standard.xfl"));
            Assert.Equal("XFL", window.ArchiveTag);
            Assert.Equal(3, window.VisibleEntryCount);
            if (Environment.GetEnvironmentVariable("GARBRO_UI_SNAPSHOT")is string snapshot)
            {
                using var frame = window.DesktopHost.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(snapshot, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }

            await window.ExtractToAsync(root);
            Assert.Equal("Hello, GARbro!\n", File.ReadAllText(Path.Combine(root, "hello.txt")));
            Assert.True(File.Exists(Path.Combine(root, "sub", "日本語.bin")));
        }
        finally
        {
            window.Close();
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
