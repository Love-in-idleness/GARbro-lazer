# GARbro-lazer — 跨平台迁移计划 / Cross-platform Migration Plan

**版本 / Version:** 1.0 (2026-10-30)  
**仓库 / Repository:** https://github.com/Love-in-idleness/GARbro-lazer
**上游 / Upstream:** https://github.com/crskycode/GARbro  
**基线 / Baseline:** `master` fork is currently identical to upstream `master` at the inspected revision; no independent no-WPF changes have yet been identified.

## 1. 目标与约束 / Goals and constraints

**P0：确保实际解包能力，而不是只追求 UI 跨平台。**
**P0: Preserve real extraction capability rather than merely porting the UI.**

- 保留现有 `GameRes`、`ArcFormats`、`Legacy`、`Experimental` 中有价值的解码算法、格式元数据及游戏专用方案。
- Preserve valuable decoders, format metadata, and game-specific schemes from `GameRes`, `ArcFormats`, `Legacy`, and `Experimental`.
- 以 .NET 10 LTS 作为主运行时，并使用 Avalonia 构建 Windows、Linux、macOS 桌面应用。
- Target .NET 10 LTS and use Avalonia for Windows, Linux, and macOS desktop applications.
- 建立独立 CLI，并使其先于 GUI 达到生产可用的解包能力。
- Establish an independent CLI and achieve reliable extraction before GUI parity.
- 解码核心不依赖 WPF、Avalonia、Windows Shell、Windows-only `System.Drawing.Common` 或音频播放设备。
- Keep the decoding core independent of WPF, Avalonia, Windows Shell, Windows-only `System.Drawing.Common`, and playback devices.
- 初期保留 MEF 插件系统；避免同时改写全部解码器和插件发现机制。
- Retain MEF initially rather than simultaneously rewriting decoders and plugin discovery.
- 不在初期删除旧 GUI 或旧格式插件；不因某个格式未通过编译就默默删掉它。
- Do not remove the legacy GUI or format plugins initially; never silently drop a decoder merely because it does not compile.

**非目标 / Non-goals:** 不是从零重写格式算法；不是重新实现整个 WPF；不是在首版就追求 Native AOT、代码裁剪或单文件发布。
**Non-goals:** Reimplementing every codec, recreating all of WPF, or requiring Native AOT, trimming, or single-file deployment in the first release.

## 2. 当前仓库风险 / Confirmed repository risks

1. `GameRes/ArcView.cs`: `MappedViewExtension` 的静态初始化调用 `kernel32!GetSystemInfo`，Linux/macOS 上直接失效；读取器依赖 `MemoryMappedViewAccessor` 与 unsafe 指针。
   `GameRes/ArcView.cs`: a static initializer calls `kernel32!GetSystemInfo`, preventing direct Linux/macOS execution; the reader relies on mapped views and unsafe pointers.
2. `GameRes/Image.cs`: `ImageData` 直接持有 WPF `BitmapSource`，公开 `PixelFormat`、`BitmapPalette` 等 WPF 类型。
   `GameRes/Image.cs`: `ImageData` directly owns WPF `BitmapSource` and exposes WPF pixel-format and palette types.
3. `GameRes/ImagePNG.cs` / `ImageBMP.cs`: 使用 `PngBitmapDecoder`、`BmpBitmapDecoder` 与 WPF encoder；PNG `oFFs` 偏移块须保留。
   `GameRes/ImagePNG.cs` / `ImageBMP.cs`: depend on WPF codecs; PNG `oFFs` offset metadata must be preserved.
4. `GameRes/FormatCatalog.cs`: 使用 MEF `AssemblyCatalog`/`DirectoryCatalog` 和 `Arc*.dll` 文件发现，且依赖基于程序集位置的资源路径。
   `GameRes/FormatCatalog.cs`: uses MEF and `Arc*.dll` discovery, with resource paths derived from assembly location.
5. `ArcFormats` 中含有与格式关联的 WPF Widget/XAML 文件，不能将整个目录不加筛选地编入 Linux 工程。
   `ArcFormats` contains decoder-specific WPF widgets and XAML; the entire folder cannot simply be included in a Linux build.
