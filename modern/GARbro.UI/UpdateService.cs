using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using GARbro.Application;

namespace GARbro.Desktop;

public sealed record ReleaseAsset(string Name, string Url, string Digest, long Size);
public sealed record UpdateRelease(string Version, string Url, ReleaseAsset[] Assets);

public static class UpdateService
{
    static readonly HttpClient client = CreateClient();
    static HttpClient CreateClient() { var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) }; c.DefaultRequestHeaders.UserAgent.ParseAdd("GARbro/0.3"); return c; }
    public static bool IsNewer(string candidate, string current) => Compare(candidate, current) > 0;
    static int Compare(string a, string b)
    {
        string Clean(string s) => s.TrimStart('v').Split('+')[0]; a = Clean(a); b = Clean(b);
        var aa = a.Split('-', 2); var bb = b.Split('-', 2);
        int main = (Version.TryParse(aa[0], out var av) ? av : new Version()).CompareTo(Version.TryParse(bb[0], out var bv) ? bv : new Version());
        if (main != 0) return main;
        if (aa.Length != bb.Length) return aa.Length == 1 ? 1 : -1;
        if (aa.Length == 1) return 0;
        var ap = aa[1].Split('.'); var bp = bb[1].Split('.');
        for (int i = 0; i < Math.Min(ap.Length, bp.Length); i++)
        { int n = int.TryParse(ap[i], out int ai) && int.TryParse(bp[i], out int bi) ? ai.CompareTo(bi) : string.CompareOrdinal(ap[i], bp[i]); if (n != 0) return n; }
        return ap.Length.CompareTo(bp.Length);
    }
    public static async Task<UpdateRelease> CheckAsync()
    {
        using var response = await client.GetAsync($"https://api.github.com/repos/{DesktopServices.RepositoryPath}/releases?per_page=100"); response.EnsureSuccessStatusCode();
        using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var releases = data.RootElement.EnumerateArray().Where(r => !r.GetProperty("draft").GetBoolean() &&
            (DesktopServices.Version.Contains('-') || !r.GetProperty("prerelease").GetBoolean())).Select(r => new UpdateRelease(r.GetProperty("tag_name").GetString(),
                r.GetProperty("html_url").GetString(), r.GetProperty("assets").EnumerateArray().Select(a => new ReleaseAsset(a.GetProperty("name").GetString(),
                    a.GetProperty("browser_download_url").GetString(), a.TryGetProperty("digest", out var digest) ? digest.GetString() : null, a.GetProperty("size").GetInt64())).ToArray())).ToArray();
        if (releases.Length == 0) throw new IOException("No release is available for this update channel.");
        return releases.Aggregate((best, next) => IsNewer(next.Version, best.Version) ? next : best);
    }
    static async Task<byte[]> Download(ReleaseAsset asset, long limit)
    {
        if (asset.Size > limit || !asset.Url.StartsWith(DesktopServices.Repository + "/releases/download/", StringComparison.Ordinal)) throw new InvalidDataException("Invalid update asset.");
        using var response = await client.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead); response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(); using var output = new MemoryStream(); var buffer = new byte[81920]; int n;
        while ((n = await stream.ReadAsync(buffer)) > 0) { if (output.Length > limit - n) throw new InvalidDataException("Update exceeds the size limit."); output.Write(buffer, 0, n); }
        byte[] data = output.ToArray(); string digest = "sha256:" + Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        if (!string.Equals(digest, asset.Digest, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update checksum does not match.");
        return data;
    }
    public static async Task<string> UpdateDatabaseAsync(UpdateRelease release, AppSettings settings)
    {
        var asset = release.Assets.Single(a => a.Name == "Formats.json.gz");
        byte[] data = await Download(asset, 32L << 20);
        string directory = Path.Combine(DesktopServices.DataDirectory, "databases", Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "Formats.json.gz"); string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, data);
            await UiPlatform.Worker.ExecuteAsync<bool>(
                new("resource-validate-schemes", temporary, Context: settings.Context), settings.Limits, null, default);
            File.Move(temporary, path, true); return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
