using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Data;
using GARbro.Domain;

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
    public new double Width { get => host?.Width ?? desiredWidth; set { desiredWidth = value; if (host != null) { host.Width = value; FitToWorkArea(host.IsVisible); } } }
    public new double Height { get => host?.Height ?? desiredHeight; set { desiredHeight = value; if (host != null) { host.Height = value; FitToWorkArea(host.IsVisible); } } }
    public new double MinWidth { get => host?.MinWidth ?? minimumWidth; set { minimumWidth = value; if (host != null) host.MinWidth = value; } }
    public new double MinHeight { get => host?.MinHeight ?? minimumHeight; set { minimumHeight = value; if (host != null) host.MinHeight = value; } }
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
            var page = new Border { Child = this, CornerRadius = new CornerRadius(0,0,16,16), ClipToBounds = true };
            page.Bind(Border.BackgroundProperty, new Avalonia.Data.Binding("Background") {Source=this});
            host = new Window { Title = title, Width = desiredWidth, Height = desiredHeight,
                MinWidth = minimumWidth, MinHeight = minimumHeight, Icon = Icon,
                WindowDecorations = WindowDecorations.Full, ExtendClientAreaToDecorationsHint = !OperatingSystem.IsMacOS(),
                ExtendClientAreaTitleBarHeightHint = 40,
                WindowStartupLocation = WindowStartupLocation, Content = page, Background = Avalonia.Media.Brushes.Transparent,
                TransparencyBackgroundFallback = Avalonia.Media.Brushes.Transparent,
                TransparencyLevelHint = new[] {WindowTransparencyLevel.Transparent,WindowTransparencyLevel.None} };
            host.PropertyChanged += (_,e) => {if(e.Property==Window.WindowStateProperty)page.CornerRadius=host.WindowState is WindowState.Maximized or WindowState.FullScreen?new CornerRadius(0):new CornerRadius(0,0,16,16);};
            page.Bind(MarginProperty, new Binding(nameof(Window.WindowDecorationMargin)) { Source = host });
            SharedWindowChrome.Attach(host, UiPlatform.CurrentText);
            FitToWorkArea(false);
            host.Opened += (_, _) =>
            {
                FitToWorkArea(true);
                Dispatcher.UIThread.Post(() => FitToWorkArea(true), DispatcherPriority.Loaded);
                Opened?.Invoke(this, EventArgs.Empty);
            };
            host.ScalingChanged += (_, _) => Dispatcher.UIThread.Post(() => FitToWorkArea(true));
            host.Closed += (_, _) => Finish(null);
            WaylandTextInput.Attach(host);
            return host;
        }
    }
    void FitToWorkArea(bool place)
    {
        if (closed || host.WindowState != WindowState.Normal) return;
        var screen = host.Screens.ScreenFromWindow(host) ?? host.Screens.Primary;
        if (screen == null || screen.WorkingArea.Width <= 0 || screen.WorkingArea.Height <= 0) return;
        double scale = screen.Scaling;
        var work = screen.WorkingArea;
        // Before mapping, some platforms cannot report the frame. Reserve space for
        // it, then use its measured size after opening. Never centre an oversized
        // client area with the caption above the screen (common at 150% scaling).
        var frame = host.FrameSize;
        double frameWidth = frame.HasValue ? Math.Max(0, frame.Value.Width - host.ClientSize.Width) : 16;
        double frameHeight = frame.HasValue ? Math.Max(0, frame.Value.Height - host.ClientSize.Height) : 48;
        var fitted = WindowGeometry.Fit(host.Width, host.Height, minimumWidth, minimumHeight,
            work.Width / scale, work.Height / scale, frameWidth + 2, frameHeight + 2);
        host.MinWidth = fitted.MinimumWidth; host.MinHeight = fitted.MinimumHeight;
        host.Width = fitted.Width; host.Height = fitted.Height;
        if (!place) return;
        var total = PixelSize.FromSize(new Size(fitted.Width + frameWidth, fitted.Height + frameHeight), scale);
        var position = host.Position;
        host.Position = new PixelPoint(WindowGeometry.ConstrainOrigin(position.X, work.X, work.Width, total.Width),
            WindowGeometry.ConstrainOrigin(position.Y, work.Y, work.Height, total.Height));
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
