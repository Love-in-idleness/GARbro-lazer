#!/usr/bin/env python3
"""Build standalone LGPL FFmpeg helpers, with corresponding source and recipe."""
import argparse
import hashlib
import json
import os
import platform
import shutil
import subprocess
import tarfile
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def build(rid, output):
    recipe = json.loads((ROOT/'tools/media-codecs.json').read_text())
    target = output.resolve()/rid
    suffix = '.exe' if rid.startswith('win-') else ''
    if all((target/(name+suffix)).is_file() for name in ('ffmpeg', 'ffprobe')) and (target/'media-codecs.json').is_file() and json.loads((target/'media-codecs.json').read_text()) == recipe:
        return target
    workspace = output.resolve()/('build-'+rid)
    workspace.mkdir(parents=True, exist_ok=True)
    archive = workspace/'source.tar.gz'
    if not archive.exists():
        with urllib.request.urlopen(recipe['url'], timeout=90) as source, archive.open('wb') as destination:
            shutil.copyfileobj(source, destination)
    if hashlib.sha256(archive.read_bytes()).hexdigest() != recipe['sha256']:
        raise ValueError('FFmpeg source checksum mismatch')
    source = workspace/('FFmpeg-'+recipe['commit'])
    if not (source/'configure').is_file():
        with tarfile.open(archive) as contents: contents.extractall(workspace, filter='data')
    options = recipe['options'] + ['--prefix='+str(target).replace('\\', '/'), '--disable-shared', '--enable-static']
    environment = os.environ.copy()
    bash = shutil.which('bash') or '/bin/bash'
    if rid.startswith('win-'):
        bash = str(Path(environment['ProgramFiles'])/'Git/bin/bash.exe')
        vswhere = Path(environment['ProgramFiles(x86)'])/'Microsoft Visual Studio/Installer/vswhere.exe'
        install = subprocess.check_output([str(vswhere), '-latest', '-property', 'installationPath'], text=True).strip()
        arch = 'arm64' if rid == 'win-arm64' else 'x64'
        command = f'call "{install}\\Common7\\Tools\\VsDevCmd.bat" -arch={arch} -host_arch=x64 >nul && set'
        lines = subprocess.check_output(['cmd', '/d', '/c', command], text=True)
        environment.update(dict(line.split('=', 1) for line in lines.splitlines() if '=' in line and not line.startswith('=')))
        # Git Bash's /usr/bin/link is not the Visual C++ linker.
        environment['MSYS2_PATH_TYPE'] = 'inherit'
        options += ['--toolchain=msvc', '--target-os=win32', '--arch='+('aarch64' if arch == 'arm64' else 'x86_64'), '--disable-asm']
        options += ['--extra-cflags=-MT']
        if arch == 'arm64': options += ['--enable-cross-compile']
    subprocess.run([bash, str(source/'configure').replace('\\', '/'), *options], cwd=source, env=environment, check=True)
    make = shutil.which('make') or shutil.which('mingw32-make')
    if not make: raise RuntimeError('GNU make is required to build FFmpeg')
    subprocess.run([make, '-j'+str(min(4, os.cpu_count() or 2))], cwd=source, env=environment, check=True)
    target.mkdir(parents=True, exist_ok=True)
    for name in ('ffmpeg', 'ffprobe'): shutil.copy2(source/(name+suffix), target/(name+suffix))
    for name in ('COPYING.LGPLv2.1', 'LICENSE.md'): shutil.copy2(source/name, target/name)
    shutil.copy2(archive, target/'ffmpeg-source.tar.gz')
    shutil.copy2(ROOT/'tools/media-codecs.json', target/'media-codecs.json')
    shutil.copy2(ROOT/'tools/build-media.py', target/'build-media.py')
    if not (rid == 'win-arm64' and platform.machine().lower() not in ('arm64','aarch64')):
        subprocess.run([str(target/('ffmpeg'+suffix)), '-version'], check=True)
    return target

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rid', required=True)
    parser.add_argument('--output', type=Path, default=ROOT/'artifacts/media')
    args = parser.parse_args()
    print(build(args.rid, args.output))
