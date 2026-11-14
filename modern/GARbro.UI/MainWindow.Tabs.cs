using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using GARbro.Application;

namespace GARbro.Desktop;

public sealed partial class MainWindow
{
    sealed class BrowserTab : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public string Header { get; private set; }
        public string Tooltip { get; private set; }
        public string Focused, Input, Query="", Scheme="none", Options="", Encoding="auto", ContextKey;
        public bool InputComplete;
        public long PreviewOffset;
        public Vector Scroll;
        public string[] Selection=Array.Empty<string>();
        public BrowserListing Listing;
        public List<ResourceLocation> History=new(), Roots=new();
        public Dictionary<string,string> Types=new(StringComparer.Ordinal);
        public int HistoryIndex=-1;
        public override string ToString()=>Header??"";
        public void Describe(string header,string tooltip)
        {
            Header=header;Tooltip=tooltip;
            PropertyChanged?.Invoke(this,new(nameof(Header)));
            PropertyChanged?.Invoke(this,new(nameof(Tooltip)));
        }
    }
    readonly ObservableCollection<BrowserTab> openTabs=new();
    BrowserTab currentTab=new();
    readonly ListBox tabStrip=new() {Name="ResourceTabs",SelectionMode=SelectionMode.Single,Classes={"browserTabs"}};
    bool changingTab;
    TaskCompletionSource browsingReady;

    Control BuildTabs()
    {
        openTabs.Add(currentTab);
        tabStrip.ItemsSource=openTabs;
        tabStrip.Bind(Avalonia.Automation.AutomationProperties.NameProperty,Bind("BrowsePanel"));
        tabStrip.ItemsPanel=new FuncTemplate<Panel>(()=>new StackPanel {Orientation=Orientation.Horizontal,Spacing=4});
        tabStrip.ItemTemplate=new FuncDataTemplate<BrowserTab>((tab,_)=>
        {
            var header=new Grid {ColumnDefinitions=new("Auto,*,Auto"),ColumnSpacing=8,MinWidth=100,MaxWidth=240};
            header.Children.Add(MaterialIcons.Create("BrowsePanel",16));
            var title=new TextBlock {VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
            title.Bind(TextBlock.TextProperty,new Binding(nameof(BrowserTab.Header)) {Source=tab});
            Grid.SetColumn(title,1);header.Children.Add(title);
            var close=new Button {Content=MaterialIcons.Create("Close",14),Classes={"tabClose"},Focusable=false};
            close.Bind(ToolTip.TipProperty,Bind("CloseTab"));
            close.Bind(Avalonia.Automation.AutomationProperties.NameProperty,Bind("CloseTab"));
            close.Click+=async(_,e)=>{e.Handled=true;await Guard(()=>CloseTab(tab));};
            Grid.SetColumn(close,2);header.Children.Add(close);
            header.Bind(ToolTip.TipProperty,new Binding(nameof(BrowserTab.Tooltip)) {Source=tab});
            return header;
        });
        tabStrip.SelectedItem=currentTab;
        tabStrip.SelectionChanged+=async(_,_)=>
        {
            if(!changingTab && tabStrip.SelectedItem is BrowserTab tab)await Guard(()=>SelectTab(tab));
        };
        var row=new Grid {ColumnDefinitions=new("*,Auto"),ColumnSpacing=4};
        row.Children.Add(tabStrip);
        var add=Button("NewTab",false);add.Content=MaterialIcons.Create("NewTab",20);add.Classes.Add("tabAdd");
        add.Bind(Avalonia.Automation.AutomationProperties.NameProperty,Bind("NewTab"));
        Grid.SetColumn(add,1);row.Children.Add(add);
        UpdateTabHeaders();return row;
    }
    void UpdateTabHeaders()
    {
        foreach(var tab in openTabs)
        {
            var location=tab.Listing?.Location;
            string title=location==null?tab.Input==null?Text["NewTab"]:Path.GetFileName(tab.Input):
                location.Directory.Length>0?Path.GetFileName(location.Directory.TrimEnd('/')):
                location.Chain.LastOrDefault()??Path.GetFileName(location.RootPath.TrimEnd(Path.DirectorySeparatorChar));
            if(location!=null && tab.Listing.Physical && Path.GetDirectoryName(tab.Input)==location.RootPath)
                title=tab.Selection.FirstOrDefault()??Path.GetFileName(tab.Input);
            tab.Describe(string.IsNullOrEmpty(title)?location?.RootPath??Text["NewTab"]:title,location?.DisplayPath??tab.Input??Text["NewTab"]);
        }
        Title=currentTab.Listing==null?"GARbro":"GARbro — "+currentTab.Header;
    }
    void SaveTabView()
    {
        currentTab.Query=search.Text??"";currentTab.Scheme=scheme.Text;currentTab.Options=options.Text;
        currentTab.Encoding=(previewEncoding.SelectedItem as UiChoice)?.Value??"auto";
        currentTab.PreviewOffset=preview?.Offset??0;
        currentTab.Selection=files.SelectedItems.Cast<BrowserEntry>().Select(e=>e.Name).ToArray();
        currentTab.Focused=(files.SelectedItem as BrowserEntry)?.Name;
        currentTab.Scroll=files.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.Offset??default;
    }
    void SetTabSelection(BrowserTab tab)
    {
        changingTab=true;tabStrip.SelectedItem=tab;changingTab=false;
    }
    async Task SelectTab(BrowserTab tab)
    {
        if(ReferenceEquals(tab,currentTab))return;
        if(busy || choosingOperation){SetTabSelection(currentTab);return;}
        SaveTabView();navigationCancellation?.Cancel();navigationCancellation=null;++navigationGeneration;
        filterCancellation?.Cancel();++filterGeneration;loading=false;progress.IsVisible=progress.IsIndeterminate=false;
        ClearPreview();currentTab=tab;SetTabSelection(tab);
        updatingRows=true;
        try
        {
            files.Selection.Clear();files.ItemsSource=Array.Empty<BrowserEntry>();files.IsEnabled=false;
            path.Text=listing?.Location.DisplayPath??"";
            search.Text=tab.Query;scheme.Text=tab.Scheme;options.Text=tab.Options;
            changingEncoding=true;
            previewEncoding.SelectedItem=previewEncoding.Items.OfType<UiChoice>().FirstOrDefault(c=>c.Value==tab.Encoding);
            changingEncoding=false;
        }
        finally {updatingRows=false;}
        BuildFolders();UpdateTabHeaders();UpdateCommands();status.Text=Text["Welcome"];
        if(listing==null)path.Focus();
        if(tab.Input!=null && !tab.InputComplete){await LoadInput(tab.Input);return;}
        if(listing!=null && tab.ContextKey!=WorkerProtocol.Serialize(Settings.Context))
            await LoadLocationAsync(listing.Location with {Fingerprint=null},false,true);
        else await RefreshFilesAsync(restoring:tab);
        if(ReferenceEquals(currentTab,tab))
        {
            UpdateListingStatus();await PreviewSelection(false,tab.PreviewOffset);
        }
    }
    async Task NewTab()
    {
        var tab=new BrowserTab();openTabs.Add(tab);await SelectTab(tab);
    }
    async Task CloseTab(BrowserTab tab)
    {
        if(busy || choosingOperation || !openTabs.Contains(tab))return;
        Task switching=Task.CompletedTask;
        if(ReferenceEquals(currentTab,tab))
        {
            int index=openTabs.IndexOf(tab);
            if(openTabs.Count==1)openTabs.Add(new BrowserTab());
            switching=SelectTab(openTabs[index==0?1:index-1]);
        }
        openTabs.Remove(tab);previewCache.Clear();UpdateCommands();
        try {await switching;}
        finally
        {
            if(tab.Listing!=null && !isClosed)await UiPlatform.Worker.ExecuteAsync<bool>(new("resource-release",null),Settings.Limits,null,lifetime.Token);
        }
    }
    Task SwitchTab(int delta) => SelectTab(openTabs[(openTabs.IndexOf(currentTab)+delta+openTabs.Count)%openTabs.Count]);
    public Task WaitForBrowsingAsync() => busy || choosingOperation ? (browsingReady??=new(TaskCreationOptions.RunContinuationsAsynchronously)).Task : Task.CompletedTask;
}
