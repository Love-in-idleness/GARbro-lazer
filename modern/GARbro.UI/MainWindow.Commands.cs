using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
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
using GARbro.Domain;

namespace GARbro.Desktop;

public sealed class DeskCommand(Func<object, Task> execute, Func<object, bool> enabled, bool concurrent = false) : ICommand
{
    public DeskCommand(Func<Task> execute, Func<bool> enabled, bool concurrent = false)
        : this(_ => execute(), _ => enabled(), concurrent) { }
    bool running;
    public event EventHandler CanExecuteChanged;
    public bool CanExecute(object parameter) => (concurrent || !running) && enabled(parameter);
    public async void Execute(object parameter)
    {
        if (!CanExecute(parameter)) return;
        running = true; Update();
        try { await execute(parameter); }
        finally { running = false; Update(); }
    }
    public void Update() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed partial class MainWindow
{
    static readonly string[] ArchiveCommands = { "ExtractSelected", "ExtractAll", "PackSelected", "PackAll" };
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
        bool CanUseListing() => !busy && !loading && !choosingOperation && listing != null;
        Register("Open", OpenPicker); Register("OpenFolder", FolderPicker);
        Register("OpenWithFormat", () => OpenWithFormat(false), () => !busy && !choosingOperation);
        Register("OpenSelectedWithFormat", () => OpenWithFormat(true),
            () => CanUseListing() && listing.Physical && files.SelectedItems.Count == 1 && files.SelectedItem is BrowserEntry { IsDirectory: false });
        Register("NewTab",NewTab,()=>!busy && !choosingOperation);
        Register("CloseTab",()=>CloseTab(currentTab),()=>!busy && !choosingOperation);
        Register("NextTab",()=>SwitchTab(1),()=>!busy && !choosingOperation && openTabs.Count>1);
        Register("PreviousTab",()=>SwitchTab(-1),()=>!busy && !choosingOperation && openTabs.Count>1);
        Register("Exit", () => { Close(); return Task.CompletedTask; }, () => true);
        Register("Cancel", () => { active?.Cancel(); navigationCancellation?.Cancel(); previewCancellation?.Cancel(); StopAudio(); return Task.CompletedTask; }, () => busy || loading || mediaPreparation!=null || previewCancellation != null || Playback != null);
        Register("Back", () => NavigateHistory(-1), () => !busy && historyIndex > 0);
        Register("Forward", () => NavigateHistory(1), () => !busy && historyIndex + 1 < history.Count);
        Register("Parent", NavigateParent, () => !busy && listing != null);
        Register("Refresh", () => LoadLocationAsync(CurrentLocation with { Fingerprint = null }, false, true),
            () => !busy && !choosingOperation && CurrentLocation != null);
        Register("Settings", () => ShowSettings());
        Register("ChangeGameScheme", ChangeGameScheme, () => !busy && !loading && !choosingOperation && listing?.ProviderId != null && !listing.Physical && listing.Tag != "media");
        foreach (string section in new[] { "General", "Appearance", "Decoding", "Formats", "Shortcuts" })
            Register(section, () => ShowSettings(section));
        Register("ExtractAll", () => ExtractPicker(false), () => CanUseListing() && listing.Entries.Length > 0);
        Register("ExtractSelected", () => ExtractPicker(true), () => CanUseListing() && files.SelectedItems.Count > 0);
        Register("PackSelected", () => CreateArchive(true), () => CanUseListing() && listing.Physical && files.SelectedItems.Count > 0);
        Register("PackAll", () => CreateArchive(false), () => CanUseListing() && listing.Physical && listing.Entries.Length > 0);
        Register("Convert", ConvertSelected, () => CanUseListing() && files.SelectedItems.Cast<BrowserEntry>().Any(e => !e.IsDirectory));
        Register("OpenSelected", OpenSelected, () => CanUseListing() && files.SelectedItem != null);
        commands.Add("OpenInNewTab", new(
            target => Guard(() => OpenEntryInNewTab((BrowserEntry)(target ?? files.SelectedItem))),
            target => CanUseListing() && (target ?? files.SelectedItem) is BrowserEntry));
        Register("SelectAll", () =>
        {
            // Bound the range even before the framework has attached its selection source.
            if (files.ItemCount > 0) files.Selection.SelectRange(0, files.ItemCount - 1);
            return Task.CompletedTask;
        }, () => !busy && files.ItemCount > 0);
        Register("CopyNames", async () =>
        {
            if (Clipboard != null) await Clipboard.SetTextAsync(string.Join(Environment.NewLine, files.SelectedItems.Cast<BrowserEntry>().Select(e => e.DisplayName)));
        }, () => CanUseListing() && files.SelectedItems.Count > 0);
        Register("Mask", async () =>
        {
            string mask = await Dialogs.Input(this, Text, "Mask", "*.*"); if (mask == null) return;
            string pattern = "^" + Regex.Escape(mask == "*.*" ? "*" : mask).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            foreach (var item in files.Items.OfType<BrowserEntry>()) if (Regex.IsMatch(item.DisplayName, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))) files.SelectedItems.Add(item);
        }, () => CanUseListing() && files.ItemCount > 0);
        Register("Explore", () => { DesktopServices.Explore(listing.Location.RootPath); return Task.CompletedTask; }, () => listing != null && (UiPlatform.CanExplore?.Invoke(listing.Location.RootPath) ?? true));
        Register("Trash", TrashSelected, () => CanUseListing() && listing.Physical && (UiPlatform.CanTrash?.Invoke() ?? true)
            && files.SelectedItems.Cast<BrowserEntry>().Any(e => !e.IsDirectory));
        Register("FitWindow", () => { if (bitmap != null) { Width = Math.Clamp(bitmap.Size.Width + 650, MinWidth, Screens?.Primary?.WorkingArea.Width ?? 2000); Height = Math.Clamp(bitmap.Size.Height + 280, MinHeight, Screens?.Primary?.WorkingArea.Height ?? 1400); browser.ColumnDefinitions[2].Width = new Avalonia.Controls.GridLength(Math.Clamp(bitmap.Size.Width, 100, Math.Max(100, Width - 600))); } return Task.CompletedTask; }, () => !UiPlatform.IsMobile && !busy && bitmap != null);
        foreach (string key in new[] { "Toolbar", "Statusbar", "FitImage" })
            Register(key, () =>
            {
                switch (key) { case "Toolbar": Settings.ToolbarVisible = !Settings.ToolbarVisible; break;
                    case "Statusbar": Settings.StatusVisible = !Settings.StatusVisible; break; case "FitImage": Settings.FitImage = !Settings.FitImage; break; }
                ApplySettings(); return key=="FitImage" && previewEntry!=null ? ShowPreviewPage(0) : Task.CompletedTask;
            });
        foreach (string page in HelpPages)
            Register(page, () => ShowHelp(page), () => !busy);
        Register("FocusMenu", () => { menu.Children.FirstOrDefault()?.Focus(); return Task.CompletedTask; }, () => true);
        Register("NextSelection", () => { files.SelectedIndex = Math.Min(files.ItemCount - 1, files.SelectedIndex + 1); return Task.CompletedTask; }, () => files.IsFocused && files.ItemCount > 0);
        foreach (var sort in new[] { ("SortName", "name"), ("SortType", "type"), ("SortSize", "size"), ("SortNone", "none") })
            Register(sort.Item1, () => { SetSort(sort.Item2); return Task.CompletedTask; });
        Register("Play", PlayAudio, () => !busy && mediaPreparation == null && preview?.Kind is "audio" or "video");
        Register("Pause", () => { Playback?.Pause(); return Task.CompletedTask; }, () => Playback != null);
        Register("Stop", () => { Playback?.Stop(); return Task.CompletedTask; }, () => Playback != null);
        Register("PreviousPage", () => ShowPreviewPage(Math.Max(0, preview.Offset - 65536)),
            () => !busy && previewCancellation == null && preview?.Offset > 0 && pageControls.IsVisible);
        Register("NextPage", () => ShowPreviewPage(preview.Offset + 65536),
            () => !busy && previewCancellation == null && preview?.More == true && pageControls.IsVisible);
        Register("Copy", async () => { if (Clipboard != null) await Clipboard.SetTextAsync(text.Text ?? ""); }, () => text.IsVisible);
        Register("JumpToOffset", JumpToOffset, () => !busy && !choosingOperation && previewCancellation == null && preview?.Kind == "binary");
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
        var file = GroupMenu("NewTab", "CloseTab", "Open", "OpenWithFormat", "OpenFolder", "Convert");
        var recent = Group("Recent"); foreach (string filename in Settings.Recent)
        { var item = new MenuItem { Header = UiPlatform.Storage.DisplayPath(filename), Icon = MaterialIcons.Create("Recent") }; item.Click += async (_, _) => await Guard(() => LoadInputsAsync(new[] { filename })); recent.Items.Add(item); }
        file.Items.Add(recent); if (!UiPlatform.IsMobile) file.Items.Add(Item("Exit"));
        var process = GroupMenu(ArchiveCommands);
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
        string[] selectionCommands = { "SelectAll", "Mask", "CopyNames", "Explore", "Trash" };
        string[] keys = selectionOnly ? selectionCommands : ["OpenSelected", "OpenInNewTab", "OpenSelectedWithFormat", .. ArchiveCommands, "Convert", .. selectionCommands, "Refresh"];
        foreach (string key in keys) context.Items.Add(Item(key));
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
    async Task OpenWithFormat(bool selectedOnly)
    {
        string input = selectedOnly ? Path.Combine(listing.Location.RootPath, ((BrowserEntry)files.SelectedItem).Name) : null;
        string filename = input == null ? null : Path.GetFileName(UiPlatform.Storage.DisplayPath(input));
        var origin = currentTab;
        int ticket = navigationGeneration;
        var format = await ChooseOperation(async () =>
        {
            if (input == null)
            {
                var picked = await UiPlatform.Storage.PickInputsAsync(false, lifetime.Token, allowMultiple: false, title: Text["OpenWithFormat"]);
                if (picked.Length == 0) return null;
                var source = picked.Single();
                input = source.Id; filename = source.DisplayName;
            }
            catalog ??= await Request<ResourceFormatInfo[]>(new("resource-catalog", null), lifetime.Token);
            if (ticket != navigationGeneration || isClosed) return null;
            return await Dialogs.InputFormat(this, Text, catalog, filename);
        });
        if (format == null || ticket != navigationGeneration || isClosed) return;
        try { await LoadInputsAsync([input], format.Id); }
        catch (IOException error)
        {
            DesktopServices.Log(error);
            if (isClosed || currentTab != origin && (currentTab.InputFormatId != format.Id || currentTab.InputComplete)) return;
            footer.IsVisible = true; status.Classes.Add("error");
            status.Text = Text.Format("OpenWithFormatFailed", format.Tag, Text.ErrorMessage(error.Message));
        }
    }

