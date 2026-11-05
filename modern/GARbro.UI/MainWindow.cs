using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using GARbro.Application;
using GARbro.Media;
using GARbro.Domain;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace GARbro.Desktop;

public sealed partial class MainWindow : AppWindow
{
    readonly SettingsStore store;
    internal AppSettings Settings;
    internal DesktopText Text;
    readonly Dictionary<string, DeskCommand> commands = new();
    readonly StackPanel menu = new() { Orientation = Orientation.Horizontal, Name = "IconMenus", Spacing = 2 };
    readonly StackPanel toolbar = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    readonly StackPanel footer = new() { Spacing = 6 };
    readonly TextBox search = new() { Name = "Search", Width = 220 };
    readonly TextBox scheme = new() { Name = "Scheme", Text = "none", Width = 250 };
    readonly TextBox options = new() { Name = "Options", AcceptsReturn = true, MinHeight = 55, PlaceholderText = "{\"Password\": \"…\"}" };
    readonly TextBox path = new() { Name = "Location", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ListBox folders = new() { Name = "Folders" };
    readonly ListBox files = new() { Name = "Files", SelectionMode = SelectionMode.Multiple };
    readonly Image image = new() { Name = "PreviewImage", Stretch = Stretch.Uniform };
    readonly ScrollViewer imageScroll = new() {HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    readonly StackPanel encodingControls = new() { Spacing = 6, IsVisible = false };
    readonly ComboBox previewEncoding = new() { Name = "PreviewEncoding", ItemsSource = new[] { "auto", "gbk", "shift_jis", "utf-8", "utf-16", "gb18030", "big5" }, SelectedIndex = 0, MinWidth = 115 };
    readonly Grid browser = new() { ColumnDefinitions = new("180,*,350"), ColumnSpacing = 14 };
    readonly TextBox text = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = FontFamily.Parse("monospace") };
    readonly TextBlock previewInfo = new() { TextWrapping = TextWrapping.Wrap, MaxHeight = 100 }, status = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock emptyTitle = new() { FontSize = 20, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center }, emptyBody = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 360, Classes = { "muted" } };
    readonly StackPanel fileEmpty = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12, Margin = new Thickness(16) };
    readonly StackPanel emptyActions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8 };
    readonly TextBlock previewPrompt = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1, Height = 4 };
    readonly StackPanel audioControls = new() { Spacing = 8, IsVisible = false };
    readonly StackPanel pageControls = new() { Orientation = Orientation.Horizontal, Spacing = 6, IsVisible = false };
    readonly Slider position = new() { Minimum = 0, Maximum = 1 }, volume = new() { Minimum = 0, Maximum = 1, Value = 1 };
    readonly TextBlock playbackInfo = new();
    readonly Expander advanced = new();
    readonly CancellationTokenSource lifetime = new();
    readonly List<ResourceLocation> history = new(), roots = new();
    readonly Dictionary<string, string> typeOverrides = new(StringComparer.Ordinal);
    readonly Dictionary<string, Dictionary<string, string>> sessionOptions = new(StringComparer.Ordinal);
    CancellationTokenSource active, previewCancellation, navigationCancellation, filterCancellation;
    int navigationGeneration, filterGeneration;
    bool loading, changingEncoding, updatingRows;
    readonly PreviewMemoryCache previewCache = new(64L << 20);
    BrowserListing listing;
    ResourceFormatInfo[] catalog;
    Bitmap bitmap;
    IAudioOutput player;
    IVideoOutput videoPlayer;
    string audioDirectory;
    ResourcePreview preview;
    BrowserEntry previewEntry;
    int historyIndex = -1, generation;
    bool isClosed, busy, changingPosition;
    public int VisibleEntryCount => files.ItemCount;
    public string ArchiveTag => listing?.Tag;
    public ResourceLocation CurrentLocation => listing?.Location;

    public MainWindow() : this(new SettingsStore()) { }
    public MainWindow(SettingsStore settingsStore)
    {
        store = settingsStore; Settings = store.Load(); Text = new(Settings.EffectiveLanguage);
        UiPlatform.CurrentText = Text;
        UiPlatform.ActivePage = this; path.IsReadOnly = UiPlatform.IsMobile;
        if (UiPlatform.IsMobile && !File.Exists(store.Path)) Settings.ToolbarVisible = false;
        Width = Math.Clamp(Settings.Width, 850, 2400); Height = Math.Clamp(Settings.Height, 540, 1800);
        MinWidth = 850; MinHeight = 540; Title = "GARbro";
        if (!UiPlatform.IsMobile)
            try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://GARbro.UI/Assets/app-icon.ico"))); } catch (FileNotFoundException) { }
        CreateCommands();
        var layout = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto,Auto"), Margin = new Thickness(16), RowSpacing = 8 };
        Classes.Add("page");
        var heading = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12 };
        var brand = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12 };
        var brandImage = new Image { Width = 36, Height = 36, VerticalAlignment = VerticalAlignment.Center, Source = new Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://GARbro.UI/Assets/app-icon-64.png"))) };
        brand.Children.Add(brandImage);
        var brandText = new StackPanel(); Grid.SetColumn(brandText, 1); brand.Children.Add(brandText);
        brandText.Children.Add(new TextBlock { Text = "GARbro", FontSize = 24, FontWeight = FontWeight.SemiBold });
        var subtitle = Label("Subtitle"); subtitle.Classes.Add("muted"); subtitle.FontSize = 12; brandText.Children.Add(subtitle); heading.Children.Add(brand);
        var compactNavigation = new Grid { ColumnDefinitions = new("*,*,*"), ColumnSpacing = 4 };
        foreach (string key in new[] { "FolderPanel", "BrowsePanel", "PreviewPanel" })
        {
            var navigationContent = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
            navigationContent.Children.Add(MaterialIcons.Create(key)); var caption = Label(key); caption.FontSize = 12; navigationContent.Children.Add(caption);
            var button = new Button { Content = navigationContent, HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "navigationItem" } };
            button.Bind(ToolTip.TipProperty, Bind(key));
            button.Click += (_, _) => { compactPanel = key; ArrangeBrowser(); foreach (var child in compactNavigation.Children.OfType<Button>()) child.Classes.Set("selected", ReferenceEquals(child, button)); }; Grid.SetColumn(button, compactNavigation.Children.Count); compactNavigation.Children.Add(button);
        }
        Grid.SetRow(compactNavigation, 5); layout.Children.Add(compactNavigation); layout.Children.Add(heading);
        Grid.SetColumn(menu, 1); menu.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(menu); BuildMenus();
        var operations = new StackPanel { Spacing = 8 };
        foreach (string key in new[] { "Back", "Forward", "Parent", "Open", "OpenFolder", "ExtractSelected", "ExtractAll", "Cancel" }) toolbar.Children.Add(IconButton(key));
        search.Bind(TextBox.PlaceholderTextProperty, Bind("Search")); operations.Children.Add(new ScrollViewer { Content = toolbar, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        path.KeyDown += async (_, e) => { if (!busy && e.Key == Key.Enter) { e.Handled = true; await Guard(() => LoadInputsAsync(new[] { path.Text })); } };
        var locationRow = new Grid { ColumnDefinitions = new("*,220"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 8, RowSpacing = 8 };
        locationRow.Children.Add(path); Grid.SetColumn(search, 1); locationRow.Children.Add(search); operations.Children.Add(locationRow);
        var advancedContent = new StackPanel { Spacing = 6 };
        var keyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        keyRow.Children.Add(Label("Scheme")); keyRow.Children.Add(scheme); advancedContent.Children.Add(keyRow); advancedContent.Children.Add(options);
        advanced.Bind(Expander.HeaderProperty, Bind("Advanced")); advanced.Content = advancedContent; operations.Children.Add(advanced);
        Grid.SetRow(operations, 2); layout.Children.Add(operations);
        var folderPanel = new DockPanel(); var foldersLabel = Label("Folders"); DockPanel.SetDock(foldersLabel, Dock.Top);
        folderPanel.Children.Add(foldersLabel); folderPanel.Children.Add(folders); browser.Children.Add(folderPanel); folderPanel.Classes.Add("surface");
        var list = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 6 };
        var headings = new Grid { ColumnDefinitions = new("*,140,90"), Margin = new Thickness(10, 0) };
        foreach (var item in new[] { ("Name", 0), ("Type", 1), ("Size", 2) })
        {
            var label = new Button { Classes = { "columnHeader" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            label.Bind(ContentControl.ContentProperty, Bind(item.Item1));
            string sort = item.Item1.ToLowerInvariant(); label.Click += (_, _) => SetSort(sort);
            Grid.SetColumn(label, item.Item2); headings.Children.Add(label);
        }
        list.Children.Add(headings);
        files.ItemTemplate = new FuncDataTemplate<BrowserEntry>((entry, _) =>
        {
            if (entry == null) return new Control();
            var row = new Grid { ColumnDefinitions = new("*,140,90"), Margin = new Thickness(8, 6) };
            var name = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 10 };
            name.Children.Add(MaterialIcons.Create(entry.IsDirectory ? "Folders" : entry.Type switch { "image" => "Images", "audio" => "Audio", "video" => "Video", "script" => "Text", "archive" => "Process", _ => "Other" }, 20));
            var filename = new TextBlock { Text = entry.DisplayName, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(filename, 1); name.Children.Add(filename); row.Children.Add(name);
            var kind = new TextBlock { Text = entry.IsDirectory ? Text["Folder"] : ResourceType(entry), Opacity = .65, FontSize = 12 };
            Grid.SetColumn(kind, 1); row.Children.Add(kind);
            var size = new TextBlock { Text = entry.IsDirectory ? "" : FormatSize(entry.Size), HorizontalAlignment = HorizontalAlignment.Right, FontSize = 12 };
            Grid.SetColumn(size, 2); row.Children.Add(size); return row;
        });
        files.DoubleTapped += async (_, _) => await Guard(OpenSelected);
        files.SelectionChanged += async (_, _) => { if(updatingRows)return; UpdateCommands(); await PreviewSelection(); };
        files.ContextMenu = BuildContextMenu(); Grid.SetRow(files, 1); list.Children.Add(files);
        fileEmpty.Children.Add(MaterialIcons.Create("OpenFolder", 48)); fileEmpty.Children.Add(emptyTitle); fileEmpty.Children.Add(emptyBody);
        foreach (string key in new[] { "Open", "OpenFolder" }) { var button = Button(key); button.Name = null; if (key == "Open") button.Classes.Add("primary"); emptyActions.Children.Add(button); }
        fileEmpty.Children.Add(emptyActions); Grid.SetRow(fileEmpty, 1); list.Children.Add(fileEmpty); Grid.SetColumn(list, 1); browser.Children.Add(list);
        var previewPanel = new Grid { RowDefinitions = new("Auto,Auto,*"), RowSpacing = 8 };
        previewPanel.Classes.Add("surface"); previewPanel.Children.Add(Label("Preview")); Grid.SetRow(previewInfo, 1); previewPanel.Children.Add(previewInfo);
        var content = new Grid(); imageScroll.Content = image; content.Children.Add(imageScroll); content.Children.Add(text); previewPrompt.Bind(TextBlock.TextProperty, Bind("SelectPreview")); content.Children.Add(previewPrompt);
        BuildPlayback(); DockPanel.SetDock(audioControls, Dock.Bottom);
        var previewStack = new DockPanel(); DockPanel.SetDock(pageControls, Dock.Bottom); previewStack.Children.Add(pageControls); previewStack.Children.Add(content);
        previewStack.Children.Insert(0, audioControls);
        Grid.SetRow(previewStack, 2); previewPanel.Children.Add(previewStack); Grid.SetColumn(previewPanel, 2); browser.Children.Add(previewPanel);
        for (int i = 0; i < browser.Children.Count; i++)
        {
            var panel = browser.Children[i]; browser.Children.RemoveAt(i);
            var surface = new Border { Child = panel, Classes = { "surface" } }; Grid.SetColumn(surface, i); browser.Children.Insert(i, surface);
        }
        Grid.SetRow(browser, 3); layout.Children.Add(browser);
        SizeChanged += (_, _) =>
        {
            bool narrow = Bounds.Width < 600; compactNavigation.IsVisible = Bounds.Width < 1000; subtitle.IsVisible = !narrow;
            bool secondMenuRow = Bounds.Width < 720;
            if (secondMenuRow && menu.Parent == heading)
            { heading.Children.Remove(menu); Grid.SetColumn(menu, 0); Grid.SetRow(menu, 1); layout.Children.Add(menu); }
            else if (!secondMenuRow && menu.Parent == layout)
            { layout.Children.Remove(menu); Grid.SetRow(menu, 0); Grid.SetColumn(menu, 1); heading.Children.Add(menu); }
            menu.Spacing = narrow ? 0 : 2; locationRow.ColumnDefinitions = new(narrow ? "*" : "*,220");
            Grid.SetColumn(search, narrow ? 0 : 1); Grid.SetRow(search, narrow ? 1 : 0); search.Width = narrow ? double.NaN : 220;
            ArrangeBrowser();
        };
        footer.Children.Add(progress); footer.Children.Add(status); Grid.SetRow(footer, 4); layout.Children.Add(footer); Content = layout;
        pageControls.Children.Add(Button("PreviousPage"));
        pageControls.Children.Add(Button("NextPage"));
        pageControls.Children.Add(Button("Copy"));
        var encodingRow=new Grid {ColumnDefinitions=new("*,140"),ColumnSpacing=8};
        var encodingLabel=Label("PreviewEncoding");encodingLabel.FontSize=12;encodingLabel.VerticalAlignment=VerticalAlignment.Center;
        encodingRow.Children.Add(encodingLabel);Grid.SetColumn(previewEncoding,1);encodingRow.Children.Add(previewEncoding);encodingControls.Children.Add(encodingRow);
        var wrap = new CheckBox { IsChecked = Settings.WrapText }; wrap.Bind(ContentControl.ContentProperty, Bind("Wrap"));
        wrap.IsCheckedChanged += (_, _) => { Settings.WrapText = wrap.IsChecked == true; text.TextWrapping = Settings.WrapText ? TextWrapping.Wrap : TextWrapping.NoWrap; };
        encodingControls.Children.Add(wrap); DockPanel.SetDock(encodingControls, Dock.Bottom); previewStack.Children.Insert(0, encodingControls);
        previewEncoding.SelectionChanged += async (_, _) => { if (!changingEncoding && previewEntry != null) await ShowPreviewPage(0); };
        text.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) wrap.IsVisible = text.IsVisible; };
        search.TextChanged += (_, _) => RefreshFiles(100);
        folders.SelectionChanged += async (_, _) =>
        {
            if (folders.SelectedItem is FolderNode { Location: ResourceLocation location } && location != CurrentLocation)
                await Guard(() => LoadLocationAsync(location));
        };
        folders.ItemTemplate = new FuncDataTemplate<FolderNode>((node, _) => new TextBlock { Text = node?.Title, Margin = new Thickness(node?.Child == true ? 16 : 0, 8, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis });
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = !busy && e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            if (busy) return;
            var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p != null).ToArray();
            if (paths?.Length > 0) await Guard(() => LoadInputsAsync(paths));
        });
        KeyDown += OnKeyDown; WaylandTextInput.Attach(this); ApplySettings(false);
        status.Text = store.LoadError == null ? Text["Welcome"] : Text["Error"] + ": " + Text.ErrorMessage(store.LoadError);
        text.IsVisible = imageScroll.IsVisible = progress.IsVisible = false;
        Closed += (_, _) =>
        {
            isClosed = true; lifetime.Cancel(); active?.Cancel(); navigationCancellation?.Cancel(); previewCancellation?.Cancel(); filterCancellation?.Cancel(); previewCache.Clear(); StopAudio(); bitmap?.Dispose();
            Settings.Width = Width; Settings.Height = Height;
            try { store.Save(Settings); } catch (IOException error) { DesktopServices.Log(error); }
        };
        Opened += (_,_) => _ = PrewarmFormats();
        UpdateCommands();
    }
    async Task PrewarmFormats()
    {
        try
        {
            var types=await Request<Dictionary<string,string>>(new("resource-warm",null),lifetime.Token);
            LightweightResources.RegisterTypes(types);
            if(listing?.Physical==true)
            {
                var source=listing;
                var entries=await Task.Run(()=>source.Entries.Select(e=>e.IsDirectory?e:e with {Type=LightweightResources.Type(e.Name)}).ToArray(),lifetime.Token);
                if(listing==source && !isClosed){listing=source with {Entries=entries};RefreshFiles();}
            }
        }
        catch(OperationCanceledException){}
        catch(Exception error){DesktopServices.Log(error);}
    }

    Binding Bind(string key) => new("[" + key + "]") { Source = Text };
    TextBlock Label(string key) { var label = new TextBlock(); label.Bind(TextBlock.TextProperty, Bind(key)); return label; }
    Button Button(string key, bool textContent = true) { var button = new Button { Command = commands[key], Name = key == "Open" ? "OpenArchive" : key == "Cancel" ? "CancelOperation" : null }; if (textContent) button.Bind(ContentControl.ContentProperty, Bind(key)); ToolTip.SetTip(button, CommandTip(key)); return button; }
    internal Button ActionButton(string key, Func<Task> run) { var b = new Button(); b.Bind(ContentControl.ContentProperty, Bind(key)); b.Click += async (_, _) => await Guard(run); return b; }
    string compactPanel = "BrowsePanel";
    void ArrangeBrowser()
    {
        if (browser.Children.Count < 3) return;
        double width = Bounds.Width;
        bool narrow = width < 600;
        browser.ColumnDefinitions = new(width >= 1000 ? "180,*,350" : width >= 600 ? "0,*,300" : "*");
        for (int i = 0; i < 3; i++)
        {
            bool visible = width >= 1000 || width >= 600 && i > 0 || narrow && i == (compactPanel == "FolderPanel" ? 0 : compactPanel == "PreviewPanel" ? 2 : 1);
            if (width >= 600 && width < 1000 && compactPanel == "FolderPanel") visible = i < 2;
            browser.Children[i].IsVisible = visible;
            Grid.SetColumn(browser.Children[i], narrow ? 0 : i);
        }
        if (width >= 600 && width < 1000 && compactPanel == "FolderPanel") browser.ColumnDefinitions = new("180,*,0");
    }
    Button IconButton(string key)
    {
        var button = Button(key, false); if(key is "ExtractSelected" or "ExtractAll")button.Name=key; button.Content = MaterialIcons.Create(key); button.Classes.Add("iconMenu");
        ToolTip.SetTip(button, CommandTip(key)); button.Bind(Avalonia.Automation.AutomationProperties.NameProperty, Bind(key)); return button;
    }
    static Control GearIcon() => new PathIcon { Width = 20, Height = 20, Data = Geometry.Parse("M10,0 L14,0 L15,3 L18,4 L21,3 L24,7 L22,10 L22,13 L24,16 L21,20 L18,19 L15,21 L14,24 L10,24 L9,21 L6,19 L3,20 L0,16 L2,13 L2,10 L0,7 L3,3 L6,4 L9,3 Z M12,7 A5,5 0 1 0 12,17 A5,5 0 1 0 12,7") };
    static string FormatSize(long n) => n < 1024 ? n + " B" : n < 1048576 ? (n / 1024d).ToString("F1") + " KiB" : (n / 1048576d).ToString("F1") + " MiB";
    string ResourceType(BrowserEntry e) => typeOverrides.GetValueOrDefault(e.Name, e.Type) switch { "image" => Text["Images"], "audio" => Text["Audio"], "video" => Text["Video"], "script" => Text["Scripts"], "archive" => Text["Archives"], "binary" => Text["Binary"], _ => Text["OtherType"] };

    internal async Task<T> Request<T>(WorkerRequest request, CancellationToken cancellation = default, IProgress<ExtractionProgress> updates = null)
    {
        string root = request.Location?.RootPath ?? request.Path ?? "";
        if (!sessionOptions.TryGetValue(root, out var profile)) sessionOptions[root] = profile = new();
        bool catalogOperation = request.Operation is "resource-catalog" or "resource-validate-schemes";
        request = request with { Context = (request.Context ?? Settings.Context) with { OptionsByFormat = new(profile) },
            OptionsJson = catalogOperation ? null : request.OptionsJson ?? (string.IsNullOrWhiteSpace(options.Text) ? null : options.Text),
            Scheme = catalogOperation ? null : request.Scheme ?? (string.IsNullOrWhiteSpace(scheme.Text) ? null : scheme.Text),
            OptionsFormatId = listing?.Location.RootPath == root ? listing.ProviderId : request.OptionsFormatId };
        while (true)
        {
            try { return await UiPlatform.Worker.ExecuteAsync<T>(request, Settings.Limits, updates, cancellation); }
            catch (WorkerOperationException e) when (e.RemoteType == nameof(InvalidSchemeDatabaseException) && request.Context.SchemesFile != null)
            {
                DesktopServices.Log(e); Settings.SchemesFile = null;
                request = request with { Context = request.Context with { SchemesFile = null } };
                store.Save(Settings);
            }
            catch (WorkerOperationException e) when (e.FormatId != null && !isClosed && !cancellation.IsCancellationRequested)
            {
                catalog ??= await Request<ResourceFormatInfo[]>(new("resource-catalog", null), cancellation);
                var value = await Dialogs.Parameters(this, Text, e.OptionsJson ?? "{}", e.Message, catalog.FirstOrDefault(f => f.Id == e.FormatId)?.Schemes);
                if (value == null) throw new OperationCanceledException();
                profile[e.ContextKey ?? e.FormatId] = value;
                request = request with { Context = request.Context with { OptionsByFormat = new(profile) },
                    CompletedPaths = (request.CompletedPaths ?? Array.Empty<string>()).Concat(e.CompletedPaths ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
            }
        }
    }

    public void StopPreviewAudio() => StopAudio();
    public void SaveState() { try { store.Save(Settings); } catch (IOException error) { DesktopServices.Log(error); } }
    public bool HandleBack() { if (Bounds.Width < 1000 && compactPanel != "BrowsePanel") { compactPanel = "BrowsePanel"; ArrangeBrowser(); return true; } if (historyIndex <= 0 || busy) return false; commands["Back"].Execute(null); return true; }
    public Task LoadArchiveAsync(string path) => LoadInputsAsync(new[] { path });
    public async Task LoadInputsAsync(IEnumerable<string> inputs)
    {
        ObjectDisposedException.ThrowIf(isClosed, this);
        foreach (string input in inputs)
        {
            var source = await UiPlatform.Storage.RestoreAsync(input);
            if (source == null) continue;
            string full = source.LocalPath;
            var location = new ResourceLocation(full);
            try
            {
                if (File.Exists(full) && LightweightResources.IsOrdinaryName(full))
                {
                    await LoadLocationAsync(new(Path.GetDirectoryName(full)));
                    files.SelectedItem = listing.Entries.FirstOrDefault(f => f.Name == Path.GetFileName(full));
                }
                else await LoadLocationAsync(location);
            }
            catch (WorkerOperationException e) when (e.RemoteType == "UnknownFormatException" && File.Exists(full))
            {
                await LoadLocationAsync(new(Path.GetDirectoryName(full)));
                files.SelectedItem = listing.Entries.FirstOrDefault(f => f.Name == Path.GetFileName(full));
            }
            if (CurrentLocation == null) continue;
            if (!roots.Any(r => r.RootPath == CurrentLocation.RootPath)) roots.Add(CurrentLocation);
            string recentId = UiPlatform.Storage.PersistentId(full); Settings.Recent.Remove(recentId); Settings.Recent.Insert(0, recentId); Settings.Recent = Settings.Recent.Take(20).ToList();
        }
        BuildFolders(); BuildMenus();
        SaveState();
    }
    async Task LoadLocationAsync(ResourceLocation location, bool recordHistory = true, bool refresh = false)
    {
        ObjectDisposedException.ThrowIf(isClosed, this);
        navigationCancellation?.Cancel(); StopAudio(); previewCancellation?.Cancel();
        int ticket = ++navigationGeneration;
        DesktopServices.Activity("load", location.DisplayPath);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        navigationCancellation = operation; loading = true; progress.IsVisible = progress.IsIndeterminate = true; UpdateCommands(); status.Text = Text["Opening"];
        try
        {
            var opened = await Request<BrowserListing>(new("resource-list", null, Location: location, Refresh: refresh), operation.Token);
            operation.Token.ThrowIfCancellationRequested(); if (isClosed || ticket != navigationGeneration) return;
            listing = opened; path.Text = UiPlatform.Storage.DisplayPath(listing.Location.RootPath) + string.Concat(listing.Location.Chain.Select(p => " → " + p)); Title = "GARbro — " + Path.GetFileName(location.RootPath.TrimEnd(Path.DirectorySeparatorChar));
            ClearPreview(); previewCache.Clear(); typeOverrides.Clear(); await RefreshFilesAsync(); BuildFolders();
            operation.Token.ThrowIfCancellationRequested(); if (ticket != navigationGeneration) return;
            if (recordHistory)
            {
                if (historyIndex + 1 < history.Count) history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
                history.Add(listing.Location); historyIndex = history.Count - 1;
            }
            UpdateListingStatus();
        }
        finally
        {
            if (ReferenceEquals(navigationCancellation, operation)) { navigationCancellation = null; loading = false; progress.IsVisible = progress.IsIndeterminate = busy; UpdateCommands(); }
        }
    }
    void UpdateListingStatus()
    {
        if (listing != null) status.Text = (listing.Physical ? Text["Folders"] : listing.Tag == "media" ? Text["Media"] : listing.Tag) + " · " + (listing.Entries.Length == 1 ? Text["OneEntry"] : Text.Format("EntryCount", listing.Entries.Length));
    }
    sealed record FolderNode(string Title, ResourceLocation Location, bool Child = false);
    void BuildFolders()
    {
        if (listing == null) return;
        // Data only: ListBox virtualizes the current level instead of creating a control per directory.
        var nodes = new List<FolderNode>();
        foreach (var root in roots.Where(r => r.RootPath != listing.Location.RootPath)) nodes.Add(new(Path.GetFileName(root.RootPath), root));
        nodes.Add(new(Text["AllFiles"],listing.Location));
        foreach (var entry in listing.Entries.Where(e=>e.IsDirectory))
            nodes.Add(new(entry.DisplayName, listing.Physical ? new(Path.Combine(listing.Location.RootPath,entry.Name)) : listing.Location.EnterDirectory(entry.Name), true));
        folders.ItemsSource = nodes;
    }
    void RefreshFiles(int delay = 0) => _ = Guard(() => RefreshFilesAsync(delay));
    async Task RefreshFilesAsync(int delay = 0)
    {
        filterCancellation?.Cancel(); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); filterCancellation = cancel;
        int ticket = ++filterGeneration;
        try
        {
            if (delay > 0) await Task.Delay(delay,cancel.Token);
            emptyTitle.Text = listing == null ? Text["WelcomeTitle"] : string.IsNullOrEmpty(search.Text) ? Text["EmptyFolder"] : Text["NoMatches"];
            emptyBody.Text = listing == null ? Text["WelcomeHint"] : string.IsNullOrEmpty(search.Text) ? Text["EmptyHint"] : Text["SearchHint"];
            emptyActions.IsVisible = listing == null;
            if (listing == null) { fileEmpty.IsVisible = true; return; }
            var source=listing; string query=search.Text??"",sort=Settings.Sort,culture=Text.Language; bool descending=Settings.SortDescending;
            var rows=await Task.Run(()=>
            {
                var data=source.Entries.Select((e,i)=>new BrowseRow(i,e.DisplayName,e.Type,e.Size,e.IsDirectory)).ToArray();
                return BrowsePlanning.Visible(data,query,sort,descending,culture).Select(i=>source.Entries[i]).ToArray();
            },cancel.Token);
            if(cancel.IsCancellationRequested || ticket!=filterGeneration || listing!=source || isClosed)return;
            var selected=files.SelectedItems.Cast<BrowserEntry>().Select(e=>e.Name).ToHashSet(StringComparer.Ordinal);
            var scroll=files.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();var scrollOffset=scroll?.Offset??default;
            updatingRows=true;
            try
            {
                files.Selection.Clear();files.ItemsSource=rows;
                files.Selection.BeginBatchUpdate();
                try
                {
                files.Selection.Clear();
                for(int i=0;i<rows.Length;i++)
                    if(selected.Contains(rows[i].Name))
                    {
                        int first=i;while(i+1<rows.Length && selected.Contains(rows[i+1].Name))i++;
                        files.Selection.SelectRange(first,i);
                    }
                }
                finally {files.Selection.EndBatchUpdate();}
            }
            finally {updatingRows=false;}
            if(scroll!=null)scroll.Offset=scrollOffset;
            fileEmpty.IsVisible=rows.Length==0;UpdateCommands();
        }
        catch(OperationCanceledException)when(cancel.IsCancellationRequested){}
        finally {if(ReferenceEquals(filterCancellation,cancel))filterCancellation=null;}
    }
    async Task OpenSelected()
    {
        if (files.SelectedItem is not BrowserEntry entry) return;
        if (!entry.IsDirectory && (listing.Physical && LightweightResources.IsOrdinaryName(entry.Name) || typeOverrides.GetValueOrDefault(entry.Name, entry.Type) == "video" || listing.Tag == "media"))
        { await PreviewSelection(); return; }
        ResourceLocation location = listing.Physical ? new(Path.Combine(listing.Location.RootPath, entry.Name)) :
            entry.IsDirectory ? listing.Location.EnterDirectory(entry.Name) : listing.Location.EnterArchive(entry.Name);
        try { await LoadLocationAsync(location); }
        catch (WorkerOperationException e) when (!entry.IsDirectory && e.RemoteType == "UnknownFormatException") { await PreviewSelection(); }
    }
    async Task PreviewSelection()
    {
        if (updatingRows) return;
        ClearPreview();
        changingEncoding=true; previewEncoding.SelectedIndex=0; changingEncoding=false;
        if (files.SelectedItem is not BrowserEntry { IsDirectory: false } entry || listing == null) return;
        previewPrompt.IsVisible = false; previewInfo.Text = Text["Preparing"];
        previewEntry = entry; encodingControls.IsVisible = true;
        await ShowPreviewPage(0, debounce: true);
    }
    void ClearPreview()
    {
        ++generation; previewCancellation?.Cancel(); StopAudio();
        previewEntry = null; preview = null; previewPrompt.IsVisible = true; bitmap?.Dispose(); bitmap = null; image.Source = null;
        imageScroll.IsVisible = text.IsVisible = audioControls.IsVisible = pageControls.IsVisible = encodingControls.IsVisible = false;
        text.Text = previewInfo.Text = "";
        UpdateCommands();
    }
    async Task ShowPreviewPage(long offset, bool debounce = false)
    {
        if (previewEntry == null || listing == null) return;
        previewCancellation?.Cancel(); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); previewCancellation = cancel;
        UpdateCommands();
        int ticket = ++generation;
        try
        {
            StopAudio();
            var location=listing.Location; var entry=previewEntry;
            string code=(previewEncoding.SelectedItem as UiChoice)?.Value??"auto";
            string forced=typeOverrides.GetValueOrDefault(entry.Name);
            string sourcePath=listing.Physical?Path.Combine(location.RootPath,entry.Name):location.RootPath;
            var fingerprint=await Task.Run(()=>ArchiveFingerprint.Read(sourcePath),cancel.Token);
            string cacheKey=WorkerProtocol.Serialize(new {location,entry.Name,fingerprint,code,forced,offset,Settings.Context,Settings.FitImage,OptionsJson=options.Text,Scheme=scheme.Text,Profile=sessionOptions.GetValueOrDefault(location.RootPath)});
            ResourcePreview result;
            if(!previewCache.TryGet(cacheKey,out result))
            {
                // Cached previews are already cheap; debounce only decoder work.
                if(debounce)await Task.Delay(120,cancel.Token);
                cancel.Token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(DesktopServices.CacheDirectory);
                string output=Path.Combine(DesktopServices.CacheDirectory,"preview-"+Guid.NewGuid().ToString("N")+".bgra");
                using(File.Create(output)){}
                try
                {
                    result=await Request<ResourcePreview>(new("resource-preview",null,Location:location,Entry:entry.Name,ForcedType:forced,Offset:offset,
                        PreviewMode:code=="hex"?"hex":"auto",PreviewEncoding:code is "hex" or "auto"?null:code,Output:output,
                        PreviewWidth:Settings.FitImage?Math.Max(320,(int)imageScroll.Bounds.Width*2):0,
                        PreviewHeight:Settings.FitImage?Math.Max(320,(int)imageScroll.Bounds.Height*2):0),cancel.Token);
                    if(result.Pixels is { } pixels)
                    {
                        if(pixels.Path!=output || pixels.Width<=0 || pixels.Height<=0 || (long)pixels.Width*pixels.Height*4!=pixels.Bytes || pixels.Bytes>100L<<20 || new FileInfo(output).Length!=pixels.Bytes)throw new InvalidDataException("Invalid preview artifact.");
                        result=result with {Image=await File.ReadAllBytesAsync(output,cancel.Token)};
                    }
                    if(ticket==generation)previewCache.Put(cacheKey,result);
                }
                finally { _ = Task.Run(()=> { try {File.Delete(output);}catch(IOException){} }); }
            }
            Bitmap prepared=null;
            if(result.Image!=null)prepared=await Task.Run(()=>PrepareBitmap(result),cancel.Token);
            if(ticket!=generation || isClosed) {prepared?.Dispose();return;}
            preview=result;previewPrompt.IsVisible=false;bitmap?.Dispose();bitmap=prepared;image.Source=bitmap;
            imageScroll.IsVisible=result.Kind is "image" or "video";audioControls.IsVisible=result.Kind is "audio" or "video";
            text.IsVisible=result.Kind is "text" or "binary";pageControls.IsVisible=text.IsVisible;
            previewInfo.Text=result.Kind is "text" or "binary"?result.Title:result.Text;
            text.Text = result.Text;
            UpdateCommands();
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception e) { if (ticket == generation) { previewPrompt.IsVisible = false; previewInfo.Text = Text["Error"] + ": " + Text.ErrorMessage(e.Message); } }
        finally { if (ReferenceEquals(previewCancellation, cancel)) previewCancellation = null; UpdateCommands(); }
    }
    void ApplySettings(bool save = true)
    {
        bool languageChanged = Text.Language != Settings.EffectiveLanguage;
        Text.Change(Settings.EffectiveLanguage);
        if (languageChanged || !previewEncoding.Items.OfType<UiChoice>().Any())
        {
            string code = (previewEncoding.SelectedItem as UiChoice)?.Value ?? "auto";
            var choices = new[] { "auto", "hex", "gbk", "shift_jis", "utf-8", "utf-16", "gb18030", "big5" }.Select(c => new UiChoice(c, c == "auto" ? Text["Automatic"] : c == "hex" ? Text["HexPreview"] : c)).ToArray();
            changingEncoding=true; previewEncoding.ItemsSource = choices; previewEncoding.SelectedItem = choices.FirstOrDefault(c => c.Value == code) ?? choices[0]; changingEncoding=false;
        }
        MaterialTheme.Apply(Settings);
        menu.IsVisible = true; toolbar.IsVisible = Settings.ToolbarVisible; footer.IsVisible = Settings.StatusVisible;
        image.Stretch = Settings.FitImage ? Stretch.Uniform : Stretch.None; text.TextWrapping = Settings.WrapText ? TextWrapping.Wrap : TextWrapping.NoWrap;
        imageScroll.HorizontalScrollBarVisibility = imageScroll.VerticalScrollBarVisibility = Settings.FitImage ?
            Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        if (languageChanged && !busy && !loading) UpdateListingStatus();
        ApplyShortcutBindings(); if (save) store.Save(Settings); BuildMenus(); RefreshFiles(); BuildFolders();
    }
    void SetBusy(bool value)
    {
        busy = value; progress.IsIndeterminate = progress.IsVisible = value; footer.IsVisible = Settings.StatusVisible || value; status.Classes.Remove("error");
        path.IsEnabled = search.IsEnabled = folders.IsEnabled = files.IsEnabled = !value;
        advanced.IsEnabled = pageControls.IsEnabled = previewEncoding.IsEnabled = audioControls.IsEnabled = !value;
        UpdateCommands();
    }
    void UpdateCommands() { foreach (var command in commands.Values) command.Update(); }
    public async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { if(!loading && !busy)status.Text = Text["Cancelled"]; }
        catch (Exception error) { footer.IsVisible = true; status.Classes.Add("error"); status.Text = Text["Error"] + ": " + Text.ErrorMessage(error.Message); DesktopServices.Log(error); }
    }
}
