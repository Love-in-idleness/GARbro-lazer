#!/usr/bin/env python3
"""Freeze source exports; this is not evidence that a decoder works at runtime."""
import argparse
import json
import re
import subprocess
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
NS = {'p': 'http://schemas.microsoft.com/developer/msbuild/2003'}
PROJECTS = ['GameRes/GameRes.csproj', 'ArcFormats/ArcFormats.csproj',
            'Legacy/Legacy.csproj', 'Experimental/Experimental.csproj']
BASELINE = '698ccc57'


def without_comments(text):
    token = re.compile(r'''@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])'|//[^\n]*|/\*.*?\*/''', re.S)
    return token.sub(lambda m: ''.join('\n' if c == '\n' else ' ' for c in m.group()) if m.group().startswith(('//', '/*')) else m.group(), text)


def conditions_at(text, offset):
    stack = []
    for line in text[:offset].splitlines():
        directive = re.match(r'\s*#(if|elif|else|endif)\b(.*)', line)
        if not directive: continue
        command, value = directive.group(1), directive.group(2).strip()
        if command == 'if': stack.append(value)
        elif command == 'elif' and stack: stack[-1] = value
        elif command == 'else' and stack: stack[-1] = '!(' + stack[-1] + ')'
        elif command == 'endif' and stack: stack.pop()
    return ' && '.join(stack) or None


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT).decode('utf-8-sig')


def inventory(ref):
    rows = []
    paths = {p.lower(): p for p in git("ls-tree", "-r", "--name-only", ref).splitlines()}
    for project in PROJECTS:
        root = ET.fromstring(git('show', f'{ref}:{project}'))
        for item in root.findall('.//p:Compile', NS):
            path = (Path(project).parent / item.attrib['Include'].replace('\\', '/')).as_posix()
            path = paths.get(path.lower(), path)
            text = without_comments(git('show', f'{ref}:{path}'))
            exports = list(re.finditer(r'\bExport\s*\(\s*typeof\s*\(\s*(ArchiveFormat|ImageFormat|AudioFormat|ScriptFormat)\s*\)\s*\)', text))
            for index, match in enumerate(exports):
                body = text[match.end():exports[index+1].start() if index+1 < len(exports) else len(text)]
                cls = re.search(r'\bclass\s+(\w+)', body)
                tag = re.search(r'\bTag\s*(?:\{\s*get\s*(?:\{\s*return\s*|=>\s*)|=>\s*)"([^"]+)"', body)
                if tag is None:
                    tag = re.search(r'\bTag\s*\{\s*get;\s*\}\s*=\s*"([^"]+)"', body)
                namespace = re.search(r'^\s*namespace\s+([\w.]+)', text[:match.start()], re.M)
                rows.append({'project': project, 'source': path,
                    'class': (namespace.group(1) + '.' if namespace else '') + (cls.group(1) if cls else 'UNRESOLVED'),
                    'assembly': root.find('.//p:AssemblyName', NS).text,
                    'contract': match.group(1), 'tag': tag.group(1) if tag else None,
                    'condition': conditions_at(text, match.start()),
                    'metadata_source': 'static-source', 'runtime_status': 'not-tested'})
    return {'baseline_commit': git('rev-parse', ref).strip(), 'schema': 2,
            'note': 'Static exports from explicit legacy Compile items; not a runtime compatibility claim.',
            'formats': sorted(rows, key=lambda r: (r['project'], r['source'], r['class']))}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ref', default=BASELINE)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    path = ROOT / 'docs/compatibility/baseline-source.json'
    text = json.dumps(inventory(args.ref), indent=2, ensure_ascii=False) + '\n'
    if args.check:
        if not path.exists() or path.read_text() != text:
            raise SystemExit('Baseline inventory differs; regenerate and review the catalog change.')
    else:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
    print(f'{len(json.loads(text)["formats"])} baseline format exports accounted for')

if __name__ == '__main__':
    main()
