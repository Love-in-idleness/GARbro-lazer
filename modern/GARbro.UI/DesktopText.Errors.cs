using GARbro.Domain;

namespace GARbro.Desktop;

public sealed partial class DesktopText
{
    // Match stable application messages at the presentation boundary. Original
    // decoder diagnostics remain intact in exported logs.
    public string ErrorMessage(string message) => MessageTranslation.Translate(message, ErrorMessages, Language);
    const string ErrorMessages = """
Invalid format setting: |格式设置无效，请在“设置 → 格式设置”中检查：
Cannot start the decoder service.|无法启动解码服务。
Decoder request exceeds its size limit.|解码请求超过大小限制。
Invalid decoder reply.|解码器返回了无效数据。
Duplicate decoder result.|解码器重复返回结果。
Unknown decoder reply.|解码器返回了未知消息。
The decoder process stopped. Completed outputs were retained; retry the task.|解码进程已停止。已完成的输出得到保留，请重试任务。
Decoder memory limit exceeded.|解码器内存用量超过限制。
Decoder time limit exceeded.|解码器运行时间超过限制。
Decoder service disconnected.|解码服务已断开连接。
Decoder service stopped.|解码服务已停止。
Decoder service refused the connection.|解码服务拒绝了连接。
Decoder request is missing.|缺少解码请求。
Reopen this resource to grant access again.|请重新打开资源以授予访问权限。
The document provider returned an invalid filename.|文档提供者返回了无效文件名。
Resource access expired. Select its folder again.|资源访问权限已失效，请重新选择文件夹。
Cannot read the selected directory. Grant access again.|无法读取所选目录，请重新授予访问权限。
The directory contains conflicting filenames: |目录中存在冲突的文件名：
Invalid resource path.|资源路径无效。
The resource changed. Refresh the folder.|资源已更改，请刷新文件夹。
Source directory nesting exceeds 64 levels.|来源目录的嵌套深度超过 64 层。
Not enough temporary storage. Choose local access or free some space.|临时存储空间不足，请选择本地访问或释放存储空间。
Cannot read the selected document.|无法读取所选文档。
Cannot replace a source document. Choose another destination.|无法覆盖来源文档，请选择其他输出位置。
Output path conflicts with a file.|输出路径与已有文件冲突。
Cannot create output folder.|无法创建输出文件夹。
Output exists: |输出文件已存在：
This storage cannot safely replace the existing file. Choose Skip or another destination.|此存储位置不支持安全替换已有文件，请选择跳过或其他输出位置。
Cannot create the output document.|无法创建输出文档。
Cannot preserve the previous output.|无法保留先前的输出文件。
Cannot commit output.|无法保存输出文件。
Cannot write output.|无法写入输出文件。
Android playback requires mono or stereo PCM16 audio.|Android 播放需要单声道或双声道的 16 位 PCM 音频。
No compatible audio output is available.|没有可用的兼容音频输出设备。
Audio output is in use.|音频输出设备正被占用。
Invalid settings collection.|设置数据无效。
Choose a color as #RRGGBB.|请使用 #RRGGBB 格式输入颜色。
Unknown theme.|未知主题。
Unknown language.|未知语言。
Choose an absolute output directory.|请选择绝对路径作为输出目录。
Settings are too large.|设置文件过大。
Unsupported settings version.|不支持此设置文件的版本。
Archive changed; reopen it before continuing.|归档已更改，请重新打开后继续。
Format database exceeds the size limit.|格式数据库超过大小限制。
Format database contains incompatible schemes.|格式数据库包含不兼容的方案。
Unknown resource operation.|未知资源操作。
Unknown worker operation.|未知解码器操作。
FFmpeg video tools are unavailable.|找不到视频解码工具 FFmpeg。Linux 请安装 ffmpeg 软件包；Windows 和 macOS 请重新安装完整程序包。
Cannot start the video decoder.|无法启动视频解码器。
Video probing exceeded its time limit.|读取视频信息超时。
The video decoder could not read this media.|视频解码器无法读取此文件，文件可能损坏或编码尚不受支持。
Video decoder memory limit exceeded.|视频解码器内存用量超过限制。
Incomplete video frame.|视频帧数据不完整。
Invalid video dimensions.|视频尺寸无效。
Video exceeds the pixel limit.|视频尺寸超过预览像素上限。
No video stream was found.|文件中没有可预览的视频流。
Video duration exceeds the preview limit.|视频时长超过预览限制。
Invalid preview artifact.|预览缓存文件无效，请重新选择文件。
""";
}
