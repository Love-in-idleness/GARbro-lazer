# GARbro user guide

Install the .NET 11 Runtime before starting a portable or side-loaded GARbro package.
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

Ctrl+O: open. Ctrl+P: settings. Ctrl+Q: quit. F3: pack selected.
F4: extract selected. F5: refresh. F6: convert. Enter: open selected.
Backspace / Ctrl+PageUp: parent. Alt+Left/Right: navigation history.
Ctrl+A: select all. Ctrl+C: copy selected filenames. Numpad +: select by wildcard.
Alt+Shift+T toggles the toolbar. Alt+Shift+S toggles the status bar.
Text fields and input methods retain their normal editing shortcuts.

## Flatpak

Use the system file picker to authorize files and output folders. Select the whole
source folder when a format needs sibling indexes or keys. The sandbox does not
provide a host recycle-bin command. See flathub.md for packaging validation status.

Choose **HEX (hexadecimal)** under **Preview encoding / HEX** to inspect any file, including images, audio and video. Each page contains up to 64 KiB with offsets, grouped bytes and ASCII. Pages can be copied. The choice affects only the current preview, resets on file selection and never changes filename decoding. Switching to HEX stops media playback. Loading and filtering run in the background; you can continue typing, cancel, or open another resource.

In Settings → Keyboard shortcuts, search for a command, record and add combinations, remove bindings, or restore one command or all defaults. Multiple combinations per command are supported. Resolve conflicts before saving. Installed DEB, MSIX, Flatpak and macOS apps appear in Open With; installation preserves existing defaults. Portable ZIP/TAR packages do not register. The interface and CLI support English, Simplified Chinese, Russian and Japanese. Use `garbro --language ru-RU help` or `garbro help --language ja-JP` to select a CLI language; JSON keys and command names remain unchanged.


### Extraction scope and output formats

Select the items first, then choose **Extract selected** (F4). **Extract all** processes the current directory, including items hidden by search. Enable recursive unpacking in the extraction dialog to enter subfolders and nested archives. For a selection, recursion stays inside those items and never adds their siblings.

Images, audio, video and scripts have separate output choices. To export a .gsc as editable .tsc text while extracting, select **Export editable text** under **Script output format**. Audio supports automatic common format, WAV, FLAC, M4A, Ogg Vorbis, Opus and AIFF on desktop; video supports MP4 or MKV with the original streams. Keep original format copies without conversion. Ordinary files go directly into the output folder; archives expand into their `.unpacked` folders.

Discard options affect output copies only. Game source files are preserved. Failures list the affected items and retain recoverable copies. A “no matching files” result does not mean files were extracted. Batch media conversion uses the same format choices.

## WebP and command-line conversion

Choose **WebP (lossless)** under Image output format when extracting or converting. Transparency is retained. The conversion dialog reports converted, skipped and failed files separately; types set to Keep original format are skipped. Creating an archive, extracting and converting all keep the selection captured when the command starts.

`garbro convert image.png -o image.webp` chooses WebP from the output extension. WAV, FLAC, M4A, MP4 and MKV work in the same way. Use `garbro extract --help` for media conversion during extraction, including `--image-format webp`, `--audio-format wav`, `--video-format mp4`, `--discard-original-media` and `--discard-child-archives`. The last option requires `--nested`. Source files are preserved.

## Convert scripts

Select a supported script on disk or in an archive, open **Convert** and choose **Export editable text** under **Script output format**.

- CodeX RScript GSC → UTF-8 TSC and Ren’Py RPYC/RPYMC → RPY/RPYM are built in.
- KiriKiri KS and TJS text → UTF-8; compiled TJS2100 → readable TJS with the bundled Furikiri Core.
- BGI/Ethornell v1 scripts (`._bs` or extensionless) → BSD and `._bp` → BPD use the built-in decoder. Auto uses CP932. GBK is also supported.
- NScripter `nscript.dat` → TXT. Auto checks for a BOM and UTF-8, then falls back to CP932. For GBK files select GBK.
- Artemis AST → UTF-8, ASB → structured JSON. Unity Utage `data.dsm` → TXT. RPG Maker XP/VX/VX Ace `Scripts.rxdata`, `Scripts.rvdata` and `Scripts.rvdata2` → editable RGSS JSON, or Ruby source. Luca System `SCRIPT.PAK` → editable Luca JSON using the game scheme.

