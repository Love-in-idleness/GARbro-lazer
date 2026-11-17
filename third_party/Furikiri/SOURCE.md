# Furikiri Core

Source: https://github.com/UlyssesWu/Furikiri/tree/c19ef85607c7de207f8edc8147926c67e75fda6c

Only the managed Core sources are included, built as a separate `Furikiri.Core.dll`
with the original assembly name and public API. The CLI is excluded. The one
upstream bytecode test resource is in `tests/fixtures/scripts-native`, not the app.
LGPL-3.0 and the incorporated GPL-3.0 texts accompany the source and assembly.

Local changes: `Helper.cs`, `Emit/DataSection.cs` and `Emit/Module.cs` bound
untrusted section counts by the remaining input, element width and an absolute
limit before allocation. All other Core files retain the pinned source.

Build with .NET SDK 11.0.100 from the repository root:

```sh
dotnet build modern/GARbro.Scripting.Tjs/GARbro.Scripting.Tjs.csproj -c Release
```

The only extra dependency is Superpower 3.1.0 (Apache-2.0). No native library,
Python runtime, CLI or game engine is needed. Desktop packages carry a source
archive and this recipe under `licenses/Furikiri-source.tar.gz`. The assembly is
not merged into GARbro; a compatible modified `Furikiri.Core.dll` can replace it
in an unpacked application payload. Store package integrity requires rebuilding
and signing a modified package. GARbro permits reverse engineering for debugging
modifications of this LGPL component, as required by its license.
