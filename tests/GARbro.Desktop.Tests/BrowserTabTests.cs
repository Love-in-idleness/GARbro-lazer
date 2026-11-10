using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GARbro.Application;
using GARbro.Desktop;
using Xunit;

public sealed class BrowserTabTests
{
    static ListBox Tabs(MainWindow w)=>w.GetVisualDescendants().OfType<ListBox>().Single(c=>c.Name=="ResourceTabs");
    static ListBox Files(MainWindow w)=>w.GetVisualDescendants().OfType<ListBox>().Single(c=>c.Name=="Files");
    static TextBox Search(MainWindow w)=>w.GetVisualDescendants().OfType<TextBox>().Single(c=>c.Name=="Search");
    static async Task Until(Func<bool> condition)
    {
        for(int i=0;i<300 && !condition();i++)await Task.Delay(20,TestContext.Current.CancellationToken);
        Assert.True(condition());await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
    }
    [AvaloniaFact]
    public async Task MultipleInputsKeepTheirFilterAndSelectionInOneWindow()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-tabs-"+Guid.NewGuid()),first=Path.Combine(root,"一 Русский"),second=Path.Combine(root,"日本語 two");
        Directory.CreateDirectory(first);Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first,"one.txt"),"first");File.WriteAllText(Path.Combine(first,"two.txt"),"second");File.WriteAllText(Path.Combine(second,"other.txt"),"other");
        var window=new MainWindow(new SettingsStore(Path.Combine(root,"settings.json")));window.Show();var host=window.DesktopHost;
        try
        {
            await window.LoadInputsAsync(new[]{first,second});Assert.Equal(2,Tabs(window).ItemCount);Assert.Equal(second,window.CurrentLocation.RootPath);
            Tabs(window).SelectedIndex=0;await Until(()=>window.CurrentLocation?.RootPath==first && Files(window).ItemCount==2);
            Search(window).Text="one";await Until(()=>Files(window).ItemCount==1);Files(window).SelectedIndex=0;Assert.Single(Files(window).SelectedItems);
            Tabs(window).SelectedIndex=1;await Until(()=>window.CurrentLocation?.RootPath==second && Files(window).ItemCount==1);
            Assert.Equal("",Search(window).Text);Assert.Empty(Files(window).SelectedItems);
            Tabs(window).SelectedIndex=0;await Until(()=>window.CurrentLocation?.RootPath==first && Files(window).SelectedItems.Count==1);
            Assert.Equal("one",Search(window).Text);Assert.Equal("one.txt",Assert.IsType<BrowserEntry>(Files(window).SelectedItem).Name);
            Assert.Same(host,window.DesktopHost);Assert.Empty(host.OwnedWindows);
            await window.LoadArchiveAsync(second);Assert.Equal(2,Tabs(window).ItemCount);Assert.Equal(second,window.CurrentLocation.RootPath);
        }
        finally {window.Close();Directory.Delete(root,true);}
    }
    [AvaloniaFact]
    public async Task TabShortcutsAndClosingTheLastTabLeaveAUsableWelcomePage()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-tab-close-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var window=new MainWindow(new SettingsStore(Path.Combine(root,"settings.json")));window.Show();
        try
        {
            await window.LoadArchiveAsync(root);
            var primary=OperatingSystem.IsMacOS()?KeyModifiers.Meta:KeyModifiers.Control;
            window.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.T,KeyModifiers=primary});
            await Until(()=>Tabs(window).ItemCount==2 && window.CurrentLocation==null);
            Assert.Equal(0,Files(window).ItemCount);
            window.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Tab,KeyModifiers=KeyModifiers.Control|KeyModifiers.Shift});
            await Until(()=>window.CurrentLocation?.RootPath==root);
            window.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.W,KeyModifiers=primary});
            await Until(()=>Tabs(window).ItemCount==1 && window.CurrentLocation==null);
            Assert.Empty(Files(window).Items);Assert.Empty(window.GetVisualDescendants().OfType<ListBox>().Single(c=>c.Name=="Folders").Items);
            await window.LoadArchiveAsync(root);Assert.Equal(1,Tabs(window).ItemCount);Assert.Equal(root,window.CurrentLocation.RootPath);
        }
        finally {window.Close();Directory.Delete(root,true);}
    }
    sealed class DelayedListing(IWorkerClient original) : IWorkerClient
    {
        public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<T> ExecuteAsync<T>(WorkerRequest request,WorkerLimits limits,System.IProgress<ExtractionProgress> progress=null,CancellationToken cancellation=default)
        {
            if(request.Operation=="resource-list")
            {
                Started.TrySetResult();await Release.Task;
                return (T)(object)new BrowserListing(request.Location,"directory",true,Array.Empty<BrowserEntry>());
            }
            return await original.ExecuteAsync<T>(request,limits,progress,cancellation);
        }
    }
    [AvaloniaFact]
    public async Task SwitchingDuringLoadingCannotOverwriteTheNewTab()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-tab-loading-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var window=new MainWindow(new SettingsStore(Path.Combine(root,"settings.json")));var original=UiPlatform.Worker;var delayed=new DelayedListing(original);UiPlatform.Worker=delayed;window.Show();
        try
        {
            var opening=window.LoadArchiveAsync(root);await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5),TestContext.Current.CancellationToken);
            window.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.T,KeyModifiers=OperatingSystem.IsMacOS()?KeyModifiers.Meta:KeyModifiers.Control});
            await Until(()=>Tabs(window).ItemCount==2 && window.CurrentLocation==null);
            delayed.Release.TrySetResult();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>opening);
            Assert.Null(window.CurrentLocation);Assert.Equal(0,Files(window).ItemCount);
            Tabs(window).SelectedIndex=0;await Until(()=>window.CurrentLocation?.RootPath==root);
        }
        finally {delayed.Release.TrySetResult();window.Close();UiPlatform.Worker=original;Directory.Delete(root,true);}
    }
    [Fact]
    public void ExistingCustomBindingsTakePriorityOverNewTabDefaults()
    {
        string path=Path.Combine(Path.GetTempPath(),"garbro-tab-keys-"+Guid.NewGuid()+".json");
        try
        {
            var old=new AppSettings {Theme="dark"};old.ShortcutBindings["Refresh"]=new[]{(OperatingSystem.IsMacOS()?"Meta":"Ctrl")+"+T"};
            File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(old));
            var store=new SettingsStore(path);var settings=store.Load();Assert.Null(store.LoadError);Assert.Equal("dark",settings.Theme);
            Assert.Empty(ShortcutCatalog.Effective(settings)["NewTab"]);
            settings.ShortcutBindings.Remove("NewTab");Assert.Throws<ArgumentException>(()=>settings.Validate());
        }
        finally {File.Delete(path);}
    }
    [Fact]
    public async Task SecondaryLaunchesQueueFilesUntilThePrimaryWindowIsReady()
    {
        string key="garbro-tab-test-"+Guid.NewGuid();using var primary=new DesktopInstance(key);using var secondary=new DesktopInstance(key);
        Assert.True(primary.IsPrimary);Assert.False(secondary.IsPrimary);
        var files=new[]{"/tmp/中文 日本語/file.xfl","C:\\Русский folder\\archive.dat"};
        await secondary.ForwardAsync(files,TestContext.Current.CancellationToken);
        var received=new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.SetReceiver(args=>received.TrySetResult(args));Assert.Equal(files,await received.Task.WaitAsync(TimeSpan.FromSeconds(5),TestContext.Current.CancellationToken));
        var activate=new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);primary.SetReceiver(args=>activate.TrySetResult(args));
        await secondary.ForwardAsync(Array.Empty<string>(),TestContext.Current.CancellationToken);Assert.Empty(await activate.Task.WaitAsync(TimeSpan.FromSeconds(5),TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task OversizedActivationDoesNotBreakTheNextRequestOrLeaveAStaleOwner()
    {
        string key="garbro-tab-test-"+Guid.NewGuid();using var primary=new DesktopInstance(key);using var secondary=new DesktopInstance(key);
        await Assert.ThrowsAsync<ArgumentException>(()=>secondary.ForwardAsync(new[]{new string('x',1<<20)},TestContext.Current.CancellationToken));
        var received=new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);primary.SetReceiver(args=>received.TrySetResult(args));
        await secondary.ForwardAsync(new[]{"archive.xfl"},TestContext.Current.CancellationToken);Assert.Equal("archive.xfl",(await received.Task.WaitAsync(TimeSpan.FromSeconds(5),TestContext.Current.CancellationToken))[0]);
        primary.Dispose();using var recovered=new DesktopInstance(key);Assert.True(recovered.IsPrimary);
    }
}
