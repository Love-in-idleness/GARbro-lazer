using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using GARbro.Domain;

namespace GARbro.Desktop;

public static class ShortcutCatalog
{
    public static readonly IReadOnlyDictionary<string, string[]> Defaults = CreateDefaults();
    static Dictionary<string, string[]> CreateDefaults()
    {
        string primary = OperatingSystem.IsMacOS() ? "Meta" : "Ctrl";
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (string key in new[] { "Open", "OpenFolder", "NewTab", "CloseTab", "NextTab", "PreviousTab", "Exit", "Cancel", "Back", "Forward", "Parent", "Refresh", "Settings", "General", "Appearance", "Decoding", "Formats", "Shortcuts",
            "ExtractAll", "ExtractSelected", "Create", "Convert", "OpenSelected", "SelectAll", "CopyNames", "Mask", "Explore", "Trash", "FitWindow", "Toolbar", "Menubar", "Statusbar", "FitImage",
            "Feedback", "Manual", "Troubleshoot", "Updates", "License", "ExportLogs", "About", "FocusMenu", "NextSelection", "SortName", "SortType", "SortSize", "SortNone", "Play", "Pause", "Stop", "NextPage", "PreviousPage", "Copy", "Wrap" }) result[key] = Array.Empty<string>();
        void Bind(string command, params string[] values) => result[command] = values;
        Bind("NewTab",primary+"+T");Bind("CloseTab",primary+"+W");Bind("NextTab","Ctrl+Tab");Bind("PreviousTab","Ctrl+Shift+Tab");
        Bind("Open", primary + "+O"); Bind("Settings", primary + "+P"); Bind("Exit", primary + "+Q");
        Bind("FitWindow", primary + "+H"); Bind("Explore", primary + "+E"); Bind("SelectAll", primary + "+A"); Bind("CopyNames", primary + "+C");
        Bind("Parent", primary + "+PageUp", "Back"); Bind("Create", "F3"); Bind("ExtractSelected", "F4"); Bind("Refresh", "F5"); Bind("Convert", "F6");
        Bind("Trash", "Delete"); Bind("OpenSelected", "Enter"); Bind("Cancel", "Escape"); Bind("Mask", "Add"); Bind("FocusMenu", "F10"); Bind("NextSelection", "Space");
        Bind("Toolbar", "Alt+Shift+T"); Bind("Menubar", "Alt+Shift+M"); Bind("Statusbar", "Alt+Shift+S"); Bind("Back", "Alt+Left"); Bind("Forward", "Alt+Right");
        return result;
    }
    public static string Normalize(string value)
    {
        string normalized = ShortcutRules.Normalize(value);
        try
        {
            var gesture = KeyGesture.Parse(normalized);
            if (gesture.Key is Key.None or Key.DeadCharProcessed or Key.ImeProcessed or Key.ImeConvert or Key.ImeNonConvert or Key.ImeAccept or Key.ImeModeChange)
                throw new ArgumentException("Invalid shortcut.");
            return ShortcutRules.Normalize(gesture.ToString());
        }
        catch (Exception e) when (e is FormatException or ArgumentException) { throw new ArgumentException("Invalid shortcut.", e); }
    }
    public static Dictionary<string, string[]> Effective(AppSettings settings)
    {
        var result=Defaults.ToDictionary(p=>p.Key,
            p=>(settings.ShortcutBindings.TryGetValue(p.Key,out var values)?values:p.Value)?.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                ??throw new ArgumentException("Invalid shortcut."),StringComparer.Ordinal);
        return result;
    }
    internal static void MigrateTabBindings(AppSettings settings)
    {
        if(settings.ShortcutBindings==null)return;
        var reserved=settings.ShortcutBindings.Where(p=>Defaults.ContainsKey(p.Key))
            .SelectMany(p=>p.Value??throw new ArgumentException("Invalid shortcut.")).Select(Normalize).ToArray();
        foreach(string key in new[]{"NewTab","CloseTab","NextTab","PreviousTab"})
            if(!settings.ShortcutBindings.ContainsKey(key))
            {
                var available=ShortcutRules.Unclaimed(Defaults[key],reserved);
                if(available.Length!=Defaults[key].Length)settings.ShortcutBindings[key]=available;
            }
    }
    public static void Validate(AppSettings settings)
    {
        if (settings.ShortcutBindings == null) throw new ArgumentException("Invalid settings collection.");
        if (ShortcutRules.Conflicts(Effective(settings)).Length > 0) throw new ArgumentException("Shortcut bindings conflict.");
    }
}
