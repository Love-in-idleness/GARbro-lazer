using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using GARbro.Application;

namespace GARbro.Desktop;

public sealed class DeskCommand(Func<Task> execute, Func<bool> enabled, bool concurrent = false) : ICommand
{
    bool running;
    public event EventHandler CanExecuteChanged;
    public bool CanExecute(object parameter) => (concurrent || !running) && enabled();
    public async void Execute(object parameter)
    {
        if (!CanExecute(parameter)) return;
        running = true; Update();
        try { await execute(); }
        finally { running = false; Update(); }
    }
    public void Update() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed partial class MainWindow
{
    (KeyGesture Gesture, string Command)[] shortcutMap = Array.Empty<(KeyGesture, string)>();
    Dictionary<string, string[]> effectiveShortcuts;
    void ApplyShortcutBindings()
    {
        effectiveShortcuts = ShortcutCatalog.Effective(Settings);
        shortcutMap = effectiveShortcuts.SelectMany(p => p.Value.Select(v => (KeyGesture.Parse(v), p.Key))).ToArray();
        foreach (var button in this.GetVisualDescendants().OfType<Button>())
            if (button.Command is DeskCommand command)
            {
                string id = commands.FirstOrDefault(p => ReferenceEquals(p.Value, command)).Key;
                if (id != null) ToolTip.SetTip(button, CommandTip(id));
            }
    }
    string CommandTip(string key) => Text[key] + (effectiveShortcuts?.GetValueOrDefault(key) is { Length: > 0 } values ? " (" + string.Join(", ", values) + ")" : "");
    void Register(string key, Func<Task> action, Func<bool> enabled = null) =>
        commands.Add(key, new(() => Guard(action), enabled ?? (() => !busy), key is "Open" or "OpenFolder" or "Back" or "Forward" or "Parent" or "Refresh"));
    void CreateCommands()
    {
        Register("Open", OpenPicker); Register("OpenFolder", FolderPicker);
        Register("Exit", () => { Close(); return Task.CompletedTask; }, () => true);
        Register("Cancel", () => { active?.Cancel(); navigationCancellation?.Cancel(); previewCancellation?.Cancel(); StopAudio(); return Task.CompletedTask; }, () => busy || loading || mediaPreparation!=null || previewCancellation != null || Playback != null);
        Register("Back", () => NavigateHistory(-1), () => !busy && historyIndex > 0);
        Register("Forward", () => NavigateHistory(1), () => !busy && historyIndex + 1 < history.Count);
        Register("Parent", NavigateParent, () => !busy && listing != null);
        Register("Refresh", () => CurrentLocation == null ? Task.CompletedTask : LoadLocationAsync(CurrentLocation with { Fingerprint = null }, false, true));
        Register("Settings", () => ShowSettings());
        foreach (string section in new[] { "General", "Appearance", "Decoding", "Formats", "Shortcuts" })
            Register(section, () => ShowSettings(section));
        Register("ExtractAll", () => ExtractPicker(false), () => !busy && !loading && listing != null);
        Register("ExtractSelected", () => ExtractPicker(true), () => !busy && !loading && listing != null && files.SelectedItems.Count > 0);
        Register("Create", CreateArchive, () => !busy && listing?.Physical == true);
        Register("Convert", ConvertSelected, () => !busy && !loading && listing != null && files.SelectedItems.Cast<BrowserEntry>().Any(e => !e.IsDirectory));
        Register("OpenSelected", OpenSelected, () => !busy && listing != null);
        Register("SelectAll", () => { files.SelectAll(); return Task.CompletedTask; });
        Register("CopyNames", async () => { if (Clipboard != null) await Clipboard.SetTextAsync(string.Join(Environment.NewLine, files.SelectedItems.Cast<BrowserEntry>().Select(e => e.Name))); });
        Register("Mask", async () =>
        {
            string mask = await Dialogs.Input(this, Text, "Mask", "*.*"); if (mask == null) return;
            string pattern = "^" + Regex.Escape(mask == "*.*" ? "*" : mask).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            foreach (var item in files.Items.OfType<BrowserEntry>()) if (Regex.IsMatch(item.DisplayName, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))) files.SelectedItems.Add(item);
        });
        Register("Explore", () => { DesktopServices.Explore(listing.Location.RootPath); return Task.CompletedTask; }, () => listing != null && (UiPlatform.CanExplore?.Invoke(listing.Location.RootPath) ?? true));
        Register("Trash", async () =>
        {
            if (!await Dialogs.Confirm(this, Text, "TrashConfirm")) return;
            StopAudio();await mediaCleanup;
            await Request<bool>(new("resource-release",null),lifetime.Token);
            foreach (var item in files.SelectedItems.Cast<BrowserEntry>().Where(e => !e.IsDirectory).ToArray())
                await DesktopServices.Trash(Path.Combine(listing.Location.RootPath, item.Name));
            await LoadLocationAsync(listing.Location, false);
        }, () => !busy && listing?.Physical == true && (UiPlatform.CanTrash?.Invoke() ?? true));
        Register("FitWindow", () => { if (bitmap != null) { Width = Math.Clamp(bitmap.Size.Width + 650, MinWidth, Screens?.Primary?.WorkingArea.Width ?? 2000); Height = Math.Clamp(bitmap.Size.Height + 280, MinHeight, Screens?.Primary?.WorkingArea.Height ?? 1400); browser.ColumnDefinitions[2].Width = new Avalonia.Controls.GridLength(Math.Clamp(bitmap.Size.Width, 100, Math.Max(100, Width - 600))); } return Task.CompletedTask; }, () => !UiPlatform.IsMobile && !busy && bitmap != null);
        foreach (string key in new[] { "Toolbar", "Menubar", "Statusbar", "FitImage" })
            Register(key, () =>
            {
                switch (key) { case "Toolbar": Settings.ToolbarVisible = !Settings.ToolbarVisible; break; case "Menubar": Settings.ToolbarVisible = !Settings.ToolbarVisible; break;
                    case "Statusbar": Settings.StatusVisible = !Settings.StatusVisible; break; case "FitImage": Settings.FitImage = !Settings.FitImage; break; }
                ApplySettings(); return key=="FitImage" && previewEntry!=null ? ShowPreviewPage(0) : Task.CompletedTask;
            });
        foreach (string page in new[] { "Feedback", "Manual", "Troubleshoot", "Updates", "License", "ExportLogs", "About" })
            Register(page, () => ShowHelp(page), () => !busy);
        Register("FocusMenu", () => { menu.Children.FirstOrDefault()?.Focus(); return Task.CompletedTask; }, () => true);
        Register("NextSelection", () => { files.SelectedIndex = Math.Min(files.ItemCount - 1, files.SelectedIndex + 1); return Task.CompletedTask; }, () => files.IsFocused && files.ItemCount > 0);
        foreach (var sort in new[] { ("SortName", "name"), ("SortType", "type"), ("SortSize", "size"), ("SortNone", "none") })
            Register(sort.Item1, () => { SetSort(sort.Item2); return Task.CompletedTask; });
        Register("Play", PlayAudio, () => !busy && mediaPreparation == null && preview?.Kind is "audio" or "video");
        Register("Pause", () => { Playback?.Pause(); return Task.CompletedTask; }, () => Playback != null);
        Register("Stop", () => { Playback?.Stop(); return Task.CompletedTask; }, () => Playback != null);
        Register("PreviousPage", () => ShowPreviewPage(Math.Max(0, (preview?.Offset ?? 0) - 65536)), () => preview != null && pageControls.IsVisible);
        Register("NextPage", () => ShowPreviewPage((preview?.Offset ?? 0) + 65536), () => preview?.More == true);
        Register("Copy", async () => { if (Clipboard != null) await Clipboard.SetTextAsync(text.Text ?? ""); }, () => text.IsVisible);
        Register("Wrap", () => { Settings.WrapText = !Settings.WrapText; ApplySettings(); return Task.CompletedTask; });
        ApplyShortcutBindings();
    }
    void SetSort(string value)
    { Settings.SortDescending = Settings.Sort == value && !Settings.SortDescending; Settings.Sort = value; ApplySettings(); }
    MenuItem Item(string key)
    {
        var item = new MenuItem { Command = commands[key], Icon = MaterialIcons.Create(key) }; item.Bind(MenuItem.HeaderProperty, Bind(key));
        if (effectiveShortcuts?.GetValueOrDefault(key)?.FirstOrDefault() is string gesture) item.InputGesture = KeyGesture.Parse(gesture);
        ToolTip.SetTip(item, CommandTip(key));
        return item;
    }
    MenuItem Group(string key) { var item = new MenuItem { Icon = MaterialIcons.Create(key) }; item.Bind(MenuItem.HeaderProperty, Bind(key)); return item; }
    void BuildMenus()
    {
        menu.Children.Clear();
        ContextMenu GroupMenu(params string[] keys)
        { var popup = new ContextMenu(); foreach (string key in keys) popup.Items.Add(Item(key)); return popup; }
        var file = GroupMenu("Open", "OpenFolder", "Create", "Convert");
        var recent = Group("Recent"); foreach (string filename in Settings.Recent)
        { var item = new MenuItem { Header = UiPlatform.Storage.DisplayPath(filename), Icon = MaterialIcons.Create("Recent") }; item.Click += async (_, _) => await Guard(() => LoadInputsAsync(new[] { filename })); recent.Items.Add(item); }
        file.Items.Add(recent); if (!UiPlatform.IsMobile) file.Items.Add(Item("Exit"));
        var process = GroupMenu("ExtractSelected", "ExtractAll");
        var selection = BuildContextMenu(true);
        var view = GroupMenu("Back", "Forward", "Parent", "Refresh", "FitWindow");
        var fit = Item("FitImage"); fit.ToggleType = MenuItemToggleType.CheckBox; fit.IsChecked = Settings.FitImage; view.Items.Add(fit);
        var sort = Group("Sort"); foreach (var pair in new[] { ("SortName", "name"), ("SortType", "type"), ("SortSize", "size"), ("SortNone", "none") })
        { var item = Item(pair.Item1); item.ToggleType = MenuItemToggleType.Radio; item.IsChecked = Settings.Sort == pair.Item2; sort.Items.Add(item); }
        view.Items.Add(sort);
        foreach (string key in new[] { "Toolbar", "Statusbar" })
        { var item = Item(key); item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = key == "Toolbar" ? Settings.ToolbarVisible : Settings.StatusVisible; view.Items.Add(item); }
        var settings = GroupMenu("General", "Appearance", "Decoding", "Formats", "Shortcuts");
        var help = GroupMenu(HelpPages);
        foreach (var group in new[] { ("File", file), ("Process", process), ("Selection", selection), ("View", view), ("Settings", settings), ("Help", help) })
        {
            var button = new Button { Name = "Menu" + group.Item1, Content = MaterialIcons.Create(group.Item1), ContextMenu = group.Item2, Classes = { "iconMenu" } };
            button.Bind(ToolTip.TipProperty, Bind(group.Item1)); button.Bind(Avalonia.Automation.AutomationProperties.NameProperty, Bind(group.Item1));
            button.Click += (_, _) => button.ContextMenu.Open(button); menu.Children.Add(button);
        }
    }
    static readonly string[] HelpPages = { "Manual", "Feedback", "Troubleshoot", "Updates", "License", "ExportLogs", "About" };
    void ShowHelpMenu(Control target)
    {
        var popup = new ContextMenu(); foreach (string page in HelpPages) popup.Items.Add(Item(page)); target.ContextMenu = popup; popup.Open(target);
    }
    ContextMenu BuildContextMenu(bool selectionOnly = false)
    {
        var context = new ContextMenu();
        foreach (string key in selectionOnly ? new[] { "SelectAll", "Mask", "CopyNames", "Explore", "Trash" } : new[] { "OpenSelected", "ExtractSelected", "ExtractAll", "Create", "Convert", "SelectAll", "Mask", "CopyNames", "Explore", "Trash", "Refresh" }) context.Items.Add(Item(key));
        var type = Group("AssignType"); foreach (var pair in new[] { ("Automatic", ""), ("Images", "image"), ("Audio", "audio"), ("Video", "video"), ("Scripts", "script"), ("Binary", "binary") })
        {
            var item = Group(pair.Item1); item.Click += async (_, _) =>
            {
                foreach (var entry in files.SelectedItems.Cast<BrowserEntry>()) { if (pair.Item2 == "") typeOverrides.Remove(entry.Name); else typeOverrides[entry.Name] = pair.Item2; }
                await PreviewSelection();
            }; type.Items.Add(item);
        }
        context.Items.Add(type); return context;
    }
    async Task OpenPicker()
    {
        var selected = await UiPlatform.Storage.PickInputsAsync(false); if (selected.Length > 0) await LoadInputsAsync(selected.Select(f => f.Id));
    }
    async Task FolderPicker()
    {
        var selected = await UiPlatform.Storage.PickInputsAsync(true); if (selected.Length > 0) await LoadInputsAsync(selected.Select(f => f.Id));
    }
    async Task NavigateHistory(int delta) { historyIndex += delta; await LoadLocationAsync(history[historyIndex], false); }
    async Task NavigateParent()
    {
        if (listing == null) return; var location = listing.Location;
        if (listing.Physical) { var parent = UiPlatform.Storage.ParentPath(location.RootPath); if (parent != null) await LoadLocationAsync(new(parent)); }
        else if (location.Directory.Length > 0) await LoadLocationAsync(location with { Directory = (Path.GetDirectoryName(location.Directory.TrimEnd('/')) ?? "").Replace('\\', '/') is string d && d.Length > 0 ? d + "/" : "" });
        else if (location.Chain.Length > 0) await LoadLocationAsync(location with { Archives = location.Chain[..^1], Directory = "" });
        else if (UiPlatform.Storage.ParentPath(location.RootPath) is string parent) await LoadLocationAsync(new(parent));
    }
    async Task ShowSettings(string section = "General")
    {
        previewCancellation?.Cancel(); StopAudio();
        catalog ??= await Request<ResourceFormatInfo[]>(new("resource-catalog", null));
        var selected = files.SelectedItem as BrowserEntry;
        if (await new SettingsWindow(Settings, Text, catalog, section).ShowDialog<bool>(this))
        {
            ApplySettings(); catalog = null;
            if (listing != null)
            {
                await LoadLocationAsync(listing.Location with { Fingerprint = null }, false, true);
                if (selected != null) files.SelectedItem = listing.Entries.FirstOrDefault(e => selected.Index >= 0 ? e.Index == selected.Index : e.Name == selected.Name);
            }
        }
    }
    async Task ShowHelp(string page)
    {
        if (page == "Feedback") { DesktopServices.OpenUrl(DesktopServices.Repository + "/issues/new/choose"); return; }
        if (page == "ExportLogs")
        {
            var selected = await UiPlatform.Storage.PickOutputAsync(false, "GARbro-diagnostics.zip");
            string target = selected?.LocalPath; if (target != null) { DesktopServices.ExportLogs(target, Settings); await UiPlatform.Storage.CommitAsync(target, OverwritePolicy.Fail); status.Text = Text.Format("Exported", UiPlatform.Storage.DisplayPath(target)); } return;
        }
        catalog ??= await Request<ResourceFormatInfo[]>(new("resource-catalog", null));
        await new HelpWindow(Text, Settings, catalog, page, store).ShowDialog(this);
    }
    sealed record ExtractionSource(ResourceLocation Location,string[] Selection,Dictionary<string,string> Overrides,bool Physical,string Tag);
    ExtractionSource CaptureExtraction(bool selectedOnly)
    {
        if(listing==null)throw new InvalidOperationException("Open a resource first.");
        return new(listing.Location,selectedOnly?files.SelectedItems.Cast<BrowserEntry>().Select(e=>e.Name).ToArray():null,
            new(typeOverrides),listing.Physical,listing.Tag);
    }
    async Task ExtractPicker(bool selectedOnly)
    {
        if(listing==null)return;
        var source=CaptureExtraction(selectedOnly);int ticket=navigationGeneration;
        if(source.Selection is {Length:0}){status.Text=Text["SelectFiles"];return;}
        var choice=await Dialogs.Extraction(this,Text,Settings.OutputDirectory,selectedOnly,source.Selection?.Length??0);
        if(choice==null)return;
        if(ticket!=navigationGeneration){status.Text=Text["ExtractionSourceChanged"];return;}
        string output=source.Physical || source.Tag=="media"?choice.Output:Path.Combine(choice.Output,
            Path.GetFileName(source.Location.Chain.LastOrDefault()??source.Location.RootPath)+".unpacked");
        await ExtractInternal(output,source,choice.Options,choice.Overwrite);
    }
    public Task ExtractToAsync(string root,bool selectedOnly=false,ResourceExtractionOptions extraction=null,OverwritePolicy overwrite=OverwritePolicy.Fail)
        =>ExtractInternal(root,CaptureExtraction(selectedOnly),extraction??new(),overwrite);
    async Task ExtractInternal(string root,ExtractionSource source,ResourceExtractionOptions extraction,OverwritePolicy overwrite)
    {
        if(source.Selection is {Length:0}){status.Text=Text["SelectFiles"];return;}
        previewCancellation?.Cancel();StopAudio();using var operation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);active=operation;SetBusy(true);
        var updates=new Progress<ExtractionProgress>(p=>{progress.IsIndeterminate=false;progress.Value=p.Total==0?0:(double)p.Completed/p.Total;status.Text=$"{p.Completed}/{p.Total} · {p.Entry}";});
        ResourceJobReport report=null;
        try
        {
            report=await Request<ResourceJobReport>(new("resource-extract",null,Location:source.Location,Output:Path.GetFullPath(root),
                Selection:source.Selection,ResourceOptions:extraction with {TypeOverrides=source.Overrides},Overwrite:overwrite),operation.Token,updates);
            status.Text=report.Files.Length==0 && report.Errors.Length==0?Text["NoExtractableFiles"]:Text.Format("Completed",report.Files.Length,report.Errors.Length);
            foreach(string error in report.Errors)DesktopServices.Log(new IOException(error));
        }
        finally {if(ReferenceEquals(active,operation))active=null;SetBusy(false);}
        if(report?.Errors.Length>0)await Dialogs.ExtractionErrors(this,Text,report.Errors);
    }
    async Task CreateArchive()
    {
        catalog ??= await Request<ResourceFormatInfo[]>(new("resource-catalog", null));
        var choice = await Dialogs.Creation(this, Text, catalog.Where(f => f.Type == "archive" && f.CanWrite).ToArray());
        if (choice == null) return;
        StopAudio();
        string[] inputs = files.SelectedItems.Cast<BrowserEntry>().Select(e => e.Name).ToArray();
        if (inputs.Length == 0) inputs = listing.Entries.Select(e => e.Name).ToArray();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); active = operation; SetBusy(true);
        try { await Request<string>(new("resource-create", listing.Location.RootPath, Output: choice.Output, Inputs: inputs, FormatId: choice.FormatId, OptionsJson: choice.OptionsJson, Overwrite: choice.Overwrite), operation.Token); }
        finally { active = null; SetBusy(false); }
    }
    async Task ConvertSelected()
    {
        var selected=files.SelectedItems.Cast<BrowserEntry>().Where(e=>!e.IsDirectory).ToArray();
        if(selected.Length==0){status.Text=Text["SelectFiles"];return;}
        var source=listing.Location;var overrides=new Dictionary<string,string>(typeOverrides);int ticket=navigationGeneration;
        catalog??=await Request<ResourceFormatInfo[]>(new("resource-catalog",null));
        var choice=await Dialogs.Conversion(this,Text,catalog.Where(f=>f.Type=="image" && f.CanWrite).ToArray(),Settings.OutputDirectory);
        if(choice==null)return;
        if(ticket!=navigationGeneration){status.Text=Text["ExtractionSourceChanged"];return;}
        StopAudio();using var operation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);active=operation;SetBusy(true);
        var failures=new List<string>();
        try
        {
            int done=0;
            foreach(var entry in selected)
            {
                try
                {
                    string target=ExtractionPath.Resolve(choice.Output,entry.Name);
                    var result=await Request<ResourceConversion>(new("resource-convert",null,Location:source,Entry:entry.Name,Output:target,FormatId:"MEDIA",Overwrite:choice.Overwrite,
                        ResourceOptions:new(ImageFormat:choice.FormatId,AudioFormat:choice.AudioFormat,VideoFormat:choice.VideoFormat,
                            TypeOverrides:overrides,DiscardOriginalMedia:choice.DiscardOriginalMedia)),operation.Token);
                    if(result.Path!=null)done++;
                }
                catch(WorkerOperationException e){failures.Add(entry.DisplayName+": "+e.Message);DesktopServices.Log(e);}
            }
            status.Text=done==0 && failures.Count==0?Text["NoConversionMatched"]:Text.Format("Completed",done,failures.Count);
        }
        finally {active=null;SetBusy(false);}
        if(failures.Count>0)await Dialogs.ExtractionErrors(this,Text,failures.ToArray());
    }
    async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key is Key.ImeProcessed or Key.DeadCharProcessed) return;
        bool editing = e.Source is Control source && (source is TextBox || source.FindAncestorOfType<TextBox>() != null);
        // Let editors and IMEs own typing, composition and standard edit/navigation keys.
        if (editing && (e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.Key is Key.A or Key.C or Key.V or Key.X or Key.Z or Key.Y or Key.Enter or Key.Escape or Key.Back or Key.Delete or Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown ||
            (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0 && e.Key is not (>= Key.F1 and <= Key.F24))) return;
        foreach (var binding in shortcutMap)
            if (binding.Gesture.Key == e.Key && binding.Gesture.KeyModifiers == e.KeyModifiers && commands[binding.Command].CanExecute(null))
            { e.Handled = true; commands[binding.Command].Execute(null); break; }
        await Task.CompletedTask;
    }
}
