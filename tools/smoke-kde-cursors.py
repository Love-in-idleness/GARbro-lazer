#!/usr/bin/env python3
"""Native cursor/caption regression in a private KWin 6 virtual session.

Run under dbus-run-session, with python3-dbus, gcc, wayland-scanner,
plasma-wayland-protocols and kwin_wayland installed. Never uses a user's session.
"""
import argparse
import ctypes as C
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import tempfile
import time
import zlib

ROOT=Path(__file__).resolve().parents[1]

class XImage(C.Structure):
    _fields_=[('width',C.c_int),('height',C.c_int),('xoffset',C.c_int),('format',C.c_int),('data',C.c_void_p),
              ('byte_order',C.c_int),('bitmap_unit',C.c_int),('bitmap_bit_order',C.c_int),('bitmap_pad',C.c_int),
              ('depth',C.c_int),('bytes_per_line',C.c_int),('bits_per_pixel',C.c_int)]

class XCursor(C.Structure):
    _fields_=[('x',C.c_short),('y',C.c_short),('width',C.c_ushort),('height',C.c_ushort),
              ('xhot',C.c_ushort),('yhot',C.c_ushort),('serial',C.c_ulong),('pixels',C.POINTER(C.c_ulong))]

def wait(test,process,description,seconds=15):
    deadline=time.monotonic()+seconds
    while not test():
        if process.poll()is not None or time.monotonic()>deadline:raise RuntimeError(description)
        time.sleep(.025)

def stop(process):
    if process is None or process.poll()is not None:return
    process.terminate()
    try:process.wait(timeout=5)
    except subprocess.TimeoutExpired:process.kill();process.wait(timeout=5)

def png(path,width,height,stride,data):
    def chunk(kind,payload):return struct.pack('!I',len(payload))+kind+payload+struct.pack('!I',zlib.crc32(kind+payload))
    rows=[]
    for y in range(height):
        row=bytearray(data[y*stride:y*stride+width*4]);row[0::4],row[2::4]=row[2::4],row[0::4]
        rows.append(b'\0'+row)
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('!2I5B',width,height,8,6,0,0,0))+
                    chunk(b'IDAT',zlib.compress(b''.join(rows)))+chunk(b'IEND',b''))

