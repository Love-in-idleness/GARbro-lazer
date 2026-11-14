# Portable scheme data

`Formats.json.gz` bundles all 77 scheme groups consumed by the migrated providers,
plus the legacy game-name map. It is generated from this repository's existing
`ArcFormats/Resources/Formats.dat`; no local user game files or credentials are included.
The original data and license remain intact.

The source is the schema-2 Windows export at commit
`a181a9760ca42734a034e7e88fc8f85cf13cbf34`, workflow run
[40984047492](https://github.com/Love-in-idleness/GARbro-lazer/actions/runs/40984047492),
artifact `legacy-runtime-catalog` (ID `12946361447`). `schemes.json` records the
source-export SHA-256, bundled JSON SHA-256 and exact applied/excluded tag sets.
The export includes private serialized fields; computed password setters are never
used to reconstruct hashed DXA keys.

Reproduce from the artifact's `Formats.json` after building the portable solution:

```sh
dotnet modern/GARbro.Cli/bin/Release/net10.0/garbro.dll schemes /path/to/Formats.json --json > import-report.json
python3 tools/prepare-schemes.py /path/to/Formats.json --report import-report.json
```

The helper requires schema 2, no import failures, matching source/report versions,
and complete accounting of source tags before replacing either bundled file.
The source data version is 148. One historical stored group, `DAT/AI5WIN`, is
excluded because its current provider infers its index keys and has no consumed
scheme property. Its provider is compiled and registered; this is an obsolete
scheme-data entry, not an omitted decoder. The original binary database retains it.

The modern reader uses a provider type allowlist and never enables BinaryFormatter.
Importing a scheme establishes data compatibility, not verified decryption for
every game represented in the database. Real-game encrypted fixtures remain necessary.
