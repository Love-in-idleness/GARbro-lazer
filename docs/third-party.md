# Third-party software

The portable projects preserve GARbro's MIT license and original source notices.
The following packages are redistributed through NuGet; their own license files
remain authoritative and must accompany binary distribution.

| Component | License | Purpose |
| --- | --- | --- |
| Magick.NET | Apache-2.0 | Portable PNG, BMP, JPEG, TIFF, WebP codecs |
| ImageMagick (bundled by Magick.NET) | ImageMagick License | Native codec implementation |
| Newtonsoft.Json | MIT | Resource options and scheme serialization |
| NAudio.Core / NAudio.Wasapi | MIT | RIFF/AIFF reading and Windows-only WMA decoding |
| NLayer | MIT | Managed MP3 decoding |
| Concentus | BSD-3-Clause | Managed Opus decoding |
| Concentus.Oggfile | MS-PL | Ogg packet reading |
| SQLite3 Multiple Ciphers / its .NET bundle | MIT | Historical RC4 SQLite index compatibility without SEE |
| SQLitePCLRaw | Apache-2.0 | Native SQLite bindings |
| Microsoft.Data.Sqlite.Core | MIT | Read-only IGS index queries |
| BCnEncoder.Net | MIT | GPU texture decoding |
| Bouncy Castle.Cryptography | MIT | Rijndael with 256-bit blocks for Side-B archives |
| CommunityToolkit.HighPerformance | MIT | Pixel and texture buffer operations |
| NVorbis | MIT | Managed Vorbis decoding |
| SharpZipLib | MIT | ZIP and BZip2 |
| K4os.Compression.LZ4 | MIT | LZ4 |
| Snappier | MIT | Snappy |
| ZstdSharp.Port | MIT | Zstandard |
| Avalonia and Inter fonts | MIT / SIL Open Font License respectively | Desktop UI and Latin font |
| Avalonia.Wayland / NWayland | MIT | Native Wayland windows and text-input-v3 |
| Silk.NET.SDL 2.23.0 | MIT | Desktop audio-device bindings |
| Ultz.Native.SDL 2.32.10 / SDL2 | zlib | Windows/macOS native audio; Linux uses system SDL2. Source notice in licenses/SDL2.txt |
| Material Symbols Outlined | Apache-2.0 | Unified interface icons; source notice in licenses/Material-Symbols.txt |
| MaterialColorUtilities | MIT | HCT Material color roles |
| FSharp.Core | MIT | Pure color and diagnostic transformations |
| SkiaSharp / Hjg.Pngcs / LibTiff.NET | MIT / Apache-2.0 / BSD-3-Clause | Portable raster backend retained for deferred Android work |
| Microsoft .NET packages | MIT | MEF, configuration and bounded NRBF record parsing |
| GPPG / GPLEX runtime sources | BSD-3-Clause | Existing Artemis IPT parser and scanner |
| xUnit and test tooling | See respective package licenses | Tests only |

Magick.NET includes native platform assets. Framework-dependent distributions
must retain their runtime directories. No package relies on System.Drawing.Common.
The core does not open playback devices; some legacy compressed WAV conversions
still require an alternative portable decoder and report that limitation.

GARbro carries a narrowly scoped Avalonia.Wayland `12.1.4-garbro.1` compatibility
build for compositor-owned standard cursors. The other Avalonia packages remain
official 12.1.4. The pinned upstream commit, complete patch, reproducible build
recipe, MIT notice and checksums are documented in
[the backend source notes](../tools/patches/avalonia-wayland/README.md).
The local NuGet package includes the upstream license and patch; release and
Flatpak builds retain that license in their dependency notices.

The GPPG and GPLEX notices in `docs/licenses` are copied from the original
maintainer's repositories:
[GPPG](https://github.com/k-john-gough/gppg/blob/master/LICENSE.md)
(blob `61bd8adb2d7088b7d8bd69e0ba35cc9bd9ad7bb4`) and
[GPLEX](https://github.com/k-john-gough/gplex/blob/master/LICENSE.md)
(blob `09bff8aa94fb915ceb4168651bf639edabb464de`). The release packager includes
these source notices alongside dependency license files.
The Concentus.Oggfile source notice is from its package's repository revision
`dee65d9de0ef009658c2d60be6fd9b29d362a482` and is included separately because the
NuGet package declares MS-PL without embedding the license text.
SQLite3 Multiple Ciphers and its NuGet bundle notices are also retained in
`docs/licenses`. The IGS cipher configuration follows the project's documented
[System.Data.SQLite compatibility mode](https://utelle.github.io/SQLite3MultipleCiphers/docs/ciphers/cipher_sds_rc4/).
SQLitePCLRaw's Apache-2.0 license is retained there as well (upstream license
blob `d645695673349e3947e8e5ae42332d0ac3164cd7`).

Media tools on Windows/macOS are FFmpeg 8.0.3 (LGPL-2.1-or-later only, no GPL/nonfree components). They run as separate processes. Corresponding source, licenses and the exact build recipe are included once in the shared `app/media` directory. Linux uses distribution FFmpeg; Flatpak builds the same pinned LGPL source inside the sandbox. See [FFmpeg notice](licenses/FFmpeg.md).
