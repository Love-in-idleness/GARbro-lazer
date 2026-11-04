using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GARbro.Application;

namespace GARbro.Desktop;

public sealed class HelpWindow : AppWindow
{
    public HelpWindow(DesktopText text, AppSettings settings, ResourceFormatInfo[] formats, string page, SettingsStore store = null)
    {
        Title = text["Help"]; Width = 850; Height = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner; WaylandTextInput.Attach(this);
        var tabs = new TabControl();
        Control Reader(string content) => new TextBox { Text = content, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(12) };
        var manual = new TabItem { Header = text["Manual"], Content = Reader(DesktopServices.ReadResource("manual." + text.Language + ".md")) }; tabs.Items.Add(manual);
        var about = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(12) };
        about.Children.Add(new TextBlock { Text = "GARbro " + DesktopServices.Version, FontSize = 20 });
        about.Children.Add(new TextBlock { Text = text["Authors"] });
        about.Children.Add(new TextBlock { Text = text["Registered"] + ": " + formats.Length + "\n" + text["RegisteredOnly"] });
        var site = new Button { Content = DesktopServices.Repository }; site.Click += (_, _) => DesktopServices.OpenUrl(DesktopServices.Repository); about.Children.Add(site);
        var formatTabs = new TabControl { Height = 390 };
        using var ledger = JsonDocument.Parse(DesktopServices.ReadResource("portable-status.json"));
        var verified = ledger.RootElement.GetProperty("compiled").EnumerateArray().ToDictionary(r => r.GetProperty("class").GetString() + "/" + r.GetProperty("tag").GetString(), r => r.Clone());
        string Details(ResourceFormatInfo format)
        {
            if (!verified.TryGetValue(format.Id.Split(':', 2).Last() + "/" + format.Tag, out var status)) return text["NotVerified"];
            bool tested = new[] { "recognition", "list", "raw_extract", "conversion" }.Any(k => status.GetProperty(k).GetString() == "fixture-verified");
            bool created = status.TryGetProperty("creation", out var creation) && creation.GetString() == "fixture-verified";
            return (tested ? text["FixtureVerified"] : created ? text["CreationVerified"] : text["NotVerified"]) + "\n" +
                string.Join(", ", status.GetProperty("fixtures").EnumerateArray().Select(f => f.GetString())) + "\n" +
                string.Join("\n", status.GetProperty("limitations").EnumerateArray().Select(f => text.Limitation(f.GetString())));
        }
        foreach (var type in new[] { ("archive", "Archives"), ("image", "Images"), ("audio", "Audio"), ("script", "Scripts") })
        {
            var panel = new DockPanel(); var search = new TextBox { PlaceholderText = text["Search"], Margin = new Avalonia.Thickness(0, 0, 0, 8) }; DockPanel.SetDock(search, Dock.Top); panel.Children.Add(search);
            var listing = new ListBox(); panel.Children.Add(listing);
            void Refresh() => listing.ItemsSource = formats.Where(f => f.Type == type.Item1 && (f.Tag + " " + f.Description + " " + string.Join(" ", f.Extensions)).Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Tag + "  " + (f.CanWrite ? text["ReadWrite"] : text["ReadOnly"]) + "  " + string.Join(", ", f.Extensions.Select(e => "." + e)) + "\n" + f.Description + " · MIT\n" + Details(f)).ToArray();
            search.TextChanged += (_, _) => Refresh(); Refresh(); formatTabs.Items.Add(new TabItem { Header = text[type.Item2], Content = panel });
        }
        formatTabs.Items.Add(new TabItem { Header = text["Video"], Content = Reader(text["VideoFormatsInfo"]) });
        about.Children.Add(formatTabs); var aboutTab = new TabItem { Header = text["About"], Content = about }; tabs.Items.Add(aboutTab);
        var license = new TabItem { Header = text["License"], Content = Reader(text["LegalNotice"] + "\n\n" + DesktopServices.ReadResource("LICENSE") + "\n\n" + DesktopServices.ReadResource("third-party.md")) }; tabs.Items.Add(license);
        var diagnostics = new TabItem { Header = text["Troubleshoot"], Content = Reader("GARbro " + DesktopServices.Version + "\n" + RuntimeInformation.OSDescription + "\n" + RuntimeInformation.FrameworkDescription + "\n" + RuntimeInformation.ProcessArchitecture + "\n\n" + DesktopServices.ReadResource("troubleshoot." + text.Language + ".md")) }; tabs.Items.Add(diagnostics);
        var updates = new StackPanel { Spacing = 12, Margin = new Avalonia.Thickness(12) }; var message = new TextBlock { TextWrapping = TextWrapping.Wrap }; updates.Children.Add(message);
        var check = new Button { Content = text["Updates"] }; var database = new Button { Content = text["DatabaseUpdate"] }; updates.Children.Add(check); updates.Children.Add(database);
        async Task Check(bool updateDatabase)
        {
            check.IsEnabled = database.IsEnabled = false; message.Text = text["Working"];
            try
            {
                var release = await UpdateService.CheckAsync();
                if (updateDatabase)
                {
                    settings.SchemesFile = await UpdateService.UpdateDatabaseAsync(release, settings);
                    (store ?? new SettingsStore()).Save(settings); message.Text = text["DatabaseUpdated"];
                }
                else
                {
                    message.Text = UpdateService.IsNewer(release.Version, DesktopServices.Version) ? text.Format("UpdateAvailable", release.Version) : text["UpdateNone"];
                    var download = new Button { Content = release.Version }; download.Click += (_, _) => DesktopServices.OpenUrl(release.Url); updates.Children.Add(download);
                }
            }
            catch (Exception error) { message.Text = text["Error"] + ": " + text.ErrorMessage(error.Message); DesktopServices.Log(error); }
            finally { check.IsEnabled = database.IsEnabled = true; }
        }
        check.Click += async (_, _) => await Check(false); database.Click += async (_, _) => await Check(true);
        var updateTab = new TabItem { Header = text["Updates"], Content = updates }; tabs.Items.Add(updateTab);
        tabs.SelectedItem = page switch { "About" => aboutTab, "License" => license, "Troubleshoot" => diagnostics, "Updates" => updateTab, _ => manual };
        var root = new DockPanel(); var close = new Button { Content = text["Close"], Margin = new Avalonia.Thickness(12), HorizontalAlignment = HorizontalAlignment.Right }; close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close); root.Children.Add(Dialogs.ResponsivePages(tabs)); Content = root;
    }
}
