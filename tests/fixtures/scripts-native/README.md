# Native script compatibility fixtures

Authored synthetic scripts; no game files. The expected binaries were produced on 2026-11-16 by independent tools, used only during development:

- `*.gsc`: LiarsoftTool 2.3.1, `--encoding cp932 --output OUTPUT INPUT.tsc`.
- `scene.bgi` and `scene._bp`: Bgi_asdisEN commit `44574e627093899efa9fb4b9b82d78e03fdfe447`, `bgias.py` / `bpas.py INPUT -c cp932`.
- `scene.asb`: msg_tool 0.4.0-alpha.3, `create -t artemis-asb -e cp932 -E utf8 scene.json scene.asb`.

The tests compare native output with these binaries, then change text and read it back. BPD includes every currently implemented variable-instruction family, relative branches, typed SLEB128 and pooled strings. These fixtures establish format compatibility; they do not certify every game engine variant.