6. `GameRes/FileSystem.cs` 的 `CreatePath` 使用 Windows 假设的大小写不敏感路径前缀检查，需重新设计输出路径安全验证。
   `GameRes/FileSystem.cs` uses a case-insensitive prefix check that requires redesign for safe cross-platform extraction paths.
7. 原有项目是非 SDK-style `.csproj`、`packages.config` 和 Windows-only CI，缺少专门的测试项目。
   The existing projects use legacy project files and Windows-only CI, with no dedicated test projects.
8. 保留资源转换、加密游戏方案、旧代码页、第三方 native 依赖与 `Legacy` 解码器是兼容性的必要组成部分。
   Conversion, encrypted-game schemes, legacy encodings, native dependencies, and `Legacy` decoders all matter to compatibility.

## 3. 目标架构 / Target architecture

```text
GARbro-lazer/
  GameRes/                    # Existing core sources, progressively ported
  ArcFormats/                 # Existing decoders
  Legacy/                     # Legacy decoders: retain, migrate separately
  Experimental/               # Experimental decoders: track explicitly
  Console/                    # Legacy CLI baseline
  GUI/                        # WPF behaviour reference, not new cross-platform UI
  modern/
    GARbro.Core/              # Archive abstractions and reader interfaces
    GARbro.Imaging/           # Pixel buffers, palette, codecs
    GARbro.Application/       # Extraction services, schemes, progress, errors
    GARbro.Cli/               # New CLI
    GARbro.Desktop/           # Avalonia UI
  tests/
    GARbro.Core.Tests/
    GARbro.Formats.Tests/
    GARbro.Integration.Tests/
    fixtures/                 # Only redistributable fixtures and manifests
  docs/
    architecture.md
    migration-plan.md
    compatibility.md
    testing.md
  GARbro.sln                  # Historical/Windows baseline until retirement
  GARbro-lazer.slnx          # Modern portable solution
  Directory.Build.props
  Directory.Packages.props
  global.json
```

在过渡期可使用独立 SDK-style 工程以 `<Compile Include=...>` 链接未改动的原始源码，但每个源码只能被一个实际程序集编译，并必须明确排除 XAML、生成文件及平台专属代码。
During transition, SDK-style projects may link untouched original sources, but avoid duplicate type ownership and explicitly exclude XAML, generated files, and platform-specific code.

依赖方向：Desktop/CLI → Application → Core/Imaging → Format providers；`GameRes` 不应反向引用 GUI。
Dependency direction: Desktop/CLI → Application → Core/Imaging → format providers; `GameRes` must not depend on the GUI.

## 4. 里程碑 / Milestones

### M0 — 基线冻结与格式清单 / Freeze baseline and inventory

**工作 / Tasks**
- 给当前 `master` 打迁移基线标签；建立 `migration/net10` 工作分支，保留原 Windows CI。
- Tag the current baseline, work on `migration/net10`, and preserve the original Windows CI.
- 在原版 Windows 运行 `Console -l` 记录归档格式；另通过 `FormatCatalog` 导出图片、音频、脚本格式与 MEF 元数据，保存机器可比对快照。
- Run legacy `Console -l` on Windows for archive formats; separately enumerate image, audio, script formats, and MEF metadata through `FormatCatalog`, saving machine-comparable snapshots.
- 建立按引擎和游戏版本划分的格式矩阵，包括密码、附加游戏数据和解密方案。
- Create an engine- and game-version-based format matrix, including passwords, additional game data, and decryption schemes.
- 准备合法可使用的最小样本：正常、空文件、截断、加密、多个条目、嵌套目录以及图像样本。
- Prepare legally usable fixtures covering normal, empty, truncated, encrypted, multi-entry, nested-directory, and image cases.

**完成标准 / Gate:** 原版 Windows CI 可运行；格式清单及测试样本索引进入版本控制；所有已知不支持项可追踪。
**Gate:** Legacy Windows CI works, format inventory and fixture manifest are versioned, and known unsupported cases are trackable.

### M1 — 跨平台基础 IO / Cross-platform archive IO

