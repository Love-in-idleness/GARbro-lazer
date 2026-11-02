#!/usr/bin/env python3
"""Mount a DMG, validate game associations and launch its app through LaunchServices."""
import argparse
import json
import plistlib
import shutil
import subprocess
import tempfile
import time
from pathlib import Path
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('dmg',type=Path)
args=parser.parse_args()
with tempfile.TemporaryDirectory(prefix='GARbro macOS 测试 ') as directory:
    root=Path(directory);mount=root/'mounted';mount.mkdir()
    subprocess.run(['hdiutil','attach','-readonly','-nobrowse','-mountpoint',str(mount),str(args.dmg.resolve())],check=True)
    try:
        app=next(mount.glob('GARbro.app'))
        info=plistlib.loads((app/'Contents/Info.plist').read_bytes())
        assert info['CFBundleDocumentTypes'][0]['LSHandlerRank']=='Alternate'
        extensions=info['CFBundleDocumentTypes'][0]['CFBundleTypeExtensions']
        assert 'xp3'in extensions and 'mp4'not in extensions and 'dat'not in extensions
        subprocess.run(['codesign','--verify','--deep','--strict',str(app)],check=True)
        subprocess.run([str(app/'Contents/MacOS/media/ffmpeg'),'-version'],check=True)
        fixture=root/'中文 文件.xp3';shutil.copy2('tests/fixtures/generated/standard.xfl',fixture)
        subprocess.run(['open','-n','-a',str(app),'--args',str(fixture)],check=True)
        time.sleep(3)
        processes=subprocess.check_output(['pgrep','-f',str(app/'Contents/MacOS/GARbro.Desktop')],text=True).splitlines()
        assert processes,'Finder/LaunchServices launch did not keep the application running'
        # Verify the worker, launched from the mounted app, reads a Unicode path.
        result=subprocess.run([str(app/'Contents/MacOS/GARbro.Worker')],input=json.dumps({'operation':'open','path':str(fixture)}),text=True,capture_output=True,check=True)
        assert json.loads(result.stdout)['data']['tag']=='XFL'
        for pid in processes:subprocess.run(['kill',pid],check=True)
    finally:subprocess.run(['hdiutil','detach',str(mount)],check=True)
print('macOS DMG, code signature, associations, app launch and Unicode worker passed.')
