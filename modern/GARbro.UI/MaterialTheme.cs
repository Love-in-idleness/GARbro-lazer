using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using GARbro.Domain;

namespace GARbro.Desktop;

public sealed class MaterialStyles : Styles
{
    public MaterialStyles() => AvaloniaXamlLoader.Load(this);
}

public static class MaterialTheme
{
    static AppSettings current;
    static Avalonia.Application initialized;
    public static IReadOnlyDictionary<string, uint> Palette(AppSettings settings, bool dark)
    {
        uint seed = 0xff000000u | uint.Parse(settings.SeedColor.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var colors = new Dictionary<string, uint>(ThemeColors.Create(seed, dark));
        if (settings.DynamicColors && UiPlatform.DynamicColor != null)
            foreach (string name in new List<string>(colors.Keys))
                if (UiPlatform.DynamicColor((dark ? "dark/" : "light/") + name) is uint dynamic) colors[name] = dynamic;
        return colors;
    }
    public static void Apply(AppSettings settings)
    {
        current = settings;
        var app = Avalonia.Application.Current;
        if (app == null) return;
        if (initialized != app)
        {
            initialized = app; app.Styles.Add(new MaterialStyles());
            app.ActualThemeVariantChanged += (_, _) => { if (current != null) Refresh(); };
        }
        app.RequestedThemeVariant = settings.Theme switch { "dark" => ThemeVariant.Dark, "light" => ThemeVariant.Light, _ => ThemeVariant.Default };
        Refresh();
    }
    static void Refresh()
    {
        var app = Avalonia.Application.Current;
        app.Resources["M3ControlHeight"] = UiPlatform.IsMobile ? 48d : 40d;
        var colors = Palette(current, app.ActualThemeVariant == ThemeVariant.Dark);
        foreach (var pair in colors) app.Resources["M3" + pair.Key] = new SolidColorBrush(Color.FromUInt32(pair.Value));
        foreach (string key in new[] { "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3" })
            app.Resources[key] = Color.FromUInt32(colors["Primary"]);
        app.Resources["SystemControlHighlightAccentBrush"] = app.Resources["M3Primary"];
        app.Resources["SystemControlForegroundBaseHighBrush"] = app.Resources["M3OnSurface"];
        app.Resources["SystemControlBackgroundAltHighBrush"] = app.Resources["M3Surface"];
        app.Resources["SystemControlBackgroundChromeMediumLowBrush"] = app.Resources["M3SurfaceContainer"];
        app.Resources["TitleBarBackgroundBrush"] = app.Resources["M3Surface"];
        app.Resources["CaptionButtonForeground"] = app.Resources["M3OnSurface"];
        var caption = Color.FromUInt32(colors["OnSurface"]);
        app.Resources["CaptionButtonHoverBackground"] = new SolidColorBrush(caption, 0.10);
        app.Resources["CaptionButtonPressedBackground"] = new SolidColorBrush(caption, 0.16);
    }
}
