using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using GARbro.Application;

namespace GARbro.Desktop;

public sealed record ExtractionChoice(string Output, ResourceExtractionOptions Options, OverwritePolicy Overwrite);
public sealed record CreationChoice(string Output, string FormatId, string OptionsJson, OverwritePolicy Overwrite);
public sealed record ConversionChoice(string Output, string FormatId, OverwritePolicy Overwrite, bool DiscardOriginalMedia, string AudioFormat = null, string VideoFormat = null);

public static class Dialogs
{
    public static Control ResponsivePages(TabControl tabs)
    {
        if (!UiPlatform.IsMobile) return tabs;
        var pages = tabs.Items.OfType<TabItem>().Select((tab, index) => (Id: index.ToString(), Label: tab.Header?.ToString() ?? "", Page: tab.Content as Control, Selected: ReferenceEquals(tab, tabs.SelectedItem))).ToArray();
        foreach (var tab in tabs.Items.OfType<TabItem>()) tab.Content = null;
        var selector = new ComboBox { ItemsSource = pages.Select(p => new UiChoice(p.Id, p.Label)).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
        var content = new ContentControl(); var root = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 12 };
        root.Children.Add(selector); Grid.SetRow(content, 1); root.Children.Add(content);
        selector.SelectionChanged += (_, _) => content.Content = pages.First(p => p.Id == ((UiChoice)selector.SelectedItem).Value).Page;
        selector.SelectedItem = selector.Items.OfType<UiChoice>().First(c => c.Value == (pages.FirstOrDefault(p => p.Selected).Id ?? pages[0].Id));
        return root;
    }
    public static AppWindow Window(DesktopText text, string title, double width = 620, double height = 500)
    {
        var window = new AppWindow { Title = text[title], Width = width, Height = height, WindowStartupLocation = WindowStartupLocation.CenterOwner, MinWidth = 400, MinHeight = 220 };
        WaylandTextInput.Attach(window); return window;
    }
    public static TextBlock Label(DesktopText text, string key) => new() { Text = text[key], TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    public static Button Button(DesktopText text, string key, Action clicked)
    { var b = new Button { Content = text[key] }; if (key is "Save" or "Start" or "Create" or "Confirm") b.Classes.Add("primary"); b.Click += (_, _) => clicked(); return b; }
    public static StackPanel Panel() => new() { Margin = new Avalonia.Thickness(18), Spacing = 10 };
    public static Control Frame(AppWindow window, StackPanel body, params Button[] buttons)
    {
        var root = new DockPanel(); var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Avalonia.Thickness(18) };
        foreach (var button in buttons) bar.Children.Add(button); DockPanel.SetDock(bar, Dock.Bottom); root.Children.Add(bar);
        root.Children.Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }); return root;
    }
    public static async Task<string> Input(AppWindow owner, DesktopText text, string title, string initial)
    {
        var window = Window(text, title, 520, 240); var body = Panel(); var input = new TextBox { Text = initial }; body.Children.Add(input);
        window.Content = Frame(window, body, Button(text, "Save", () => window.Close(input.Text)), Button(text, "Cancel", () => window.Close()));
        return await window.ShowDialog<string>(owner);
    }
    public static async Task<bool> Confirm(AppWindow owner, DesktopText text, string question)
    {
        var window = Window(text, "Confirm", 520, 240); var body = Panel(); body.Children.Add(Label(text, question));
        window.Content = Frame(window, body, Button(text, "Confirm", () => window.Close(true)), Button(text, "Cancel", () => window.Close(false)));
        return await window.ShowDialog<bool>(owner);
    }
    public static async Task<string> Parameters(AppWindow owner, DesktopText text, string json, string notice, string[] schemes = null)
    {
        var window = Window(text, "MissingOptions"); var body = Panel(); body.Children.Add(new TextBlock { Text = notice, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var form = new ParameterForm(text, json, schemes); body.Children.Add(form); var error = new TextBlock(); body.Children.Add(error);
        window.Content = Frame(window, body, Button(text, "Save", () => { try { window.Close(form.Value()); } catch (Exception e) { error.Text = text["Error"] + ": " + text.ErrorMessage(e.Message); } }), Button(text, "Cancel", () => window.Close()));
        return await window.ShowDialog<string>(owner);
    }
    static StackPanel Output(AppWindow owner, DesktopText text, string initial, out TextBox path)
    {
        var row = new StackPanel { Spacing = 6 }; row.Children.Add(Label(text, "Output"));
        var input = PathBox(initial); path = input; row.Children.Add(input);
        var browse = new Button { Content = text["Browse"] }; browse.Click += async (_, _) =>
        { var selected = await UiPlatform.Storage.PickOutputAsync(true); if (selected != null) SetPath(input, selected.LocalPath); };
        row.Children.Add(browse); return row;
    }
    public static TextBox PathBox(string path)
    {
        var box = new TextBox { IsReadOnly = UiPlatform.IsMobile }; SetPath(box, path); return box;
    }
    public static void SetPath(TextBox box, string path)
    {
        box.Tag = UiPlatform.IsMobile ? path : null;
        box.Text = UiPlatform.IsMobile && !string.IsNullOrEmpty(path) ? UiPlatform.Storage.DisplayPath(path) : path;
    }
    public static string PathValue(TextBox box) => box.Tag as string ?? box.Text;
    static ComboBox Overwrite(DesktopText text) => new() { ItemsSource = new[] { text["Fail"], text["Skip"], text["Replace"] }, SelectedIndex = 0 };
    public static async Task<ExtractionChoice> Extraction(AppWindow owner,DesktopText text,string initial,bool selectedOnly,int selectedCount)
    {
        var window=Window(text,selectedOnly?"ExtractSelected":"ExtractAll",780,730);var body=Panel();
        body.Children.Add(new TextBlock {Text=selectedOnly?text.Format("ExtractionSelectionScope",selectedCount):text["ExtractionAllScope"],TextWrapping=Avalonia.Media.TextWrapping.Wrap,Classes={"muted"}});
        body.Children.Add(Output(owner,text,initial,out var path));
        var policy=new Grid {ColumnDefinitions=new("Auto,*"),ColumnSpacing=12};var policyLabel=Label(text,"Overwrite");policyLabel.VerticalAlignment=VerticalAlignment.Center;policy.Children.Add(policyLabel);
        var overwrite=Overwrite(text);Grid.SetColumn(overwrite,1);policy.Children.Add(overwrite);body.Children.Add(policy);
        var recursion=new Grid {ColumnDefinitions=new("*,Auto,130"),ColumnSpacing=10};body.Children.Add(recursion);
        var recurse=new CheckBox {Name="RecursiveExtraction",Content=new TextBlock {Text=text["Recursive"],TextWrapping=Avalonia.Media.TextWrapping.Wrap},IsChecked=false};recursion.Children.Add(recurse);
        var depthLabel=Label(text,"MaxDepth");depthLabel.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(depthLabel,1);recursion.Children.Add(depthLabel);
        var depth=new NumericUpDown {Name="ExtractionDepth",Minimum=0,Maximum=64,Value=8,IsEnabled=false};Grid.SetColumn(depth,2);recursion.Children.Add(depth);
        var discardArchives=new CheckBox {Name="DiscardChildArchives",Content=new TextBlock {Text=text["DiscardChildArchives"],TextWrapping=Avalonia.Media.TextWrapping.Wrap},IsChecked=false,IsEnabled=false,IsVisible=false};body.Children.Add(discardArchives);
        recurse.IsCheckedChanged+=(_,_)=>{discardArchives.IsVisible=depth.IsEnabled=discardArchives.IsEnabled=recurse.IsChecked==true;};
        var advanced=Panel();advanced.Margin=new Avalonia.Thickness(0,8,0,0);
        var types=new[]{("Images","image"),("Audio","audio"),("Video","video"),("Scripts","script"),("Archives","archive"),("Other","")};
        var checks=new List<CheckBox>();var row=new WrapPanel();
        foreach(var type in types){var check=new CheckBox {Content=text[type.Item1],IsChecked=true,Margin=new Avalonia.Thickness(0,0,14,0)};checks.Add(check);row.Children.Add(check);}advanced.Children.Add(row);
        body.Children.Add(Label(text,"MediaConversion"));var conversion=new MediaConversionPanel(text);body.Children.Add(conversion);
        var discardMedia=new CheckBox {Name="DiscardOriginalMedia",Content=new TextBlock {Text=text["DiscardOriginalMedia"],TextWrapping=Avalonia.Media.TextWrapping.Wrap},IsChecked=false,IsEnabled=false};
        conversion.Changed+=()=>discardMedia.IsEnabled=conversion.HasConversion;body.Children.Add(discardMedia);
        body.Children.Add(new TextBlock {Text=text["SourceMediaPreserved"],TextWrapping=Avalonia.Media.TextWrapping.Wrap,Classes={"muted"}});
        var offset=new CheckBox {Content=text["AdjustOffset"],IsChecked=false};advanced.Children.Add(offset);
        body.Children.Add(new Expander {Header=text["ResourceTypes"],Content=advanced,HorizontalAlignment=HorizontalAlignment.Stretch});var error=new TextBlock {TextWrapping=Avalonia.Media.TextWrapping.Wrap};body.Children.Add(error);
        window.Content=Frame(window,body,Button(text,"Start",()=>
        {
            try
            {
                if(string.IsNullOrWhiteSpace(PathValue(path)) || !Path.IsPathFullyQualified(PathValue(path)))throw new ArgumentException(text["SelectOutput"]);
                string[] selected=types.Where((type,i)=>checks[i].IsChecked==true).Select(type=>type.Item2).ToArray();
                if(selected.Length==0)throw new ArgumentException(text["SelectResourceTypes"]);
                window.Close(new ExtractionChoice(Path.GetFullPath(PathValue(path)),new(Recursive:recurse.IsChecked==true,MaxDepth:(int)(depth.Value??8),Types:selected,
                    ImageFormat:conversion.ImageFormat,AdjustImageOffset:offset.IsChecked==true,AudioFormat:conversion.AudioFormat,VideoFormat:conversion.VideoFormat,
                    DiscardChildArchives:recurse.IsChecked==true && discardArchives.IsChecked==true,DiscardOriginalMedia:conversion.HasConversion && discardMedia.IsChecked==true),(OverwritePolicy)overwrite.SelectedIndex));
            }
            catch(Exception e){error.Text=text["Error"]+": "+text.ErrorMessage(e.Message);}
        }),Button(text,"Cancel",()=>window.Close()));
        return await window.ShowDialog<ExtractionChoice>(owner);
    }
    public static Task ExtractionErrors(AppWindow owner,DesktopText text,string[] failures)
    {
        var window=Window(text,"TaskErrors",700,480);var body=Panel();body.Children.Add(Label(text,"TaskErrorsHint"));
        string Detail(string value){string translated=text.ErrorMessage(value);if(translated!=value)return translated;int separator=value.IndexOf(": ",StringComparison.Ordinal);return separator<0?translated:value[..(separator+2)]+text.ErrorMessage(value[(separator+2)..]);}
        body.Children.Add(new TextBox {Text=string.Join(Environment.NewLine+Environment.NewLine,failures.Select(Detail)),IsReadOnly=true,AcceptsReturn=true,TextWrapping=Avalonia.Media.TextWrapping.Wrap,MinHeight=220});
        window.Content=Frame(window,body,Button(text,"Close",()=>window.Close()));return window.ShowDialog(owner);
    }
    static ComboBox FormatPicker(ResourceFormatInfo[] formats) => new() { ItemsSource = formats, DisplayMemberBinding = new Binding("Tag"), SelectedIndex = 0 };
    public static async Task<CreationChoice> Creation(AppWindow owner, DesktopText text, ResourceFormatInfo[] formats)
    {
        var window = Window(text, "Create", 650, 650); var body = Panel();
        body.Children.Add(Label(text, "Format")); var format = FormatPicker(formats); body.Children.Add(format);
        body.Children.Add(Label(text, "Filename")); var output = PathBox(""); body.Children.Add(output);
        var browse = new Button { Content = text["Browse"] }; browse.Click += async (_, _) =>
        { var f = (ResourceFormatInfo)format.SelectedItem; var selected = await UiPlatform.Storage.PickOutputAsync(false, "archive." + f.Extensions.FirstOrDefault()); if (selected != null) SetPath(output, selected.LocalPath); }; body.Children.Add(browse);
        var host = new ContentControl(); body.Children.Add(host); ParameterForm form = null;
        void Change() { var selected = format.SelectedItem as ResourceFormatInfo; form = new(text, selected?.OptionsJson, selected?.Schemes); host.Content = form; }
        format.SelectionChanged += (_, _) => Change(); Change();
        body.Children.Add(Label(text, "Overwrite")); var overwrite = Overwrite(text); body.Children.Add(overwrite); var error = new TextBlock(); body.Children.Add(error);
        window.Content = Frame(window, body, Button(text, "Create", () =>
        { try { if (!Path.IsPathFullyQualified(PathValue(output) ?? "")) throw new ArgumentException(text["Filename"]); window.Close(new CreationChoice(PathValue(output), ((ResourceFormatInfo)format.SelectedItem).Id, form.Value(), (OverwritePolicy)overwrite.SelectedIndex)); } catch (Exception e) { error.Text = text["Error"] + ": " + text.ErrorMessage(e.Message); } }), Button(text, "Cancel", () => window.Close()));
        return await window.ShowDialog<CreationChoice>(owner);
    }
    public static async Task<ConversionChoice> Conversion(AppWindow owner,DesktopText text,ResourceFormatInfo[] formats,string initial)
    {
        var window=Window(text,"Convert",780,540);var body=Panel();body.Children.Add(Output(owner,text,initial,out var path));
        var conversion=new MediaConversionPanel(text,formats,true);body.Children.Add(conversion);
        var discard=new CheckBox {Content=new TextBlock {Text=text["DiscardOriginalMedia"],TextWrapping=Avalonia.Media.TextWrapping.Wrap},IsChecked=true};body.Children.Add(discard);
        body.Children.Add(new TextBlock {Text=text["SourceMediaPreserved"],TextWrapping=Avalonia.Media.TextWrapping.Wrap,Classes={"muted"}});
        body.Children.Add(Label(text,"Overwrite"));var overwrite=Overwrite(text);body.Children.Add(overwrite);var error=new TextBlock {TextWrapping=Avalonia.Media.TextWrapping.Wrap};body.Children.Add(error);
        window.Content=Frame(window,body,Button(text,"Start",()=>
        {
            if(!Path.IsPathFullyQualified(PathValue(path)??"")){error.Text=text["SelectOutput"];return;}
            if(!conversion.HasConversion){error.Text=text["SelectConversionFormat"];return;}
            window.Close(new ConversionChoice(PathValue(path),conversion.ImageFormat,(OverwritePolicy)overwrite.SelectedIndex,discard.IsChecked==true,conversion.AudioFormat,conversion.VideoFormat));
        }),Button(text,"Cancel",()=>window.Close()));
        return await window.ShowDialog<ConversionChoice>(owner);
    }
}

/// <summary>Typed primitive options with a JSON editor for nested cipher/scheme objects.</summary>
public sealed class ParameterForm : StackPanel
{
    readonly JsonObject data;
    readonly List<Action> apply = new();
    public ParameterForm(DesktopText text, string json, string[] schemes = null)
    {
        Spacing = 8;
        data = JsonNode.Parse(string.IsNullOrWhiteSpace(json) || json == "null" ? "{}" : json) as JsonObject ?? new();
        Children.Add(Dialogs.Label(text, "Options"));
        if (schemes is { Length: > 0 })
        {
            Children.Add(Dialogs.Label(text, "Scheme"));
            var selected = new ComboBox { ItemsSource = new[] { "" }.Concat(schemes).ToArray(), SelectedIndex = 0 };
            Children.Add(selected);
            apply.Add(() => { if (selected.SelectedItem is string name && name.Length > 0) data["$scheme"] = name; });
        }
        foreach (var property in data.ToArray())
        {
            string name = property.Key; var value = property.Value;
            if (value is JsonValue scalar && scalar.TryGetValue<bool>(out var boolean))
            {
                var check = new CheckBox { Content = text.ParameterName(name), IsChecked = boolean }; Children.Add(check); apply.Add(() => data[name] = check.IsChecked == true);
            }
            else
            {
                Children.Add(new TextBlock { Text = text.ParameterName(name) });
                bool complex = value is JsonObject or JsonArray;
                bool password = name.Contains("Password", StringComparison.OrdinalIgnoreCase) || name.Contains("PassPhrase", StringComparison.OrdinalIgnoreCase) || name.Contains("Secret", StringComparison.OrdinalIgnoreCase);
                bool isString = value is JsonValue primitive && primitive.TryGetValue<string>(out _) || value == null && password;
                var box = new TextBox { Text = value == null ? isString ? "" : "null" : isString ? value.GetValue<string>() : value.ToJsonString(), AcceptsReturn = complex, MinHeight = complex ? 70 : 0 };
                if (name.Contains("Password", StringComparison.OrdinalIgnoreCase) || name.Contains("Secret", StringComparison.OrdinalIgnoreCase)) box.PasswordChar = '●';
                Children.Add(box);
                apply.Add(() => data[name] = isString ? JsonValue.Create(box.Text ?? "") : JsonNode.Parse(box.Text ?? "null"));
            }
        }
        if (data.Count == 0)
        {
            var box = new TextBox { Text = "{}", AcceptsReturn = true, MinHeight = 100 }; Children.Add(box);
            apply.Add(() => { var parsed = JsonNode.Parse(box.Text) as JsonObject ?? throw new ArgumentException("Expected a JSON object."); data.Clear(); foreach (var p in parsed) data[p.Key] = p.Value?.DeepClone(); });
        }
    }
    public string Value()
    {
        foreach (var action in apply) action();
        string json = data.ToJsonString();
        using var document = JsonDocument.Parse(json);
        if (json.Contains("\"$type\"", StringComparison.Ordinal) || json.Contains("\"$ref\"", StringComparison.Ordinal)) throw new ArgumentException("Type metadata is not allowed.");
        return json;
    }
}
