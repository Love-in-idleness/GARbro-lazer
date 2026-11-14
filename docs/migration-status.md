# Migration status

This is an executable migration preview. It is not the completed 1.0 compatibility release.

| Milestone | Implemented and verified | Remaining gate |
| --- | --- | --- |
| M0 | Frozen inventories; 1,247 Release exports plus four DEBUG-only declarations; generated fixtures and a pinned public SDK corpus | Extend engine-specific actual-game cases |
| M1 | Portable mapped I/O, boundaries, partial reads, code pages and MEF; three-platform tests | Continue regression coverage as providers migrate |
| M2 | Independent pixel buffers and basic raster codecs; packed/indexed/alpha vectors and WPF comparisons | Broader actual-image corpus and metadata parity; WPF vectors cover all observed source pixel kinds |
| M3 | All 1,247 Release exports registered, including all 300 Legacy and 13 Experimental exports; 24 archive tags verified on 37 synthetic variants | CAB Quantum/LZX/sets and remaining provider variants; actual-game compatibility corpus |
| M4 | CLI commands, JSON, options, batches, explicit overwrites, progress/cancellation in services | Validate game-specific decryption, conversion variants and production corpus |
| M5 | Path/link/collision and output limits; supervised workers with timeout, resident-memory checks, cancellation and temporary-output cleanup | Broader actual-game corpus and recursive archive variants |
| M6 | Shared desktop commands, directories/nested VFS, recursive extraction, 22 writers, conversion, settings, help and manual SDL audio; native Wayland/text-input-v3 | Real-device and file-manager acceptance; see desktop-parity.md |
| M7 | Three-platform archives, DEB/MSIX build and install checks, self-signed Windows test certificates; source/asset manifests and checksums | Complete preview CI and remaining actual-game gates before a stable release |

The catalog ledger gives every original provider an explicit status; an export
that is unported or untested is never counted as extraction support. Generated
fixtures prove only their documented variants. See [compatibility](compatibility.md)
and [testing](testing.md) for reproducible evidence.