**工作 / Tasks**
- 保持 `ArcView` 的公开 API 稳定，替换 `kernel32!GetSystemInfo` 与错误的映射偏移假设。
- Preserve the public `ArcView` API while replacing Win32 system calls and platform-specific mapping assumptions.
- 优先以 `System.IO.MemoryMappedFiles` 的跨平台用法实现；必要时加 `RandomAccess`/`FileStream` 回退路径。
- Prefer cross-platform `System.IO.MemoryMappedFiles`, with `RandomAccess`/`FileStream` fallback when needed.
- 检查 `AcquirePointer`/`ReleasePointer` 的配对、页面边界、空文件、截断文件、超大偏移及溢出。
- Verify pointer acquisition/release, page boundaries, empty/truncated files, large offsets, and overflow.
- 注册 `CodePagesEncodingProvider` 以保持 CP932、CP936 等旧编码可用。
- Register `CodePagesEncodingProvider` so CP932, CP936, and similar legacy encodings remain available.
- 迁移 `ApplicationSettingsBase`、配置文件读取、MEF `System.ComponentModel.Composition` 包，确保 Linux 下不会调用 Windows 注册表。
- Adapt settings and MEF to modern .NET without invoking Windows Registry on Linux.

**完成标准 / Gate:** Linux 上不启动 GUI 即可打开测试档案、列出条目并完整复制原始字节；边界测试通过。
**Gate:** Linux can open fixture archives, list entries, and copy raw bytes without a GUI, passing boundary tests.

### M2 — 独立图像模型 / WPF-free imaging model

**工作 / Tasks**
- 定义 `ImageData` 背后的平台无关缓冲区：宽、高、行跨度、格式、像素、调色板、偏移 X/Y、可选 DPI。
- Define a platform-neutral image buffer with width, height, stride, format, pixels, palette, X/Y offsets, and optional DPI.
- 至少覆盖现有使用的 Bgra32、Bgr24、Bgr565、Bgr555、Gray8、Indexed1/4/8 等类型，并根据源码清单补全。
- Cover existing BGRA32, BGR24, BGR565, BGR555, Gray8, Indexed1/4/8, and other source-observed formats.
- 在内部命名空间实现小型兼容接口，避免伪造整个 `System.Windows.*` 公共命名空间。
- Implement small internal compatibility adapters without recreating the public `System.Windows.*` namespace.
- 重写 PNG/BMP/JPEG/TIFF 基础读写；编码后应比较解码像素，不要求不同编码器产生完全相同的压缩字节。
- Replace basic PNG/BMP/JPEG/TIFF I/O; compare decoded pixels rather than demanding identical compressed bytes from different encoders.
- 检查 Alpha、调色板顺序、stride 对齐、上下翻转、16-bit packed pixels、4-bit packed pixels、PNG `oFFs`。
- Verify alpha, palette order, stride alignment, vertical flipping, 16/4-bit packing, and PNG `oFFs`.

**完成标准 / Gate:** 图像模型完全不引用 WPF；样本的宽高、偏移、像素数据与原版一致。
**Gate:** The imaging model has no WPF references, and fixture dimensions, offsets, and pixels match the original.

### M3 — 解码器迁移与格式清单 / Decoder migration and format inventory

**工作 / Tasks**
- 将原 `ArcFormats` 中的 WPF Widget/对话框从解码逻辑中剥离；将参数请求改为 UI 无关的选项提供者。
- Separate WPF widgets/dialogs from `ArcFormats` decoders; replace user interaction with a UI-independent options provider.
- 保留 MEF `[Export]`、优先级与查找规则；解决 `Assembly.Location` 在特殊部署模式下不可用的问题。
- Retain MEF exports, priorities, and lookup behaviour; fix assumptions about `Assembly.Location`.
- 先迁移 XP3、RPA、NSA、PFS、LWG、XFL、LIM、YPF、QLIE、NPA 等代表性格式，再按目录分批迁移其余格式。
- Start with representative XP3, RPA, NSA, PFS, LWG, XFL, LIM, YPF, QLIE, NPA formats, then migrate remaining directories in batches.
- 将插件状态分为：已编译、已识别、已列目录、已成功提取、已验证转换，不合并为单个“支持”。
- Record separate statuses: builds, recognises, lists, extracts, and converts; do not collapse them into one “supported” flag.
- 全量覆盖 `Legacy` 需单独里程碑；不可为了使主项目通过编译而删除旧格式。
- Give `Legacy` a separate completion gate; do not drop old formats simply to make the build pass.
- Windows-only native 格式后端应被隔离，列出平台限制或实现替代，而不是造成整个程序启动失败。
- Isolate Windows-only native backends and disclose unsupported platforms rather than letting them crash the application.

