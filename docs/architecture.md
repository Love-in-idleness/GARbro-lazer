# Architecture

The historical solution remains intact. SDK projects link original sources;
original decoder algorithms are not duplicated in the application or UI.

- `GARbro.Imaging`: owned immutable pixel buffers, packed formats, palettes and codecs.
- `GARbro.Core`: GameRes I/O, archive/image/audio abstractions and MEF discovery.
- `GARbro.Formats`: selected original providers, emitted as `ArcFormats.Portable.dll`.
- `GARbro.Application`: archive sessions, UI-independent options, extraction and previews.
- `GARbro.Cli`: command parsing and presentation of service results.
- `GARbro.Worker`: bounded-protocol host used by the desktop application.

Core references Imaging; providers reference Core; Application references both
Core and providers. MEF preserves exports, metadata and original priorities.
Only the historical build includes WPF widget adapters. The modern source uses
explicit portable image types, without defining counterfeit System.Windows types.

Original decoders share a global catalog and sometimes use the process working
directory. The application serializes decoder operations and restores that
directory after each operation. CLI commands execute in supervised child processes.
The desktop keeps archive metadata and starts a worker for opening, previews and
extraction. Its UI process does not load a live archive session. File size/time
fingerprints reject stale metadata if an archive changes between operations.

The parent monitors elapsed time and resident memory, terminates the worker tree
on cancellation or a limit violation, and removes this operation's registered,
nonce-named temporary outputs. Completed outputs and pre-existing files remain.
The defaults are 300 seconds and 1 GiB per operation; CLI flags and desktop
settings can change them. Memory is sampled, so this is a resource guard,
not an OS security sandbox or an exact allocator cap. Direct library callers must
choose their own process boundary. Protocol messages are bounded and use fixed
JSON data models without CLR type activation.

Linux chooses native Wayland first, with X11 fallback. The official Avalonia
Wayland backend supplies text-input-v3; a public client-reset adapter re-enables
cached surrounding-text state after focus so wlroots negotiates the capability.
No reflection or patched framework binaries are used. Windows/macOS retain their
native backends.


`ResourceLocation` identifies a physical root, nested VFS archive chain and current
virtual directory. `ResourceSession` exists only in the worker and retains the
original VFS lookup behavior for auxiliary indexes/keys. Loads return metadata;
they do not extract files. `DecoderContext` snapshots encoding, format settings,
schemes and session parameters per operation. Explicit cipher encodings stay fixed.
Session parameter keys include the provider and complete archive location, so
sibling archives of the same format can use different keys. Named schemes select
trusted bundled objects; JSON never chooses a CLR type. Resource browsing keeps
XFL child containers visible while the compatible single-file service retains
the historical automatic flattening behavior.

Recursive jobs use one serial queue, shared entry/output/ratio budgets and an
eight-level default. Child archives are copied and expanded next to that copy.
Parameter retries report committed paths and include them in the resumed budget.
Worker-owned scratch directories are registered for parent cleanup after a kill.
Audio decoding produces a bounded PCM16 WAV in a parent-owned cache directory;
JSON transfers metadata only. The desktop uses SDL audio, leaving the native
Avalonia window backend in charge of Wayland/text input. Closing or changing the
selection stops the device and removes the cache.

Settings use atomic versioned per-user JSON. Keys/passwords stay in session data.
Offline help embeds bilingual manuals, licenses and a per-provider verification
ledger. Only explicit user actions check releases/download compatible databases;
SHA-256 and bounded schema validation precede atomic replacement in user storage.