def main():
    import dbus
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet',default='dotnet')
    parser.add_argument('--probe',type=Path,default=ROOT/'tests/GARbro.Desktop.NativeSmoke/bin/Release/net10.0/GARbro.Desktop.NativeSmoke.dll')
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--expect-fallback',action='store_true',help='Require protocol absence, e.g. on KWin 5.27')
    parser.add_argument('--cursor-size',default='8',help='Client fallback size; ignored by compositor-owned shapes')
    args=parser.parse_args();output=args.output.resolve();output.mkdir(parents=True,exist_ok=True)
    bus=dbus.SessionBus()
    if bus.name_has_owner('org.kde.KWin'):
        raise RuntimeError('An existing KWin session is on this bus. Run inside a private dbus-run-session.')
    compositor=pointer=app=xserver=None
    with tempfile.TemporaryDirectory(prefix='garbro-kwin-')as temporary:
        run=Path(temporary);runtime=run/'runtime';runtime.mkdir(mode=0o700);config=run/'config';config.mkdir()
        settings=config/'kcminputrc'
        def theme(name,size):
            settings.write_text(f'[Mouse]\ncursorTheme={name}\ncursorSize={size}\n')
            if compositor:
                dbus.Interface(bus.get_object('org.kde.KWin','/KWin'),'org.kde.KWin').reconfigure()
                message=dbus.lowlevel.SignalMessage('/KGlobalSettings','org.kde.KGlobalSettings','notifyChange')
                message.append(dbus.Int32(5),dbus.Int32(0));bus.send_message(message);time.sleep(.3)
        theme('breeze_cursors',24)
        env=os.environ.copy();env.update(XDG_RUNTIME_DIR=str(runtime),XDG_CONFIG_HOME=str(config),XDG_SESSION_TYPE='wayland',
            XDG_CURRENT_DESKTOP='KDE',LIBGL_ALWAYS_SOFTWARE='1',QT_QUICK_BACKEND='software',KWIN_SCREENSHOT_NO_PERMISSION_CHECKS='1',
            KWIN_WAYLAND_NO_PERMISSION_CHECKS='1',
            GARBRO_LINUX_BACKEND='wayland')
        for key in ('DISPLAY','WAYLAND_DISPLAY','WAYLAND_SOCKET'):env.pop(key,None)
        for mode,suffix in [('client-header','.h'),('private-code','.c')]:
            subprocess.run(['wayland-scanner',mode,'/usr/share/plasma-wayland-protocols/fake-input.xml',str(run/('fake-input'+suffix))],check=True)
        subprocess.run(['cc','-std=c11','-Wall','-Wextra','-Werror','-I',str(run),str(ROOT/'tests/wayland/kde-pointer.c'),
                        str(run/'fake-input.c'),'-lwayland-client','-lm','-o',str(run/'pointer')],check=True)
        try:
            with (output/'kwin.log').open('w')as kwinlog,(output/'application.log').open('w')as applog:
                # A nested llvmpipe output enables KWin's actual screenshot
                # effect without a hardware DRM node. Clients remain Wayland.
                readfd,writefd=os.pipe()
                xserver=subprocess.Popen(['Xvfb','-displayfd',str(writefd),'-screen','0','1280x900x24','-nolisten','tcp'],pass_fds=(writefd,),stdout=kwinlog,stderr=subprocess.STDOUT)
                os.close(writefd)
                with os.fdopen(readfd)as display:env['DISPLAY']=':'+display.readline().strip()
                compositor=subprocess.Popen(['kwin_wayland','--x11-display',env['DISPLAY'],'--width','1280','--height','900','--no-lockscreen',
                    '--no-global-shortcuts','--no-kactivities'],env=env,stdout=kwinlog,stderr=subprocess.STDOUT)
                wait(lambda:list(runtime.glob('wayland-*.lock')),compositor,'KWin did not start.')
                env['WAYLAND_DISPLAY']=next(runtime.glob('wayland-*.lock')).name.removesuffix('.lock')
                wait(lambda:bus.name_has_owner('org.kde.KWin'),compositor,'KWin DBus missing.')
                owner=bus.call_blocking('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus',
                    'GetConnectionUnixProcessID','s',('org.kde.KWin',))
                if int(owner)!=compositor.pid:raise RuntimeError('The test does not own this KWin session.')
                pointer=subprocess.Popen([str(run/'pointer')],env=env,text=True,stdin=subprocess.PIPE,stdout=subprocess.PIPE)
                if pointer.stdout.readline().strip()!='ready':raise RuntimeError('KWin test pointer unavailable.')
                def move(point):
                    pointer.stdin.write(f'move {point["x"]} {point["y"]}\n');pointer.stdin.flush()
                    assert pointer.stdout.readline().strip()=='ok';time.sleep(.15)
                def button(state):
                    pointer.stdin.write(f'button {state}\n');pointer.stdin.flush();assert pointer.stdout.readline().strip()=='ok';time.sleep(.1)
                client=env.copy();client.pop('DISPLAY',None);client.update(WAYLAND_DEBUG='client',XCURSOR_THEME='Breeze_Light',XCURSOR_SIZE=args.cursor_size)
                reply=output/'cursor-reply.json';reply.unlink(missing_ok=True)
                (output/'cursor-command.json').unlink(missing_ok=True)
                app=subprocess.Popen([args.dotnet,str(args.probe),str(ROOT/'tests/fixtures/generated/standard.xfl'),str(output),'--wayland-cursor'],
                    env=client,stdout=applog,stderr=subprocess.STDOUT)
                wait(reply.exists,app,'Cursor probe did not start.');initial=json.loads(reply.read_text());targets=initial['targets'];sequence=0
                def command(mode,**values):
                    nonlocal sequence,targets
                    sequence+=1;pending=output/'cursor-command.pending'
                    pending.write_text(json.dumps(dict(sequence=sequence,mode=mode,**values)));pending.replace(output/'cursor-command.json')
                    wait(lambda:json.loads(reply.read_text())['sequence']==sequence,app,'Probe command failed: '+mode)
                    answer=json.loads(reply.read_text());targets=answer.get('targets',targets);return answer
                # KWin 6 needs a DRM render node for its OpenGL screenshot
                # effect. A QPainter-only CI host still exposes the actual
                # nested output and its native X cursor through X11/XFixes.
                screenshot=(dbus.Interface(bus.get_object('org.kde.KWin.ScreenShot2','/org/kde/KWin/ScreenShot2'),'org.kde.KWin.ScreenShot2')
                            if bus.name_has_owner('org.kde.KWin.ScreenShot2')else None)
                x11=C.CDLL('libX11.so.6');fixes=C.CDLL('libXfixes.so.3')
                x11.XOpenDisplay.argtypes=[C.c_char_p];x11.XOpenDisplay.restype=C.c_void_p
                display=x11.XOpenDisplay(env['DISPLAY'].encode());assert display
                x11.XDefaultRootWindow.argtypes=[C.c_void_p];x11.XDefaultRootWindow.restype=C.c_ulong
                root=x11.XDefaultRootWindow(display)
                x11.XGetImage.argtypes=[C.c_void_p,C.c_ulong,C.c_int,C.c_int,C.c_uint,C.c_uint,C.c_ulong,C.c_int];x11.XGetImage.restype=C.POINTER(XImage)
                x11.XDestroyImage.argtypes=[C.POINTER(XImage)];x11.XFree.argtypes=[C.c_void_p]
                fixes.XFixesGetCursorImage.argtypes=[C.c_void_p];fixes.XFixesGetCursorImage.restype=C.POINTER(XCursor)
                def capture(name,cursor=False):
                    nonlocal screenshot
                    if screenshot is None:
                        if cursor:
                            image=fixes.XFixesGetCursorImage(display);info=image.contents;width,height=info.width,info.height;stride=width*4
                            data=b''.join(int(info.pixels[i]&0xffffffff).to_bytes(4,'little')for i in range(width*height));x11.XFree(image)
                        else:
                            image=x11.XGetImage(display,root,0,0,1280,900,C.c_ulong(-1).value,2);assert image
                            info=image.contents;width,height,stride=info.width,info.height,info.bytes_per_line
                            data=bytearray(C.string_at(info.data,stride*height));data[3::4]=b'\xff'*(len(data)//4);x11.XDestroyImage(image)
                        png(output/(name+'.png'),width,height,stride,data);return data,(width,height,stride)
                    with tempfile.TemporaryFile()as raw:
                        try:
                            metadata=screenshot.CaptureWorkspace(dbus.Dictionary({'include-cursor':dbus.Boolean(cursor)},signature='sv'),dbus.types.UnixFd(raw.fileno()))
                        except dbus.DBusException as error:
                            # Newer KWin advertises ScreenShot2 even when its
                            # QPainter backend cannot fulfill captures.
                            if error.get_dbus_name()!='org.kde.KWin.ScreenShot2.Error.Cancelled':raise
                            screenshot=None
                            return capture(name,cursor)
                        width,height,stride=(int(metadata[k])for k in ('width','height','stride'));size=stride*height
                        deadline=time.monotonic()+5
                        while os.fstat(raw.fileno()).st_size<size:
                            if time.monotonic()>deadline:raise RuntimeError('Screenshot data incomplete.')
                            time.sleep(.02)
                        raw.seek(0);data=raw.read(size)
                    png(output/(name+'.png'),width,height,stride,data);return data,(width,height,stride)
                for appearance,language in [('dark','zh-CN'),('light','en-US')]:
                    command('theme',theme=appearance,language=language);move(targets['blank']);capture(appearance+'-normal')
                    for name,point in targets['captions'].items():
                        move(point);capture(appearance+'-'+name+'-hover');button(1);capture(appearance+'-'+name+'-pressed');move(targets['blank']);button(0)
                command('theme',theme='dark',language='zh-CN');command('cursor',cursor='Arrow');move(targets['blank'])
                samples=[]
                for name,size in [('breeze_cursors',24),('breeze_cursors',48),('Breeze_Light',48)]:
                    theme(name,size);move(targets['text']);move(targets['blank']);background,_=capture(f'{name}-{size}-no-cursor')
                    data,shape=capture(f'{name}-{size}-cursor',True)
                    width,height,stride=shape
                    pixels=[(x,y)for y in range(height)for x in range(width)
                            if (data[y*stride+x*4:y*stride+x*4+4]!=background[y*stride+x*4:y*stride+x*4+4]
                                if screenshot else data[y*stride+x*4+3]!=0)]
                    bounds=[max(p[axis]for p in pixels)-min(p[axis]for p in pixels)+1 for axis in (0,1)]
                    samples.append(dict(theme=name,size=size,shape=shape,visibleSize=bounds,sha256=hashlib.sha256(data).hexdigest()))
                for cursor in ('Ibeam','Hand','Wait','AppStarting','SizeWestEast','SizeNorthSouth','DragCopy','DragMove','DragLink','None','Arrow'):
                    command('cursor',cursor=cursor);move(targets['blank']);capture('cursor-'+cursor,True)
                command('cursor',cursor='Arrow');command('popup');move(targets['popup']);capture('popup',True);command('close-popup')
                command('settings');move(targets['text']);capture('settings-text',True);command('close-dialog')
                command('stop');assert app.wait(timeout=10)==0
            protocol=(output/'application.log').read_text()
            if args.expect_fallback:
                assert 'wp_cursor_shape_manager_v1'not in protocol,'Fallback test requires protocol absence.'
                assert re.search(r'wl_pointer[#@]\d+\.set_cursor\(\d+, wl_surface[#@]',protocol),'System library fallback was not used.'
            else:
                assert 'wp_cursor_shape_device_v1' in protocol and '.set_shape(' in protocol
                assert not re.search(r'wl_pointer[#@]\d+\.set_cursor\(\d+, wl_surface[#@]',protocol),'Client cursor image used despite cursor-shape support.'
                assert samples[0]['visibleSize']!=samples[1]['visibleSize'],'System cursor size did not change.'
                assert samples[1]['sha256']!=samples[2]['sha256'],'System cursor theme did not change.'
            (output/'verification.json').write_text(json.dumps(dict(initial=initial,samples=samples,standardCursorProtocol=not args.expect_fallback,
                fallbackSize=args.cursor_size if args.expect_fallback else None,
                capture='KWin ScreenShot2'if screenshot else 'Nested KWin output / native XFixes cursor snapshots'),indent=2)+'\n')
            print('KWin system cursor protocol and shared caption rendering passed.')
        finally:stop(app);stop(pointer);stop(compositor);stop(xserver)

if __name__=='__main__':main()
