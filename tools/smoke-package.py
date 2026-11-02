#!/usr/bin/env python3
"""Unpack and exercise a generated preview package on its target platform."""
import argparse
import gzip
import hashlib
import json
import os
import subprocess
import sys
import tarfile
import tempfile
import zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--rid',required=True)
parser.add_argument('--output',type=Path,default=ROOT/'artifacts')
parser.add_argument('--dotnet',default='dotnet')
parser.add_argument('--version',default=os.environ.get('GARBRO_PACKAGE_VERSION','1.0.0'))
args=parser.parse_args()
name='garbro-'+args.version+'-'+args.rid
archive=args.output/(name+('.zip'if args.rid.startswith('win-')else'.tar.gz'))
with tempfile.TemporaryDirectory(prefix='garbro-package-')as folder:
    root=Path(folder)
    if archive.suffix=='.zip':
        with zipfile.ZipFile(archive)as source:
            for item in source.namelist():
                if not (root/item).resolve().is_relative_to(root.resolve()):
                    raise ValueError('Archive contains an escaping path')
            source.extractall(root)
    else:
        with tarfile.open(archive)as source:
            source.extractall(root,filter='data')
    package=root/name
    for relative in ('LICENSE','dependencies.json','licenses','compatibility.md',
                     'compatibility/baseline-source.json','compatibility/baseline-runtime.json',
                     'compatibility/portable-status.json','migration-status.md'):
        assert (package/relative).exists(),relative
    cli=package/'cli/garbro.dll'
    desktop=package/('GARbro.app/Contents/Resources/app'if args.rid.startswith('osx-')else'desktop')
    if args.rid.startswith('osx-'):assert (package/'GARbro.app/Contents/MacOS/GARbro.Desktop').exists()
    assert (desktop/'GARbro.Desktop.dll').exists()
    sdl='SDL2.dll'if args.rid.startswith('win-')else'libSDL2-2.0.dylib'if args.rid.startswith('osx-')else'libSDL2-2.0.so'
    if args.rid.startswith('linux-'):assert not (desktop/sdl).exists(),'Linux uses the distribution SDL2 library'
    else:assert (desktop/sdl).exists(),'Bundled audio library missing: '+sdl
    for relative in ('GARbro.Worker.dll','GARbro.Worker.runtimeconfig.json','GARbro.Worker.deps.json'):
        assert (desktop/relative).exists(),relative
    if args.rid.startswith('win-'):assert (desktop/'GARbro.Worker.exe').exists(),'MSIX requires the packaged worker apphost'
    worker=subprocess.run([args.dotnet,str(desktop/'GARbro.Worker.dll')],
        input=json.dumps({'operation':'open','path':str(ROOT/'tests/fixtures/generated/standard.xfl')}),
        text=True,capture_output=True,check=True,timeout=45)
    opened=json.loads(worker.stdout)
    assert opened['kind']=='result' and opened['data']['tag']=='XFL' and len(opened['data']['entries'])==3
    preview=subprocess.run([args.dotnet,str(desktop/'GARbro.Worker.dll')],
        input=json.dumps({'operation':'resource-preview','location':{'rootPath':str(ROOT/'tests/fixtures/generated')},'entry':'movie.mpg'}),
        text=True,capture_output=True,check=True,timeout=45)
    result=json.loads(preview.stdout)
    assert result['kind']=='result' and result['data']['kind']=='video',result
    if not args.rid.startswith('linux-'):
        helpers=desktop/'media'
        assert (helpers/('ffmpeg.exe' if args.rid.startswith('win-')else'ffmpeg')).exists()
        recipe=json.loads((helpers/'media-codecs.json').read_text())
        assert hashlib.sha256((helpers/'ffmpeg-source.tar.gz').read_bytes()).hexdigest()==recipe['sha256']
        assert (helpers/'COPYING.LGPLv2.1').exists()
    database=cli.parent/'GameData/Formats.json.gz'
    assert database.read_bytes()==(desktop/'GameData/Formats.json.gz').read_bytes()
    schemes=json.loads(gzip.decompress(database.read_bytes()))
    metadata=json.loads(database.with_name('schemes.json').read_text())
    assert database.with_name('schemes.json').read_bytes()==(desktop/'GameData/schemes.json').read_bytes()
    assert schemes['Version']==metadata['version']==148 and schemes['PortableSchemaVersion']==2
    assert sorted(schemes['SchemeMap'])==metadata['applied']
    assert hashlib.sha256(gzip.decompress(database.read_bytes())).hexdigest()==metadata['json_sha256']
    assert json.loads((package/'dependencies.json').read_text())
    for notice in ('GPPG.md','GPLEX.md','Concentus.Oggfile.txt','SQLite3MC.txt','SQLite3MC-NuGet.txt','SQLitePCLRaw.txt','NWayland.txt'):
        assert (package/'licenses/source-notices'/notice).exists(),notice
    subprocess.run([sys.executable,str(ROOT/'tools/smoke-cli.py'),
                    '--dotnet',args.dotnet,'--cli',str(cli)],check=True,cwd=root)
print('Unpacked package smoke checks passed:',args.rid)
