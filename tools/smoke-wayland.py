#!/usr/bin/env python3
"""Test native Wayland and text-input-v3 inside an owned headless Sway session."""
import argparse
import os
import shlex
import shutil
import stat
import subprocess
import tempfile
import time
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]


def wait_for(test,process,description,seconds=10):
    deadline=time.monotonic()+seconds
    while not test():
        if process.poll()is not None or time.monotonic()>deadline:
            raise RuntimeError(description)
        time.sleep(.025)


def stop(process):
    if process is None or process.poll()is not None:return
    process.terminate()
    try:process.wait(timeout=5)
    except subprocess.TimeoutExpired:process.kill();process.wait(timeout=5)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet',default='dotnet')
    parser.add_argument('--sway',default='sway')
    parser.add_argument('--ime-probe',type=Path,help='Use an already compiled probe instead of building it')
    parser.add_argument('--output',type=Path,default=ROOT/'artifacts/wayland-ime')
    args=parser.parse_args()
    output=args.output.resolve();output.mkdir(parents=True,exist_ok=True)
    compositor=helper=None
    with tempfile.TemporaryDirectory(prefix='garbro-wayland-')as directory:
        run=Path(directory);runtime=run/'runtime';runtime.mkdir(mode=0o700)
        probe=run/'probe';probe.mkdir()
        if args.ime_probe:
            executable=args.ime_probe.resolve()
        else:
            for source,stem in [('input-method-unstable-v2','input-method'),('virtual-keyboard-unstable-v1','virtual-keyboard')]:
                xml=ROOT/'tests/wayland/protocols'/(source+'.xml')
                for mode,suffix in [('client-header','.h'),('private-code','.c')]:
                    subprocess.run(['wayland-scanner',mode,str(xml),str(run/(stem+suffix))],check=True)
            flags=shlex.split(subprocess.check_output(['pkg-config','--cflags','--libs','wayland-client','xkbcommon'],text=True))
            executable=run/'ime-probe'
            subprocess.run(['cc','-std=c11','-Wall','-Wextra','-Werror','-I',str(run),
                str(ROOT/'tests/wayland/ime-probe.c'),str(run/'input-method.c'),str(run/'virtual-keyboard.c'),
                *flags,'-o',str(executable)],check=True)
        config=run/'sway.conf'
        config.write_text('xwayland disable\noutput * mode 1280x900\ninput * xkb_layout us\n'
                          'seat seat0 fallback true\nfocus_follows_mouse no\ndefault_border none\n')
        environment=os.environ.copy()
        environment.update(XDG_RUNTIME_DIR=str(runtime),XDG_SESSION_TYPE='wayland',
            WLR_BACKENDS='headless',WLR_RENDERER='pixman',WLR_LIBINPUT_NO_DEVICES='1',LIBGL_ALWAYS_SOFTWARE='1',
            GARBRO_LINUX_BACKEND='wayland')
        # No connection to an existing display or the user's input-method seat.
        for name in ('DISPLAY','WAYLAND_DISPLAY','WAYLAND_SOCKET','SWAYSOCK'):
            environment.pop(name,None)
        try:
            with (output/'compositor.log').open('w')as compositor_log,(output/'ime.log').open('w')as ime_log,(output/'application.log').open('w')as app_log:
                compositor=subprocess.Popen([args.sway,'-c',str(config)],env=environment,
                    stdout=compositor_log,stderr=subprocess.STDOUT)
                def sockets():return [p for p in runtime.glob('wayland-*')if stat.S_ISSOCK(p.stat().st_mode)]
                wait_for(sockets,compositor,'Headless Wayland compositor did not start.')
                environment['WAYLAND_DISPLAY']=sockets()[0].name
                helper=subprocess.Popen([str(executable),str(probe)],env=environment,stdout=ime_log,stderr=subprocess.STDOUT)
                wait_for(lambda:(probe/'ime-helper-ready').exists(),helper,'Input-method protocol probe did not start.')
                environment['WAYLAND_DEBUG']='client'
                subprocess.run([args.dotnet,str(ROOT/'tests/GARbro.Desktop.NativeSmoke/bin/Release/net10.0/GARbro.Desktop.NativeSmoke.dll'),
                    str(ROOT/'tests/fixtures/generated/standard.xfl'),str(probe),'--wayland-ime'],
                    env=environment,stdout=app_log,stderr=subprocess.STDOUT,check=True,timeout=45)
                if helper.wait(timeout=5)!=0:raise RuntimeError('Input-method protocol probe failed.')
                shutil.copy2(probe/'window.png',output/'window.png')
                shutil.copy2(probe/'window-with-titlebar.png',output/'window-with-titlebar.png')
        finally:
            stop(helper);stop(compositor)
    text=(output/'application.log').read_text()
    for event in ('zwp_text_input_v3','preedit_string','commit_string','delete_surrounding_text'):
        if event not in text:raise AssertionError('Missing Wayland protocol evidence: '+event)
    # Sway does not implement maximization, so there is no maximized state to
    # restore. Openbox and Win32 verify maximize/restore state transitions;
    # this compositor verifies the requests it actually accepts over Wayland.
    for event in ('set_maximized()','set_minimized()'):
        if event not in text:raise AssertionError('Missing Wayland caption action request: '+event)
    print('Native Wayland text-input-v3 passed: preedit, CJK commit, UTF-8 deletion and focus transfer.')
    print('Shared Wayland caption buttons and maximize/minimize protocol requests passed.')


if __name__=='__main__':main()
