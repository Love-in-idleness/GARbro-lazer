#!/usr/bin/env python3
"""Rebuild the pinned MIT Wayland backend without rebuilding the other Avalonia packages."""
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import tarfile
import tempfile
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PATCH = ROOT / 'tools/patches/avalonia-wayland'
VENDOR = ROOT / 'vendor/nuget'


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def normalize_package(source, target):
    """NuGet emits random relationship IDs/core-property names and ZIP dates."""
    with zipfile.ZipFile(source) as archive:
        entries = {name: archive.read(name) for name in archive.namelist()}
    core = next(name for name in entries if name.endswith('.psmdcp'))
    stable = 'package/services/metadata/core-properties/avalonia-wayland.psmdcp'
    properties = ET.fromstring(entries.pop(core))
    for child in list(properties):
        if child.tag.rsplit('}', 1)[-1] in ('created', 'modified'):
            properties.remove(child)
    entries[stable] = ET.tostring(properties, encoding='utf-8', xml_declaration=True)
    relations = ET.fromstring(entries['_rels/.rels'])
    for index, child in enumerate(relations):
        child.set('Id', 'R' + str(index))
        if child.get('Target', '').lstrip('/') == core:
            child.set('Target', '/' + stable)
    entries['_rels/.rels'] = ET.tostring(relations, encoding='utf-8', xml_declaration=True)
    target.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(target, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, content in sorted(entries.items()):
            item = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
            item.create_system = 3
            item.external_attr = 0o100644 << 16
            item.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(item, content, compresslevel=9)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workdir', type=Path, default=ROOT / 'artifacts/patched-wayland')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/patched-packages')
    parser.add_argument('--verify', action='store_true', help='Compare the rebuild with the recorded local package')
    args = parser.parse_args()
    metadata = json.loads((PATCH / 'manifest.json').read_text())
    patch = PATCH / 'system-cursors.patch'
    patch_hash = digest(patch)
    if metadata['patchSha256'] and patch_hash != metadata['patchSha256']:
        raise ValueError('The source patch differs from its recorded hash.')
    args.workdir = args.workdir.resolve()
    args.workdir.mkdir(parents=True, exist_ok=True)
    archive = args.workdir / 'upstream.tar.gz'
    if not archive.exists():
        with urllib.request.urlopen(metadata['sourceUrl'], timeout=90) as response, archive.open('wb') as target:
            shutil.copyfileobj(response, target)
    if digest(archive) != metadata['sourceSha256']:
        raise ValueError('Upstream archive checksum mismatch.')
    source = args.workdir / ('source-' + metadata['upstreamCommit'][:12] + '-' + patch_hash[:12])
    stamp = source / '.garbro-source-patch'
    if not stamp.is_file():
        with tempfile.TemporaryDirectory(dir=args.workdir) as directory:
            with tarfile.open(archive) as upstream:
                upstream.extractall(directory, filter='data')
            extracted = Path(directory) / ('Avalonia-' + metadata['upstreamCommit'])
            env = dict(os.environ, GIT_CEILING_DIRECTORIES=str(extracted.parent))
            subprocess.run(['git', 'apply', '--check', str(patch)], cwd=extracted, env=env, check=True)
            subprocess.run(['git', 'apply', str(patch)], cwd=extracted, env=env, check=True)
            extracted.rename(source)
            stamp.write_text(patch_hash + '\n')
    if stamp.read_text().strip() != patch_hash:
        raise ValueError('The build source has a different patch stamp.')
    packed = args.workdir / 'packed'
    subprocess.run(['dotnet', 'pack', str(PATCH / 'PatchedWayland.csproj'), '-c', 'Release',
                    '--disable-build-servers', '-m:1', '-p:AvaloniaSourceRoot=' + str(source),
                    '-p:RestoreConfigFile=' + str(ROOT / 'NuGet.Config'),
                    '-o', str(packed)], cwd=ROOT, check=True)
    filename = metadata['package'].lower() + '.' + metadata['version'] + '.nupkg'
    target = args.output.resolve() / filename
    normalize_package(packed / (metadata['package'] + '.' + metadata['version'] + '.nupkg'), target)
    actual = digest(target)
    if args.verify:
        if actual != metadata['packageSha256'] or digest(VENDOR / filename) != actual:
            raise ValueError('The rebuilt package does not match the recorded package; use SDK ' + metadata['sdk'])
    print(str(target) + '\nSHA-256: ' + actual)


if __name__ == '__main__':
    main()
