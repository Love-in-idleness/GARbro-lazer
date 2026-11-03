using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;

namespace GARbro.Desktop;

/// <summary>One caption theme and the same controls on Win32, X11 and Wayland.</summary>
public static class SharedWindowChrome
{
    public static void Attach(Window window, DesktopText text)
    {
        if (Avalonia.Application.Current is { } app && app.TryFindResource("GarbroWindowDecorations", out var resource))
            window.WindowDecorationsTheme = (ControlTheme)resource;
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!window.IsVisible) return;
            var root = window.GetVisualAncestors().LastOrDefault() ?? window;
            var buttons = root.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Name is "PART_MinimizeButton" or "PART_MaximizeButton" or "PART_CloseButton" or "PART_PopoverCloseButton" or "PART_PopoverFullScreenButton").ToArray();
            void Shape()
            {
                if(root.Bounds.Width<=0 || root.Bounds.Height<=0)return;
                double radius=window.WindowState is WindowState.Maximized or WindowState.FullScreen?0:16;
                root.Clip=new RectangleGeometry(new Rect(root.Bounds.Size),radius,radius);
                NativeRoundedFrame.Apply(window,radius);
            }
            void Update()
            {
                Shape();
                foreach (var button in buttons)
                {
                    string key = button.Name switch { "PART_MinimizeButton" => "Minimize", "PART_MaximizeButton" => window.WindowState == WindowState.Maximized ? "Restore" : "Maximize", "PART_PopoverFullScreenButton" => "Restore", _ => "Close" };
                    ToolTip.SetTip(button, text[key]); AutomationProperties.SetName(button, text[key]);
                }
            }
            void LanguageChanged(object sender, PropertyChangedEventArgs args) => Update();
            void StateChanged(object sender, AvaloniaPropertyChangedEventArgs args) { if (args.Property == Window.WindowStateProperty) Update(); }
            void SizeChanged(object sender,AvaloniaPropertyChangedEventArgs args) {if(args.Property==Visual.BoundsProperty)Shape();}
            root.PropertyChanged += SizeChanged;
            text.PropertyChanged += LanguageChanged; window.PropertyChanged += StateChanged;
            window.Closed += (_, _) => { root.PropertyChanged -= SizeChanged;text.PropertyChanged -= LanguageChanged; window.PropertyChanged -= StateChanged; };
            Update();
        }, DispatcherPriority.Loaded);
    }
}
