#!/usr/bin/env python3
"""Mount a DMG, validate game associations and launch its app through LaunchServices."""
import argparse
import json
import os
import plistlib
import shutil
import subprocess
import tempfile
import time
from pathlib import Path
from macos_support import minimum_system_versions
from macos_association_probe import handlers,default_handler
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('dmg',type=Path)
args=parser.parse_args()
with tempfile.TemporaryDirectory(prefix='GARbro macOS 测试 ') as directory:
    root=Path(directory);mount=root/'mounted';mount.mkdir()
    subprocess.run(['hdiutil','attach','-readonly','-nobrowse','-mountpoint',str(mount),str(args.dmg.resolve())],check=True)
    try:
        mounted=mount/'GARbro/GARbro.app'
        app=root/'installed/GARbro.app';app.parent.mkdir();shutil.copytree(mounted,app,symlinks=True)
        register='/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister'
        previous_zip_default=default_handler('public.zip-archive')
        subprocess.run([register,'-f',str(app)],check=True)
        info=plistlib.loads((app/'Contents/Info.plist').read_bytes())
        assert info['CFBundleDocumentTypes'][0]['LSHandlerRank']=='Alternate'
        extensions=info['CFBundleDocumentTypes'][0]['CFBundleTypeExtensions']
        assert {'xp3','xfl','zip','dat','bin'} <= set(extensions) and 'mp4'not in extensions
        assert set(info['CFBundleLocalizations'])=={'en','zh-Hans','ru','ja'}
        assert info['UTImportedTypeDeclarations']
        assert default_handler('public.zip-archive')==previous_zip_default
        subprocess.run(['codesign','--verify','--deep','--strict',str(app)],check=True)
        payload=app/'Contents/Resources/app'
        for name in ('ffmpeg','ffprobe'):
            commands=subprocess.check_output(['otool','-l',str(payload/'media'/name)],text=True)
            versions=minimum_system_versions(commands)
            assert versions,'Video helper has no minimum-system declaration'
            assert all(tuple(map(int,v.split('.'))) <= (14,0,0) for v in versions),f'{name} requires macOS {versions}'
            print(name,'minimum macOS:',', '.join(versions))
        subprocess.run([str(payload/'media/ffmpeg'),'-version'],check=True)
        fixture=root/'中文 Русский 日本語 文件.xfl';shutil.copy2('tests/fixtures/generated/standard.xfl',fixture)
        runtime=['--env','DOTNET_ROOT='+os.environ['DOTNET_ROOT']] if os.environ.get('DOTNET_ROOT')else[]
        for suffix in ('xfl','dat','bin','zip'):
            sample=fixture.with_suffix('.'+suffix);sample.write_bytes(fixture.read_bytes())
            available=handlers(sample)
            assert str(app.resolve()) in available,(suffix,available)
        logs=[Path.home()/'Library/Application Support/GARbro/desktop.log',Path.home()/'.local/share/GARbro/desktop.log']
        starts={p:p.stat().st_size if p.exists()else 0 for p in logs}
        def wait_activation(count):
            deadline=time.monotonic()+25
            while time.monotonic()<deadline:
                records=[]
                for p in logs:
                    if p.exists():
                        with p.open()as stream:
                            stream.seek(starts[p]);records.extend(json.loads(line)for line in stream if line.strip())
                successes=[r for r in records if r.get('action')=='file-activation' and 'success=True; archive=XFL; entries=3'in r.get('detail','')]
                if len(successes)>=count:return
                time.sleep(.2)
            raise AssertionError(('Finder document event did not load archive',records))
        # Actual document activation, without argv injection.
        subprocess.run(['open',*runtime,'-n','-a',str(app),str(fixture)],check=True)
        wait_activation(1)
        second=root/'Second Русский 日本語.xfl';second.write_bytes(fixture.read_bytes())
        subprocess.run(['open','-a',str(app),str(second)],check=True)
        wait_activation(2)
        processes=subprocess.check_output(['pgrep','-f',str(app/'Contents/MacOS/GARbro.Desktop')],text=True).splitlines()
        assert processes,'Finder/LaunchServices launch did not keep the application running'
        # Verify the worker, launched from the mounted app, reads a Unicode path.
        result=subprocess.run([str(payload/'GARbro.Worker')],input=json.dumps({'operation':'open','path':str(fixture)}),text=True,capture_output=True,check=True)
        assert json.loads(result.stdout)['data']['tag']=='XFL'
        for pid in processes:subprocess.run(['kill',pid],check=True)
        subprocess.run([register,'-u',str(app)],check=True)
        assert default_handler('public.zip-archive')==previous_zip_default
    finally:subprocess.run(['hdiutil','detach',str(mount)],check=True)
print('macOS DMG, code signature, associations, app launch and Unicode worker passed.')
