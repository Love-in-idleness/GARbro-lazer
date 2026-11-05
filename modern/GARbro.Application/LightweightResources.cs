using System;
using System.IO;
using System.Linq;
using System.Text;
using GARbro.Media;

namespace GARbro.Application;

/// <summary>Bounded filesystem operations that do not initialize the format catalogue.</summary>
public static class LightweightResources
{
    public static bool IsTextName(string name) => Path.GetExtension(name).ToLowerInvariant() is
        ".txt" or ".json" or ".xml" or ".csv" or ".ini" or ".log" or ".md" or ".yaml" or ".yml" or ".ks" or ".lua" or ".js" or ".css" or ".html";
    public static bool IsOrdinaryName(string name) => IsTextName(name) || MediaInfo.IsVideoName(name) || MediaInfo.IsAudioName(name) || Path.GetExtension(name).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".wav" or ".mp3" or ".ogg" or ".opus" or ".flac" or ".aiff";
    static System.Collections.Generic.Dictionary<string,string> registered = new(StringComparer.OrdinalIgnoreCase);
    public static void RegisterTypes(System.Collections.Generic.Dictionary<string,string> types) => registered=new(types,StringComparer.OrdinalIgnoreCase);
    public static string Type(string name) => MediaInfo.IsVideoName(name)?"video":MediaInfo.IsAudioName(name)?"audio":registered.TryGetValue(Path.GetExtension(name).TrimStart('.'),out string kind)?kind:CommonType(name);
    static string CommonType(string name) => MediaInfo.IsVideoName(name) ? "video" : IsTextName(name) ? "script" : Path.GetExtension(name).ToLowerInvariant() switch
    { ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" => "image", ".wav" or ".mp3" or ".ogg" or ".opus" or ".flac" or ".aiff" => "audio", _ => "" };
    public static bool IsPlainTextFile(string path)
    {
        using var file=File.OpenRead(path);Span<byte> sample=stackalloc byte[256];int n=file.Read(sample);sample=sample[..n];
        if(sample.StartsWith(new byte[]{255,254}) || sample.StartsWith(new byte[]{254,255}) || sample.StartsWith(new byte[]{0,0,254,255}))return true;
        foreach(byte b in sample)if(b<32 && b is not (9 or 10 or 13))return false;
        return true;
    }
    public static bool TryExecute(WorkerRequest request, out object value, System.Threading.CancellationToken cancellation = default)
    {
        value = null;
        var location = request.Location;
        if (location == null || location.Chain.Length != 0 || !Directory.Exists(location.RootPath)) return false;
        if (request.Operation == "resource-list")
        {
            var entries = new DirectoryInfo(location.RootPath).EnumerateFileSystemInfos().Select(f =>
                NewEntry(f)).ToArray();
            BrowserEntry NewEntry(FileSystemInfo f) {cancellation.ThrowIfCancellationRequested();return new(f.Name,f.Name,f is DirectoryInfo ? "directory" : Type(f.Name),f is FileInfo file ? file.Length : 0,f is DirectoryInfo);}
            value = new BrowserListing(location, "directory", true, entries); return true;
        }
        if (request.Operation != "resource-preview" || request.ForcedType is not (null or "binary") || !(request.PreviewMode == "hex" || request.ForcedType == "binary" || IsTextName(request.Entry))) return false;
        string root = Path.GetFullPath(location.RootPath), path = Path.GetFullPath(Path.Combine(root, request.Entry));
        string relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar)) throw new IOException("Resource leaves its input directory.");
        if(request.PreviewMode!="hex" && request.ForcedType!="binary" && !IsPlainTextFile(path))return false;
        using var input = File.OpenRead(path);
        value = ReadPage(input, request.Entry, request.Offset, request.PreviewMode == "hex" || request.ForcedType == "binary", request.PreviewEncoding ?? request.Context?.TextEncoding ?? "auto");
        return true;
    }
    public static ResourcePreview ReadPage(Stream input, string name, long offset, bool hex, string code, bool script = false)
    {
        if (offset < 0 || input.CanSeek && offset > input.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        byte[] header = new byte[4]; int headerLength = input.ReadAtLeast(header, 4, false);
        if (input.CanSeek) input.Position = offset;
        else
        {
            if (offset == 0) input = new PrefixReadStream(header.AsMemory(0, headerLength).ToArray(), input);
            else
            {
                long left = offset - headerLength;
                if (left < 0) throw new ArgumentOutOfRangeException(nameof(offset));
                byte[] skip = new byte[81920];
                while (left > 0) { int n = input.Read(skip, 0, (int)Math.Min(left, skip.Length)); if (n == 0) throw new ArgumentOutOfRangeException(nameof(offset)); left -= n; }
            }
        }
        byte[] data = new byte[65537]; int count = input.ReadAtLeast(data, data.Length, false); bool more = count > 65536; count = Math.Min(count, 65536);
        string text;
        if (!hex)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding encoding = new DecoderContext(TextEncoding: code).ResolveEncoding() ?? new UTF8Encoding(false, true);
            int bom = 0;
            if (code is null or "auto")
            {
                var h = header.AsSpan(0, headerLength);
                if (h.StartsWith(new byte[] {255,254,0,0})) { encoding = Encoding.UTF32; bom = 4; }
                else if (h.StartsWith(new byte[] {0,0,254,255})) { encoding = new UTF32Encoding(true,true); bom = 4; }
                else if (h.StartsWith(new byte[] {255,254})) { encoding = Encoding.Unicode; bom = 2; }
                else if (h.StartsWith(new byte[] {254,255})) { encoding = Encoding.BigEndianUnicode; bom = 2; }
                else if (h.StartsWith(new byte[] {239,187,191})) { encoding = Encoding.UTF8; bom = 3; }
            }
            int start = offset == 0 ? Math.Min(bom, count) : 0;
            // A page boundary may split a multibyte character; do not flush that decoder.
            var chars = new char[encoding.GetMaxCharCount(count)];
            try { int n = encoding.GetDecoder().GetChars(data, start, count-start, chars, 0, !more); text = new string(chars,0,n); }
            catch (DecoderFallbackException) { text = Encoding.GetEncoding(932).GetString(data,start,count-start); }
            hex = !script && text.Count(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) > Math.Max(1,text.Length/20);
            if (!hex) return new("text",name,text,Offset:offset,More:more);
        }
        var result = new StringBuilder(count * 5);
        for (int i=0;i<count;i+=16)
        {
            result.Append((offset+i).ToString("X8")).Append("  ");
            int n = Math.Min(16,count-i);
            for(int j=0;j<16;j++) { if(j==8)result.Append(' '); if(j<n)result.Append(data[i+j].ToString("X2")).Append(' '); else result.Append("   "); }
            result.Append(" | ");
            for(int j=0;j<n;j++)result.Append(data[i+j] is >=32 and <127?(char)data[i+j]:'.');
            result.AppendLine();
        }
        return new("binary",name,result.ToString(),Offset:offset,More:more);
    }
    sealed class PrefixReadStream(byte[] prefix, Stream tail) : Stream
    {
        int position;
        public override int Read(byte[] b,int o,int n) { int m=Math.Min(n,prefix.Length-position);prefix.AsSpan(position,m).CopyTo(b.AsSpan(o));position+=m;return m>0?m:tail.Read(b,o,n); }
        public override int Read(Span<byte> b) { int m=Math.Min(b.Length,prefix.Length-position);prefix.AsSpan(position,m).CopyTo(b);position+=m;return m>0?m:tail.Read(b); }
        public override bool CanRead=>true; public override bool CanSeek=>false; public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException(); public override long Position {get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Flush(){} public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException(); public override void SetLength(long n)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int n)=>throw new NotSupportedException();
    }
}