**完成标准 / Gate:** 所有基线格式条目有可审计状态；每个已宣称跨平台支持的解码器都有测试样本，未验证功能不得标为通过。
**Gate:** Every baseline format has an auditable status; every cross-platform supported decoder has a fixture; unverified features are never marked as passing.

### M4 — 生产可用 CLI / Production-capable CLI

**工作 / Tasks**
- 提供 `formats`、`probe`、`list`、`extract`、`convert` 命令；支持自动检测及显式选择格式/游戏方案。
- Implement `formats`, `probe`, `list`, `extract`, and `convert`, with autodetection and explicit format/game-scheme selection.
- 参数包括输出目录、覆盖策略、递归、游戏密钥或配置、批处理、机器可读 JSON 结果与稳定退出码。
- Support output directories, overwrite policies, recursion, game keys/settings, batches, machine-readable JSON, and stable exit codes.
- 归档文件必须能先提取原始数据；图像音频转换是独立选项，失败不应阻止原始提取。
- Raw extraction must work independently; optional image/audio conversion failures should not suppress raw output.
- 用异步任务、取消支持和进度回调包装核心服务，但不在底层解码器中引入 UI 依赖。
- Expose cancellable operations and progress reporting without introducing UI dependencies into decoders.

**完成标准 / Gate:** Linux CLI 在代表性测试集上可识别、列出并解包，输出文件与原版基线一致。
**Gate:** Linux CLI recognises, lists, and extracts representative fixtures with outputs matching the legacy baseline.

### M5 — 全量兼容与异常处理 / Coverage parity and error handling

**工作 / Tasks**
- 逐模块迁移余下 `ArcFormats`、`Legacy` 和必要的 `Experimental`；处理第三方 native 库的 Linux/macOS 分发。
- Port remaining formats and relevant experimental codecs, including native dependency deployment.
- 分类错误：未知格式、坏归档、缺密钥、缺 DLL、仅 Windows 可用、转换失败、路径冲突等。
- Classify unknown format, corrupt archive, missing key, missing native library, Windows-only implementation, conversion failure, and path collision.
- 将提取路径限制在用户选择的输出根目录内，拒绝穿越路径、绝对路径和危险的符号链接目标。
- Contain extraction inside the selected root and reject traversal, absolute paths, and unsafe symlink targets.
- 加入可配置的单条目大小、总输出量、递归深度、解压率异常、超时/取消与内存限制。
- Add configurable per-entry, total-output, nesting, decompression-ratio, cancellation, and memory safeguards.

**完成标准 / Gate:** 与原版的格式清单差异均被解释；目标平台可用功能以真实样本证明；安全测试通过。
**Gate:** Every format-catalog difference is explained, platform claims are backed by fixtures, and security tests pass.

### M6 — Avalonia 图形界面 / Avalonia GUI

**工作 / Tasks**
- 按 MVP 顺序实现：选择归档、目录树、文件列表、提取、错误提示；然后加入图片、文本、音频预览和高级参数。
- Build the MVP in order: open archive, directory tree, file list, extraction, errors; then add image/text/audio preview and advanced parameters.
- GUI 调用 `GARbro.Application`，不得直接解析归档、读取游戏密钥或实现解压算法。
- The GUI calls `GARbro.Application`; it must not implement archive parsing, key handling, or decompression algorithms.
- Linux 默认优先使用原生 Wayland，并支持 text-input-v3 输入法协议；文件选择优先适配 XDG Desktop Portal，保留 X11 回退。
- Prefer native Wayland with text-input-v3 on Linux, use XDG Desktop Portal for file selection, and retain X11 fallback.
- 长时间任务需有进度、取消和错误报告，避免 UI 线程阻塞；保留常用快捷键与多语言资源。
- Provide progress, cancellation, and error reporting without blocking the UI; retain key shortcuts and localisation.

