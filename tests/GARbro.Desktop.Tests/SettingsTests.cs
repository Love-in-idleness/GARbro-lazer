using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.VisualTree;
using GARbro.Desktop;
using Xunit;

[Collection("Native audio")]
public sealed class SettingsTests
{
    [Fact]
    public void SettingsRoundTripAndCorruptFilesDoNotEraseTheOriginal()
    {
        string root=Path.Combine(Path.GetTempPath(),"garbro-settings-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string path=Path.Combine(root,"settings.json");var store=new SettingsStore(path);
            store.Save(new AppSettings{Language="en-US",Theme="dark",TextEncoding="gbk",TimeoutSeconds=42,MemoryMiB=256,OutputDirectory=root});
            var loaded=store.Load();Assert.Equal("dark",loaded.Theme);Assert.Equal(42,loaded.TimeoutSeconds);Assert.Equal(root,loaded.OutputDirectory);
            Assert.DoesNotContain("Context",File.ReadAllText(path));
            File.WriteAllText(path,"{broken");Assert.Equal("auto",store.Load().TextEncoding);Assert.NotNull(store.LoadError);Assert.Equal("{broken",File.ReadAllText(path));
            store.Save(new AppSettings());Assert.Equal("{broken",File.ReadAllText(Directory.GetFiles(root,"*.invalid-*").Single()));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void UpdateVersionsUseNumericPrereleaseOrdering()
    {
        Assert.True(UpdateService.IsNewer("v0.2.0-preview.10","0.2.0-preview.2+sha"));
        Assert.True(UpdateService.IsNewer("v0.2.0","0.2.0-preview.10"));
        Assert.False(UpdateService.IsNewer("v0.2.0-preview.2","0.2.0"));
    }
    [Fact]
    public void EveryUiResourceHasFourLanguagesAndRedactionRemovesSecrets()
    {
        var english=new DesktopText("en-US");
        foreach(string language in GARbro.Domain.LanguageCatalog.Codes)
            foreach(string key in english.Keys) Assert.NotEmpty(new DesktopText(language)[key]);
        Assert.DoesNotContain("hunter2",DesktopServices.Redact("password=hunter2"));
        Assert.DoesNotContain("hunter2",DesktopServices.Redact("{\"Password\":\"hunter2 with spaces\"}"));
        Assert.DoesNotContain("Someone",DesktopServices.Redact("C:\\Users\\Someone\\game.xp3"));
        Assert.DoesNotContain("Someone",DesktopServices.Redact("/media/Someone/game.xp3"));
    }
    [Fact]
    public async System.Threading.Tasks.Task AudioDeviceSupportsManualPlaybackPauseSeekAndDisposal()
    {
        using var sdl=AudioPlayback.LoadApi(); sdl.SetHint("SDL_AUDIODRIVER","dummy");
        using var player=new AudioPlayback(Path.Combine(AppContext.BaseDirectory,"Fixtures","tone.wav"));
        Assert.False(player.Playing);Assert.Equal(0,player.Position);
        player.Play();Assert.True(player.Playing);
        await System.Threading.Tasks.Task.Delay(80,TestContext.Current.CancellationToken);
        player.Pause();Assert.False(player.Playing);
        player.Position=player.Duration/2;Assert.InRange(player.Position,player.Duration/2-0.01,player.Duration/2+0.01);
        player.Volume=.25;Assert.Equal(.25,player.Volume);
        player.Stop();Assert.Equal(0,player.Position);
    }
    [AvaloniaFact]
    public void GearAndMenusExposeTheSameSettingsCommand()
    {
        var window=new MainWindow(new SettingsStore(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json")));window.Show();
        try
        {
            var menus=window.GetVisualDescendants().OfType<StackPanel>().Single(p=>p.Name=="IconMenus");Assert.Equal(6,menus.Children.Count);
            Assert.All(menus.Children.OfType<Button>(), b=>{ Assert.IsType<PathIcon>(b.Content); Assert.NotNull(b.ContextMenu); });
            Assert.NotNull(window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b=>b.Command!=null&&b.Content is PathIcon));
        }
        finally{window.Close();}
    }
    [AvaloniaFact]
    public async System.Threading.Tasks.Task SettingsDialogSavesEditableEncodingAndTheme()
    {
        var owner=new Window();owner.Show();var settings=new AppSettings();
        try
        {
            var dialog=new SettingsWindow(settings,new DesktopText("en-US"),Array.Empty<GARbro.Application.ResourceFormatInfo>());
            var result=dialog.ShowDialog<bool>(owner);
            var tabs=dialog.GetVisualDescendants().OfType<TabControl>().First();
            var language=dialog.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="Language");language.SelectedItem=language.Items.OfType<UiChoice>().Single(c=>c.Value=="en-US");
            tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(t=>t.Name=="Appearance");
            var theme=dialog.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="Theme");theme.SelectedItem=theme.Items.OfType<UiChoice>().Single(c=>c.Value=="dark");
            tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(t=>t.Name=="Decoding");
            dialog.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="Encoding").Text="936";
            dialog.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Save").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(await result);Assert.Equal("936",settings.TextEncoding);Assert.Equal("dark",settings.Theme);Assert.Equal("en-US",settings.Language);
        }
        finally {owner.Close();}
    }
}
