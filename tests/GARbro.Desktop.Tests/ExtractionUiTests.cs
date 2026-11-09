using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GARbro.Application;
using GARbro.Desktop;
using Xunit;

public sealed class ExtractionUiTests
{
    [AvaloniaFact]
    public async Task ArchiveCreationCapturesInputsAndPreventsOverlappingDataDialogs()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-create-snapshot-"+Guid.NewGuid()),input=Path.Combine(root,"input");Directory.CreateDirectory(input);
        var store=new SettingsStore(Path.Combine(root,"settings.json"));store.Save(new AppSettings {Language="en-US"});var window=new MainWindow(store);window.Show();var text=new DesktopText("en-US");
        try
        {
            foreach(string name in new[]{"one.txt","two.txt","leave.txt"})File.WriteAllText(Path.Combine(input,name),name);
            window.GetVisualDescendants().OfType<Expander>().Single(c=>c.Header as string==text["Advanced"]).IsExpanded=true;
            await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
            window.GetVisualDescendants().OfType<TextBox>().Single(c=>c.Name=="Scheme").Text="reader-only-scheme";
            window.GetVisualDescendants().OfType<TextBox>().Single(c=>c.Name=="Options").Text="{\"Password\":\"reader-only\"}";
            await window.LoadArchiveAsync(input);var list=await VisibleFiles(window,3);
            foreach(var entry in list.Items.OfType<BrowserEntry>().Where(e=>e.Name!="leave.txt"))list.SelectedItems.Add(entry);
            var menu=window.GetVisualDescendants().OfType<Button>().Single(c=>c.Name=="MenuFile").ContextMenu;
            menu.Items.OfType<MenuItem>().Single(c=>c.Header as string==text["Create"]).Command.Execute(null);
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(c=>c.Name=="ExtractAll").Command.CanExecute(null));
            for(int i=0;i<100 && !window.DesktopHost.OwnedWindows.Any();i++)await Task.Delay(100);
            var dialog=Assert.Single(window.DesktopHost.OwnedWindows);var controls=dialog.GetVisualDescendants().ToArray();
            var format=controls.OfType<ComboBox>().Single(c=>c.Name=="ArchiveOutputFormat");format.SelectedItem=format.Items.OfType<ResourceFormatInfo>().Single(f=>f.Tag=="XFL");
            string archive=Path.Combine(root,"result.xfl");Dialogs.SetPath(controls.OfType<TextBox>().Single(c=>c.Name=="ArchiveOutputPath"),archive);
            list.SelectedItems.Clear();list.SelectedItems.Add(list.Items.OfType<BrowserEntry>().Single(e=>e.Name=="leave.txt"));
            controls.OfType<Button>().Single(c=>c.Content as string==text["Create"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for(int i=0;i<150 && !File.Exists(archive);i++)await Task.Delay(100);
            Assert.True(File.Exists(archive),string.Join(" | ",window.GetVisualDescendants().OfType<TextBlock>().Select(c=>c.Text)));
            var created=await WorkerArchiveSession.RequestAsync<BrowserListing>(Path.Combine(AppContext.BaseDirectory,"GARbro.Worker.dll"),new("resource-list",null,Location:new(archive)),new WorkerLimits(),null,default);
            Assert.Equal(new[]{"one.txt","two.txt"},created.Entries.Select(e=>e.Name).OrderBy(n=>n).ToArray());
        }
        finally {window.Close();Directory.Delete(root,true);}
    }
    [AvaloniaFact]
    public async Task ExtractionMenuHasOnlySelectedAndAllAndRecursionCannotWidenTheSelection()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-extract-ui-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var window=new MainWindow();window.Show();
        try
        {
            foreach(string name in new[]{"first.xfl","second.xfl","leave.xfl"})File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","standard.xfl"),Path.Combine(root,name));
            await window.LoadArchiveAsync(root);
            var files=await VisibleFiles(window,3);
            foreach(var item in files.Items.OfType<BrowserEntry>().Where(e=>e.Name!="leave.xfl"))files.SelectedItems.Add(item);
            var process=window.GetVisualDescendants().OfType<Button>().Single(c=>c.Name=="MenuProcess").ContextMenu;
            Assert.Equal(2,process.Items.Count);Assert.DoesNotContain("Recursive",ShortcutCatalog.Defaults.Keys);
            Assert.NotNull(window.GetVisualDescendants().OfType<Button>().Single(c=>c.Name=="ExtractSelected"));
            await window.ExtractToAsync(Path.Combine(root,"out"),true,new(Recursive:true,DiscardChildArchives:true));
            Assert.True(File.Exists(Path.Combine(root,"out","first.xfl.unpacked","hello.txt")));
            Assert.True(File.Exists(Path.Combine(root,"out","second.xfl.unpacked","hello.txt")));
            Assert.False(Directory.Exists(Path.Combine(root,"out","leave.xfl.unpacked")));
        }
        finally {window.Close();Directory.Delete(root,true);}
    }
    [AvaloniaFact]
    public async Task ChangingSelectionWhileTheDialogIsOpenDoesNotChangeTheCapturedInput()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-extraction-snapshot-"+Guid.NewGuid()),input=Path.Combine(root,"input"),output=Path.Combine(root,"out");Directory.CreateDirectory(input);
        var store=new SettingsStore(Path.Combine(root,"settings.json"));store.Save(new AppSettings {Language="en-US",OutputDirectory=output});var window=new MainWindow(store);window.Show();
        try
        {
            foreach(string name in new[]{"first.xfl","second.xfl","leave.xfl"})File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","standard.xfl"),Path.Combine(input,name));
            await window.LoadArchiveAsync(input);var files=await VisibleFiles(window,3);
            foreach(var item in files.Items.OfType<BrowserEntry>().Where(e=>e.Name is "first.xfl" or "second.xfl"))files.SelectedItems.Add(item);
            window.GetVisualDescendants().OfType<Button>().Single(c=>c.Name=="ExtractSelected").Command.Execute(null);
            await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
            var host=Assert.Single(window.DesktopHost.OwnedWindows);var controls=host.GetVisualDescendants().ToArray();
            controls.OfType<CheckBox>().Single(c=>c.Name=="RecursiveExtraction").IsChecked=true;
            controls.OfType<CheckBox>().Single(c=>c.Name=="DiscardChildArchives").IsChecked=true;
            files.SelectedItems.Clear();
            controls.OfType<Button>().Single(c=>c.Content as string=="Start").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var extractAll=window.GetVisualDescendants().OfType<Button>().Single(c=>c.Name=="ExtractAll");
            for(int i=0;i<100;i++)
            {
                if(File.Exists(Path.Combine(output,"second.xfl.unpacked","hello.txt")) && extractAll.Command.CanExecute(null))break;
                await Task.Delay(100);
            }
            Assert.True(File.Exists(Path.Combine(output,"first.xfl.unpacked","hello.txt")));Assert.True(File.Exists(Path.Combine(output,"second.xfl.unpacked","hello.txt")));
            Assert.False(Directory.Exists(Path.Combine(output,"leave.xfl.unpacked")));
        }
        finally {window.Close();Directory.Delete(root,true);}
    }
    [AvaloniaFact]
    public async Task ExtractionDialogKeepsItsScopeAndExposesIndependentMediaFormats()
    {
        var owner=new AppWindow();owner.Show();var text=new DesktopText("en-US");
        try
        {
            var result=Dialogs.Extraction(owner,text,Path.GetTempPath(),true,2);
            var host=Assert.Single(owner.DesktopHost.OwnedWindows);var controls=host.GetVisualDescendants().ToArray();
            var recurse=controls.OfType<CheckBox>().Single(c=>c.Name=="RecursiveExtraction");Assert.False(recurse.IsChecked);
            var discard=controls.OfType<CheckBox>().Single(c=>c.Name=="DiscardChildArchives");Assert.False(discard.IsEnabled);
            recurse.IsChecked=true;Assert.True(discard.IsEnabled);discard.IsChecked=true;
            var audio=controls.OfType<ComboBox>().Single(c=>c.Name=="AudioOutputFormat");audio.SelectedItem=audio.Items.OfType<UiChoice>().Single(c=>c.Value=="WAV");
            var image=controls.OfType<ComboBox>().Single(c=>c.Name=="ImageOutputFormat");image.SelectedItem=image.Items.OfType<UiChoice>().Single(c=>c.Value=="WEBP");
            var video=controls.OfType<ComboBox>().Single(c=>c.Name=="VideoOutputFormat");video.SelectedItem=video.Items.OfType<UiChoice>().Single(c=>c.Value=="MP4");
            var discardMedia=controls.OfType<CheckBox>().Single(c=>c.Name=="DiscardOriginalMedia");Assert.True(discardMedia.IsEnabled);discardMedia.IsChecked=true;
            Assert.Contains(controls.OfType<TextBlock>(),c=>c.Text==text.Format("ExtractionSelectionScope",2));
            controls.OfType<Button>().Single(c=>c.Content as string==text["Start"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var choice=await result;Assert.True(choice.Options.Recursive);Assert.True(choice.Options.DiscardChildArchives);Assert.Equal("WEBP",choice.Options.ImageFormat);Assert.Equal("WAV",choice.Options.AudioFormat);Assert.Equal("MP4",choice.Options.VideoFormat);Assert.True(choice.Options.DiscardOriginalMedia);
        }
        finally {owner.Close();}
    }
    [AvaloniaFact]
    public async Task BatchConversionReportsSkippedTypesAndWritesTheSelectedWebp()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-batch-ui-"+Guid.NewGuid()),input=Path.Combine(root,"input"),output=Path.Combine(root,"out");Directory.CreateDirectory(input);
        var store=new SettingsStore(Path.Combine(root,"settings.json"));store.Save(new AppSettings {Language="en-US",OutputDirectory=output});
        var window=new MainWindow(store);window.Show();var text=new DesktopText("en-US");
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","colors-rgba.png"),Path.Combine(input,"image.png"));File.WriteAllText(Path.Combine(input,"note.txt"),"keep this source");
            await window.LoadArchiveAsync(input);var files=await VisibleFiles(window,2);
            SelectionCommand(window).Execute(null);Assert.Equal(2,files.SelectedItems.Count);
            var menu=window.GetVisualDescendants().OfType<Button>().Single(c=>c.Name=="MenuFile").ContextMenu;
            menu.Items.OfType<MenuItem>().Single(c=>c.Header as string==text["Convert"]).Command.Execute(null);
            for(int i=0;i<100 && !window.DesktopHost.OwnedWindows.Any();i++)await Task.Delay(100);
            var dialog=Assert.Single(window.DesktopHost.OwnedWindows);var controls=dialog.GetVisualDescendants().ToArray();
            var image=controls.OfType<ComboBox>().Single(c=>c.Name=="ImageOutputFormat");image.SelectedItem=image.Items.OfType<UiChoice>().Single(c=>c.Label==text["WebpLossless"]);
            foreach(var name in new[]{"AudioOutputFormat","VideoOutputFormat"})
            {var selector=controls.OfType<ComboBox>().Single(c=>c.Name==name);selector.SelectedItem=selector.Items.OfType<UiChoice>().Single(c=>c.Value=="");}
            Assert.Contains(controls.OfType<TextBlock>(),c=>c.Text==text.Format("ConversionSelectionScope",2));
            controls.OfType<Button>().Single(c=>c.Content as string==text["Start"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for(int i=0;i<150;i++)
            {
                if(window.GetVisualDescendants().OfType<TextBlock>().Any(c=>c.Text==text.Format("ConversionCompleted",1,1,0)))break;
                await Task.Delay(100);
            }
            Assert.True(File.Exists(Path.Combine(output,"image.webp")));Assert.False(File.Exists(Path.Combine(output,"note.txt")));
            Assert.True(File.Exists(Path.Combine(input,"image.png")));Assert.Equal("keep this source",File.ReadAllText(Path.Combine(input,"note.txt")));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),c=>c.Text==text.Format("ConversionCompleted",1,1,0));
        }
        finally {window.Close();Directory.Delete(root,true);}
    }
    static System.Windows.Input.ICommand SelectionCommand(MainWindow window) =>
        window.GetVisualDescendants().OfType<Button>().Single(c=>c.Name=="MenuSelection").ContextMenu.Items.OfType<MenuItem>()
            .Single(c=>c.Header as string==UiPlatform.CurrentText["SelectAll"]).Command;

    static async Task<ListBox> VisibleFiles(MainWindow window,int count)
    {
        var files=window.GetVisualDescendants().OfType<ListBox>().Single(c=>c.Name=="Files");
        for(int i=0;i<200 && files.ItemCount!=count;i++)await Task.Delay(25,TestContext.Current.CancellationToken);
        await Dispatcher.UIThread.InvokeAsync(()=>window.UpdateLayout(),DispatcherPriority.Background);
        Assert.Equal(count,files.ItemCount);Assert.Equal(count>0,files.IsEnabled);
        return files;
    }

    [AvaloniaFact]
    public async Task SelectAllIsBoundedAndUnavailableForEmptyOrFilteredLists()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-select-all-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var window=new MainWindow(new SettingsStore(Path.Combine(root,"settings.json")));window.Show();
        try
        {
            var files=await VisibleFiles(window,0);Assert.False(SelectionCommand(window).CanExecute(null));
            SelectionCommand(window).Execute(null);Assert.Empty(files.SelectedItems);
            string input=Path.Combine(root,"input");Directory.CreateDirectory(input);
            await window.LoadArchiveAsync(input);await VisibleFiles(window,0);
            Assert.False(SelectionCommand(window).CanExecute(null));
            foreach(string name in new[]{"one.txt","two.txt"})File.WriteAllText(Path.Combine(input,name),name);
            await window.LoadArchiveAsync(input);await VisibleFiles(window,2);
            Assert.True(SelectionCommand(window).CanExecute(null));SelectionCommand(window).Execute(null);Assert.Equal(2,files.SelectedItems.Count);
            var search=window.GetVisualDescendants().OfType<TextBox>().Single(c=>c.Name=="Search");search.Text="no matching file";
            await VisibleFiles(window,0);Assert.False(SelectionCommand(window).CanExecute(null));
            window.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.A,KeyModifiers=KeyModifiers.Control});
            Assert.Empty(files.SelectedItems);Assert.True(search.IsEnabled);
            search.Text="";await VisibleFiles(window,2);SelectionCommand(window).Execute(null);Assert.Equal(2,files.SelectedItems.Count);
        }
        finally {window.Close();Directory.Delete(root,true);}
    }

}
