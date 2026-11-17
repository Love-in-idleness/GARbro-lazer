# Native script scheme data

`Luca.json` contains game schemes derived from LuckSystem commit
`8146592658f61cd4016464fa67da6348c6bbc3af` (MIT, see `docs/licenses/LuckSystem.txt`).
Generate it without executing a plugin:

```sh
python3 tools/import-luca-schemes.py /path/to/LuckSystem/data SchemeTool/Data/Luca.json
```

The records become `LucaProfile` / `LucaScheme` fields in the ordinary `SchemeMap`
under `LUCA-SCRIPT`. The application reads only the resulting portable database.
No Python plugin, Go program or interpreter is distributed.

`Opcodes` preserves byte-index-to-name mapping. `Parameters` contains binary-layout
rules: `u8`, `u16`, `u32`, terminated (`s:`) or length-prefixed (`l:`) strings with
`text`, `expression` or a fixed encoding, and local/cross-entry jumps (`j:`).
`@name` binds a decoded value for a conditional or count. Bounded `remaining`,
`nonempty`, `equal`, `notequal`, `repeat` and `while` groups end with `end`.
`peek16` selects a layout without consuming bytes; `raw` preserves remaining data.
These are data-layout rules, not Python expressions or executable plugins.

The library validates layout size/nesting, input bounds, loop progress and output
length. Unknown opcodes and missing branch layouts prevent modified output. Jump targets are relocated over
the entire SCRIPT.PAK; unresolved targets fail rather than being guessed.

Build the portable SchemeTool with .NET 11, then merge into an existing database:

```sh
dotnet run --project modern/GARbro.SchemeTool -c Release -- \
  --input modern/GameData/Formats.json.gz --luca SchemeTool/Data/Luca.json \
  --output artifacts/schemes/Formats.json --report artifacts/schemes/report.json \
  --version 150
python3 tools/prepare-schemes.py artifacts/schemes/Formats.json \
  --report artifacts/schemes/report.json
```

Use a version greater than the input. The initial Luca import generated version
149 from 148. Only `LUCA-SCRIPT` and the database version are replaced; existing
scheme records and GameMap retain their original serialized values. The old
Windows SchemeTool remains available for historical `Formats.dat` maintenance.
