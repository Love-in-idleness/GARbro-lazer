# Avalonia Wayland system-cursor compatibility patch

This is Avalonia's MIT-licensed Wayland backend, built from commit
`2d79ba55fae1b1e71c69f93a668080c9029e6a31` (12.1.4). Only this backend is
rebuilt; all other Avalonia packages stay on the official 12.1.4 release.

The patch uses the optional `wp_cursor_shape_v1` protocol already provided by
NWayland 0.11.0. Standard pointer shapes belong to the compositor, which selects
the user's theme, size, scaling and animation. No cursor images are added to
GARbro. Pointer enter serials and shape-device lifetime remain per pointer/seat.
`None` still hides the pointer, and the framework's existing bitmap-cursor API
is preserved. Compositors without cursor-shape use libwayland-cursor, reading
`XCURSOR_THEME` and `XCURSOR_SIZE`; their themes are loaded lazily.

`system-cursors.patch` is the complete backend source change. The repository's
`manifest.json` records archive, patch and package hashes. Rebuild with a .NET 10 SDK and Git:

```sh
python3 tools/build-patched-wayland.py
python3 tools/build-patched-wayland.py --verify
```

The reference build uses SDK 10.0.403. The build project compiles against the
official runtime assemblies: Avalonia's public reference assemblies deliberately
exclude private backend contracts. Assembly name, version and strong-name key
stay identical to the official backend. The package ID is unchanged; its package
version is `12.1.4-garbro.1` to avoid replacing an official NuGet cache entry.

The checked-in local NuGet package allows ordinary and offline builds without
downloading or rebuilding the framework. No alternate package server is required.
The upstream license is included in the package under `licenses/MIT.md`.
