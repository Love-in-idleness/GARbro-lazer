using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GARbro.Application;

namespace GARbro.Desktop;

/// <summary>UI-owned LRU; decoded pixels and text share one strict byte budget.</summary>
internal sealed class PreviewMemoryCache(long capacity)
{
    readonly Dictionary<string,LinkedListNode<(string Key,ResourcePreview Value,long Bytes)>> items=new(StringComparer.Ordinal);
    readonly LinkedList<(string Key,ResourcePreview Value,long Bytes)> recent=new();
    long used;
    public bool TryGet(string key,out ResourcePreview value)
    {
        if(!items.TryGetValue(key,out var node)) {value=null;return false;}
        recent.Remove(node);recent.AddFirst(node);value=node.Value.Value;return true;
    }
    public void Put(string key,ResourcePreview value)
    {
        long bytes=(value.Image?.LongLength??0)+(value.Text?.Length??0)*2L+512;
        if(items.Remove(key,out var old)){recent.Remove(old);used-=old.Value.Bytes;}
        if(bytes>capacity)return;
        while(used+bytes>capacity && recent.Last is { } last){used-=last.Value.Bytes;items.Remove(last.Value.Key);recent.RemoveLast();}
        items[key]=recent.AddFirst((key,value,bytes));used+=bytes;
    }
    public void Clear(){items.Clear();recent.Clear();used=0;}
}
public sealed partial class MainWindow
{
    static Bitmap PrepareBitmap(ResourcePreview result)
    {
        if(result.Pixels is { } pixels)
        {
            var pinned=GCHandle.Alloc(result.Image,GCHandleType.Pinned);
            try {return new Bitmap(PixelFormat.Bgra8888,AlphaFormat.Unpremul,pinned.AddrOfPinnedObject(),new PixelSize(pixels.Width,pixels.Height),new Vector(96,96),pixels.Width*4);}
            finally {pinned.Free();}
        }
        using var input=new MemoryStream(result.Image);return new Bitmap(input);
    }
}
