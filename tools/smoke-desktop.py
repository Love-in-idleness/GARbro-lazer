#!/usr/bin/env python3
"""Exercise the native Avalonia window; Linux requires a display or xvfb-run."""
import argparse
import os
import subprocess
import time
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet',default='dotnet')
parser.add_argument('--output',type=Path,default=ROOT/'artifacts/native-desktop')
parser.add_argument('--window-manager',help='Start a window manager inside an owned Xvfb display for real caption action tests')
args=parser.parse_args()
manager=None
manager_log=None
try:
    if args.window_manager:
        args.output.mkdir(parents=True,exist_ok=True)
        manager_log=(args.output/'window-manager.log').open('w')
        manager=subprocess.Popen([args.window_manager,'--sm-disable'],stdout=manager_log,stderr=subprocess.STDOUT)
        time.sleep(.5)
        if manager.poll()is not None:raise RuntimeError('The test window manager did not start: '+(args.output/'window-manager.log').read_text())
    subprocess.run([args.dotnet,str(ROOT/'tests/GARbro.Desktop.NativeSmoke/bin/Release/net10.0/GARbro.Desktop.NativeSmoke.dll'),
                    str(ROOT/'tests/fixtures/generated/standard.xfl'),str(args.output.resolve())],check=True,timeout=90 if os.environ.get("GARBRO_LOCALIZATION_SNAPSHOTS")=="1" else 45)
finally:
    if manager is not None:
        manager.terminate();manager.wait(timeout=5)
    if manager_log is not None:manager_log.close()
