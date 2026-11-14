using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GARbro.Application;
using GARbro.Desktop;
using GARbro.Domain;
using Xunit;

public sealed class LocalizationShortcutTests
{
    [Theory]
    [InlineData("en-US", "# GARbro user guide", "# Troubleshooting")]
    [InlineData("zh-CN", "# GARbro 使用说明", "# 故障排查")]
    [InlineData("ru-RU", "# Руководство GARbro", "# Решение проблем")]
    [InlineData("ja-JP", "# GARbro の使い方", "# トラブルシューティング")]
    public void OfflineHelpIsReadableFromThePublishedUiAssembly(string language, string manual, string troubleshooting)
    {
        Assert.StartsWith(manual, DesktopServices.ReadResource("manual."+language+".md"));
        Assert.StartsWith(troubleshooting, DesktopServices.ReadResource("troubleshoot."+language+".md"));
    }
    [Theory]
    [InlineData("ru-BY", "ru-RU")]
    [InlineData("ja", "ja-JP")]
    [InlineData("zh-TW", "zh-CN")]
    [InlineData("fr-FR", "en-US")]
    public void SystemLanguageResolutionIsSeparateFromEncoding(string system, string expected)
    {
        Assert.Equal(expected, LanguageCatalog.Resolve("auto", system));
        Assert.Equal("ja-JP", LanguageCatalog.Resolve("ja-JP", system));
        var settings = new AppSettings {Language=expected, TextEncoding="gbk"}; settings.Validate();
        Assert.Equal(936, settings.Context.ResolveEncoding().CodePage);
        Assert.Equal("gbk",settings.TextEncoding);
    }
    [Fact]
    public void SettingsKeepDisabledBindingsAndMigrateBothPreviousVersions()
    {
        string directory=Path.Combine(Path.GetTempPath(),"garbro-shortcuts-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try
        {
            var store=new SettingsStore(Path.Combine(directory,"settings.json"));
            foreach(int version in new[]{1,2})
            {
                File.WriteAllText(store.Path,"{\"Version\":"+version+",\"Language\":\"ru-RU\",\"TextEncoding\":\"shift_jis\",\"Volume\":0.4}");
                var migrated=store.Load();Assert.Null(store.LoadError);Assert.Equal(3,migrated.Version);Assert.Equal(.4,migrated.Volume);
                migrated.ShortcutBindings["Open"]=Array.Empty<string>();
                migrated.ShortcutBindings["Refresh"]=new[]{"Control+Shift+R","F5"};store.Save(migrated);
                var loaded=store.Load();Assert.Empty(ShortcutCatalog.Effective(loaded)["Open"]);
                Assert.Equal(new[]{"Ctrl+Shift+R","F5"},ShortcutCatalog.Effective(loaded)["Refresh"]);
                Assert.Equal("shift_jis",loaded.TextEncoding);
            }
        }
        finally{Directory.Delete(directory,true);}
    }
    [Fact]
    public void AliasesConflictAndAnUnknownFutureCommandDoesNotErasePreferences()
    {
        var settings=new AppSettings();settings.ShortcutBindings["Refresh"]=new[]{(OperatingSystem.IsMacOS()?"Meta":"Control")+"+O"};
        Assert.Throws<ArgumentException>(()=>settings.Validate());
        settings.ShortcutBindings["Open"]=Array.Empty<string>();
        settings.ShortcutBindings["FutureCommand"]=new[]{"FutureKey"};settings.Validate();
        Assert.Throws<ArgumentException>(()=>ShortcutCatalog.Normalize("Ctrl+Ctrl+O"));
    }
    [AvaloniaFact]
    public async Task CustomBindingUpdatesMenuAndDispatchWithoutStealingTextEditing()
    {
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");var store=new SettingsStore(path);
        var settings=new AppSettings {Language="en-US"};settings.ShortcutBindings["Toolbar"]=new[]{"Ctrl+Shift+B"};store.Save(settings);
        var window=new MainWindow(store);window.Show();
        try
        {
            var view=window.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="MenuView");
            var toggle=view.ContextMenu.Items.OfType<MenuItem>().Single(m=>m.Header as string=="Show toolbar");
            Assert.Equal("Ctrl+Shift+B",toggle.InputGesture.ToString());
            bool before=toggle.IsChecked;
            window.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.B,KeyModifiers=KeyModifiers.Control|KeyModifiers.Shift});
            await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
            Assert.Equal(!before,store.Load().ToolbarVisible);
            var search=window.GetVisualDescendants().OfType<TextBox>().Single(c=>c.Name=="Search");search.Focus();
            search.Text="text copied from editor";search.SelectAll();
            // Headless uses its own platform settings even on macOS. Match the editor's actual configuration.
            var copy=Avalonia.Application.Current.PlatformSettings.HotkeyConfiguration.Copy.First(g=>g.Key==Key.C);
            var modifiers=RawInputModifiers.None;
            if(copy.KeyModifiers.HasFlag(KeyModifiers.Control))modifiers|=RawInputModifiers.Control;
            if(copy.KeyModifiers.HasFlag(KeyModifiers.Meta))modifiers|=RawInputModifiers.Meta;
            if(copy.KeyModifiers.HasFlag(KeyModifiers.Shift))modifiers|=RawInputModifiers.Shift;
            if(copy.KeyModifiers.HasFlag(KeyModifiers.Alt))modifiers|=RawInputModifiers.Alt;
            window.DesktopHost.KeyPress(copy.Key,modifiers,PhysicalKey.C,"c");
            await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
            Assert.Equal("text copied from editor",await window.Clipboard.TryGetTextAsync());
        }
        finally{window.Close();File.Delete(path);}
    }
    [AvaloniaFact]
    public void RecordingAddsBindingsAndShowsConflictsBeforeSaving()
    {
        var settings=new AppSettings();var panel=new ShortcutSettingsPanel(settings,new DesktopText("ja-JP"));
        var host=new Window{Content=panel};host.Show();
        try
        {
            var commands=panel.GetVisualDescendants().OfType<ListBox>().Single(b=>b.Name=="ShortcutCommands");
            commands.SelectedItem=commands.Items.OfType<UiChoice>().Single(c=>c.Value=="Refresh");
            var record=panel.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string==new DesktopText("ja-JP")["ShortcutRecord"]);
            record.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            host.KeyPress(Key.O,OperatingSystem.IsMacOS()?RawInputModifiers.Meta:RawInputModifiers.Control,PhysicalKey.O,"o");
            panel.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string==new DesktopText("ja-JP")["ShortcutAdd"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.NotNull(panel.ValidationError);Assert.Contains("更新",panel.ValidationError);
            settings.ShortcutBindings["Open"]=Array.Empty<string>();Assert.Null(panel.ValidationError);
        }
        finally{host.Close();}
    }
    [Fact]
    public void FileActivationAcceptsUnicodePathsAndFileUris()
    {
        string path=Path.Combine(Path.GetTempPath(),"GARbro 中文 Русский 日本語 "+Guid.NewGuid()+".xfl");File.WriteAllText(path,"");
        try{Assert.Equal(new[]{path},FileActivationRouter.Normalize(new[]{path,new Uri(path).AbsoluteUri,"missing-file"}));}
        finally{File.Delete(path);}
    }
}