**完成标准 / Gate:** GUI 能完成与 CLI 相同的基础打开、列表、提取流程，并通过 Linux/Windows/macOS 烟雾测试。
**Gate:** The GUI performs the same basic open/list/extract operations as CLI, passing cross-platform smoke tests.

### M7 — CI、发行与维护 / CI, releases, and maintenance

**工作 / Tasks**
- 保留 Windows 历史版构建，新增 `ubuntu-latest`、`windows-latest`、`macos-latest` 上的 .NET 10 build/test。
- Preserve the Windows legacy build and add .NET 10 build/test across Ubuntu, Windows, and macOS.
- 在 CI 中检查 `GameRes`/核心无 `System.Windows.*`、`PresentationCore`、`PresentationFramework`、`WindowsBase` 和 UI 库引用。
- Fail CI when the core references WPF assemblies or UI libraries.
- `dotnet test` 包括单元、集成、黄金数据、损坏文件和高优先级格式回归；跨平台工具需一致的样本输出。
- Run unit, integration, golden-data, malformed-input, and priority-format regression tests with consistent cross-platform output.
- 首版发布普通框架依赖型构建，再发布 self-contained；初期禁用 trimming、AOT、单文件压缩以避免 MEF 反射与程序集路径问题。
- First ship framework-dependent builds, then self-contained packages; defer trimming, AOT, and single-file bundling because of MEF and path assumptions.
- 发布 Linux tar.gz/AppImage 或 Flatpak（后者可作为后续工作），Windows zip/installer，macOS app bundle；明确哪些平台格式尚不可用。
- Release Linux packages, Windows archives/installers, and macOS application bundles, documenting any platform-specific format limitations.
- 记录开源许可证与第三方包许可证，不把用户本地游戏文件或密钥提交进仓库。
- Preserve license notices and audit third-party licenses; never commit local game assets or keys.

**完成标准 / Gate:** CI 三平台通过；具有可复现的发布制品、兼容性报告和明确已知限制；移植目标符合版本发布定义。
**Gate:** Three-platform CI passes, release artifacts are reproducible, and compatibility reports and known limitations are published.

## 5. 测试与兼容性指标 / Test and compatibility metrics

**建立格式清单 / Maintain a machine-readable format ledger:**

```text
format_tag,engine,variant,platform,build,recognition,list,raw_extract,conversion,fixture,notes
XP3,KiriKiri,standard,linux,pass,pass,pass,pass,pass,fixture-001,...
PFS,Artemis,pf8,linux,pass,pass,pass,pass,not-tested,fixture-002,...
```

示例行仅表示建议的数据格式，不代表目前已验证成功。
The example rows demonstrate a proposed format, not verified test results.

- 原始提取：比较每个条目的路径、大小和 SHA-256；包括目录结构及重名冲突处理。
- Raw extraction: compare paths, lengths, and SHA-256 digests, including directory layout and collisions.
- 图像转换：比较归一化 RGBA 像素、宽高、偏移、调色板语义和透明度；容忍 PNG/JPEG 编码器字节差异。
- Image conversion: compare normalised RGBA pixels, dimensions, offsets, palette semantics, and alpha; allow encoder-byte differences.
- 音频：比较 PCM 样本、通道、采样率、位深、长度和循环元数据（如适用）。
- Audio: compare PCM samples, channels, sample rate, bit depth, length, and loop metadata where applicable.
- 加密：分别测试无密钥、正确密钥、错误密钥和游戏特定方案。
- Encryption: test no key, correct key, wrong key, and game-specific schemes independently.
- 稳定性：测试截断文件、非法长度、错误偏移、压缩炸弹、循环嵌套、非法字符及路径穿越。
- Stability: test truncated files, invalid lengths, bad offsets, decompression bombs, recursive nesting, invalid names, and traversal.
- 覆盖率：区分“已注册格式”“有测试的格式”“成功识别”“成功解包”四个分母/分子；不要报告单一未经定义的支持率。
- Coverage: distinguish registered, fixture-backed, recognised, and extractable formats; do not publish a single undefined support percentage.

## 6. PR 拆分建议 / Suggested pull-request sequence

