# WPF、1.0.1 与 1.0.2 用时比较 / GUI timing comparison

## 简体中文

这些数据来自同一台 Windows x64 CI 虚拟机，操作真实的三个 GUI，计时到列表或图片更新，并经过相同的布局与显示同步。每个场景分别启动 5 个新进程，每个进程记录首次操作和 7 次重复操作；下表为中位数，单位为毫秒。

1.0.2 大幅缩短了重复浏览、目录枚举及缓存预览的等待。首次归档／图片访问仍有格式初始化成本，窗口启动也没有快过旧版。以下保留这些差距，不能把热访问数据当作冷启动数据。

## Measurements

| 操作 / Operation (ms, median) | WPF | 1.0.1 | 1.0.2 |
| --- | ---: | ---: | ---: |
| 小型 XFL 归档 / Small XFL archive (首次 / first) | 78.8 | 1067.0 | 739.0 |
| 小型 XFL 归档 / Small XFL archive (重复 / warm) | 78.0 | 958.1 | 62.7 |
| 一万文件目录 / 10,000-file directory (首次 / first) | 166.7 | 1148.3 | 173.4 |
| 一万文件目录 / 10,000-file directory (重复 / warm) | 111.4 | 1010.6 | 78.6 |
| 小 PNG 预览 / Small PNG preview (首次 / first) | 79.8 | 991.7 | 677.8 |
| 小 PNG 预览 / Small PNG preview (重复 / warm) | 63.2 | 913.9 | 48.3 |
| 窗口就绪 / Window ready | 781.3 | 1027.1 | 1157.1 |

## UI 调度延迟 / UI-dispatch delay

下表为每个场景 5 个进程各自 P95 的中位数，单位毫秒；记录 10 ms UI 定时器的额外等待，并非一次文件操作的总用时。 / Median of the five per-process P95 values; extra delay of a 10 ms UI timer, not operation duration.

| 场景 / Scenario | WPF | 1.0.1 | 1.0.2 |
| --- | ---: | ---: | ---: |
| 归档 / Archive | 53.2 | 16.2 | 27.2 |
| 目录 / Directory | 115.6 | 16.4 | 39.8 |
| 图片 / Image | 37.8 | 11.2 | 29.1 |

## 方法与边界

- 系统 `Windows-2025Server-10.0.26100-SP0`; CPU 报告为 `AMD EPYC 9V45 96-Core Processor`。这是共享云端虚拟机，不能代表所有用户设备。
- WPF 使用原版应用（其程序集版本为 1.5.45.1）；1.0.1 来自已发布标签，1.0.2 使用最终发行提交。驱动没有替换应用的加载、解码或列表逻辑。
- 每次计时包含实际加载／预览、结果验证、布局、17 ms 等待和 `DwmFlush`。图片包含各版本原有的选择防抖策略。操作前先进入图片所在目录；窗口启动另计。
- “首次”使用新进程，保留系统文件缓存，并非清空磁盘缓存后的首次读取。WPF 在启动时初始化格式系统；新版保留正常的延迟初始化／后台预热行为。窗口就绪从测试驱动入口计时，不是操作系统创建进程的完整启动时间。
- 重复操作在内容相同的 A、B 路径间切换，避免把 WPF 忽略同路径重新打开计作一次加载。图片重复访问允许使用应用自身缓存。
- 测试文件是小型合成 XFL、72 字节 PNG，以及含一万个空文件的目录，不代表大型游戏、网络磁盘或复杂图像的用时。
- 原始 45 组进程记录、每次用时、输入 SHA-256、UI 调度延迟和源提交见 [JSON](benchmarks/v1.0.2/windows-x64.json)。构建与测量可由 [GUI comparison workflow](../.github/workflows/gui-comparison.yml) 复现。
- 本次执行记录 / Recorded run: [GitHub Actions](https://github.com/Love-in-idleness/GARbro-lazer/actions/runs/41590453953)。

## English

The three real GUIs ran on the same Windows x64 CI VM, with five fresh processes per scenario and seven warm samples in each process. Values above are medians in milliseconds. Loading is timed through validated list/image state and a shared layout, 17 ms and DWM presentation barrier. Image selection includes each application's normal debounce. Window-ready time starts at the benchmark driver's entry, separately from loading.

1.0.2 substantially improves repeated archive access, directory enumeration and cached image preview. First archive/image access still pays format initialization, and window startup remains slower than WPF. OS file caches are retained; these are process-cold measurements, not cold-disk measurements. WPF initializes formats during startup while the modern GUIs retain deferred initialization/background prewarming. Alternating identical A/B paths prevents WPF's same-path no-op from being counted as loading.

The inputs are a tiny synthetic XFL, a 72-byte PNG and 10,000 empty files. The results do not predict large games, network disks or complex images. The linked raw JSON records all samples, source revisions, fixture hashes and UI-dispatch delays. The original WPF GUI reports assembly version 1.5.45.1.

## Source revisions

- 1.0.1: `4fbd482832a210ad3cbccab4b2376d2ee5cccdc6`
- 1.0.2 and original WPF source: `4f12f736f57b02f90693a99c3b1b93867c525d14`
