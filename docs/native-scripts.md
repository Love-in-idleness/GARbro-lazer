# Native script codecs and .NET 11

The 1.1.1 development tree uses SDK 11.0.100 / runtime 11.0.0. No Python, Rust, C++ runtime or separately installed compiler is required for TSC→GSC, BSD→BGI, BPD→BGI-BP, structured ASB JSON→ASB, TXT→NSCRIPT or TXT→DSM. Export for these formats is also built in. Ren'Py/RGSS/compiled-TJS exporters retain their existing optional desktop tools.

`GARbro.Scripting` contains pure managed F# parsing and format transformations. File ownership, bounded staging, cancellation, worker supervision and atomic output commit remain in the application. Sources are UTF-8 or BOM-marked Unicode; target encodings use strict fallback. Auto is CP932 for GSC/BGI/NScripter, UTF-8 for ASB; DSM's encoding and cryptographic parameters are fixed by its format. Sources and previous output survive a failed conversion. Fixed output names are checked for collisions before GUI batch conversion.

## Compatibility and provenance

- GSC is independently implemented from the binary field layouts and [TSC format reference](https://github.com/Love-in-idleness/LiarsoftTool/blob/master/docs/GSC_OPCODE_REFERENCE.en.md). No GPL implementation is copied into this MIT project. Supports modern-36 / legacy-28, the six published instruction schemas, labels, VM operands, strings, data blocks and named-entry trailer relocation. Unknown layouts export a checksummed raw envelope; that fallback is preservation, not editable dialogue. Recognized source metadata is retained; stale encoding comments do not override the explicit target encoding.
- BGI/BPD ported from [Bgi_asdisEN](https://github.com/Checkumeito/Bgi_asdisEN/tree/44574e627093899efa9fb4b9b82d78e03fdfe447) (MIT, Daniel Wu, 2025). The embedded opcode table includes its standard aliases and mapping names; [license](licenses/Bgi_asdisEN.txt). Supports inline strings, raw string bytes, imports/exports/padding, labels and the published BPD variable-operand families. External `#string_table` is deliberately rejected rather than reading arbitrary paths; merge it into a self-contained BSD first. Engine-specific unknown instruction lengths are rejected.
- ASB uses the existing full JSON array of labels and command objects (`name`, `line_number`, `attributes`), matching the public Artemis ASB wire layout described by [msg-tool](https://github.com/lifegpc/msg-tool/blob/master/src/scripts/artemis/asb.rs). This is an independent BCL implementation, without Lua formatting or execution. It does not accept dialogue-only exports or compile Lua source to VM bytecode.
- NScripter uses the existing XOR codec; UTAGE DSM uses AES-256-CBC / PKCS7 with the existing PBKDF2-SHA1 parameters, preserving the original format.

Synthetic [compatibility fixtures](../tests/fixtures/scripts-native/README.md) were compiled with independent tools. Those tools are development oracles only and are absent from application/package dependencies. The compatibility range is the implemented wire layouts; a successful conversion does not prove that a game engine accepts a new encoding.

## Builds

Android remains a separate project, so desktop builds do not install mobile workloads. Android 12+ ARM64/x64 APKs use the same UI and a bound decoder service. The Android workflow runs one x64 emulator smoke pass, then builds architecture-specific packages. APK publication requires persistent signing credentials; development artifacts use temporary keys.

Store builds pin runtime/ILLink 11.0.0. The Wayland compatibility package remains pinned to its original SDK 10.0.403 and net10.0 assembly identity, consumed unchanged by .NET 11. Flatpak uses a checksummed, build-only official SDK 11.0.100 archive because no dotnet11 SDK extension is published yet; the SDK is removed during cleanup. Offline NuGet generation resolves with the same SDK in the Freedesktop sandbox.

Validation results are recorded after the focused CI runs. Local application compilation passed under .NET 11; local test dependency copying stopped when disk space ran out. No macOS tests are scheduled.