These script operations work offline without extra tools. Choose **Script output format → Export** for the default output, or **Ruby source** for RGSS source only. Select **RGSS** or **LUCA** to compile the exported JSON; keep its entries and metadata, and edit the text values. Luca asks for the game's scheme, such as LOOPERS. Ren’Py and compiled TJS currently support export only. Unsupported variants report an error and preserve existing output.
```sh
garbro convert Scripts.rvdata2 -o Scripts.rvdata2.rgss.json
garbro convert Scripts.rvdata2.rgss.json -o Scripts.rvdata2 --to RGSS
garbro convert Scripts.rvdata2 -o scripts.rb --to RB
garbro convert SCRIPT.PAK -o SCRIPT.PAK.luca.json --scheme LOOPERS
garbro convert SCRIPT.PAK.luca.json -o SCRIPT.PAK --to LUCA
```


`garbro convert scene.gsc -o scene.tsc --encoding gbk` selects GBK explicitly. Auto uses CP932 for GSC/BGI. Use `garbro convert scripts.rpa --entry game/script.rpyc -o script.rpy` for an archive entry, using the entry name shown by `garbro list`.

`--recursive` traverses disk folders; `--nested` expands child archives. Ordinary recursive disk scans skip unrecognized files and keep relative directories. Global options may precede the command; `--` protects input filenames beginning with a dash. Existing single-archive extraction and JSON field names remain supported.

## Packing archives

Use **Pack selected** (F3) or **Pack all** from the toolbar, resource operations menu or context menu. **Pack all** includes items hidden by the search filter. Both commands include subfolders. Choose a writable archive format, an output file and, if needed, expand **Archive format options**.

Image, audio, video and script conversion are optional. **Script output format** defaults to **Do not convert scripts**. Choose a recompilation target to pack an edited script in its game format. **Omit originals from the archive after conversion** includes only the converted version of each affected file. Other files are packed as usual. Source files are preserved, and audio AUTO passthrough is packed once. A conversion failure or name conflict cancels the archive and preserves the previous output.

```sh
garbro create "assets" -o "data.xfl"
garbro create "assets" -o "data.zip" --image-format webp --audio-format wav --discard-original-media
garbro create "assets" -o "data.xp3" --format XP3 --scheme none
```

`create --help` lists writer parameters, encoding, overwrite and resource limits. A single input folder contributes its contents; multiple inputs retain paths relative to their common parent. Only existing writable archive formats are offered. Converted media may require matching game-engine support; script references are not updated. When using the CLI, run `garbro convert` to recompile scripts before packing.

## Script recompilation (1.1.2)

Select the edited files and choose **Convert → Script output format → Recompile**. Choose **Do not convert scripts** to skip scripts. Choose GSC for TSC, BGI for BSD, BGI-BP for BPD, or ASB for its structured JSON export. Plain text can become NSCRIPT (`nscript.dat`) or DSM (`data.dsm`). Each target uses its original intermediate format; dialogue-only ASB JSON is not accepted.

These six compilers are built into GARbro and work on desktop and Android without Python or another compiler. Source text must use UTF-8 or a Unicode BOM. The text encoding option selects the compiled strings: Auto uses CP932 for GSC/BGI/NScripter and UTF-8 for ASB; DSM always uses UTF-8. Characters that cannot be encoded produce an error. BGI sources referencing an external `#string_table` must first be merged into a self-contained BSD. Unknown GSC layouts export as checked raw envelopes; their payload is preserved, not translated.

```sh
garbro convert edited.tsc -o scene.gsc --encoding shift-jis
garbro convert edited.bsd -o scene --to BGI
garbro convert edited.bpd -o scene._bp --to BGI-BP
garbro convert edited.json -o scene.asb
garbro convert edited.txt -o nscript.dat --to NSCRIPT --encoding gbk
garbro convert edited.txt -o data.dsm --to DSM
```

Use a separate output folder. Source files stay unchanged; failed compilation preserves an existing output. When several text files would all become `nscript.dat` or `data.dsm`, convert them separately. Recompilation does not change the game engine or guarantee support for a new character set.

## Android development build

Android 12 or later is required. Choose the ARM64 APK for most phones and tablets, or x64 for an x86 emulator. Install through Android's package installer; use the system file/folder picker to grant access. GARbro uses the same tabbed interface and background decoder service. Video/audio preview uses Android's system player. Built-in script processing is available on Android; FFmpeg-based media conversion is unavailable. CI APKs are development builds, not an F-Droid/IzzyOnDroid release; temporary-key builds cannot update an installation signed with a different key.

Desktop version 1.1.2 requires .NET 11. Store and Flatpak include their runtime; other desktop packages require the matching .NET 11 Runtime to be installed.
