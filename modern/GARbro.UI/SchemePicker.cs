using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace GARbro.Desktop;

sealed class SchemePicker : StackPanel
{
    public ListBox List { get; } = new() { Name = "GameSchemeList", Height = 230 };
    public UiChoice Choice => List.SelectedItem as UiChoice;

    public static string Label(DesktopText text, string value) => value switch
    {
        null or "" => text["Automatic"],
        "none" => text["NoEncryption"],
        _ => value
    };

    public SchemePicker(DesktopText text, string[] schemes, string selected = null)
    {
        Spacing = 8;
        var values = new[] { "" }.Concat(schemes).Distinct(StringComparer.Ordinal)
            .Select(value => new UiChoice(value, Label(text, value))).ToArray();
        var search = new TextBox { Name = "GameSchemeSearch", PlaceholderText = text["GameSchemeSearch"] };
        Avalonia.Automation.AutomationProperties.SetName(search, text["GameSchemeSearch"]);
        Avalonia.Automation.AutomationProperties.SetName(List, text["Scheme"]);
        List.ItemTemplate = new FuncDataTemplate<UiChoice>((item, _) => new TextBlock
        {
            Text = item?.Label, TextWrapping = TextWrapping.Wrap
        });
        var empty = Dialogs.Label(text, "NoGameSchemeMatches");
        empty.Classes.Add("muted");
        void Filter()
        {
            string current = Choice?.Value ?? selected ?? "";
            string query = search.Text?.Trim() ?? "";
            var matches = values.Where(item => item.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            List.ItemsSource = matches;
            List.SelectedItem = matches.FirstOrDefault(item => item.Value == current) ?? (matches.Length == 1 ? matches[0] : null);
            empty.IsVisible = matches.Length == 0;
        }
        search.TextChanged += (_, _) => Filter();
        Children.Add(search); Children.Add(List); Children.Add(empty);
        Filter();
    }
}
