using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using GARbro.Domain;

namespace GARbro.Desktop;

public sealed class ShortcutSettingsPanel : UserControl
{
    readonly AppSettings settings;
    readonly DesktopText text;
    readonly ListBox commands = new() { Name = "ShortcutCommands" };
    readonly ListBox bindings = new() { Name = "ShortcutBindings", MinHeight = 70, MaxHeight = 100 };
    readonly TextBox search = new() { Name = "ShortcutSearch" };
    readonly TextBlock error = new() { Classes = { "error" }, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock pending = new() { FontWeight = FontWeight.SemiBold, MinHeight = 24 };
    readonly Button record, add;
    bool recording;
    string pendingGesture;
    string CommandId => (commands.SelectedItem as UiChoice)?.Value;
    public string ValidationError
    {
        get
        {
            try
            {
                var conflicts = ShortcutRules.Conflicts(ShortcutCatalog.Effective(settings));
                return conflicts.Length == 0 ? null : text.Format("ShortcutConflict", conflicts[0].Gesture, text[conflicts[0].FirstCommand], text[conflicts[0].SecondCommand]);
            }
            catch (ArgumentException e) { return text.ErrorMessage(e.Message); }
        }
    }
    public ShortcutSettingsPanel(AppSettings settings, DesktopText text)
    {
        this.settings = settings; this.text = text;
        var root = new Grid { RowDefinitions = new("Auto,Auto,*,Auto"), RowSpacing = 12, Margin = new Thickness(10), Height = 450 };
        root.Children.Add(new TextBlock { Text = text["ShortcutHint"], TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
        search.PlaceholderText = text["ShortcutSearch"]; Grid.SetRow(search, 1); root.Children.Add(search);
        var columns = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 16 }; Grid.SetRow(columns, 2); root.Children.Add(columns);
        commands.ItemTemplate = new FuncDataTemplate<UiChoice>((choice, _) =>
        {
            if (choice == null) return new Control();
            var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 10, Margin = new Thickness(2, 4) };
            row.Children.Add(MaterialIcons.Create(choice.Value, 18));
            var label = new TextBlock { Text = choice.Label, TextWrapping = TextWrapping.Wrap, MaxWidth = 260, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1); row.Children.Add(label); return row;
        });
        ScrollViewer.SetHorizontalScrollBarVisibility(commands, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        columns.Children.Add(commands);
        var editor = new StackPanel { Spacing = 10 }; Grid.SetColumn(editor, 1); columns.Children.Add(editor);
        editor.Children.Add(bindings); editor.Children.Add(pending);
        Button Action(string key, Action action)
        { var button = new Button { Content = text[key], HorizontalAlignment = HorizontalAlignment.Stretch }; button.Click += (_, _) => action(); return button; }
        record = Action("ShortcutRecord", () => { recording = !recording; record.Content = text[recording ? "ShortcutRecording" : "ShortcutRecord"]; record.Focus(); });
        add = Action("ShortcutAdd", () =>
        {
            if (CommandId == null || pendingGesture == null) return;
            var current = ShortcutCatalog.Effective(settings)[CommandId];
            settings.ShortcutBindings[CommandId] = current.Append(pendingGesture).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            pendingGesture = null; UpdateBindings();
        });
        var recordingRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        recordingRow.Children.Add(record); Grid.SetColumn(add, 1); recordingRow.Children.Add(add); editor.Children.Add(recordingRow);
        var resetRow = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 8 };
        editor.Children.Add(resetRow);
        resetRow.Children.Add(Action("ShortcutRemove", () =>
        {
            if (CommandId != null && bindings.SelectedItem is string gesture)
                settings.ShortcutBindings[CommandId] = ShortcutCatalog.Effective(settings)[CommandId].Where(s => s != gesture).ToArray();
            UpdateBindings();
        }));
        var reset = Action("ShortcutReset", () => { if (CommandId != null) settings.ShortcutBindings.Remove(CommandId); UpdateBindings(); });
        Grid.SetColumn(reset, 1); resetRow.Children.Add(reset);
        editor.Children.Add(Action("ShortcutResetAll", () => { settings.ShortcutBindings.Clear(); UpdateBindings(); }));
        Grid.SetRow(error, 3); root.Children.Add(error); Content = root;
        search.TextChanged += (_, _) => UpdateCommands();
        commands.SelectionChanged += (_, _) => { recording = false; pendingGesture = null; UpdateBindings(); };
        AddHandler(KeyDownEvent, CaptureKey, RoutingStrategies.Tunnel, true);
        UpdateCommands();
    }
    void UpdateCommands()
    {
        string selected = CommandId;
        commands.ItemsSource = ShortcutCatalog.Defaults.Keys.Where(k => text[k].Contains(search.Text ?? "", StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(k => text[k], StringComparer.CurrentCulture).Select(k => new UiChoice(k, text[k])).ToArray();
        commands.SelectedItem = commands.Items.OfType<UiChoice>().FirstOrDefault(c => c.Value == selected) ?? commands.Items.OfType<UiChoice>().FirstOrDefault();
        UpdateBindings();
    }
    void UpdateBindings()
    {
        bindings.ItemsSource = CommandId == null ? Array.Empty<string>() : ShortcutCatalog.Effective(settings)[CommandId];
        pending.Text = pendingGesture ?? (bindings.ItemCount == 0 ? text["ShortcutUnbound"] : "");
        add.IsEnabled = CommandId != null && pendingGesture != null;
        record.IsEnabled = CommandId != null; record.Content = text[recording ? "ShortcutRecording" : "ShortcutRecord"];
        error.Text = ValidationError;
    }
    void CaptureKey(object sender, KeyEventArgs e)
    {
        if (!recording) return;
        e.Handled = true;
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None or Key.ImeProcessed) return;
        try { pendingGesture = ShortcutCatalog.Normalize(new KeyGesture(e.Key, e.KeyModifiers).ToString()); }
        catch (ArgumentException ex) { error.Text = text.ErrorMessage(ex.Message); return; }
        recording = false; UpdateBindings();
    }
}
