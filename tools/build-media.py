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
    if all((target/(name+suffix)).is_file() for name in ('ffmpeg', 'ffprobe')) and (target/'media-codecs.json').is_file() and json.loads((target/'media-codecs.json').read_text()) == recipe and (target/'build-media.py').is_file() and (target/'build-media.py').read_bytes() == Path(__file__).read_bytes():
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
    if rid.startswith('osx-'):
        environment['MACOSX_DEPLOYMENT_TARGET'] = '14.0'
        options += ['--extra-cflags=-mmacosx-version-min=14.0', '--extra-ldflags=-mmacosx-version-min=14.0']
    bash = shutil.which('bash') or '/bin/bash'
    make = shutil.which('make') or shutil.which('mingw32-make')
    if rid.startswith('win-'):
        msys = Path(environment.get('MSYS2_LOCATION',r'C:\msys64'))/'usr/bin'
        bash = str(msys/'bash.exe')
        make = str(msys/'make.exe')
        if not Path(bash).is_file() or not Path(make).is_file():raise RuntimeError('Install MSYS2 make and pkgconf, then set MSYS2_LOCATION.')
        vswhere = Path(environment['PROGRAMFILES(X86)'])/'Microsoft Visual Studio/Installer/vswhere.exe'
        install = subprocess.check_output([str(vswhere), '-latest', '-property', 'installationPath'], text=True).strip()
        arch = 'arm64' if rid == 'win-arm64' else 'x64'
        command = f'chcp 65001 >nul && call "{install}\\Common7\\Tools\\VsDevCmd.bat" -arch={arch} -host_arch=x64 >nul && set'
        lines = subprocess.check_output(command, shell=True, text=True, encoding='utf-8')
        environment.update({key.upper(): value for key,value in (line.split('=', 1) for line in lines.splitlines() if '=' in line and not line.startswith('='))})
        previous = {part.lower() for part in os.environ['PATH'].split(os.pathsep)}
        compiler = [part for part in environment['PATH'].split(os.pathsep) if part.lower() not in previous]
        environment['PATH'] = os.pathsep.join(compiler + [str(msys), os.environ['PATH']])
        environment['SHELL'] = str(msys/'sh.exe').replace('\\', '/')
        environment['MSYSTEM'] = 'MSYS'
        # Keep Visual C++'s linker ahead of the POSIX link utility.
        environment['MSYS2_PATH_TYPE'] = 'inherit'
        options += ['--toolchain=msvc', '--target-os=win32', '--arch='+('aarch64' if arch == 'arm64' else 'x86_64'), '--disable-asm']
        options += ['--extra-cflags=-MT']
        if arch == 'arm64': options += ['--enable-cross-compile']
    subprocess.run([bash, str(source/'configure').replace('\\', '/'), *options], cwd=source, env=environment, check=True)
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
