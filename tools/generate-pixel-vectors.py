#!/usr/bin/env python3
"""Independent portable pixel vectors, cross-checked with WPF in Windows CI."""
import base64
from bisect import bisect_right
import json
import struct
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
gray16_bounds=json.loads((ROOT/'tests/fixtures/generated/gray16-quantization.json').read_text())['lowerBounds']
cases=[]
palette=[(0,0,0,255),(255,0,0,80),(0,255,0,255),(0,0,255,255)]
def add(name,kind,data,width,height=1,stride=None,colors=None):
 bits={'Bgr24':24,'Rgb24':24,'Bgr565':16,'Bgr555':16,'Gray16':16,'Gray8':8,'Gray4':4,'BlackWhite':1,'Indexed1':1,'Indexed2':2,'Indexed4':4,'Indexed8':8}.get(kind,32)
 stride=stride or (width*bits+7)//8;output=[]
 for y in range(height):
  for x in range(width):
   p=y*stride+x*bits//8;r=g=b=0;a=255
   if kind in ('Bgra32','Bgr32','Pbgra32'):
    b,g,r,a=data[p:p+4]
    if kind=='Bgr32':a=255
    if kind=='Pbgra32':b,g,r=[min(255,v*255//a)if a else 0 for v in (b,g,r)]
   elif kind=='Bgr24':b,g,r=data[p:p+3]
   elif kind=='Rgb24':r,g,b=data[p:p+3]
   elif kind in ('Bgr565','Bgr555'):
    v=int.from_bytes(data[p:p+2],'little');six=kind=='Bgr565';maximum=63 if six else 31
    b=((v&31)<<3)|((v&31)>>2);gv=(v>>5)&maximum;g=((gv<<2)|(gv>>4))if six else((gv<<3)|(gv>>2));rv=(v>>(11 if six else 10))&31;r=(rv<<3)|(rv>>2)
   elif kind=='Gray16':b=g=r=bisect_right(gray16_bounds,int.from_bytes(data[p:p+2],'little'))
   elif kind=='Gray8':b=g=r=data[p]
   else:
    value=(data[p]>>(8-bits-x*bits%8))&((1<<bits)-1)
    if kind in ('BlackWhite','Gray4'):b=g=r=value*255//((1<<bits)-1)
    else:r,g,b,a=colors[value]
   output.extend((b,g,r,a))
 cases.append({'name':name,'format':kind,'width':width,'height':height,'stride':stride,
  'pixels':base64.b64encode(data).decode(),'palette':[dict(zip(('r','g','b','a'),c))for c in colors]if colors else None,
  'bgra32':base64.b64encode(bytes(output)).decode()})
for kind in ('Bgra32','Bgr32'):
 add(kind,kind,bytes([0,0,0,0,10,20,30,80,255,255,255,255,63,127,191,128]),4)
add('premultiplied','Pbgra32',bytes([0,0,0,0,1,2,3,7,32,64,128,128,50,60,70,80]),4)
for kind in ('Bgr24','Rgb24'):add(kind,kind,bytes([0,0,0,1,2,3,127,128,129,254,255,253]),4)
for kind in ('Bgr565','Bgr555'):
 values=[((i//2)<<11)|(i<<5)|(i//2)for i in range(64)]if kind=='Bgr565'else[(i<<10)|(i<<5)|i for i in range(32)]
 add(kind,kind,b''.join(struct.pack('<H',v)for v in values),len(values))
add('Gray16','Gray16',struct.pack('<21H',0,1,127,128,129,254,255,256,257,383,384,385,511,512,513,32766,32767,32768,65407,65408,65535),21)
add('Gray8','Gray8',bytes([0,1,63,127,128,191,254,255]),8)
add('Gray4','Gray4',bytes([0x01,0x78,0xef]),6)
add('BlackWhite','BlackWhite',bytes([0x5a]),8)
add('Indexed1','Indexed1',bytes([0x5a]),8,colors=palette[:2])
add('Indexed2','Indexed2',bytes([0x1b,0xe4]),8,colors=palette)
add('Indexed4-odd-width-padding','Indexed4',bytes([0x12,0x30,0xcc,0xdd,0x32,0x10,0xee,0xff]),3,2,4,palette)
add('Indexed8','Indexed8',bytes([0,1,2,3,3,2,1,0]),8,colors=palette)
path=Path(__file__).resolve().parents[1]/'tests/fixtures/generated/pixel-vectors.json'
path.write_text(json.dumps(cases,indent=2)+'\n')
print(len(cases),'pixel vectors generated')
