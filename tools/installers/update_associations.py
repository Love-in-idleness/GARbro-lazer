#!/usr/bin/env python3
"""Add host MIME aliases to the package-owned DEB desktop entry, never defaults."""
import json
import re
from pathlib import Path


def supplement(extensions, paths):
    result=set()
    for path in paths:
        if not path.is_file():continue
        for line in path.read_text().splitlines():
            fields=line.split(':')
            if len(fields)<3 or line.startswith('#'):continue
            mime,glob=fields[1:3]
            if (glob.startswith('*.') and glob[2:].lower() in extensions and
                re.fullmatch(r'[a-zA-Z0-9!#$&^_.+-]+/[a-zA-Z0-9!#$&^_.+-]+',mime) and
                not mime.startswith(('audio/','image/','video/','text/')) and
                not any(part in mime for part in ('executable','sharedlib','shellscript','msdownload'))):result.add(mime)
    return result


def main():
    desktop=Path('/usr/share/applications/org.garbro.GARbro.desktop')
    manifest=json.loads(Path('/usr/share/garbro/associations.json').read_text())
    extra=supplement(set(manifest['extensions']),[Path('/usr/local/share/mime/globs2'),Path('/usr/share/mime/globs2')])
    source=desktop.read_text();lines=source.splitlines()
    for i,line in enumerate(lines):
        if line.startswith('MimeType='):
            values=set(line.removeprefix('MimeType=').split(';'))|extra;values.discard('')
            lines[i]='MimeType='+';'.join(sorted(values))+';'
    desktop.write_text('\n'.join(lines)+'\n')


if __name__=='__main__':main()
