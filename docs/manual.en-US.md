# GARbro user guide

Install the .NET 10 Runtime before starting a portable or side-loaded GARbro package.
The Microsoft Store and Flatpak packages carry their runtime. Linux DEB installations also
need the native libraries listed in linux-desktop.md. Windows MSIX preview
packages use a test certificate; trust the certificate supplied with that release
before installing or upgrading. ZIP packages remain available.

The six outlined icon menus group File, Process, Selection, View, Settings and Help.
Hover over an icon for its name; every popup combines icons with text.

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
Convert multimedia writes new image/audio files to the chosen destination.
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
