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

public sealed class DeskCommand(Func<Task> execute, Func<bool> enabled) : ICommand
{
    bool running;
    public event EventHandler CanExecuteChanged;
    public bool CanExecute(object parameter) => !running && enabled();
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
    void Register(string key, Func<Task> action, Func<bool> enabled = null) =>
        commands.Add(key, new(() => Guard(action), enabled ?? (() => !busy)));
    void CreateCommands()
    {
        Register("Open", OpenPicker); Register("OpenFolder", FolderPicker);
        Register("Exit", () => { Close(); return Task.CompletedTask; }, () => true);
        Register("Cancel", () => { active?.Cancel(); previewCancellation?.Cancel(); StopAudio(); return Task.CompletedTask; }, () => busy || previewCancellation != null || Playback != null);
        Register("Back", () => NavigateHistory(-1), () => !busy && historyIndex > 0);
        Register("Forward", () => NavigateHistory(1), () => !busy && historyIndex + 1 < history.Count);
        Register("Parent", NavigateParent, () => !busy && listing != null);
        Register("Refresh", () => CurrentLocation == null ? Task.CompletedTask : LoadLocationAsync(CurrentLocation with { Fingerprint = null }, false));
        Register("Settings", () => ShowSettings());
        foreach (string section in new[] { "General", "Appearance", "Decoding", "Formats" })
            Register(section, () => ShowSettings(section));
        Register("ExtractAll", () => ExtractPicker(false, false), () => !busy && listing != null);
        Register("ExtractSelected", () => ExtractPicker(true, false), () => !busy && listing != null && files.SelectedItems.Count > 0);
        Register("Recursive", () => ExtractPicker(false, true), () => !busy && listing != null);
        Register("Create", CreateArchive, () => !busy && listing?.Physical == true);
        Register("Convert", ConvertSelected, () => !busy && listing != null && files.SelectedItems.Cast<BrowserEntry>().Any(e => !e.IsDirectory));
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
            StopAudio(); foreach (var item in files.SelectedItems.Cast<BrowserEntry>().Where(e => !e.IsDirectory).ToArray())
                await DesktopServices.Trash(Path.Combine(listing.Location.RootPath, item.Name));
            await LoadLocationAsync(listing.Location, false);
        }, () => !busy && listing?.Physical == true && (UiPlatform.CanTrash?.Invoke() ?? true));
        Register("FitWindow", () => { if (bitmap != null) { Width = Math.Clamp(bitmap.Size.Width + 650, MinWidth, Screens?.Primary?.WorkingArea.Width ?? 2000); Height = Math.Clamp(bitmap.Size.Height + 280, MinHeight, Screens?.Primary?.WorkingArea.Height ?? 1400); browser.ColumnDefinitions[2].Width = new Avalonia.Controls.GridLength(Math.Clamp(bitmap.Size.Width, 100, Math.Max(100, Width - 600))); } return Task.CompletedTask; }, () => !UiPlatform.IsMobile && !busy && bitmap != null);
        foreach (string key in new[] { "Toolbar", "Menubar", "Statusbar", "FitImage" })
            Register(key, () =>
            {
                switch (key) { case "Toolbar": Settings.ToolbarVisible = !Settings.ToolbarVisible; break; case "Menubar": Settings.ToolbarVisible = !Settings.ToolbarVisible; break;
                    case "Statusbar": Settings.StatusVisible = !Settings.StatusVisible; break; case "FitImage": Settings.FitImage = !Settings.FitImage; break; }
                ApplySettings(); return Task.CompletedTask;
            });
        foreach (string page in new[] { "Feedback", "Manual", "Troubleshoot", "Updates", "License", "ExportLogs", "About" })
            Register(page, () => ShowHelp(page), () => !busy);
    }
    MenuItem Item(string key, string gesture = null)
    {
        var item = new MenuItem { Command = commands[key], Icon = MaterialIcons.Create(key) }; item.Bind(MenuItem.HeaderProperty, Bind(key));
        if (gesture != null) item.InputGesture = KeyGesture.Parse(gesture);
        return item;
    }
    MenuItem Group(string key) { var item = new MenuItem { Icon = MaterialIcons.Create(key) }; item.Bind(MenuItem.HeaderProperty, Bind(key)); return item; }
    void BuildMenus()
    {
        menu.Children.Clear();
        ContextMenu GroupMenu(params string[] keys)
        { var popup = new ContextMenu(); foreach (string key in keys) popup.Items.Add(Item(key)); return popup; }
        var file = GroupMenu("Open", "OpenFolder");
        var recent = Group("Recent"); foreach (string filename in Settings.Recent)
        { var item = new MenuItem { Header = UiPlatform.Storage.DisplayPath(filename), Icon = MaterialIcons.Create("Recent") }; item.Click += async (_, _) => await Guard(() => LoadInputsAsync(new[] { filename })); recent.Items.Add(item); }
        file.Items.Add(recent); if (!UiPlatform.IsMobile) file.Items.Add(Item("Exit", "Ctrl+Q"));
        var process = GroupMenu("ExtractAll", "ExtractSelected", "Recursive", "Create", "Convert", "Cancel");
        var selection = BuildContextMenu(true);
        var view = GroupMenu("Back", "Forward", "Parent", "Refresh", "FitWindow");
        var fit = Item("FitImage"); fit.ToggleType = MenuItemToggleType.CheckBox; fit.IsChecked = Settings.FitImage; view.Items.Add(fit);
        var sort = Group("Sort"); foreach (var pair in new[] { ("Name", "name"), ("Type", "type"), ("Size", "size"), ("Unsorted", "none") })
        { var item = Group(pair.Item1); item.ToggleType = MenuItemToggleType.Radio; item.IsChecked = Settings.Sort == pair.Item2; item.Click += (_, _) => { Settings.SortDescending = Settings.Sort == pair.Item2 && !Settings.SortDescending; Settings.Sort = pair.Item2; ApplySettings(); }; sort.Items.Add(item); }
        view.Items.Add(sort);
        foreach (string key in new[] { "Toolbar", "Statusbar" })
        { var item = Item(key); item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = key == "Toolbar" ? Settings.ToolbarVisible : Settings.StatusVisible; view.Items.Add(item); }
        var settings = GroupMenu("General", "Appearance", "Decoding", "Formats");
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
        foreach (string key in selectionOnly ? new[] { "SelectAll", "Mask", "CopyNames", "Explore", "Trash" } : new[] { "OpenSelected", "ExtractSelected", "Recursive", "Create", "Convert", "SelectAll", "Mask", "CopyNames", "Explore", "Trash", "Refresh" }) context.Items.Add(Item(key));
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
                await LoadLocationAsync(listing.Location with { Fingerprint = null }, false);
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
    async Task ExtractPicker(bool selectedOnly, bool recursive)
    {
        if (listing == null) return;
        var choice = await Dialogs.Extraction(this, Text, Settings.OutputDirectory, recursive);
        if (choice == null) return;
        string output = listing.Physical ? choice.Output : Path.Combine(choice.Output,
            Path.GetFileName(listing.Location.Chain.LastOrDefault() ?? listing.Location.RootPath) + ".unpacked");
        await ExtractInternal(output, selectedOnly, choice.Options, choice.Overwrite);
    }
    public Task ExtractToAsync(string root, bool selectedOnly = false) => ExtractInternal(root, selectedOnly, new(), OverwritePolicy.Fail);
    async Task ExtractInternal(string root, bool selectedOnly, ResourceExtractionOptions extraction, OverwritePolicy overwrite)
    {
        if (listing == null) throw new InvalidOperationException("Open a resource first.");
        string[] selection = selectedOnly ? files.SelectedItems.Cast<BrowserEntry>().Select(e => e.Name).ToArray() : null;
        if (selectedOnly && selection.Length == 0) { status.Text = Text["SelectFiles"]; return; }
        previewCancellation?.Cancel(); StopAudio(); using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); active = operation; SetBusy(true);
        var updates = new Progress<ExtractionProgress>(p => { progress.IsIndeterminate = false; progress.Value = p.Total == 0 ? 0 : (double)p.Completed / p.Total; status.Text = $"{p.Completed}/{p.Total} · {p.Entry}"; });
        try
        {
            var report = await Request<ResourceJobReport>(new("resource-extract", null, Location: listing.Location, Output: Path.GetFullPath(root),
                Selection: selection, ResourceOptions: extraction with { TypeOverrides = new(typeOverrides) }, Overwrite: overwrite), operation.Token, updates);
            status.Text = Text.Format("Completed", report.Files.Length, report.Errors.Length);
            foreach (string error in report.Errors) DesktopServices.Log(new IOException(error));
        }
        finally { if (ReferenceEquals(active, operation)) active = null; SetBusy(false); }
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
        var selected = files.SelectedItems.Cast<BrowserEntry>().Where(e => !e.IsDirectory).ToArray(); if (selected.Length == 0) { status.Text = Text["SelectFiles"]; return; }
        catalog ??= await Request<ResourceFormatInfo[]>(new("resource-catalog", null));
        var choice = await Dialogs.Conversion(this, Text, catalog.Where(f => f.Type == "image" && f.CanWrite).ToArray(), Settings.OutputDirectory);
        if (choice == null) return;
        StopAudio();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); active = operation; SetBusy(true);
        try
        {
            int done = 0; var failures = new List<string>();
            foreach (var entry in selected)
            {
                try
                {
                    string format = typeOverrides.GetValueOrDefault(entry.Name, entry.Type) == "audio" ? "AUDIO" : choice.FormatId;
                    string extension = format == "AUDIO" ? "wav" : catalog.Single(f => f.Id == format).Extensions.FirstOrDefault() ?? "png";
                    string target = ExtractionPath.Resolve(choice.Output, Path.ChangeExtension(entry.Name, extension));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    await Request<ResourceConversion>(new("resource-convert", null, Location: listing.Location, Entry: entry.Name, Output: target, FormatId: format, Overwrite: choice.Overwrite,
                        ResourceOptions: new(DiscardOriginalMedia: choice.DiscardOriginalMedia)), operation.Token);
                    done++;
                }
                catch (WorkerOperationException e) { failures.Add(e.Message); DesktopServices.Log(e); }
            }
            status.Text = Text.Format("Completed", done, failures.Count);
        }
        finally { active = null; SetBusy(false); }
    }
    async void OnKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool editing = e.Source is Control source && (source is TextBox || source.FindAncestorOfType<TextBox>() != null);
        if (editing && !(ctrl && e.Key is Key.O or Key.P or Key.Q) && e.Key is not (Key.Escape or Key.F10)) return;
        string key = ctrl ? e.Key switch { Key.O => "Open", Key.P => "Settings", Key.Q => "Exit", Key.H => "FitWindow", Key.E => "Explore",
            Key.A when !editing => "SelectAll", Key.C when !editing => "CopyNames", Key.PageUp => "Parent", _ => null } : e.Key switch
            { Key.F3 => "Create", Key.F4 => "ExtractSelected", Key.F5 => "Refresh", Key.F6 => "Convert", Key.Delete when !editing => "Trash",
                Key.Enter when !editing => "OpenSelected", Key.Back when !editing => "Parent", Key.Escape => "Cancel", Key.Add when !editing => "Mask", _ => null };
        if (e.KeyModifiers == (KeyModifiers.Alt | KeyModifiers.Shift)) key = e.Key switch { Key.T => "Toolbar", Key.M => "Menubar", Key.S => "Statusbar", _ => key };
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) key = e.Key switch { Key.Left => "Back", Key.Right => "Forward", _ => key };
        if (e.Key == Key.F10) { menu.Children.FirstOrDefault()?.Focus(); e.Handled = true; return; }
        if (key != null && commands[key].CanExecute(null)) { e.Handled = true; commands[key].Execute(null); }
        if (e.Key == Key.Space && !editing && files.IsFocused && files.ItemCount > 0)
        { files.SelectedIndex = Math.Min(files.ItemCount - 1, files.SelectedIndex + 1); e.Handled = true; }
        await Task.CompletedTask;
    }
}
