#!/usr/bin/env python3
"""Verify the installed Flatpak maps a real window; run under xvfb-run in CI."""
import subprocess
import time
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
APP='io.github.love_in_idleness.GARbro-no-wpf'
log=ROOT/'artifacts/flatpak-gui-startup.log'
log.parent.mkdir(parents=True,exist_ok=True)
with log.open('w') as output:
    process=subprocess.Popen(['flatpak','run','--socket=x11','--nosocket=wayland',
        '--env=GARBRO_LINUX_BACKEND=x11',APP],stdout=output,stderr=subprocess.STDOUT)
    try:
        deadline=time.monotonic()+30
        while time.monotonic()<deadline:
            if process.poll() is not None:
                raise RuntimeError('Flatpak desktop exited before showing a window: '+log.read_text())
            windows=subprocess.check_output(['xwininfo','-root','-tree'],text=True)
            visible=False
            for line in windows.splitlines():
                if '"GARbro"' in line:
                    details=subprocess.check_output(['xwininfo','-id',line.split()[0]],text=True)
                    visible='Map State: IsViewable' in details
                    if visible:break
            if visible:
                print('Installed Flatpak desktop window is visible.')
                break
            time.sleep(0.25)
        else:
            raise TimeoutError('Flatpak desktop did not show a window within 30 seconds: '+log.read_text())
    finally:
        subprocess.run(['flatpak','kill',APP],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,check=False)
        try:process.wait(timeout=10)
        except subprocess.TimeoutExpired:process.kill();process.wait()
