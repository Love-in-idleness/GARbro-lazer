using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GARbro.Application;
using GARbro.Desktop;
using Xunit;

public sealed class AdvancedResourceUiTests
{
    static readonly DesktopText Text = new("en-US");
    static SettingsStore Settings(string root)
    {
        var store = new SettingsStore(Path.Combine(root, "settings.json"));
        store.Save(new AppSettings { Language = "en-US", TextEncoding = "utf-8" });
        return store;
    }
    static T Control<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    static Button Action(Visual root, string key) =>
        root.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == Text[key]);
    static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static async Task Until(Func<bool> predicate)
    {
        for (int i = 0; i < 400 && !predicate(); i++) await Task.Delay(25, TestContext.Current.CancellationToken);
        Assert.True(predicate());
    }
    static async Task<Window> Dialog(MainWindow window)
    {
        await Until(() => window.DesktopHost.OwnedWindows.Any());
        return Assert.Single(window.DesktopHost.OwnedWindows);
    }
    static async Task ChooseFormat(MainWindow window, string kind, string tag)
    {
        var files = Control<ListBox>(window, "Files");
        files.ContextMenu.Items.OfType<MenuItem>().Single(c => c.Header as string == Text["OpenSelectedWithFormat"]).Command.Execute(null);
        var dialog = await Dialog(window);
        Control<ComboBox>(dialog, "InputFormatKind").SelectedIndex = kind == "archive" ? 0 : 1;
        Control<TextBox>(dialog, "InputFormatSearch").Text = tag;
        var formats = Control<ListBox>(dialog, "InputFormatList");
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        formats.SelectedItem = formats.Items.OfType<UiChoice>().First(c => c.Label.StartsWith(tag + " · ", StringComparison.Ordinal));
        Click(Action(dialog, "Confirm"));
    }
    static void Capture(Window host, string name)
    {
        string folder = Environment.GetEnvironmentVariable("GARBRO_ADVANCED_SCREENSHOTS");
        if (folder == null) return;
        Directory.CreateDirectory(folder);
        using var frame = host.CaptureRenderedFrame();
        frame.Save(Path.Combine(folder, name + ".png"), new PngBitmapEncoderOptions());
    }

    [AvaloniaFact]
    public async Task SpecifiedArchiveOpensInItsOwnTabAndExtractsWithTheSameDecoder()
    {
        string root = Directory.CreateTempSubdirectory("garbro-open-format-").FullName;
        string input = Path.Combine(root, "sample.png");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "standard.xfl"), input);
        var window = new MainWindow(Settings(root)); window.Show();
        try
        {
            await window.LoadArchiveAsync(input);
            Assert.Null(window.CurrentLocation.InputFormatId);
            Assert.Equal(root, window.CurrentLocation.RootPath);
            await ChooseFormat(window, "archive", "ZIP");
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(c => c.Text?.Contains("Could not open with ZIP") == true));
            var tabs = Control<ListBox>(window, "ResourceTabs");
            tabs.SelectedIndex = 0;
            await Until(() => window.CurrentLocation?.RootPath == root && Control<ListBox>(window, "Files").SelectedItem is BrowserEntry);
            await ChooseFormat(window, "archive", "XFL");
            await Until(() => window.ArchiveTag == "XFL" && window.VisibleEntryCount == 3);
            Assert.NotNull(window.CurrentLocation.InputFormatId);
            string output = Path.Combine(root, "out");
            await window.ExtractToAsync(output);
            Assert.Equal("Hello, GARbro!\n", File.ReadAllText(Path.Combine(output, "hello.txt")));
            await window.LoadArchiveAsync(input);
            Assert.Null(window.CurrentLocation.InputFormatId);
            Assert.Equal(root, window.CurrentLocation.RootPath);
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }

    [AvaloniaTheory]
    [InlineData(2, 1, 5, 4)]
    [InlineData(-1, -1, 2, 2)]
    public async Task SpecifiedImageUsesTheChosenReaderAndConversionAppliesOffset(int x, int y, uint width, uint height)
    {
        string root = Directory.CreateTempSubdirectory("garbro-convert-offset-").FullName;
        string input = Path.Combine(root, "source.txt");
        byte[] pixels = Enumerable.Repeat(new byte[] { 8, 16, 32, 255 }, 9).SelectMany(p => p).ToArray();
        using (var file = File.Create(input))
            GameRes.ImageFormat.Png.Write(file, GameRes.ImageData.Create(
                new GameRes.ImageMetaData { Width = 3, Height = 3, OffsetX = x, OffsetY = y, BPP = 32 },
                GARbro.Imaging.PixelFormats.Bgra32, null, pixels));
        var window = new MainWindow(Settings(root)); window.Show();
        try
        {
            await window.LoadArchiveAsync(input);
            await ChooseFormat(window, "image", "PNG");
            await Until(() => Control<Image>(window, "PreviewImage").Source != null && window.CurrentLocation.InputFormatId != null);
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F6 });
            var dialog = await Dialog(window);
            var offset = Control<CheckBox>(dialog, "AdjustImageOffset");
            Assert.True(offset.IsEnabled);
            Assert.False(offset.IsChecked == true);
            offset.IsChecked = true;
            string output = Path.Combine(root, "out");
            Dialogs.SetPath(Control<TextBox>(dialog, "OutputDirectoryPath"), output);
            Click(Action(dialog, "Start"));
            string converted = Path.Combine(output, "source.png");
            await Until(() => File.Exists(converted));
            var image = ArchiveService.ReadImage(converted);
            Assert.Equal(width, image.Width);
            Assert.Equal(height, image.Height);
            Assert.Equal(0, image.OffsetX);
            Assert.Equal(0, image.OffsetY);
            var bytes = image.Bitmap.ToBgra32();
            Assert.Equal(pixels[..4], bytes.AsSpan((int)(Math.Max(0, y) * width + Math.Max(0, x)) * 4, 4).ToArray());
            if (x > 0) Assert.Equal(new byte[4], bytes[..4]);
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task DecompiledPreviewPreservesUtf8AcrossPagesAndHexCanJumpWithinCompressedEntries()
    {
        string root = Directory.CreateTempSubdirectory("garbro-script-hex-").FullName;
        string input = Path.Combine(root, "scripts.zip");
        using (var zip = ZipFile.Open(input, ZipArchiveMode.Create))
        {
            zip.CreateEntry("empty.bin");
            using var entry = zip.CreateEntry("nscript.dat", CompressionLevel.Optimal).Open();
            entry.Write(Encoding.UTF8.GetBytes(new string('a', 65535) + "日本語\n").Select(b => (byte)(b ^ 0x84)).ToArray());
        }
        var window = new MainWindow(Settings(root)); window.Show();
        try
        {
            await window.LoadArchiveAsync(input);
            var files = Control<ListBox>(window, "Files");
            files.SelectedItem = files.Items.OfType<BrowserEntry>().Single(e => e.Name == "nscript.dat");
            var mode = Control<ComboBox>(window, "PreviewEncoding");
            var preview = window.GetVisualDescendants().OfType<TextBox>().Single(c => c.IsReadOnly && c.AcceptsReturn);
            void Select(string value) => mode.SelectedItem = mode.Items.OfType<UiChoice>().Single(c => c.Value == value);
            Select("script");
            var next = Control<Button>(window, "NextPage");
            await Until(() => next.Command.CanExecute(null) && preview.Text?.Length == 65535);
            next.Command.Execute(null);
            await Until(() => preview.Text == "日本語\n");
            Select("hex");
            var jump = Action(window, "JumpToOffset");
            await Until(() => jump.Command.CanExecute(null) && jump.IsVisible);
            Capture(window.DesktopHost, "hex-desktop");
            jump.Command.Execute(null);
            var dialog = await Dialog(window);
            Control<TextBox>(dialog, "HexOffset").Text = "0x2";
            Click(Action(dialog, "Confirm"));
            await Until(() => preview.Text?.StartsWith("00000002  E5 E5", StringComparison.Ordinal) == true && jump.Command.CanExecute(null));
            string previous = preview.Text;
            jump.Command.Execute(null);
            dialog = await Dialog(window);
            Control<TextBox>(dialog, "HexOffset").Text = "FFFFFFFF";
            Click(Action(dialog, "Confirm"));
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(c => c.Text?.Contains(Text["HexOffsetOutOfRange"]) == true));
            Assert.Equal(previous, preview.Text);
            window.MinWidth = 360; window.Width = 400;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var compactPreview = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("navigationItem")
                && b.GetVisualDescendants().OfType<TextBlock>().Any(c => c.Text == Text["PreviewPanel"]));
            Click(compactPreview);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Capture(window.DesktopHost, "hex-narrow");
            files.SelectedItem = files.Items.OfType<BrowserEntry>().Single(e => e.Name == "empty.bin");
            Select("hex");
            await Until(() => jump.Command.CanExecute(null) && preview.Text == "");
            Assert.False(next.Command.CanExecute(null));
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }

    sealed class InputStorage(string source) : IStorageAccess
    {
        public string Source { get; set; } = source;
        public Task<ResourceSource[]> PickInputsAsync(bool folders, CancellationToken cancellation = default,
            bool allowMultiple = true, string title = null, string[] patterns = null)
        {
            Assert.False(allowMultiple);
            Assert.Equal(new[] { "*.json", "*.json.gz" }, patterns);
            return Task.FromResult(new[] { new ResourceSource(Source, Path.GetFileName(Source), Source) });
        }
        public Task<ResourceSource> PickOutputAsync(bool folder, string suggestedName = null, CancellationToken cancellation = default) => throw new NotSupportedException();
        public Task<ResourceSource> RestoreAsync(string id, CancellationToken cancellation = default) => throw new NotSupportedException();
        public Task PrepareAsync(ResourceSource source, CancellationToken cancellation = default) => Task.CompletedTask;
        public Task CommitAsync(string localPath, OverwritePolicy overwrite, CancellationToken cancellation = default) => throw new NotSupportedException();
        public string DisplayPath(string localPath) => localPath;
        public string PersistentId(string localPath) => localPath;
    }

    [AvaloniaFact]
    public async Task HelpImportsBothDatabaseFormsAndAnInvalidImportKeepsTheActiveFile()
    {
        string root = Directory.CreateTempSubdirectory("garbro-scheme-import-").FullName;
        string input = Path.Combine(root, "local.json"), gzip = input + ".gz";
        File.WriteAllText(input, """
            {"PortableSchemaVersion":2,"Version":152,"SchemeMap":{
              "XP3":{"$type":"GameRes.Formats.KiriKiri.Xp3Scheme, ArcFormats","KnownSchemes":{
                "GUI synthetic":{"$type":"GameRes.Formats.KiriKiri.XorCrypt, ArcFormats","m_key":227}
              },"NoCryptTitles":[]},
              "LOCAL-SYNTHETIC":{}
            }}
            """);
        using (var stream = new GZipStream(File.Create(gzip), CompressionMode.Compress)) stream.Write(File.ReadAllBytes(input));
        var storage = UiPlatform.Storage;
        var directory = UiPlatform.DataDirectory;
        var picker = new InputStorage(input);
        UiPlatform.Storage = picker; UiPlatform.DataDirectory = root;
        var store = Settings(root);
        var settings = store.Load();
        MaterialTheme.Apply(settings);
        var owner = new AppWindow(); owner.Show();
        var help = new HelpWindow(Text, settings, [], "Updates", store);
        var pending = help.ShowDialog(owner);
        try
        {
            var import = Control<Button>(help, "ImportSchemes");
            Click(import);
            await Until(() => import.IsEnabled && settings.SchemesFile?.EndsWith("Formats.json") == true);
            Assert.Equal(settings.SchemesFile, store.Load().SchemesFile);
            picker.Source = gzip;
            Click(import);
            await Until(() => import.IsEnabled && settings.SchemesFile?.EndsWith("Formats.json.gz") == true);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Capture(help.DesktopHost, "help-import");
            string active = settings.SchemesFile;
            Assert.Equal(active, store.Load().SchemesFile);
            var restored = store.Load();
            var formats = await UiPlatform.Worker.ExecuteAsync<ResourceFormatInfo[]>(
                new("resource-catalog", null, Context: restored.Context), restored.Limits);
            Assert.Contains("GUI synthetic", formats.Single(f => f.Tag == "XP3" && f.Type == "archive").Schemes);
            File.WriteAllText(input, """{"Version":153,"SchemeMap":{"XP3":{}}}""");
            picker.Source = input;
            Click(import);
            await Until(() => import.IsEnabled && help.GetVisualDescendants().OfType<TextBlock>()
                .Any(c => c.Text?.Contains(Text["Error.Format database contains incompatible schemes."]) == true));
            Assert.Equal(active, settings.SchemesFile);
            Assert.Equal(active, store.Load().SchemesFile);
        }
        finally
        {
            help.Close(); await pending; owner.Close();
            UiPlatform.Storage = storage; UiPlatform.DataDirectory = directory;
            Directory.Delete(root, true);
        }
    }
}
