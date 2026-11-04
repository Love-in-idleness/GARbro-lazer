# Flathub preparation / Flathub 发布准备

Application ID: `io.github.love_in_idleness.GARbro-no-wpf`. The GitHub namespace
matches the maintainer and repository, without claiming the original GARbro domain.
The supported targets are x86_64 and aarch64 on Freedesktop 26.08 with .NET 10.
The Flatpak carries its runtime within the sandbox. DEB and side-loaded Windows
packages continue to use external .NET runtimes.

Run the **Flatpak preparation** workflow on a reviewed source commit. It resolves
NuGet dependencies using the installed Flatpak SDK, records SHA-512 hashes and
architecture restrictions, pins the Git source commit, then builds without network
access in the build sandbox. Downloads happen before compilation. The output
includes an installable `.flatpak` and the two files needed for a Flathub manifest PR.
For local use, install `flatpak`, `org.flatpak.Builder`, the Freedesktop SDK and its
`org.freedesktop.Sdk.Extension.dotnet10//26.08` extension, then run:

```sh
python3 tools/prepare-flathub.py --in-sdk --revision FULL_REVIEWED_COMMIT
python3 tools/check-flatpak.py
appstreamcli validate --no-net packaging/flatpak/*.metainfo.xml
flatpak run --filesystem="$PWD" org.flatpak.Builder --user --install-deps-from=flathub --compose-url-policy=full --mirror-screenshots-url=https://dl.flathub.org/media/ --repo=artifacts/flatpak-repo build-flatpak artifacts/flathub/io.github.love_in_idleness.GARbro-no-wpf.json
```

The manifest grants Wayland, fallback X11, GPU rendering, PulseAudio and network
access for user-triggered update checks. It grants no host filesystem or broad
D-Bus access. Open inputs and output folders using the system file picker. Select
the containing folder for sibling indexes and keys. The recycle-bin command is
disabled in this sandbox because a reliable host trash operation is unavailable.
SDL2 must come from the Freedesktop runtime; NuGet SDL binaries are removed.
The package excludes WOFF/WOFF2 web fonts from its own font enumeration: host
cache entries without a family can hang the bundled Skia font manager at startup.
Desktop TTF/OTF fonts remain available; host fonts and settings are not changed.

Before submitting to Flathub's `new-pr` branch, complete both architecture builds,
run the current Flathub manifest and repository linters, validate metadata, and add
publicly hosted screenshots of this build. Test portal selection of files and
folders, drag/drop and file activation, extraction to an authorized destination,
audio, worker cancellation/limits, Wayland text input, and all four languages in light/dark themes.
Test with no host .NET installation and after a Flatpak upgrade. The preparation
workflow does not submit or publish to Flathub. Existing installed application
defaults remain unchanged. Android, F-Droid and IzzyOnDroid work is deferred.

## 中文

应用 ID 使用维护者与仓库对应的 GitHub 命名空间，支持 x86_64、aarch64。
Flatpak 使用 Freedesktop 26.08 和 .NET 10，在沙箱内携带运行时；DEB 和 Windows
侧载包继续依赖外部运行时。运行上述命令或手动触发 **Flatpak preparation** 工作流，
可生成固定源码提交、附 SHA-512 校验的离线 NuGet 清单和安装测试包。
依赖解析应使用实际 Flatpak SDK，构建阶段不开放网络。

权限包括 Wayland、备用 X11、图形加速、音频及用户主动检查更新需要的网络。
默认不开放整个主目录、宿主文件系统或完整 D-Bus。通过系统文件选择器授权来源和输出目录；
需要同目录索引或密钥时，选择整个文件夹。沙箱无法可靠调用宿主回收站，因此禁用该命令。
SDL2 使用 Freedesktop 运行时提供的库，不使用 NuGet 附带的 SDL。
包内字体配置暂时过滤 WOFF/WOFF2 网页字体，避免宿主缓存中缺少字体族的条目使
Skia 在启动时死循环。普通 TTF/OTF 桌面字体仍可使用，不修改宿主字体或设置。

提交前仍须通过双架构构建、Flathub 官方检查器和元数据验证，并提供当前版本的公开截图。
需实测文件门户、目录与拖放、文件关联、提取输出、音频、取消和资源限制、Wayland 输入法、
中英俄日四语言与深浅主题，以及无宿主 .NET 的启动和升级。工作流仅准备资产，不自动提交或发布。
Android 开发及 F-Droid、IzzyOnDroid 的发布准备已暂缓。

## Submit the release bundle

The release's `flathub-submission.zip` contains the app manifest with a full source
commit, `nuget-sources.json` with both architectures' download hashes, and
`flathub.json`. Extract them together at the root of a branch based on Flathub's
`new-pr` branch. Do not replace the source commit with a moving branch.

1. Fork https://github.com/flathub/flathub with the `new-pr` branch included.
2. Create a branch from `new-pr`, add these three JSON files, commit and push.
3. Open a pull request targeting `flathub/flathub:new-pr`, titled
   `Add io.github.love_in_idleness.GARbro-no-wpf`.
4. Explain that this is the maintained cross-platform GARbro fork, built from source;
   the independent FFmpeg helpers use LGPL source, and .NET uses the SDK extension.
   Attach the matching release/CI links and describe the permissions above.
5. Address Flathub review and any required namespace/maintainer verification.
   An open pull request is a submission, not publication; catalog availability
   begins only after Flathub accepts and builds the repository.

The current GitHub integration returned HTTP 403 (`Resource not accessible by
integration`) when attempting to create the required fork. It cannot submit the
PR with its current permissions. The generated bundle is ready for an account
with fork/push access; no repository password belongs in the manifest.

正式版的 `flathub-submission.zip` 内含固定源码提交、双架构 NuGet 校验清单和
Flathub 架构配置。将三个 JSON 文件放在从 `flathub/flathub:new-pr` 创建的分支根目录，
提交 PR 后按平台审核意见处理。当前 GitHub 集成创建 fork 时返回 403，需有权限的
账户执行这一步。提交、审核通过和商店可下载是三个不同状态。
