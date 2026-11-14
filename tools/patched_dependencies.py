"""Checksummed local framework packages shared by CI and offline packaging."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def local_packages(root=ROOT):
    folder = root / 'tools/patches/avalonia-wayland'
    metadata = json.loads((folder / 'manifest.json').read_text())
    patch_hash = hashlib.sha256((folder / 'system-cursors.patch').read_bytes()).hexdigest()
    if patch_hash != metadata['patchSha256']:
        raise ValueError('Wayland source-patch checksum mismatch.')
    identity = metadata['package'].lower() + '/' + metadata['version']
    package = root / 'vendor/nuget' / (identity.replace('/', '.') + '.nupkg')
    if hashlib.sha256(package.read_bytes()).hexdigest() != metadata['packageSha256']:
        raise ValueError('Wayland local-package checksum mismatch.')
    return {identity: package}


if __name__ == '__main__':
    for identity, package in local_packages().items():
        print('Verified local dependency:', identity, package.name)
