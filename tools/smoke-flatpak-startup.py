#!/usr/bin/env python3
"""Check installed Flatpak startup and host Open With/document-portal activation."""
import os
import subprocess
import tempfile
import time
from pathlib import Path
from association_probe import verify_candidate

ROOT=Path(__file__).resolve().parents[1]
APP='io.github.love_in_idleness.GARbro-no-wpf'
log=ROOT/'artifacts/flatpak-gui-startup.log';log.parent.mkdir(parents=True,exist_ok=True)
exports=Path.home()/'.local/share/flatpak/exports/share'
entry=exports/'applications'/(APP+'.desktop')
assert entry.is_file(),entry
source=entry.read_text()
assert '--file-forwarding'in source and '@@ %F @@'in source,source
assert 'TryExec=garbro-desktop'not in source
os.environ['XDG_DATA_DIRS']=str(exports)+':'+os.environ.get('XDG_DATA_DIRS','/usr/local/share:/usr/share')
os.environ.setdefault('XDG_CURRENT_DESKTOP','GNOME')

def visible(title):
    windows=subprocess.check_output(['xwininfo','-root','-tree'],text=True)
    for line in windows.splitlines():
        if '"'+title+'"'in line:
            details=subprocess.check_output(['xwininfo','-id',line.split()[0]],text=True)
            if 'Map State: IsViewable'in details:return True
    return False

def wait_window(title):
    deadline=time.monotonic()+30
    while time.monotonic()<deadline:
        if visible(title):return
        time.sleep(.25)
    diagnostics=subprocess.check_output(['xwininfo','-root','-tree'],text=True)
    subprocess.run(['flatpak','ps'],check=False)
    app_log=Path.home()/'.var/app'/APP/'data/GARbro/desktop.log'
    if app_log.exists():diagnostics+='\n'+'\n'.join(app_log.read_text().splitlines()[-20:])
    raise TimeoutError('Installed Flatpak did not map '+title+': '+log.read_text()+'\n'+diagnostics)

with tempfile.TemporaryDirectory(prefix='GARbro 中文 Русский 日本語 ')as directory,log.open('w')as output:
    root=Path(directory)
    # Only this disposable test profile changes its default handler.
    os.environ['XDG_CONFIG_HOME']=str(root/'config')
    process=subprocess.Popen(['flatpak','run','--socket=x11','--nosocket=wayland',
        '--env=GARBRO_LINUX_BACKEND=x11',APP],stdout=output,stderr=subprocess.STDOUT)
    try:
        wait_window('GARbro')
        print('Installed Flatpak window is visible with host font configuration.')
        subprocess.run(['flatpak','kill',APP],check=True);process.wait(timeout=10)
        fixture=root/'资源 Русский 日本語.xfl';fixture.write_bytes((ROOT/'tests/fixtures/generated/standard.xfl').read_bytes())
        for suffix in ('dat','bin','zip'):
            sample=fixture.with_suffix('.'+suffix);sample.write_bytes(fixture.read_bytes());verify_candidate(sample,APP+'.desktop')
        mime=verify_candidate(fixture,APP+'.desktop')
        subprocess.run(['gio','mime',mime,APP+'.desktop'],check=True,stdout=output,stderr=subprocess.STDOUT)
        subprocess.run(['gio','open',str(fixture)],check=True,stdout=output,stderr=subprocess.STDOUT)
        wait_window('GARbro — '+fixture.name)
        print('Host Open With candidate, system activation and document portal file access passed.')
    finally:
        subprocess.run(['flatpak','kill',APP],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,check=False)
        if process.poll()is None:
            try:process.wait(timeout=10)
            except subprocess.TimeoutExpired:process.kill();process.wait()
