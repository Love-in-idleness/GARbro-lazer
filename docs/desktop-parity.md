# Desktop command migration ledger

Baseline: `GUI/MainWindow.xaml`, `GUI/MainWindow.xaml.cs`, `GUI/GarExtract.cs`,
`GUI/GarCreate.cs`, `GUI/GarConvert.cs`, Preferences and About windows. Only
commands with an implementation count; the historical Rename stub is excluded.
The shared `DeskCommand` supplies menu, toolbar, context-menu and keyboard actions.

| Legacy command / feature | New entry / shortcut | Enable condition | Verification |
|---|---|---|---|
| File: Open | File → Open; toolbar; Ctrl+O; file/folder drop; startup arguments | Idle | DesktopTests, NativeSmoke, DEB activation |
| Open physical folders / multiple roots | File → Open folder; folder tree | Idle | ResourceBrowserTests; directories remain lazy |
| Create archive | File / context → Create; F3 | Physical directory, idle | All 22 declared writable archive providers create and re-read a synthetic archive in ResourceBrowserTests |
| Convert multimedia | File / context → Convert; F6 | Listing open, idle; choose files | Existing image/audio conversion tests; per-file errors retained |
| Recent files / exit | File → Recent / Exit; Ctrl+Q | Recent entries exist / always | SettingsTests; native startup/close |
| Enter item, parent, next selection | Double click / Enter; Backspace / Ctrl+PageUp; Space | Listing open; text editing takes priority | Browser tests and native window smoke |
| Back / forward | Toolbar; Alt+Left / Alt+Right | History exists, idle | ResourceLocation keeps physical root, archive chain and virtual directory |
| Refresh | View / context / toolbar; F5 | Idle | DesktopTests |
| Select all / mask / copy names | Context; Ctrl+A / numpad + / Ctrl+C | Idle; selection where needed | Shared commands; text-box editing is handled first |
| Assign resource type | Context → Resource type | Listing open, idle | Preview dispatch accepts explicit type; assignment is session-local |
| Explorer / delete | Context; Ctrl+E / Delete | Physical resource for deletion, confirmation; idle | GIO/Recycle Bin/Finder integration; archive entries cannot be deleted |
| Extract all / selected | File / context / toolbar; F4 selected | Listing open, idle; selection for selected operation | Existing safety tests, GUI extraction smoke |
| Directory batch / nested recursion | File → Recursive extraction; extraction dialog | Idle, output selected | ResourceBrowserTests covers unknown-extension child, quota sharing, parameter retry and retained archive copies |
| Types / conversion / overwrite / image offset | Extraction dialog | Valid output, at least one type | Bounded atomic conversion; fail/skip/replace policy; source archive protected |
| Name / type / size / original sorting | View → Sort; repeat choice reverses direction | Listing open | Directory-first display; original preserves source indices |
| Image original / fit / fit window | View → Fit image / Fit window; Ctrl+H | Image available for window sizing | Pixel and native-window tests |
| Menu / toolbar / status visibility | View; Alt+Shift+M/T/S; Alt reveals menu | Idle | Saved layout preferences |
| Preferences / per-format settings | Gear; View → Settings; Ctrl+P | Idle | SettingsTests: persistence, corruption preservation, languages; format encoding overrides; secret settings remain in memory |
| About | Help / ? → About | Idle | Full archive/image/audio/script catalog, extensions, read/write, fixture evidence and limits; MIT and dependency notices |
| Troubleshooting / update check | Help / ? pages | Idle; update only on explicit click | Version ordering tests, checksum verification, bounded worker database validation and fallback |
| New guide / feedback / license / diagnostics | Help / ? pages | Idle | Offline bilingual manuals, Issues chooser, redacted diagnostic ZIP; no automatic upload |
| Audio preview | Preview: Play / Pause / Stop / seek / volume | Audio selected; Play starts decoding | WAV, MP3, Vorbis, Opus and AIFF decoder fixtures; SDL dummy-device playback state test |
| Video preview and media wrapper handling | Preview: manual Play / Pause / Stop / seek / volume | Video selected | MediaWorkflowTests, VideoTests; isolated FFmpeg processes, bounded frames/cache, no repeated DAT/BOGUS navigation |
| Optional archive/media output cleanup | Extraction/conversion checkboxes | Conversion/recursion enabled | Multi-level success, depth/output failure, skipped conversion, source preservation tests |
| Text / scripts / hex preview | Preview; paging / copy / encoding / wrap | Ordinary resource selected | NScripter XOR decoding test, hex paging, Unicode BOM across pages and selectable code page |

Format verification is limited to named synthetic fixtures and the pinned public
SDK corpus. A registered reader, writer or scheme is not proof of compatibility
with every game. See `compatibility/portable-status.json` and `testing.md`.
Physical Trash, native file-manager drag/drop and real audio devices still need
desktop acceptance on each supported desktop; CI uses disposable displays and
SDL's dummy device. Native Wayland text-input-v3 has a separate regression probe.

Deliberately deferred: task thread count. Android remains paused. No placeholder commands
are counted as migrated. Installation checks and remaining platform limitations
are documented separately in install.md and release notes.