| PR | 目标 / Scope | 必要验证 / Required validation |
|---|---|---|
| 00 | 基线标签、格式清单、CI / Baseline and inventory | 原版 Windows 构建 / Legacy Windows build |
| 01 | SDK 架构、编码和配置 / SDK scaffolding, encodings, settings | 新项目 restore/build / New project builds |
| 02 | ArcView 跨平台化 / Portable ArcView | Linux 归档 IO golden tests |
| 03 | 图像数据模型 / WPF-free image model | 像素测试 / Pixel tests |
| 04 | PNG/BMP/JPEG/TIFF / Basic image codecs | 跨平台 round trip & offsets |
| 05 | MEF 和无 UI 参数接口 / Plugins and headless options | 插件清单对比 / Catalog diff |
| 06 | 核心归档格式 / Priority archive formats | 原始数据 SHA-256 / Raw hashes |
| 07 | CLI、解包安全 / CLI and extraction safety | 端到端解包 / End-to-end extraction |
| 08 | Legacy 与余下格式 / Remaining formats | 格式矩阵与回归 / Format ledger |
| 09 | Avalonia MVP / GUI MVP | 三平台烟雾测试 / OS smoke tests |
| 10 | 预览、国际化、发行 / Preview, localisation, releases | 全矩阵和打包 / Full CI and packaging |

推荐使用依赖清晰的小 PR；不要让 AI 一次性迁移数百个解码器并靠删文件消灭错误。
Prefer small dependency-scoped PRs; do not ask an AI tool to migrate hundreds of decoders at once and eliminate compile errors by removing files.

## 7. 初始可执行任务 / Immediate first iteration

1. 创建 `migration/net10` 分支，冻结原版构建及格式清单。
   Create `migration/net10`, freeze the legacy build and catalog inventory.
2. 新建 `GARbro-lazer.slnx` 与最小 SDK-style 平台 IO 测试项目，不直接移除旧解决方案；不要在完全消除图像依赖前要求整个 `GameRes` 编译通过。
   Create `GARbro-lazer.slnx` and a minimal SDK-style platform-I/O test project without deleting the old solution; do not require the entire `GameRes` assembly to build before imaging dependencies are removed.
3. 独立修复 `GameRes/ArcView.cs`，让 Linux 上的简单归档读取单测通过。
   Make `GameRes/ArcView.cs` portable and pass Linux archive-read tests.
4. 在图像重构前，做 CP932、MEF 插件枚举、选项提供者的基本集成测试。
   Add CP932, MEF registration, and options-provider integration tests before changing imaging internals.
5. 对每轮工作报告：提交/PR、改动文件、Windows/Linux/macOS 构建结果、通过的格式、未通过的格式、下一轮最适合的任务。
   After every iteration, report commits/PRs, changed files, platform build results, passing/failing formats, and the next task.

## 8. 完成定义 / Definition of done

**“可在 Linux 编译”不是项目完成标准。**
**“Compiles on Linux” is not the definition of project completion.**

发布 1.0 需满足：核心不依赖 WPF；跨平台 CLI 与 GUI 可用；有可追溯的格式兼容矩阵；主要解包场景在真实样本下通过；不能工作的旧格式和平台限制明确记录；测试及 CI 完成；许可证和文档齐全。
Release 1.0 requires a WPF-free core, working CLI and GUI, a traceable compatibility matrix, fixture-backed extraction success, explicit outstanding incompatibilities, cross-platform CI, and complete documentation/licensing.

跨平台覆盖应以**有样本的归档提取行为**衡量，而非仅以实现文件数量或插件标签数量衡量。
Cross-platform coverage must be measured by **fixture-backed extraction behaviour**, not merely decoder-file counts or plugin tags.

## 9. 参考 / References

- Fork: https://github.com/Love-in-idleness/GARbro-lazer
- Upstream: https://github.com/crskycode/GARbro
- Framework support policy: https://dotnet.microsoft.com/en-us/platform/support/policy
- Avalonia platforms: https://docs.avaloniaui.net/docs/supported-platforms
- MEF package: https://www.nuget.org/packages/System.ComponentModel.Composition
- Cross-platform System.Drawing policy: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/system-drawing
