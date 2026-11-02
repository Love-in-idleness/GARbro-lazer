using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using GARbro.Application;

namespace GARbro.Desktop;

public sealed class AppSettings
{
    public int Version { get; set; } = 2;
    public string SeedColor { get; set; } = "#6750A4";
    public bool DynamicColors { get; set; } = true;
    public string Language { get; set; } = "auto";
    public string Theme { get; set; } = "system";
    public string TextEncoding { get; set; } = "auto";
    public double TimeoutSeconds { get; set; } = 300;
    public long MemoryMiB { get; set; } = 1024;
    public string OutputDirectory { get; set; } = "";
    public bool MenuVisible { get; set; } = true;
    public bool ToolbarVisible { get; set; } = true;
    public bool StatusVisible { get; set; } = true;
    public bool FitImage { get; set; } = true;
    public bool WrapText { get; set; } = true;
    public string Sort { get; set; } = "name";
    public bool SortDescending { get; set; }
    public double Width { get; set; } = 1180;
    public double Height { get; set; } = 780;
    public double Volume { get; set; } = 1;
    public List<string> Recent { get; set; } = new();
    public Dictionary<string, JsonElement> FormatSettings { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<string, JsonElement> SessionFormatSettings { get; set; } = new();
    public string SchemesFile { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public WorkerLimits Limits => new(TimeoutSeconds, checked(MemoryMiB * (1L << 20)));
    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveLanguage => Language == "auto" ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en-US" : Language;
    [System.Text.Json.Serialization.JsonIgnore]
    public DecoderContext Context => new(TextEncoding, EffectiveLanguage, FormatSettings.Concat(SessionFormatSettings).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value), SchemesFile);
    public void Validate()
    {
        if (OutputDirectory == null || Recent == null || FormatSettings == null) throw new ArgumentException("Invalid settings collection.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(SeedColor ?? "", "^#[0-9a-fA-F]{6}$")) throw new ArgumentException("Choose a color as #RRGGBB.");
        Limits.Validate();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _ = Context.ResolveEncoding();
        if (Theme is not ("system" or "light" or "dark")) throw new ArgumentException("Unknown theme.");
        if (Language is not ("auto" or "en-US" or "zh-CN")) throw new ArgumentException("Unknown language.");
        if (OutputDirectory.Length > 0 && !Path.IsPathFullyQualified(OutputDirectory)) throw new ArgumentException("Choose an absolute output directory.");
    }
}

public sealed class SettingsStore
{
    public string Path { get; }
    public string LoadError { get; private set; }
    public SettingsStore(string path = null) => Path = path ?? UiPlatform.SettingsPath ?? Environment.GetEnvironmentVariable("GARBRO_SETTINGS_PATH") ??
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GARbro", "settings.json");
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new();
            if (new FileInfo(Path).Length > 1 << 20) throw new InvalidDataException("Settings are too large.");
            var result = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new();
            if (result.Version is not (1 or 2)) throw new InvalidDataException("Unsupported settings version.");
            result.Version = 2; result.MenuVisible = true;
            result.Validate();
            return result;
        }
        catch (Exception e) when (e is IOException or JsonException or ArgumentException or OverflowException)
        { LoadError = e.Message; return new(); }
    }
    public void Save(AppSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)));
        string temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            if (LoadError != null && File.Exists(Path))
            {
                string backup = Path + ".invalid-" + Guid.NewGuid().ToString("N");
                File.Copy(Path, backup);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(backup, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            File.Move(temporary, Path, true);
            LoadError = null;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed partial class DesktopText : INotifyPropertyChanged
{
    readonly Dictionary<string, (string En, string Zh)> strings = new(StringComparer.Ordinal);
    public string Language { get; private set; }
    public event PropertyChangedEventHandler PropertyChanged;
    public DesktopText(string language)
    {
        Language = language;
        foreach (string line in Data.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = line.Trim().Split('|');
            if (pair.Length == 3) strings.Add(pair[0], (pair[1], pair[2]));
        }
    }
    public string this[string key] => strings.TryGetValue(key, out var value) ? Language == "zh-CN" ? value.Zh : value.En : key;
    public void Change(string language) { Language = language; PropertyChanged?.Invoke(this, new("Item[]")); }
    public string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, this[key], args);
    public string Limitation(string message) => Language == "zh-CN" ? message switch
    {
        "Creation/read-back covers a small synthetic file with explicit required keys; not every option or real-game variant." => "已验证使用必要密钥创建并回读小型合成文件；尚未覆盖所有选项或实际游戏变体。",
        "Decoding requires Windows Media Foundation." => "解码需要 Windows Media Foundation。",
        "Fractional target tile coordinates require a resampling backend; integer-aligned composition is implemented." => "小数坐标的图块合成需要重采样后端；目前已实现整数坐标合成。",
        "Only stored and MSZIP folders are implemented. Quantum, LZX, split blocks and multi-cabinet sets report unsupported. Non-UTF-8 names use the current culture ANSI code page." => "目前仅支持未压缩与 MSZIP 数据。Quantum、LZX、分块和多卷 CAB 会报告不支持；非 UTF-8 文件名使用当前区域的 ANSI 代码页。",
        "Requires separately supplied hibiki_works.json or an imported scheme. Legacy BinaryFormatter auxiliary files are not loaded. Only the synthetic portable fixture is verified." => "需要另行提供 hibiki_works.json 或导入游戏方案。不加载旧式 BinaryFormatter 辅助文件；目前仅验证了可移植合成样本。",
        "Synthetic portable decoding is verified; the historical Windows backend is not compared for this fixture." => "已验证可移植后端解码合成样本；该样本尚未与原版 Windows 后端比较。",
        "The WebP-strip fixture is verified against synthetic pixels in the portable runtime; the original Windows native WebP backend is not compared." => "已验证可移植运行时对 WebP 图像条合成像素的解码；尚未与原版 Windows 原生 WebP 后端比较。",
        "Uses open-source SQLite3 Multiple Ciphers RC4 compatibility mode. Synthetic plain and fully encrypted SQLite indexes are verified; mixed plaintext-page-1 databases and actual game indexes are unverified. Active journals are rejected." => "使用开源 SQLite3 Multiple Ciphers 的 RC4 兼容模式。已验证合成的明文和完整加密 SQLite 索引；第一页明文的混合加密数据库及实际游戏索引尚未验证。不接受仍有活动日志的数据库。",
        _ => message
    } : message;
    public string ParameterName(string name, string fallback = null) => strings.ContainsKey("Parameter." + name) ? this["Parameter." + name] : fallback ?? name;
    public IEnumerable<string> Keys => strings.Keys;
    const string Data = """
Video|Video|视频
Media|Media|媒体
NoVideo|Video playback is unavailable.|视频播放不可用。
VideoMuted|Audio output is unavailable; playing video muted.|音频设备不可用，正在静音播放视频。
VideoAudioUnavailable|Audio decoding is unavailable; playing video muted.|音频解码不可用，正在静音播放视频。
DiscardChildArchives|Do not keep extracted child archives after successful recursive extraction|递归解包成功后不保留子归档文件
DiscardOriginalMedia|Do not keep original media copies in the output after conversion|转换成功后不保留输出中的原始多媒体格式
SourceMediaPreserved|Source game files are always preserved. Clear this option to also copy the original format into the output folder.|始终保留来源游戏文件。取消勾选可将原始格式也复制到输出文件夹。
InvalidFormatValue|Enter a valid numeric value for {0}.|请为“{0}”输入有效数值。
ClearCache|Clear downloaded source cache|清理下载的来源缓存
CacheCleared|Source cache cleared|来源缓存已清理
CacheHint|Keeps your original documents and exported files. Reopen shared files if their temporary permission has expired.|保留原始文档和已导出的文件。临时授权失效后，请重新打开分享的文件。
Appearance|Appearance|外观
Decoding|Decoding|解码
Purple|Purple|紫色
Blue|Blue|蓝色
Green|Green|绿色
Rose|Rose|玫红色
Amber|Amber|琥珀色
Folder|Folder|文件夹
OtherType|Other|其他
OneEntry|1 item|1 项
EntryCount|{0} items|{0} 项
WelcomeTitle|Explore your game resources|浏览游戏资源
WelcomeHint|Open a file or folder to browse, preview and extract its contents. You can also drag files here.|打开文件或目录，即可浏览、预览和提取资源。也可以将文件拖放到这里。
EmptyFolder|Nothing here yet|这里还没有文件
EmptyHint|This location contains no entries.|当前位置没有可显示的项目。
NoMatches|No matching files|没有匹配的文件
SearchHint|Try another filename or clear the search.|试试其他文件名，或清空搜索内容。
Authors|Original author: morkt · MIT · GARbro-no-wpf contributors|原作者：morkt · MIT · GARbro-no-wpf 贡献者
LegalNotice|Licenses and copyright notices below retain their original wording.|以下许可证与版权声明保留原文。
InterruptedTask|The previous task was interrupted. Completed outputs were retained; reopen the source to retry.|上次任务已中断，已完成的输出得到保留；重新打开来源后可重试。
Parameter.AFAEncodingCP|AFA filename encoding|AFA 文件名编码
Parameter.BMPEnableExtensions|Enable BMP extensions|启用 BMP 扩展
Parameter.JPEGQuality|JPEG quality|JPEG 质量
Parameter.NOAEncodingCP|NOA filename encoding|NOA 文件名编码
Parameter.NexasEncodingCP|Nexas filename encoding|Nexas 文件名编码
Parameter.ODNAudioSampleRate|ODN audio sample rate|ODN 音频采样率
Parameter.OGGFixCrc|Repair Ogg checksums|修复 Ogg 校验值
Parameter.PFSEncodingCP|PFS filename encoding|PFS 文件名编码
Parameter.RCTApplyMask|Apply RCT masks|应用 RCT 蒙版
Parameter.RCTOverlayFrames|Overlay RCT frames|叠加 RCT 图像帧
Parameter.TIM2AlphaFormat|TIM2 alpha format|TIM2 透明度格式
Parameter.ZIPEncodingCP|ZIP filename encoding|ZIP 文件名编码
Parameter.AsbKey|ASB key|ASB 密钥
Parameter.BaseArchive|Base archive|基础归档
Parameter.BodyName|Data filename|数据文件名
Parameter.CodePage|Code page|代码页
Parameter.CompressContents|Compress contents|压缩文件内容
Parameter.CompressIndex|Compress index|压缩索引
Parameter.CompressionLevel|Compression level|压缩级别
Parameter.CompressionType|Compression method|压缩方式
Parameter.DecoderFallback|Decoder fallback|解码回退方式
Parameter.EncoderFallback|Encoder fallback|编码回退方式
Parameter.EncodingName|Encoding name|编码名称
Parameter.Encryption|Encryption|加密
Parameter.EncryptionInfo|Encryption parameters|加密参数
Parameter.ExtraKey|Additional key|附加密钥
Parameter.FileMap|File mapping|文件映射
Parameter.FileNameEncoding|Filename encoding|文件名编码
Parameter.GameKeyData|Game key data|游戏密钥数据
Parameter.HashAfterCrypt|Hash after encryption|加密后计算哈希
Parameter.HeaderName|Index filename|索引文件名
Parameter.IsBrowserDisplay|Browser display support|浏览器显示支持
Parameter.IsBrowserSave|Browser save support|浏览器保存支持
Parameter.IsMailNewsDisplay|Mail display support|邮件显示支持
Parameter.IsMailNewsSave|Mail save support|邮件保存支持
Parameter.IsReadOnly|Read only|只读
Parameter.IsSingleByte|Single-byte encoding|单字节编码
Parameter.Key|Key|密钥
Parameter.Key1|First key|第一密钥
Parameter.Key2|Second key|第二密钥
Parameter.Keyword|Keyword|关键字
Parameter.MaxCharCount|Maximum character count|最大字符数
Parameter.MsdPassword|MSD password|MSD 密码
Parameter.NameLength|Filename length|文件名长度
Parameter.ObfuscatedIndex|Obfuscate index|混淆索引
Parameter.PassPhrase|Passphrase|口令
Parameter.Password|Password|密码
Parameter.RetainDirs|Preserve directories|保留目录
Parameter.Scheme|Game scheme|游戏方案
Parameter.ScrambleContents|Scramble contents|扰乱文件内容
Parameter.Secret|Secret|机密参数
Parameter.StartupTjsNotEncrypted|Keep startup.tjs unencrypted|不加密 startup.tjs
Parameter.UseBaseArchive|Use a base archive|使用基础归档
Parameter.Version|Format version|格式版本
Parameter.WebName|Web encoding name|网页编码名称
Parameter.WindowsCodePage|Windows code page|Windows 代码页
Process|Process resources|处理资源
Selection|Selection and actions|选择与操作
Color|Seed color|种子色
DynamicColors|Use system dynamic colors|使用系统动态色
AllFilesAccess|Allow access to shared storage…|允许访问共享存储…
Notifications|Allow progress notifications…|允许进度通知…
BrowsePanel|Files|文件列表
PreviewPanel|Preview|资源预览
FolderPanel|Folders|目录导航
File|File|文件
View|View|视图
Help|Help|帮助
Open|Open file…|打开文件…
OpenFolder|Open folder…|打开目录…
OpenSelected|Open selected item|打开所选项目
Create|Create archive…|创建归档…
Convert|Convert multimedia…|批量媒体转换…
Recent|Recent files|最近打开
Exit|Exit|退出
ExtractAll|Extract all…|全部提取…
ExtractSelected|Extract selected…|提取所选…
Recursive|Recursive extraction…|递归解包…
Cancel|Cancel|取消
Settings|Settings|设置
FitWindow|Fit window to image|窗口适应图片
FitImage|Fit image to preview|图片适应预览区
Refresh|Refresh|刷新
Toolbar|Show toolbar|显示工具栏
Menubar|Show menu bar|显示菜单栏
Statusbar|Show status bar|显示状态栏
Sort|Sort by|排序方式
Name|Name|名称
Type|Type|类型
Size|Size|大小
Unsorted|Original order|原始顺序
Back|Back|后退
Forward|Forward|前进
Parent|Up one level|上一级
Search|Search files…|搜索文件…
Advanced|Advanced options|高级选项
Scheme|Scheme (none = unencrypted)|方案（none 表示未加密）
Folders|Folders|文件夹
Preview|Preview|预览
AllFiles|All files|全部文件
Welcome|Open or drop files and folders to browse resources.|打开或拖入文件及目录，浏览其中的资源。
Opening|Opening…|正在打开…
Working|Working…|正在处理…
Cancelled|Operation cancelled.|操作已取消。
Error|Operation failed|操作失败
Details|Details|详情
SelectPreview|Select a file to preview.|选择文件以查看预览。
SelectFiles|Select files first.|请先选择文件。
Output|Output directory|输出目录
Browse|Browse…|浏览…
Overwrite|Existing files|已有文件
Fail|Report a conflict|报告冲突
Skip|Skip existing|跳过已有文件
Replace|Replace existing|替换已有文件
Images|Images|图像
Audio|Audio|音频
Scripts|Scripts / text|脚本 / 文本
Other|Other resources|其他资源
ImageFormat|Convert images to (empty = original)|图片转换格式（空表示保留原始格式）
AudioConvert|Convert audio to common formats|音频转换为通用格式
MaxDepth|Maximum recursive depth|最大递归层数
Start|Start|开始
Completed|Completed: {0} files, {1} errors.|已完成：{0} 个文件，{1} 项错误。
General|General|常规
Formats|Format settings|格式设置
Language|Language|语言
Auto|Follow system / automatic|跟随系统 / 自动
English|English|英语
Chinese|简体中文|简体中文
Theme|Theme|主题
System|Follow system|跟随系统
Light|Light|浅色
Dark|Dark|深色
Encoding|Text / filename encoding|文本 / 文件名编码
Time|Time limit (seconds)|时间上限（秒）
Memory|Memory limit (MiB)|内存上限（MiB）
DefaultOutput|Default output directory|默认输出目录
Wrap|Wrap text|文本自动换行
Save|Save|保存
Close|Close|关闭
Defaults|Inherit global defaults|继承全局默认值
Play|Play|播放
Pause|Pause|暂停
Stop|Stop|停止
Volume|Volume|音量
Preparing|Preparing preview…|正在准备预览…
VideoFormatsInfo|Video preview uses FFmpeg. Common containers: MPEG, MP4/MOV, AVI, WMV/ASF, Matroska/WebM, Ogg video, FLV and IVF. Read-only; encoding support depends on the installed decoder. Regression samples cover MPEG-2 video with MP2 audio and silent MPEG-4 video. Windows/macOS packages include LGPL FFmpeg tools and corresponding source. Linux uses distribution FFmpeg.|视频预览使用 FFmpeg，支持 MPEG、MP4/MOV、AVI、WMV/ASF、Matroska/WebM、Ogg 视频、FLV、IVF 等常用容器。仅支持读取；具体编码取决于已安装的解码器。回归样本覆盖 MPEG-2 视频及 MP2 音频、无声 MPEG-4 视频。Windows/macOS 附 LGPL FFmpeg 工具及对应源码，Linux 使用系统 FFmpeg。
NextPage|Next page|下一页
PreviousPage|Previous page|上一页
Copy|Copy|复制
SelectAll|Select all|全选
Mask|Select by wildcard…|按通配符选择…
CopyNames|Copy filenames|复制文件名
AssignType|Assign resource type|指定资源类型
Automatic|Detect automatically|自动识别
Explore|Show in file manager|在文件管理器中显示
Trash|Move to recycle bin…|移入回收站…
TrashConfirm|Move the selected physical files to the recycle bin?|将所选磁盘文件移入回收站？
Confirm|Confirm|确认
Feedback|Report a problem|问题反馈
Manual|User guide|使用说明
Troubleshoot|Troubleshooting|故障排查
Updates|Check for updates|检查更新
License|Software licenses|软件协议
ExportLogs|Export logs…|日志导出…
About|About|关于
Archives|Archives|归档
Registered|Registered providers|已注册提供者
Verified|Verification status|验证状态
ReadOnly|Read only|只读
ReadWrite|Read / write|可读写
RegisteredOnly|Registered; game variants may be unverified.|已注册；具体游戏变体可能尚未验证。
AdjustOffset|Apply image offsets to the converted canvas|转换时将图像偏移应用到画布
FixtureVerified|Verified on named test fixtures|已在列出的测试样本上验证
CreationVerified|Synthetic archive creation and read-back verified|已验证合成归档的创建与回读
NotVerified|Registered; decoding has not been verified|已注册；解码尚未验证
Verification|Verification details and limits|验证详情与限制
Options|Format parameters (JSON)|格式参数（JSON）
Format|Format|格式
Filename|Output filename|输出文件名
MissingOptions|Password or game parameters required|需要密码或游戏参数
Subtitle|Browse, preview and extract game resources|浏览、预览与提取游戏资源
NoAudio|Audio output is unavailable.|音频输出设备不可用。
UpdateNone|This version is up to date.|当前已是最新版本。
UpdateAvailable|A newer version is available: {0}|发现新版本：{0}
DatabaseUpdate|Update format database|更新格式数据库
DatabaseUpdated|Format database updated. New tasks will use it.|格式数据库已更新，新任务将使用新版本。
SelectOutput|Choose output directory|选择输出目录
Exported|Exported: {0}|已导出：{0}
NoFormats|No additional settings for this format.|该格式没有额外设置。
Binary|Hexadecimal|十六进制
Text|Text / script|文本 / 脚本
Metadata|Resource information|资源信息
""";
}
