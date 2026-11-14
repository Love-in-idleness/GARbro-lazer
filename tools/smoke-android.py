#!/usr/bin/env python3
"""Install the internal APK and exercise the shared UI and isolated decoder on Android."""
import argparse
import json
import subprocess
import time
from pathlib import Path


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('apk',type=Path)
    parser.add_argument('--adb',default='adb')
    parser.add_argument('--output',type=Path,default=Path('artifacts/android-smoke'))
    parser.add_argument('--timeout',type=int,default=900)
    parser.add_argument('--architecture',choices=['X64','Arm64'],required=True)
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    package='org.garbro.garbro.test'
    def adb(*command,check=True,binary=False):
        return subprocess.run([args.adb,*command],check=check,capture_output=True,text=not binary)
    end=time.monotonic()+args.timeout
    while time.monotonic()<end:
        if adb('shell','getprop','sys.boot_completed',check=False).stdout.strip()=='1':break
        time.sleep(3)
    else:raise RuntimeError('Android did not finish booting')
    adb('install','-r',str(args.apk))
    sdk=int(adb('shell','getprop','ro.build.version.sdk').stdout.strip())
    if sdk>=33:adb('shell','pm','grant',package,'android.permission.POST_NOTIFICATIONS')
    adb('shell','am','force-stop',package)
    adb('shell','run-as',package,'rm','-f','files/test-results.json')
    adb('logcat','-c')
    adb('shell','am','start','-n',package+'/org.garbro.garbro.MainActivity','--ez','garbro.test','true')
    try:
        started=time.monotonic();end=started+args.timeout
        while time.monotonic()<end:
            reply=adb('shell','run-as',package,'cat','files/test-results.json',check=False)
            if reply.returncode==0:
                result=json.loads(reply.stdout);(args.output/'results.json').write_text(json.dumps(result,indent=2)+'\n')
                if not result['success']:raise RuntimeError(result.get('error','Android self-test failed'))
                if result['architecture']!=args.architecture:raise RuntimeError('Wrong native process architecture: '+result['architecture'])
                break
            if time.monotonic()-started>30 and adb('shell','pidof',package,check=False).returncode!=0:
                crash=adb('logcat','-b','crash','-d',check=False).stdout
                raise RuntimeError('Android app stopped before completing its tests.\n'+crash[-6000:])
            time.sleep(3)
        else:raise RuntimeError('Android self-test timed out')
        image=adb('exec-out','screencap','-p',binary=True).stdout;(args.output/'window.png').write_bytes(image)
        archives=adb('exec-out','run-as',package,'tar','-C','files/self-test/created-archives','-cf','-','.',binary=True).stdout
        (args.output/'created-archives.tar').write_bytes(archives)
        # Opening a new Activity must not lose the shared browser or settings.
        adb('shell','am','start','-n',package+'/org.garbro.garbro.MainActivity')
        print('Android native smoke passed:',args.architecture,', '.join(result['checks']))
    finally:
        logs=adb('logcat','-d',check=False).stdout;(args.output/'logcat.txt').write_text(logs)
        adb('shell','am','force-stop',package,check=False)


if __name__=='__main__':main()
