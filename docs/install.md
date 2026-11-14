# Install GARbro 1.0

Portable downloads, DEB and side-loaded MSIX require the external **.NET 10 Runtime**
(not the Windows Desktop Runtime or SDK), matching the package architecture. Linux
packages support Debian 13 and Ubuntu 24.04 on x64 and ARM64. Store submission
packages carry their runtime; they are not ordinary side-loading downloads.

## Debian / Ubuntu

Enable a .NET 10 package source for your distribution using Microsoft's official
instructions (Ubuntu's distribution archive may also supply it). Then install:

```sh
sudo apt install ./garbro-1.0.6-linux-x64.deb
```

The examples use x64; replace `linux-x64` with `linux-arm64` for ARM64.
APT installs the declared runtime and native dependencies. Launch GARbro from
the application menu, `garbro-desktop`, or Open With in a file manager. Native
Wayland is preferred; X11 is the fallback. The launcher does not open a terminal.
Game-resource MIME types are registered without changing existing defaults.
Choose GARbro in your file manager's Open With dialog to make it the default.
Uninstall with `sudo apt remove garbro`. User settings stay in your user profile.

Linux audio uses the distribution SDL2 library (`libsdl2-2.0-0`, version 2.0.22
or later). The tarball also uses this system dependency; it does not bundle SDL.
Video preview also requires the system `ffmpeg` package (installed automatically by DEB). Windows and macOS packages include their native SDL library and FFmpeg tools.

The Linux tarball can also be unpacked and run without installation. See
[linux-desktop.md](linux-desktop.md) for native libraries and input-method diagnostics.

## Windows MSIX

Install the x64 or ARM64 .NET 10 Runtime matching your chosen package first. Download the MSIX and **matching public
`.cer` certificate** from the same release and verify `SHA256SUMS`.
The MSIX uses a self-signed preview certificate. Review its subject
`CN=GARbro-no-wpf-Test` and fingerprint before trusting it. In an administrator
PowerShell, explicitly trust the downloaded public certificate:

```powershell
Import-Certificate -FilePath .\garbro-1.0.6-win-x64.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage .\garbro-1.0.6-win-x64.msix
```

For ARM64, replace `win-x64` with `win-arm64` in both commands.
Trust the new matching certificate again for every release, then install the new
MSIX over the old version. The fixed package identity preserves upgrade order.
The signing private key is discarded by CI and is never distributed. Remove the
app in Windows Settings; remove the test certificate from Trusted People when
you no longer need it. Windows 10 2004 or later is required by the manifest;
CI tests installation and activation on its Windows runner, not every Windows edition.

Open With offers GARbro for supported archives, including XFL, ZIP, DAT and BIN,
and game-resource extensions. Installation preserves the current default
application. Common media and executable types are not registered.

The ZIP is an alternative that needs no certificate trust. Unpack all files and
run `app\GARbro.Desktop.exe`; the CLI is `app\garbro.exe`.

## macOS

macOS 14 or later is supported on Intel (`osx-x64`) and Apple silicon (`osx-arm64`).
Install the matching .NET 10 Runtime, open the DMG and copy its whole GARbro folder
to `/Applications` or `~/Applications`. Open `GARbro.app`; the command-line tool
is `GARbro.app/Contents/Resources/app/garbro` and shares the application's libraries.
Tarballs are also available. Keep the accompanying files together.

The app has an ad-hoc signature for local integrity; it is not Apple Developer ID
signed or notarized. If macOS blocks the download, review it and use the specific
app's Open Anyway action in System Settings → Privacy & Security. Do not disable
Gatekeeper globally. The package registers game-specific Open With handlers without
replacing your defaults. Click Play for audio/video; files never autoplay.

## Settings and diagnostics

Settings use an atomic, versioned JSON file in the user's application-data
directory. Passwords and keys stay in the session. Help → Export logs creates a
local ZIP with redacted paths and environment information; it is never uploaded
automatically. The bundled manuals are available in English, Simplified Chinese,
Russian and Japanese. The compatibility ledger lists format-specific limits.

## Flatpak / Flathub

Release downloads include `.flatpak` bundles for x64 and ARM64. Install the Flathub
remote for their Freedesktop runtime, then install your downloaded bundle:

```sh
flatpak remote-add --user --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
flatpak install --user ./garbro-1.0.6-linux-x64.flatpak
flatpak run io.github.love_in_idleness.GARbro-lazer
```

These bundles include .NET and video tools. Flathub catalog publication is pending;
see [submission instructions](flathub.md) for the exact immutable manifest bundle.
The Microsoft Store submission package is also downloadable; Store publication
requires Partner Center access. These are separate from the ready-to-install bundles.

Flatpak 安装包支持 x64、ARM64，自带运行时及视频工具，可按上述命令直接安装。Flathub 和微软商店尚未上架，提交资料与权限要求见对应文档。Android 开发继续暂缓。
