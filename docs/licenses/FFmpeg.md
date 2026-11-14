# FFmpeg media helpers

Copyright the FFmpeg developers. FFmpeg 8.0.3, commit `8ae0b34901ba60a802f183ee75a250a9fc3e09a5`.
https://ffmpeg.org/ — https://github.com/FFmpeg/FFmpeg

Our Windows/macOS helpers are built without GPL, nonfree, network or external codec libraries and are licensed under LGPL 2.1 or later. GARbro invokes the independent command-line executables; it does not link to FFmpeg libraries. You may replace the helper executables with compatible builds.

On Windows, `ffmpeg.exe` and `ffprobe.exe` share the versioned FFmpeg DLLs in the same directory. Both GARbro's GUI and CLI use this single helper directory. macOS retains the existing standalone helper layout.

Each redistributed helper directory includes `COPYING.LGPLv2.1`, `LICENSE.md`, the complete corresponding upstream source archive (`ffmpeg-source.tar.xz`), `media-codecs.json` with configure options and source checksum, and `build-media.py`. The archive is recompressed losslessly from the pinned upstream download; `source-archive.json` records the original, distributed and uncompressed TAR checksums. The source archive includes all code needed to rebuild/relink the helper programs. To rebuild from the project checkout, run `python tools/build-media.py --rid win-x64` (or win-arm64, osx-x64, osx-arm64). GNU make and the platform C toolchain are required. On Windows install Visual Studio C++ build tools and MSYS2 packages `make` and `pkgconf`, and set `MSYS2_LOCATION` to that installation. No GPL/nonfree add-ons are used. Normal distribution build tools and system libraries are required.

Linux DEB/portable editions use the system FFmpeg package under that distribution's license terms. Flatpak builds the pinned source from the manifest; its source is available through the Flatpak source mechanisms.

The native conversion recipe enables MPEG-4/AAC MP4, Matroska stream copy, WAV, FLAC and M4A output using FFmpeg’s built-in LGPL codecs and muxers. No external codec library is added.

The AV1 parser also enables the coded-bitstream metadata required by this FFmpeg version's MOV/MP4 muxer. This keeps its bitstream-type table nonempty in the minimal profile, including MSVC builds; it does not add an AV1 encoder or an external dependency.
