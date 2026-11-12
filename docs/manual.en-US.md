# GARbro user guide

Install the .NET 10 Runtime before starting a portable or side-loaded GARbro package.
The Microsoft Store and Flatpak packages carry their runtime. Linux DEB installations also
need the native libraries listed in linux-desktop.md. Windows MSIX preview
packages use a test certificate; trust the certificate supplied with that release
before installing or upgrading. ZIP packages remain available.

The six outlined icon menus group File, Process, Selection, View, Settings and Help.
Hover over an icon for its name; every popup combines icons with text.

## Browsing tabs

GARbro uses one main window. Opening or dropping several files or folders creates a tab for each; opening a resource again selects its existing tab. Later launches and Open With send files to the existing window. Each tab keeps its location, navigation history, search and selection.

Use Ctrl/Cmd+T for a new tab, Ctrl/Cmd+W to close it, and Ctrl+Tab / Ctrl+Shift+Tab to switch tabs. These bindings can be changed in Settings → Shortcuts. Switching tabs stops media playback; closing the last tab returns to the welcome page. The location bar navigates within the current tab.

## Browse resources

Use File → Open or Open folder, or drop files and folders onto the window.
Double-click a folder or archive to enter it. Back, Forward and Up navigate
physical directories and nested archives. Loading only reads resources.
Use the search box, sorting menu, Select all or wildcard selection to choose files.
The context menu can assign a resource type when automatic identification needs
help. Show in file manager locates the outer physical file.

## Extract and convert

Extract all and Extract selected open an output dialog. Recursive extraction
also scans child directories and nested archives. Child archive copies normally remain
beside their filename.unpacked directories. Select “Do not keep extracted child archives”
to remove these copies after the whole job succeeds. Conversion can also omit original
media copies in the output. Failures, depth limits and skipped conversions retain their copies.
Input game files are never deleted by these options.
Choose conflict handling, resource types and optional conversion in the dialog.
Cancellation keeps completed files and removes unfinished temporary output.
Limits cover the whole recursive operation; increase them in Settings when needed.

Create archive uses the selected files/directories, or the current directory
when nothing is selected. Select a writable format and review its parameters.
Batch convert writes new image/audio files to the chosen destination.
Physical files can be moved to the operating system's recycle bin after confirmation.

## Preview

Images support original size and fit mode. Text and hexadecimal views have paging
and copy controls. Select GBK, Shift-JIS or another encoding in Settings if names
or text are garbled. Format-defined Unicode fields keep their defined encoding.
Audio selection shows metadata; click Play to prepare and play the audio.
Video selection shows a still preview. Click Play to watch MPEG, MP4, AVI, WMV,
Matroska/WebM or other supported containers. Pause, Stop, position and volume controls
are available for both audio and video. Switching files stops playback and clears its cache.
Linux uses system FFmpeg and SDL2; install `ffmpeg` if video tools are missing.
Windows and macOS packages include the video tools. No audio device means video plays muted.
Unsupported codecs report an error; registered formats do not imply every game
variant has been verified.

## Settings and help

The gear opens language, light/dark/system theme, encoding, output directory,
time/memory limits, viewer and format settings. Ordinary settings persist across
restarts. Passwords entered for decoding remain in the current session.
The question mark and Help menu provide the guide, diagnostics, licenses, updates,
log export and About. About lists registered formats and their declared read/write
capability; see compatibility.md for independently verified variants.
Update checks are manual. Application downloads open the project release page;
compatible format databases are checksum-checked before installation in your user
data directory. No reports or logs are uploaded automatically.

## Shortcuts

Ctrl+O: open. Ctrl+P: settings. Ctrl+Q: quit. F3: create archive.
F4: extract selected. F5: refresh. F6: convert. Enter: open selected.
Backspace / Ctrl+PageUp: parent. Alt+Left/Right: navigation history.
Ctrl+A: select all. Ctrl+C: copy selected filenames. Numpad +: select by wildcard.
Alt+Shift+T/M/S: toggle toolbar/menu/status bar.
Text fields and input methods retain their normal editing shortcuts.

## Flatpak

Use the system file picker to authorize files and output folders. Select the whole
source folder when a format needs sibling indexes or keys. The sandbox does not
provide a host recycle-bin command. See flathub.md for packaging validation status.

Choose **HEX (hexadecimal)** under **Preview encoding / HEX** to inspect any file, including images, audio and video. Each page contains up to 64 KiB with offsets, grouped bytes and ASCII. Pages can be copied. The choice affects only the current preview, resets on file selection and never changes filename decoding. Switching to HEX stops media playback. Loading and filtering run in the background; you can continue typing, cancel, or open another resource.

