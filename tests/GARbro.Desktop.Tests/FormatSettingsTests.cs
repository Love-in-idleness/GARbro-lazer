using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GARbro.Application;
using GARbro.Desktop;
using GARbro.Domain;
using Xunit;

public sealed class FormatSettingsEditorTests
{
    [Theory]
    [InlineData("uint", "4294967295")]
    [InlineData("int", "-2147483648")]
    [InlineData("long", "-9223372036854775808")]
    [InlineData("ulong", "18446744073709551615")]
    [InlineData("decimal", "12.5")]
    public void NumericSettingsRemainJsonNumbersWithoutLosingRange(string kind, string input)
    {
        var value = FormatSettingValues.Parse(kind, input);
        Assert.Equal(JsonValueKind.Number, value.ValueKind);
        Assert.Equal(input, value.GetRawText());
    }

    [Theory]
    [InlineData("uint", "-1", typeof(OverflowException))]
    [InlineData("uint", "4294967296", typeof(OverflowException))]
    [InlineData("uint", "44100.5", typeof(FormatException))]
    [InlineData("double", "NaN", typeof(FormatException))]
    [InlineData("float", "Infinity", typeof(FormatException))]
    public void InvalidNumericSettingsAreRejected(string kind, string input, Type errorType) =>
        Assert.Throws(errorType, () => FormatSettingValues.Parse(kind, input));

    static async Task Drain(AppWindow page) => await Dispatcher.UIThread.InvokeAsync(() => page.UpdateLayout(), DispatcherPriority.Background);
    static void Save(SettingsWindow page) => page.GetVisualDescendants().OfType<Button>()
        .Single(b => b.Content as string == "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static void SelectFormat(SettingsWindow page, ResourceFormatInfo format) => page.GetVisualDescendants()
        .OfType<ListBox>().Single(c => c.Name == "FormatList").SelectedItem = format;

    [AvaloniaFact]
    public async Task SavingAndReloadingAnUnsignedSettingStillAllowsFolderAndArchiveBrowsing()
    {
        string root = Path.Combine(Path.GetTempPath(), "garbro-unsigned-setting-" + Guid.NewGuid());
        var owner = new Window(); owner.Show(); MainWindow browser = null; SettingsWindow dialog = null;
        try
        {
            var formats = await UiPlatform.Worker.ExecuteAsync<ResourceFormatInfo[]>(new("resource-catalog", null), new());
            var format = formats.First(f => f.Settings.Any(s => s.Name == "ODNAudioSampleRate"));
            var settings = new AppSettings { Language = "en-US" };
            dialog = new SettingsWindow(settings, new DesktopText("en-US"), new[] { format }, "Formats");
            var result = dialog.ShowDialog<bool>(owner); await Drain(dialog);
            SelectFormat(dialog, format); await Drain(dialog);
            dialog.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "SettingODNAudioSampleRate").Text = "48000";
            await Drain(dialog); Save(dialog); Assert.True(await result);
            string key = format.Id + "/ODNAudioSampleRate";
            Assert.Equal(JsonValueKind.Number, settings.FormatSettings[key].ValueKind);
            Assert.Equal(48000u, settings.FormatSettings[key].GetUInt32());
            var store = new SettingsStore(Path.Combine(root, "settings.json")); store.Save(settings);
            browser = new MainWindow(store); browser.Show();
            string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
            await browser.LoadInputsAsync(new[] { fixtures }); Assert.Equal("directory", browser.ArchiveTag);
            await browser.LoadArchiveAsync(Path.Combine(fixtures, "standard.xfl")); Assert.Equal("XFL", browser.ArchiveTag);
        }
        finally { dialog?.Close(false); browser?.Close(); owner.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task OutOfRangeUnsignedInputCannotBeSaved()
    {
        var owner = new Window(); owner.Show(); SettingsWindow dialog = null;
        try
        {
            var format = new ResourceFormatInfo("sample-format", "TEST", "archive", "Test", Array.Empty<string>(), false,
                new[] { new FormatSettingInfo("SampleRate", "Sample rate", "", "uint", JsonSerializer.SerializeToElement(44100u)) }, "null");
            var settings = new AppSettings();
            dialog = new SettingsWindow(settings, new DesktopText("en-US"), new[] { format }, "Formats");
            var result = dialog.ShowDialog<bool>(owner); await Drain(dialog); SelectFormat(dialog, format); await Drain(dialog);
            var field = dialog.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "SettingSampleRate");
            field.Text = "4294967296"; await Drain(dialog); Save(dialog);
            Assert.False(result.IsCompleted);
            Assert.Contains("Sample rate", dialog.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "SettingsError").Text);
            field.Text = "48000"; await Drain(dialog); Save(dialog); Assert.True(await result);
            Assert.Equal(48000u, settings.FormatSettings["sample-format/SampleRate"].GetUInt32());
        }
        finally { dialog?.Close(false); owner.Close(); }
    }
}
