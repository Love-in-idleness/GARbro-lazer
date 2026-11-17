#!/usr/bin/env python3
"""Authored Ruby Marshal / Luca fixtures, independently encoded with the wire layouts."""
import json
import struct
import zlib
from pathlib import Path

ROOT=Path(__file__).resolve().parent
SCHEMES=json.loads((ROOT.parents[2]/'SchemeTool/Data/Luca.json').read_text())

def fixnum(n):
    if not n:return b'\0'
    if 0<n<123:return bytes([n+5])
    if -124<n<0:return bytes([(n-5)&255])
    raw=n.to_bytes(4,'little',signed=True)
    while len(raw)>1 and raw[-1]==(255 if n<0 else 0):raw=raw[:-1]
    return bytes([(-len(raw) if n<0 else len(raw))&255])+raw

def ruby_string(data,utf8=False):
    value=b'"'+fixnum(len(data))+data
    return b'I'+value+fixnum(1)+b':'+fixnum(1)+b'E'+b'T' if utf8 else value

def ruby_array(items):return b'['+fixnum(len(items))+b''.join(items)

def ruby_fixture(extension):
    encoding='utf-8' if extension=='rvdata2' else 'cp932'
    source="puts '日本語'\n".encode(encoding)
    name='例'.encode(encoding)
    a=ruby_array([b'i'+fixnum(7),ruby_string(name,extension=='rvdata2'),ruby_string(zlib.compress(source))])
    b=ruby_array([b'i'+fixnum(42),ruby_string(b'empty'),ruby_string(zlib.compress(b''))])
    return b'\x04\x08'+ruby_array([a,b])

def string(value,encoding,length=False):
    data=value.encode(encoding)
    prefix=b''
    if length:
        size=(-len(data))&65535 if encoding=='utf-8' else len(data)//2
        prefix=struct.pack('<H',size)
    return prefix+data+(b'\0\0' if encoding=='utf-16-le' else b'\0')

def message(game,text):
    u16=struct.pack('<H',7)
    wide=lambda s:string(s,'utf-16-le',True)
    utf=lambda s:string(s,'utf-8',True)
    if game=='LB_EN':return u16+string(text,'utf-16-le')+string('English','utf-16-le')+b'\0'
    if game in ('SP','CartagraHD'):return u16+wide(text)+b'\0'
    if game=='ButterflysPoisonBC':return u16+wide(text)+utf('English')
    if game=='PlanetarianSG':return u16+wide(text)+utf('English')+wide('French')+wide('中文一')+wide('中文二')
    return u16+wide(text)+utf('English')+wide('中文')

def command(op,payload=b'',flags=0):
    data=struct.pack('<HBB',len(payload)+4,op,flags)+payload
    return data+(b'\0' if len(data)%2 else b'')

def luca_fixture(game,profile):
    op={name:i for i,name in enumerate(profile['Opcodes'])}
    msg=command(op['MESSAGE'],message(game,'日本語'))
    other=command(op['MESSAGE'],message(game,'台詞'))
    target=len(other)
    far=b''
    if 'FARCALL' in profile['Parameters']:
        params=struct.pack('<H',1)+string('scene1','shift_jis' if game in ('SP','LB_EN') else 'utf-8',game not in ('SP','LB_EN'))+struct.pack('<I',target)
        far=command(op['FARCALL'],params)
    jump=command(op['GOTO'],struct.pack('<I',len(msg)+len(far)+8))
    script=msg+far+jump+command(op['RETURN'])
    other+=command(op['RETURN'])
    header=bytearray(128)
    struct.pack_into('<IIII',header,0,128,2,50,16)
    struct.pack_into('<II',header,32,512,56)
    header[56:70]=b'scene0\0scene1\0'
    out=header
    for i,data in enumerate((script,other)):
        out.extend(b'\0'*((-len(out))%16))
        struct.pack_into('<II',out,40+i*8,len(out)//16,len(data))
        out.extend(data)
    return bytes(out)

def main():
    for extension in ('rxdata','rvdata','rvdata2'):(ROOT/('scripts.'+extension)).write_bytes(ruby_fixture(extension))
    # Source byte string is object #3 and is deliberately shared by both entries.
    first=ruby_array([b'i'+fixnum(1),ruby_string(b'one'),ruby_string(zlib.compress(b'puts 1\n'))])
    second=ruby_array([b'i'+fixnum(2),ruby_string(b'two'),b'@'+fixnum(3)])
    (ROOT/'scripts-shared.rvdata2').write_bytes(b'\x04\x08'+ruby_array([first,second]))
    for name,profile in SCHEMES.items():(ROOT/('luca-'+name+'.pak')).write_bytes(luca_fixture(name,profile))
    print('Generated RGSS and',len(SCHEMES),'Luca authored fixtures.')

if __name__=='__main__':main()
