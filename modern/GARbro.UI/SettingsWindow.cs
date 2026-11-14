using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using GARbro.Application;
using GARbro.Domain;

namespace GARbro.Desktop;

public sealed class SettingsWindow : AppWindow
{
    public SettingsWindow(AppSettings settings, DesktopText text, ResourceFormatInfo[] formats, string section = "General")
    {
        Title = text["Settings"]; Width = 820; Height = 720; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WaylandTextInput.Attach(this);
        var working = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings));
        working.SessionFormatSettings = new(settings.SessionFormatSettings);
        var invalidFields = new Dictionary<string, string>();
        var body = Dialogs.Panel(); var tabs = new TabControl(); body.Children.Add(tabs);
        var general = new StackPanel { Spacing = 10, Margin = new Avalonia.Thickness(10) };
        var appearance = new StackPanel { Spacing = 10, Margin = new Avalonia.Thickness(10) };
        var decoding = new StackPanel { Spacing = 10, Margin = new Avalonia.Thickness(10) };
        ComboBox Choice(string key, string[] values, string selected)
        {
            var panel = key == "Theme" ? appearance : key == "Encoding" ? decoding : general;
            panel.Children.Add(Dialogs.Label(text, key));
            var choices = values.Select(value => new UiChoice(value, value switch { "auto" => text["Auto"], "system" => text["System"], "light" => text["Light"], "dark" => text["Dark"], "en-US" => text["English"], "zh-CN" => text["Chinese"], "ru-RU" => text["Russian"], "ja-JP" => text["Japanese"], _ => value })).ToArray();
            var combo = new ComboBox { Name = key, ItemsSource = choices, SelectedItem = choices.FirstOrDefault(c => c.Value == selected) };
            panel.Children.Add(combo); return combo;
        }
        var language = Choice("Language", new[] { "auto", "en-US", "zh-CN", "ru-RU", "ja-JP" }, working.Language);
        var theme = Choice("Theme", new[] { "system", "light", "dark" }, working.Theme);
        appearance.Children.Add(Dialogs.Label(text, "Color")); var color = new TextBox { Text = working.SeedColor, Name = "SeedColor" }; appearance.Children.Add(color);
        var dynamic = new CheckBox { Content = text["DynamicColors"], IsChecked = working.DynamicColors, IsEnabled = UiPlatform.DynamicColor != null };
        var swatches = new WrapPanel { Margin = new Avalonia.Thickness(0, 4) };
        foreach (var swatch in new[] { ("Purple", "#6750A4"), ("Blue", "#3561A8"), ("Green", "#006C4C"), ("Rose", "#984061"), ("Amber", "#855400") })
        {
            var button = new Button { Width = 40, Height = 40, Margin = new Avalonia.Thickness(0, 0, 8, 0), Background = Avalonia.Media.Brush.Parse(swatch.Item2), CornerRadius = new Avalonia.CornerRadius(20) };
            ToolTip.SetTip(button, text[swatch.Item1]); Avalonia.Automation.AutomationProperties.SetName(button, text[swatch.Item1]);
            button.Click += (_, _) => { color.Text = swatch.Item2; dynamic.IsChecked = false; }; swatches.Children.Add(button);
        }
        appearance.Children.Add(swatches); appearance.Children.Add(dynamic);
        if (UiPlatform.IsMobile)
        {
            var access = new Button { Content = text["AllFilesAccess"] }; access.Click += (_, _) => UiPlatform.RequestAllFilesAccess?.Invoke(); general.Children.Add(access);
            var notification = new Button { Content = text["Notifications"] }; notification.Click += (_, _) => UiPlatform.RequestNotificationPermission?.Invoke(); general.Children.Add(notification);
            var cache = new Button { Content = text["ClearCache"] };
            cache.Click += async (_, _) =>
            {
                cache.IsEnabled = false;
                try { if (UiPlatform.ClearStorageCache != null) await UiPlatform.ClearStorageCache(); cache.Content = text["CacheCleared"]; }
                catch (Exception e) { cache.Content = text.ErrorMessage(e.Message); }
                finally { cache.IsEnabled = true; }
            };
            general.Children.Add(cache); general.Children.Add(new TextBlock { Text = text["CacheHint"], Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Choice("Encoding", new[] { "auto", "gbk", "shift_jis", "utf-8", "utf-16", "utf-16BE", "gb18030", "big5" }.Concat(Encoding.GetEncodings().Select(e => e.Name)).Distinct().ToArray(), working.TextEncoding); encoding.IsEditable = true;
        decoding.Children.Add(Dialogs.Label(text, "Time")); var time = new NumericUpDown { Minimum = 1, Maximum = 86400, Value = (decimal)working.TimeoutSeconds }; decoding.Children.Add(time);
        decoding.Children.Add(Dialogs.Label(text, "Memory")); var memory = new NumericUpDown { Minimum = 64, Maximum = 1048576, Value = working.MemoryMiB }; decoding.Children.Add(memory);
        general.Children.Add(Dialogs.Label(text, "DefaultOutput")); var output = Dialogs.PathBox(working.OutputDirectory); general.Children.Add(output);
        var browse = new Button { Content = text["Browse"] }; browse.Click += async (_, _) =>
        { var folder = await UiPlatform.Storage.PickOutputAsync(true); if (folder != null) Dialogs.SetPath(output, folder.LocalPath); }; general.Children.Add(browse);
        var fit = new CheckBox { Content = text["FitImage"], IsChecked = working.FitImage }; var wrap = new CheckBox { Content = text["Wrap"], IsChecked = working.WrapText };
        appearance.Children.Add(fit); appearance.Children.Add(wrap);
        foreach (var tab in new[] { ("General", general), ("Appearance", appearance), ("Decoding", decoding) })
            tabs.Items.Add(new TabItem { Name = tab.Item1, Header = text[tab.Item1], Content = tab.Item2 });
        var formatPanel = new Grid { ColumnDefinitions = new("180,*"), ColumnSpacing = 12, Height = 450 };
        var list = new ListBox { Name = "FormatList", ItemsSource = formats.Where(f => f.Settings.Length > 0).ToArray(), DisplayMemberBinding = new Binding("Tag") };
        var fields = new StackPanel { Spacing = 8 }; var fieldsScroll = new ScrollViewer { Content = fields }; Grid.SetColumn(fieldsScroll, 1);
        formatPanel.Children.Add(list); formatPanel.Children.Add(fieldsScroll);
        list.SelectionChanged += (_, _) =>
        {
            fields.Children.Clear();
            if (list.SelectedItem is not ResourceFormatInfo format) return;
            foreach (var setting in format.Settings)
            {
                string key = format.Id + "/" + setting.Name;
                bool secret = setting.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) || setting.Name.Contains("Key", StringComparison.OrdinalIgnoreCase) || setting.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase);
                var destination = secret ? working.SessionFormatSettings : working.FormatSettings;
                JsonElement value = destination.GetValueOrDefault(key, setting.Value);
                fields.Children.Add(new TextBlock { Text = text.ParameterName(setting.Name, setting.Label), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                if (setting.Kind == "bool")
                {
                    var check = new CheckBox { IsChecked = value.ValueKind == JsonValueKind.True }; fields.Children.Add(check);
                    check.IsCheckedChanged += (_, _) => destination[key] = JsonSerializer.SerializeToElement(check.IsChecked == true);
                }
                else if (setting.Kind == "encoding")
                {
                    var combo = new ComboBox { ItemsSource = new[] { text["Defaults"], "932", "936", "65001", "54936", "950", "1200", "1201" },
                        SelectedItem = working.FormatSettings.ContainsKey(key) ? value.ToString() : text["Defaults"], IsEditable = true };
                    fields.Children.Add(combo);
                    combo.SelectionChanged += (_, _) =>
                    { if (combo.SelectedIndex == 0) destination.Remove(key); else if (int.TryParse(combo.SelectedItem?.ToString(), out int cp)) destination[key] = JsonSerializer.SerializeToElement(cp); };
                }
                else
                {
                    var box = new TextBox { Name = "Setting" + setting.Name, Text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString() }; fields.Children.Add(box);
                    if (secret) box.PasswordChar = '●';
                    void UpdateValue()
                    {
                        try
                        {
                            destination[key] = FormatSettingValues.Parse(setting.Kind, box.Text);
                            invalidFields.Remove(key);
                        }
                        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
                        { invalidFields[key] = text.ParameterName(setting.Name, setting.Label); }
                    }
                    box.TextChanged += (_, _) => UpdateValue();
                    // Validate existing values as well as edits. Visiting a format
                    // must never turn its integer default into a JSON string.
                    UpdateValue();
                }
            }
        };
        tabs.Items.Add(new TabItem { Name = "Formats", Header = text["Formats"], Content = formatPanel });
        var shortcuts = new ShortcutSettingsPanel(working, text);
        tabs.Items.Add(new TabItem { Name = "Shortcuts", Header = text["Shortcuts"], Content = shortcuts });
        foreach (var tab in tabs.Items.OfType<TabItem>()) { tab.FontSize = 14; tab.Padding = new Avalonia.Thickness(12, 8); }
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Name == section) ?? tabs.Items[0];
        if (UiPlatform.IsMobile) { body.Children.Remove(tabs); body.Children.Insert(0, Dialogs.ResponsivePages(tabs)); }
        var error = new TextBlock { Name = "SettingsError", Classes = { "error" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap }; body.Children.Add(error);
        Content = Dialogs.Frame(this, body, Dialogs.Button(text, "Save", () =>
        {
            try
            {
                if (invalidFields.Count > 0) { error.Text = text.Format("InvalidFormatValue", invalidFields.Values.First()); return; }
                if (shortcuts.ValidationError is string conflict) { error.Text = conflict; return; }
                working.SeedColor = color.Text; working.DynamicColors = dynamic.IsChecked == true;
                working.Language = (language.SelectedItem as UiChoice)?.Value ?? "auto"; working.Theme = (theme.SelectedItem as UiChoice)?.Value ?? "system";
                string code = encoding.Text ?? (encoding.SelectedItem as UiChoice)?.Value ?? "auto";
                working.TextEncoding = code == text["Auto"] ? "auto" : code;
                working.TimeoutSeconds = (double)(time.Value ?? 300); working.MemoryMiB = (long)(memory.Value ?? 1024);
                working.OutputDirectory = Dialogs.PathValue(output)?.Trim() ?? ""; working.FitImage = fit.IsChecked == true; working.WrapText = wrap.IsChecked == true; working.Validate();
                foreach (var property in typeof(AppSettings).GetProperties().Where(p => p.CanWrite)) property.SetValue(settings, property.GetValue(working));
                Close(true);
            }
            catch (Exception e) { error.Text = text.ErrorMessage(e.Message); }
        }), Dialogs.Button(text, "Cancel", () => Close(false)));
    }
}