    async Task JumpToOffset()
    {
        int ticket = generation;
        long? offset = await Dialogs.JumpToOffset(this, Text, preview.Offset);
        if (offset == null || ticket != generation || isClosed) return;
        await ShowPreviewPage(offset.Value, keepPageOnError: true, forceHex: true);
    }
    async Task NavigateHistory(int delta)
    {
        int target = historyIndex + delta;
        int ticket = navigationGeneration + 1;
        await LoadLocationAsync(history[target], false);
        if (ticket == navigationGeneration && !isClosed)
        {
            historyIndex = target;
            UpdateCommands();
        }
    }
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
        string database = Settings.SchemesFile;
        await new HelpWindow(Text, Settings, catalog, page, store).ShowDialog(this);
        if (database != Settings.SchemesFile && !isClosed)
        {
            catalog = null;
            previewCache.Clear();
            catalog = await Request<ResourceFormatInfo[]>(new("resource-catalog", null), lifetime.Token);
            bool needsScheme = RemoveUnavailableSchemes();
            if (listing != null) await LoadLocationAsync(listing.Location with { Fingerprint = null }, false, true);
            if (needsScheme && !isClosed) status.Text = Text["SchemeSelectionNeedsUpdate"];
        }
    }
    bool RemoveUnavailableSchemes()
    {
        var names = catalog.Where(format => format.Schemes != null).ToDictionary(format => format.Id, format => format.Schemes);
        bool removed = false;
        foreach (var profile in sessionOptions.Values)
        foreach (var (key, value) in profile.ToArray())
        {
            int separator = key.LastIndexOf("::", StringComparison.Ordinal);
            string id = separator < 0 ? key : key[(separator + 2)..];
            string name = SchemeName(value);
            if (name is "" or "none" || !names.TryGetValue(id, out var choices) || choices.Contains(name)) continue;
            var parameters = (JsonObject)JsonNode.Parse(value);
            parameters.Remove("$scheme");
            if (parameters.Count == 0) profile.Remove(key);
            else profile[key] = parameters.ToJsonString();
            removed = true;
        }
        return removed;
    }
    sealed record ExtractionSource(ResourceLocation Location,string[] Selection,Dictionary<string,string> Overrides,bool Physical,string Tag);
    bool choosingOperation;
    async Task<T> ChooseOperation<T>(Func<Task<T>> choose) where T:class
    {
        if(choosingOperation)return null;
        choosingOperation=true;UpdateCommands();
        try {return await choose();}
        finally {choosingOperation=false;UpdateCommands();}
    }
    static string SchemeName(string json) => json == null ? "" :
        (JsonNode.Parse(json) as JsonObject)?["$scheme"] is JsonValue value && value.TryGetValue<string>(out var name) ? name : "";

    void RefreshSchemeButton()
    {
        var source = listing;
        string name = source?.ProviderId == null ? "" : SchemeName(sessionOptions.GetValueOrDefault(source.Location.RootPath)
            ?.GetValueOrDefault(ScopedOptionsProvider.FormatKey(source.Location, source.ProviderId)));
        string label = SchemePicker.Label(Text, name);
        scheme.Content = label + "…";
        ToolTip.SetTip(scheme, label);
        Avalonia.Automation.AutomationProperties.SetName(scheme, Text["Scheme"] + " · " + label);
    }

    async Task ChangeGameScheme()
    {
        var source = listing;
        var location = source.Location;
        int ticket = navigationGeneration;
        string key = ScopedOptionsProvider.FormatKey(location, source.ProviderId);
        if (!sessionOptions.TryGetValue(location.RootPath, out var profile)) sessionOptions[location.RootPath] = profile = new();
        string previous = profile.GetValueOrDefault(key);
        string selected = SchemeName(previous);
        var choice = await ChooseOperation(async () =>
        {
            catalog ??= await Request<ResourceFormatInfo[]>(new("resource-catalog", null));
            if (ticket != navigationGeneration || isClosed) return null;
            var available = catalog.FirstOrDefault(f => f.Id == source.ProviderId)?.Schemes;
            if (available is { Length: > 0 }) return await Dialogs.SelectScheme(this, Text, available, selected);
            status.Text = Text["NoGameSchemes"];
            return null;
        });
        if (choice == null || choice.Value == selected || ticket != navigationGeneration || isClosed) return;
        var parameters = JsonNode.Parse(previous ?? (string.IsNullOrWhiteSpace(options.Text) ? "{}" : options.Text)) as JsonObject ?? new();
        if (choice.Value.Length == 0) parameters.Remove("$scheme");
        else parameters["$scheme"] = choice.Value;
        if (parameters.Count == 0) profile.Remove(key);
        else profile[key] = parameters.ToJsonString();
        try { await LoadLocationAsync(location with { Fingerprint = null }, false, true); }
        catch
        {
            if (previous == null) profile.Remove(key);
            else profile[key] = previous;
            RefreshSchemeButton();
            throw;
        }
    }
    ExtractionSource CaptureExtraction(bool selectedOnly)
    {
        if(listing==null)throw new InvalidOperationException("Open a resource first.");
        return new(listing.Location,selectedOnly?files.SelectedItems.Cast<BrowserEntry>().Select(e=>e.Name).ToArray():null,
            new(typeOverrides),listing.Physical,listing.Tag);
    }
    string DefaultOutputDirectory(ResourceLocation source, bool physical)
    {
        if (!string.IsNullOrWhiteSpace(Settings.OutputDirectory)) return Settings.OutputDirectory;
        if (UiPlatform.IsMobile) return null;
        return physical ? source.RootPath : Path.GetDirectoryName(source.RootPath);
    }
    async Task ExtractPicker(bool selectedOnly)
    {
        if(listing==null)return;
        var source=CaptureExtraction(selectedOnly);int ticket=navigationGeneration;
        if(source.Selection is {Length:0}){status.Text=Text["SelectFiles"];return;}
        var choice = await ChooseOperation(async () =>
        {
            catalog ??= await Request<ResourceFormatInfo[]>(new("resource-catalog", null), lifetime.Token);
            if (ticket != navigationGeneration || isClosed) return null;
            return await Dialogs.Extraction(this, Text, DefaultOutputDirectory(source.Location, source.Physical),
                selectedOnly, source.Selection?.Length ?? 0, catalog.Where(f => f.Type == "image" && f.CanWrite).ToArray());
        });
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
    async Task CreateArchive(bool selectedOnly)
    {
        var source = listing.Location;
        int ticket = navigationGeneration;
        var entries = selectedOnly ? files.SelectedItems.Cast<BrowserEntry>() : listing.Entries;
        string[] inputs = entries.Select(e => e.Name).ToArray();
        if (inputs.Length == 0) return;
        var choice=await ChooseOperation(async()=>
        {
            catalog??=await Request<ResourceFormatInfo[]>(new("resource-catalog",null));
            if(ticket!=navigationGeneration){status.Text=Text["ExtractionSourceChanged"];return (CreationChoice)null;}
            return await Dialogs.Creation(this,Text,catalog,source.RootPath,inputs.Length,selectedOnly,DefaultOutputDirectory(source,true));
        });
        if(choice==null)return;
        if(ticket!=navigationGeneration){status.Text=Text["ExtractionSourceChanged"];return;}
        previewCancellation?.Cancel();StopAudio();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); active = operation; SetBusy(true);
        var updates=new Progress<ExtractionProgress>(p=>
        {
            progress.IsIndeterminate=p.Total<=0; if(p.Total>0)progress.Value=(double)p.Completed/p.Total;
            string phase=p.Phase switch {"collect"=>"PackingCollect","convert"=>"PackingConvert",_=>"PackingWrite"};
            status.Text=Text[phase]+" · "+p.Entry;
        });
        try
        {
            var result=await Request<ArchivePackingResult>(new("resource-pack", source.RootPath, Output: choice.Output, Inputs: inputs, FormatId: choice.FormatId, OptionsJson: choice.OptionsJson, Overwrite: choice.Overwrite, PackingOptions:choice.PackingOptions), operation.Token,updates);
            status.Text=result.Written?Text.Format("PackingCompleted",result.Entries,result.Converted):Text["PackingSkipped"];
        }
        finally { active = null; SetBusy(false); }
    }
    async Task TrashSelected()
    {
        var source = listing.Location;
        int ticket = navigationGeneration;
        var paths = files.SelectedItems.Cast<BrowserEntry>().Where(e => !e.IsDirectory)
            .Select(e => Path.Combine(source.RootPath, e.Name)).ToArray();
        string[] confirmed = await ChooseOperation(async () =>
            await Dialogs.Confirm(this, Text, Text.Format("TrashConfirm", paths.Length)) ? paths : null);
        if (confirmed == null || ticket != navigationGeneration || isClosed) return;
        previewCancellation?.Cancel();
        StopAudio();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        active = operation;
        SetBusy(true);
        bool changed = false;
        try
        {
            await mediaCleanup;
            await Request<bool>(new("resource-release", null), operation.Token);
            foreach (string filename in confirmed)
            {
                operation.Token.ThrowIfCancellationRequested();
                await DesktopServices.Trash(filename);
                changed = true;
            }
        }
        finally
        {
            active = null;
            SetBusy(false);
            if (changed && ticket == navigationGeneration && !isClosed)
                await Guard(() => LoadLocationAsync(source, false, true));
        }
    }
    async Task ConvertSelected()
    {
        var selected=files.SelectedItems.Cast<BrowserEntry>().Where(e=>!e.IsDirectory).ToArray();
        if(selected.Length==0){status.Text=Text["SelectFiles"];return;}
        var source=listing.Location;string output=DefaultOutputDirectory(source,listing.Physical);var overrides=new Dictionary<string,string>(typeOverrides);int ticket=navigationGeneration;
        var choice=await ChooseOperation(async()=>
        {
            catalog??=await Request<ResourceFormatInfo[]>(new("resource-catalog",null));
            if(ticket!=navigationGeneration){status.Text=Text["ExtractionSourceChanged"];return (ConversionChoice)null;}
            bool offerScripts= selected.Any(e=>ScriptOutputFormats.ForInput(e.Name)!=null || ScriptOutputFormats.NeedsSignature(e.Name) || ScriptOutputFormats.HasRecompileInput(e.Name));
            return await Dialogs.Conversion(this,Text,catalog.Where(f=>f.Type=="image"&&f.CanWrite).ToArray(),output,selected.Length,offerScripts);
        });
        if(choice==null)return;
        if(ticket!=navigationGeneration){status.Text=Text["ExtractionSourceChanged"];return;}
        if(choice.ConvertScripts && choice.ScriptFormat!=null)
        {
            var destinations=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in selected.Where(e=>ScriptOutputFormats.CanRecompile(e.Name,choice.ScriptFormat)))
            {
                string name=ScriptOutputFormats.RecompileOutput(ExtractionScope.RelativeName(source.Directory,entry.Name),choice.ScriptFormat);
                string destination=ExtractionPath.Resolve(choice.Output,name);
                if(!destinations.Add(destination))throw new IOException("Conversion output collides with another resource: "+destination);
            }
        }
        previewCancellation?.Cancel();StopAudio();using var operation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);active=operation;SetBusy(true);
        var failures=new List<string>();
        try
        {
            int done=0,skipped=0,processed=0;
            progress.IsIndeterminate=false;progress.Value=0;
            foreach(var entry in selected)
            {
                operation.Token.ThrowIfCancellationRequested();
                status.Text=Text.Format("ConversionProgress",processed+1,selected.Length,entry.DisplayName);
                try
                {
                    string target=ExtractionPath.Resolve(choice.Output,ExtractionScope.RelativeName(source.Directory,entry.Name));
                    var result=await Request<ResourceConversion>(new("resource-convert",null,Location:source,Entry:entry.Name,Output:target,FormatId:"MEDIA",Overwrite:choice.Overwrite,
                        ResourceOptions:new(ImageFormat:choice.FormatId,AudioFormat:choice.AudioFormat,VideoFormat:choice.VideoFormat,
                            TypeOverrides:overrides,DiscardOriginalMedia:choice.DiscardOriginalMedia,ConvertScripts:choice.ConvertScripts,ScriptFormat:choice.ScriptFormat,
                            AdjustImageOffset:choice.AdjustImageOffset)),operation.Token);
                    if(result.Path!=null && result.Written)done++;else skipped++;
                }
                catch(WorkerOperationException e){failures.Add(entry.DisplayName+": "+e.Message);DesktopServices.Log(e);}
                processed++;progress.Value=processed/(double)selected.Length;
            }
            status.Text=Text.Format("ConversionCompleted",done,skipped,failures.Count);
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
