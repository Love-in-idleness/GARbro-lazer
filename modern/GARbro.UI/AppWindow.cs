using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace GARbro.Desktop;

/// <summary>A shared page. Desktop hosts a real native window; Android pushes a page.</summary>
public class AppWindow : UserControl
{
    public AppWindow() => Classes.Add("page");
    Window host;
    TaskCompletionSource<object> completion;
    double desiredWidth = 620, desiredHeight = 500, minimumWidth = 400, minimumHeight = 220;
    string title = "GARbro";
    bool closed;
    public event EventHandler Opened;
    public event EventHandler Closed;
    public string Title { get => host?.Title ?? title; set { title = value; if (host != null) host.Title = value; } }
    public new double Width { get => host?.Width ?? desiredWidth; set { desiredWidth = value; if (host != null) host.Width = value; } }
    public new double Height { get => host?.Height ?? desiredHeight; set { desiredHeight = value; if (host != null) host.Height = value; } }
    public new double MinWidth { get => minimumWidth; set { minimumWidth = value; if (host != null) host.MinWidth = value; } }
    public new double MinHeight { get => minimumHeight; set { minimumHeight = value; if (host != null) host.MinHeight = value; } }
    public WindowStartupLocation WindowStartupLocation { get; set; } = WindowStartupLocation.CenterOwner;
    public WindowIcon Icon { get; set; }
    public IStorageProvider StorageProvider => TopLevel.GetTopLevel(this)?.StorageProvider;
    public IClipboard Clipboard => TopLevel.GetTopLevel(this)?.Clipboard;
    public Screens Screens => host?.Screens;
    public IWindowImpl PlatformImpl => host?.PlatformImpl;

    public Window DesktopHost
    {
        get
        {
            if (UiPlatform.IsMobile) throw new PlatformNotSupportedException("Android pages do not create desktop windows.");
            if (host != null) return host;
            host = new Window { Title = title, Width = desiredWidth, Height = desiredHeight,
                MinWidth = minimumWidth, MinHeight = minimumHeight, Icon = Icon,
                WindowDecorations = WindowDecorations.None, ExtendClientAreaToDecorationsHint = false,
                WindowStartupLocation = WindowStartupLocation, Content = this };
            // Keep the native caption (including its system buttons) above page content.
            // Apply it to the platform as well: assigning the default styled value alone
            // does not force a refresh of an existing native frame.
            host.WindowDecorations = WindowDecorations.Full;
            host.Opened += (_, _) => Opened?.Invoke(this, EventArgs.Empty);
            host.Closed += (_, _) => Finish(null);
            WaylandTextInput.Attach(host);
            return host;
        }
    }
    public void Show() => DesktopHost.Show();
    public void Activate() => host?.Activate();
    public Task ShowDialog(AppWindow owner) => ShowDialog<object>(owner);
    public Task<T> ShowDialog<T>(Window owner) => DesktopHost.ShowDialog<T>(owner);
    public async Task<T> ShowDialog<T>(AppWindow owner)
    {
        if (!UiPlatform.IsMobile) return await ShowDialog<T>(owner.DesktopHost);
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pageContent = Content as Control; Content = null;
        var layout = new DockPanel(); var heading = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12, Margin = new Thickness(12, 8) };
        var back = new Button { Content = MaterialIcons.Create("Back"), Classes = { "iconMenu" } }; back.Click += (_, _) => Close();
        Avalonia.Automation.AutomationProperties.SetName(back, UiPlatform.CurrentText["Back"]);
        heading.Children.Add(back); var label = new TextBlock { Text = Title, FontSize = 22, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }; Grid.SetColumn(label, 1); heading.Children.Add(label);
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading); if (pageContent != null) layout.Children.Add(pageContent); Content = layout;
        UiPlatform.Navigation.Push(this);
        Opened?.Invoke(this, EventArgs.Empty);
        object result = await completion.Task;
        return result is T typed ? typed : default;
    }
    public void Close(object result = null)
    {
        if (host != null) host.Close(result);
        else { UiPlatform.Navigation?.Pop(this); Finish(result); }
    }
    void Finish(object result)
    {
        if (closed) return;
        closed = true; completion?.TrySetResult(result); Closed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class PageNavigation : Grid
{
    public PageNavigation(AppWindow root) => Children.Add(root);
    public void Push(AppWindow page) { foreach (var child in Children) child.IsVisible = false; Children.Add(page); }
    public void Pop(AppWindow page) { Children.Remove(page); if (Children.Count > 0) Children[^1].IsVisible = true; }
    public bool Back() { if (Children.Count <= 1) return false; ((AppWindow)Children[^1]).Close(); return true; }
}
