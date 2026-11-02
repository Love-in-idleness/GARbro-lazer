# Compatibility

The migration baseline is commit `698ccc57` (local tag `migration-baseline-2026-10-30`).
`compatibility/baseline-source.json` records 1,251 actual export declarations,
including four DEBUG-only providers. The observed Release catalog contains 1,247
exports and is frozen in `compatibility/baseline-runtime.json`. Every difference
is accounted for; commented-out declarations are excluded. Assemblies are part
of provider identity because the original catalog reuses one full class name.
`compatibility/portable-status.json` applies an explicit unported/not-tested
default to all baseline providers and overrides only compiled/tested variants.

The portable runtime currently discovers all 1,247 Release exports. Generated fixtures verify
raw extraction for XFL, LWG, SAR, NSA, PFS (pf6/pf8), XP3 (raw/zlib), RPA-3, NPA,
YPF 0x100, PACK/QLIE v2, ZIP (stored/deflate), Majiro v1/v2/v3, DL1 (raw/LZSS),
PAK/ALPHA, PAK/ASURA (raw/LZSS), HZC/MULTI (two zlib-compressed frames),
DAT/GX4LIB, BYTES/UNITY, RGSSAD v1/v3, DAT/IRIS and CAB (stored/MSZIP).
LIM BGR565 pixels, WAV/AIFF PCM retention, managed MP3/Vorbis/Opus conversion, and basic PNG/BMP/TIFF round trips
are tested. These tests cover only the listed variants; they do not establish
compatibility with all games or all encryption schemes.

The four DEBUG-only declarations are outside the Release migration target.
Other variants of a compiled format remain unverified. `Legacy` now builds and registers all 300 Release exports. Most of its providers
remain untested; the named synthetic archive fixtures and MBP/CBF BGR555 images cover
only their listed variants. `Experimental` builds all 13 Release exports.
IGS uses the open-source SQLite3 Multiple Ciphers backend with historical RC4
compatibility, replacing the SEE-dependent System.Data.SQLite backend in modern
projects. Native assets include macOS arm64.
Portable scheme import/export is implemented and synthetic polymorphic schemes
round-trip. The source-version-148 database imports for all 77 consumed scheme groups,
including more than 500 XP3 definitions; that data is bundled in a deterministic
compressed JSON file. Import success does not verify all game-specific decryption.
Bundled file-name lists are retained. User-supplied auxiliary game files, remaining
native backends and actual-game baseline comparisons retain the documented compatibility limits.
The first stable desktop release does not claim that every registered game variant is verified.

XP3 requests encryption options even for archives with clear encryption flags,
matching the old decoder's behavior. Use `--scheme none` explicitly for unencrypted
XP3 fixtures. A missing key is reported; it is never silently guessed as no encryption.

The CI matrix builds and tests Linux x64/ARM64, Windows x64/ARM64 and macOS Intel/Apple silicon, including
archive, image, audio, safety and headless GUI checks. Preview archives are unpacked
and their published CLI, codecs and bundled scheme data are exercised separately. All 31 shared positive archive fixtures also
are checked against the historical Windows decoders in CI, alongside MBP/CBF, AGF and BBM pixels and metadata. Headless GUI tests do not replace native
file-picker and audio-playback testing on actual desktops. Native window tests
also exercise open/list/extract on each platform; Linux has a separate headless
Wayland compositor and real text-input-v3 protocol test.

The registered catalog count measures compilation and discovery, not successful
extraction. WMA decoding requires Windows Media Foundation. DAT/SIDEB uses
256-bit-block Rijndael through the managed Bouncy Castle backend with CBC and
zero padding; a shared synthetic archive verifies UTF-8 names and zlib extraction.
PE32 and PE32+ resource enumeration, numeric/Unicode identifiers, empty data and
fixed file/product versions use bounded managed mappings. Their fixtures have
portable tests and a Windows API comparison step; multilingual resource selection
and other game-specific executable variants remain unverified.

Portable scheme schema 2 retains inherited private fields and avoids computed
password setters. Bundled group identities and data digests are checked after
packaging; see `modern/GameData/README.md` for export provenance.

Owned mutable canvases support tile placement, cropping and premultiplied source-over
composition. PMS, DPNG, CWP and HZC fixtures cover known pixels, offsets and
historical opaque CWP output. MAGES fractional target coordinates require a
resampling backend and are rejected explicitly. NekoNyan WebP strips use the
portable raster backend; their FFmpeg-generated lossless fixture is checked
against known pixels without relying on the original Windows-only codec.

GX4Lib and Unity companion indexes are read as constrained NRBF records using
System.Formats.Nrbf. Serialized CLR types are never loaded or instantiated.
Synthetic indexes cover generic headers, UTF-8 names, an unused list-array slot,
empty entries and raw extraction. Input size, array counts and entry placements
are bounded; wrong types and excessive array counts are rejected.

IPT and JSON-based ATX composites have shared known-pixel fixtures. IPT retains
the inherited unsigned-number text grammar; its internal canvas also bounds
negative tile placement. ATX retains the original first-block-only behavior and
does not decode protobuf atlases. RPG Maker image/audio tests switch between two
synthetic game roots with different keys and compare image pixels/Vorbis samples.
WebP and Opus have independently encoded fixtures checked in the portable runtime;
these audio/image fixtures are not compared with the historical native backends.

CAB uses managed bounded block decoding for stored and MSZIP folders, including
the preceding 32 KiB dictionary, checksums and reserve areas. Quantum/LZX folders,
split data and multi-cabinet sets report unsupported. The three-block synthetic
MSZIP fixture requires dictionary history; corrupted checksums are rejected.
Nested UTF-8 CAB names have a portable-only case because the historical wrapper
returns only each file's basename; the shared cases use flat ASCII names.

Hibiki loads a separately supplied `GameData/hibiki_works.json` with a fixed JSON
model and no CLR type metadata. The old `hibiki_works.dat` auxiliary file is not
included in the repository and is not deserialized by the portable runtime.
For example, `{"KnownSchemes":{"Game title":{"ContentKey":"base64 key bytes"}}}`
defines one game; optional `ArcMap` tables use `Name`, `Offset`, `Size` records.
When several game schemes exist, select one with
`--options '{"Scheme":"Game title"}'`. No game keys are supplied by the synthetic
fixture. Its portable test verifies encrypted prefixes, clear tails, empty entries
and rejection of entries outside the archive.

IGS synthetic cases cover plain and RC4 SQLite indexes with 1,024/4,096/65,536-byte
pages, both built-in passwords, compact and full-sized AES-CBC prefixes, empty
entries, clear tails and long UTF-8 names used to derive resource keys. Queries
use read-only connections, bounded values/counts/VM work and fixed parameters.
Invalid placements, invalid PKCS7 padding and active journals are rejected.
Mixed plaintext-page-1 databases and actual-game indexes remain unverified.
The historical SEE-dependent provider is not used as a fixture oracle; Python's
SQLite/RC4 and OpenSSL independently generate these fixtures.

Video preview uses separate FFmpeg processes. Synthetic MPEG-2/MP2 and silent MPEG-4 clips verify metadata, poster decoding and playback/pause/seek/stop. The GUI bounds frames to 960×540 at 30 fps and inputs to 2 GiB. Codec availability beyond those samples is not a per-game verification claim.

Intel macOS uses Magick.NET 14.17.2, the latest release with an Intel native asset; 15.0.0 no longer ships that asset. Other desktop targets retain 15.0.0. The same image/codec fixture suite runs on both macOS architectures.