In Settings → Keyboard shortcuts, search for a command, record and add combinations, remove bindings, or restore one command or all defaults. Multiple combinations per command are supported. Resolve conflicts before saving. Installed DEB, MSIX, Flatpak and macOS apps appear in Open With; installation preserves existing defaults. Portable ZIP/TAR packages do not register. The interface and CLI support English, Simplified Chinese, Russian and Japanese. Use `garbro --language ru-RU help` or `garbro help --language ja-JP` to select a CLI language; JSON keys and command names remain unchanged.


### Extraction scope and output formats

Select the items first, then choose **Extract selected** (F4). **Extract all** processes the current directory, including items hidden by search. Enable recursive unpacking in the extraction dialog to enter subfolders and nested archives. For a selection, recursion stays inside those items and never adds their siblings.

Images, audio and video have separate output choices. Audio supports automatic common format, WAV, FLAC and M4A; video supports MP4 or MKV with the original streams. Keep original format copies without conversion. Ordinary files go directly into the output folder; archives expand into their `.unpacked` folders.

Discard options affect output copies only. Game source files are preserved. Failures list the affected items and retain recoverable copies. A “no matching files” result does not mean files were extracted. Batch media conversion uses the same format choices.

## WebP and command-line conversion

Choose **WebP (lossless)** under Image output format when extracting or converting. Transparency is retained. The conversion dialog reports converted, skipped and failed files separately; types set to Keep original format are skipped. Creating an archive, extracting and converting all keep the selection captured when the command starts.

`garbro convert image.png -o image.webp` chooses WebP from the output extension. WAV, FLAC, M4A, MP4 and MKV work in the same way. Use `garbro extract --help` for media conversion during extraction, including `--image-format webp`, `--audio-format wav`, `--video-format mp4`, `--discard-original-media` and `--discard-child-archives`. The last option requires `--nested`. Source files are preserved.

## Convert scripts

Select a supported script on disk or in an archive, open **Convert** and enable **Convert scripts**.

- CodeX RScript GSC → UTF-8 TSC uses LiarsoftTool. Ren’Py RPYC → RPY uses unrpyc.
- KiriKiri KS and TJS text → UTF-8. Compiled TJS2100 uses tjs2dec.
- BGI/Ethornell v1 scripts (`._bs` or extensionless) → BSD and `._bp` → BPD use Bgi_asdisEN. Auto uses CP932. GBK is also supported.
- NScripter `nscript.dat` → TXT. Auto checks for a BOM and UTF-8, then falls back to CP932. For GBK files select GBK.
- Artemis AST → UTF-8, ASB → structured JSON. Unity Utage `data.dsm` → TXT. RPG Maker XP, VX and VX Ace `Scripts.rxdata`, `Scripts.rvdata` and `Scripts.rvdata2` → Ruby source.

Download optional tools under **Help → Tool downloads**. Put LiarsoftTool 2.3.1+, unrpyc 2.x and tjs2dec on `PATH`, or set `GARBRO_LIARSOFTTOOL`, `GARBRO_UNRPYC` and `GARBRO_TJS2DEC` to their absolute paths. Set `GARBRO_BGI_ASDIS` to the absolute path of the unpacked Bgi_asdisEN repository. For ASB, use msg-tool v0.4.0-alpha.3 or newer as `msg_tool` on `PATH` or set `GARBRO_MSG_TOOL`. For RGSS, use rvpacker-txt-rs v19 or newer on `PATH` or set `GARBRO_RVPACKER`. Both variables take absolute paths to the executables. Bgi_asdisEN and `unrpyc.py` require Python 3.9+ on `PATH`. Flatpak helpers must be able to run in the sandbox. For Luca System `SCRIPT.PAK`, run LuckSystem separately with the opcode table for your game.

`garbro convert scene.gsc -o scene.tsc --encoding gbk` selects GBK explicitly. Auto lets LiarsoftTool detect the encoding. Use `garbro convert scripts.rpa --entry game/script.rpyc -o script.rpy` for an archive entry, using the entry name shown by `garbro list`.

`--recursive` traverses disk folders; `--nested` expands child archives. Ordinary recursive disk scans skip unrecognized files and keep relative directories. Global options may precede the command; `--` protects input filenames beginning with a dash. Existing single-archive extraction and JSON field names remain supported.
