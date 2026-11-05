using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace GARbro.Desktop;

public static class MaterialIcons
{
    static readonly Dictionary<string, Geometry> geometries = new();
    static string Name(string key) => key switch
    {
        "File" or "OpenFolder" => "folder_open", "Open" => "draft", "Folders" => "folder", "Process" => "inventory_2",
        "Selection" or "Mask" or "AssignType" => "checklist", "View" => "view_sidebar",
        "FolderPanel" => "folder", "BrowsePanel" => "description", "PreviewPanel" => "image",
        "Settings" => "settings", "Shortcuts" => "tune", "General" or "Formats" => "tune", "Appearance" => "palette", "Decoding" => "text_fields", "Help" => "help", "Back" => "arrow_back", "Forward" => "arrow_forward",
        "Parent" => "arrow_upward", "Refresh" => "refresh", "Create" => "archive",
        "ExtractAll" => "unarchive", "ExtractSelected" => "download", "Convert" => "transform",
        "Cancel" or "Exit" or "Close" => "close", "SelectAll" => "select_all", "CopyNames" or "Copy" => "content_copy",
        "Explore" => "open_in_new", "Trash" => "delete", "SortName" or "SortType" or "SortSize" or "SortNone" or "Sort" or "Name" or "Size" or "Type" or "Unsorted" => "sort",
        "FitWindow" or "FitImage" => "fit_screen", "Images" => "image", "Audio" => "music_note", "Video" => "movie", "Search" => "search",
        "Recent" => "history", "About" => "info", "Language" => "translate", "Theme" => "dark_mode", "Color" => "palette",
        "Encoding" or "Scripts" => "text_fields", "Save" => "save", "Play" => "play_arrow", "Pause" => "pause", "Stop" => "stop",
        "PreviousPage" => "skip_previous", "NextPage" => "skip_next", "Feedback" => "bug_report", "Manual" => "menu_book",
        "Updates" or "DatabaseUpdate" => "system_update_alt", "License" => "gavel", "ExportLogs" => "download",
        "Troubleshoot" => "warning", "Binary" or "Text" or "Other" => "description", _ => "more_horiz"
    };
    public static PathIcon Create(string key, double size = 22)
    {
        string name = Name(key);
        if (!geometries.TryGetValue(name, out var geometry))
        {
            using var stream = AssetLoader.Open(new Uri("avares://GARbro.UI/Assets/Icons/" + name + ".svg"));
            var svg = XDocument.Load(stream);
            string data = string.Join(" ", svg.Descendants().Where(e => e.Name.LocalName == "path" && (string)e.Attribute("fill") != "none").Select(e => (string)e.Attribute("d")));
            geometries[name] = geometry = Geometry.Parse(data);
        }
        return new PathIcon { Data = geometry, Width = size, Height = size };
    }
}
