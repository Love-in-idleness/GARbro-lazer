"""Prepare the same pinned, build-only SDK as the Flatpak manifest."""
import hashlib
import json
import platform
import shutil
import tarfile
import urllib.request
from pathlib import Path


def prepare_sdk(root):
    arch={'x86_64':'x86_64','aarch64':'aarch64'}[platform.machine()]
    manifest=json.loads((root/'packaging/flatpak/io.github.love_in_idleness.GARbro-lazer.json').read_text())
    module=next(m for m in manifest['modules'] if m['name']=='dotnet11-sdk')
    source=next(s for s in module['sources'] if arch in s['only-arches'])
    target=root/'artifacts'/('flatpak-sdk-'+arch)
    stamp=target/'.source-sha512'
    if stamp.is_file() and stamp.read_text().strip()==source['sha512']:return target
    target.mkdir(parents=True,exist_ok=True)
    archive=target.parent/('sdk-'+arch+'.tar.gz')
    try:
        with urllib.request.urlopen(source['url'],timeout=120) as response,archive.open('wb') as output:
            shutil.copyfileobj(response,output)
        with archive.open('rb') as stream:
            if hashlib.file_digest(stream,'sha512').hexdigest()!=source['sha512']:raise ValueError('SDK checksum mismatch')
        with tarfile.open(archive) as compressed:compressed.extractall(target,filter='data')
        stamp.write_text(source['sha512']+'\n')
    finally:archive.unlink(missing_ok=True)
    return target
