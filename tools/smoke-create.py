#!/usr/bin/env python3
"""Check the create command without repeating the general CLI regression suite."""
import argparse
import hashlib
import json
import shutil
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', default='dotnet')
args = parser.parse_args()
cli = ROOT/'modern/GARbro.Cli/bin/Release/net11.0/garbro.dll'

def run(*arguments, code=0):
    result = subprocess.run([args.dotnet, str(cli), '--json', *map(str, arguments)], capture_output=True, text=True)
    assert result.returncode == code, (arguments, result.returncode, result.stdout, result.stderr)
    return json.loads(result.stdout)

for language, word in [('en-US', 'archive'), ('zh-CN', '归档'), ('ru-RU', 'архив'), ('ja-JP', 'アーカイブ')]:
    help_text = subprocess.check_output([args.dotnet, str(cli), '--language', language, 'create', '--help'], text=True)
    assert word in help_text.lower() and '--discard-original-media' in help_text

with tempfile.TemporaryDirectory(prefix='garbro-create-cli-') as directory:
    root = Path(directory); source = root/'中文 Русский 日本語'; source.mkdir()
    shutil.copy2(ROOT/'tests/fixtures/generated/colors-rgba.png', source/'scene.png')
    (source/'notes.txt').write_text('selected text', encoding='utf-8')
    packed = root/'media.zip'
    report = run('create', source, '-o', packed, '--image-format', 'WEBP', '--discard-original-media')
    assert report['written'] and report['entries'] == 2 and report['converted'] == 1
    entries = run('list', packed)[0]['entries']
    assert {entry['name'] for entry in entries} == {'scene.webp', 'notes.txt'}
    previous = hashlib.sha256(packed.read_bytes()).hexdigest()
    assert not run('create', source, '-o', packed, '--overwrite', 'skip')['written']
    run('create', source, '-o', packed, '--overwrite', 'replace', '--max-total', '1', code=3)
    assert hashlib.sha256(packed.read_bytes()).hexdigest() == previous
    literal = root/'-literal.txt'; literal.write_text('another explicit input')
    selected = run('create', '-o', root/'multiple.zip', '--', source, literal)
    assert selected['entries'] == 3
    names = {entry['name'] for entry in run('list', root/'multiple.zip')[0]['entries']}
    assert '-literal.txt' in names and source.name+'/notes.txt' in names
    # A previous output inside the input directory must not pack itself on replacement.
    inside = source/'inside.xfl'
    assert run('create', source, '-o', inside)['entries'] == 2
    assert run('create', source, '-o', inside, '--overwrite', 'replace')['entries'] == 2
print('Create CLI: four-language help, media conversion, JSON, selection, skip, limits and output exclusion passed.')
