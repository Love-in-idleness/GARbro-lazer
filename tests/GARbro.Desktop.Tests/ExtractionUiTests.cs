using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GARbro.Application;
using GARbro.Desktop;
using Xunit;

public sealed class ExtractionUiTests
{
    [AvaloniaFact]
    public async Task ExtractionMenuHasOnlySelectedAndAllAndRecursionCannotWidenTheSelection()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-extract-ui-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var window=new MainWindow();window.Show();
        try
        {
            foreach(string name in new[]{"first.xfl","second.xfl","leave.xfl"})File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","standard.xfl"),Path.Combine(root,name));
            await window.LoadArchiveAsync(root);
            var files=window.GetVisualDescendants().OfType<ListBox>().Single(c=>c.Name=="Files");
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
            await window.LoadArchiveAsync(input);var files=window.GetVisualDescendants().OfType<ListBox>().Single(c=>c.Name=="Files");
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
            var video=controls.OfType<ComboBox>().Single(c=>c.Name=="VideoOutputFormat");video.SelectedItem=video.Items.OfType<UiChoice>().Single(c=>c.Value=="MP4");
            var discardMedia=controls.OfType<CheckBox>().Single(c=>c.Name=="DiscardOriginalMedia");Assert.True(discardMedia.IsEnabled);discardMedia.IsChecked=true;
            Assert.Contains(controls.OfType<TextBlock>(),c=>c.Text==text.Format("ExtractionSelectionScope",2));
            controls.OfType<Button>().Single(c=>c.Content as string==text["Start"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var choice=await result;Assert.True(choice.Options.Recursive);Assert.True(choice.Options.DiscardChildArchives);Assert.Equal("WAV",choice.Options.AudioFormat);Assert.Equal("MP4",choice.Options.VideoFormat);Assert.True(choice.Options.DiscardOriginalMedia);
        }
        finally {owner.Close();}
    }
}
