#!/usr/bin/env python3
"""Deterministic synthetic format fixtures; no commercial-game data or keys."""
from pathlib import Path
import struct as s
import hashlib
import json
import zlib
import wave
import io
import math
import zipfile
import subprocess
import sys
from fixture_nrbf import gx4_index, unity_index
from fixture_pe import executable
from fixture_experimental import rgss, iris, hibiki
from fixture_cabinet import cabinet
from fixture_igs import igs

ROOT=Path(__file__).resolve().parents[1]/'tests/fixtures/generated'
ROOT.mkdir(parents=True,exist_ok=True)
FILES=[('hello.txt',b'Hello, GARbro!\n'),('sub/日本語.bin',bytes(range(32))),('empty.txt',b'')]
manifest=[]
portable_manifest=[]
def put(name,tag,data,files,variant,portable=False):
 (ROOT/name).parent.mkdir(parents=True,exist_ok=True)
 (ROOT/name).write_bytes(data)
 (portable_manifest if portable else manifest).append({'file':name,'format':tag,'variant':variant,'archive_sha256':hashlib.sha256(data).hexdigest(),
  'entries':[{'path':n,'size':len(d),'sha256':hashlib.sha256(d).hexdigest()}for n,d in files]})
def chunk(tag,data):return tag+s.pack('<Q',len(data))+data

def xfl(files):
 index=b'';body=b''
 for name,data in files:
  index+=name.encode('cp932').ljust(32,b'\0')+s.pack('<II',len(body),len(data));body+=data
 return s.pack('<III',0x1424c,len(index),len(files))+index+body
put('standard.xfl','XFL',xfl(FILES),FILES,'raw, nested paths, CP932, empty entry')
put('unsafe.xfl','XFL',xfl([('../escape.txt',b'no')]),[('../escape.txt',b'no')],'path traversal; application must reject')
put('collision.xfl','XFL',xfl([('A.txt',b'A'),('a.txt',b'a')]),[('A.txt',b'A'),('a.txt',b'a')],'case collision; application must reject')
for nsa in [False,True]:
 index=b'';body=b''
 for name,data in FILES:
  index+=name.encode('cp932')+b'\0'+(b'\0'if nsa else b'')+s.pack('>II',len(body),len(data))+(s.pack('>I',len(data))if nsa else b'');body+=data
 put('standard.nsa'if nsa else 'standard.sar','NSA'if nsa else 'SAR',s.pack('>HI',len(FILES),6+len(index))+index+body,FILES,'uncompressed entries')
for version in [6,8]:
 size=4+sum(4+len(n.encode('utf-8'))+12 for n,d in FILES);index=s.pack('<I',len(FILES));body=b''
 for name,data in FILES:
  raw=name.encode('utf-8');index+=s.pack('<I',len(raw))+raw+s.pack('<III',0,7+size+len(body),len(data));body+=data
 if version==8:
  key=hashlib.sha1(index).digest();body=b''
  for name,data in FILES:body+=bytes(v^key[i%len(key)]for i,v in enumerate(data))
 put(f'pf{version}.pfs','PFS',f'pf{version}'.encode()+s.pack('<I',len(index))+index+body,FILES,'pf8 SHA-1 XOR'if version==8 else 'pf6 raw')
for compressed in [False,True]:
 header=b'XP3\r\n \n\x1a\x8b\x67\x01';body=b'';index=b''
 for name,data in FILES:
  packed=zlib.compress(data)if compressed and data else data
  info=s.pack('<IQQH',0,len(data),len(packed),len(name))+name.encode('utf-16-le')
  segment=s.pack('<IQQQ',int(compressed and bool(data)),19+len(body),len(data),len(packed))
  index+=chunk(b'File',chunk(b'info',info)+chunk(b'segm',segment)+chunk(b'adlr',s.pack('<I',zlib.adler32(data))))
  body+=packed
 index_offset=19+len(body)
 if compressed:
  packed=zlib.compress(index);index=b'\1'+s.pack('<QQ',len(packed),len(index))+packed
 else:index=b'\0'+s.pack('<Q',len(index))+index
 put('compressed.xp3'if compressed else 'standard.xp3','XP3',header+s.pack('<Q',index_offset)+body+index,FILES,'zlib entries and index'if compressed else 'uncompressed, segmented')
# RPA-3.0's protocol-2 pickle uses BINSTRING, not executable GLOBAL opcodes.
key=0x12345678;body=b'';index=b'\x80\x02}('
for name,data in FILES:
 raw=name.encode();index+=b'T'+s.pack('<I',len(raw))+raw+b']J'+s.pack('<I',(34+len(body))^key)+b'J'+s.pack('<I',len(data)^key)+b'\x86a';body+=data
index+=b'u.';header=f'RPA-3.0 {34+len(body):016x} {key:08x}\n'.encode()
put('standard.rpa','RPA',header+body+zlib.compress(index),FILES,'RPA-3, safe pickle protocol 2')
# NPA names are obfuscated even when the data encryption flag is clear.
size=sum(len(n.encode('cp932'))+21 for n,d in FILES);index=b'';body=b''
for number,(name,data)in enumerate(FILES):
 raw=name.encode('cp932');encoded=bytes((v-(252*i-6-number))&255 for i,v in enumerate(raw))
 index+=s.pack('<I',len(raw))+encoded+b'\2'+s.pack('<IIII',0,len(body),len(data),len(data));body+=data
header=b'NPA\1\0\0\0'+s.pack('<iiBBiii',2,3,0,0,len(FILES),0,len(FILES))+b'\0'*8+s.pack('<I',size)
put('standard.npa','NPA',header+index+body,FILES,'uncompressed, obfuscated CP932 names')
# YPF 0x100 uses the documented pair-swap table and an inferred name XOR key.
swap=bytes([3,0x48,6,0x35,0x0c,0x10,0x11,0x19,0x1c,0x1e,9,11,13,19,21,27,32,35,38,41,44,47,46,50])
def swaplen(n):return swap[swap.index(n)^1]if n in swap else n
size=sum(23+len(n.encode('cp932'))for n,d in FILES);index=b'';body=b''
for name,data in FILES:
 raw=name.encode('cp932');packed=zlib.compress(data)if data else data
 index+=s.pack('<IB',zlib.crc32(raw),swaplen(len(raw))^255)+bytes(v^0x42 for v in raw)+s.pack('<BBIIII',0,int(bool(data)),len(data),len(packed),32+size+len(body),zlib.crc32(data));body+=packed
put('standard.ypf','YPF',s.pack('<IIII',0x465059,0x100,len(FILES),size)+b'\0'*16+index+body,FILES,'YPF 0x100, guessed key, zlib content')
# QLIE v2: XOR-obfuscated names, unencrypted payloads, with hash index layout.
body=b''.join(d for n,d in FILES);index=b'';offset=0
for name,data in FILES:
 raw=name.encode('cp932');key=len(raw)+(0xc4^0x3e);encoded=bytes(v^((((i+1)^key)+i+1)&255)for i,v in enumerate(raw))
 index+=s.pack('<H',len(raw))+encoded+s.pack('<qIIiiI',offset,len(data),len(data),0,0,0);offset+=len(data)
footer=b'FilePackVer2.0'.ljust(16,b'\0')+s.pack('<iq',len(FILES),len(body))
put('standard.pack','PACK/QLIE',body+index+footer,FILES,'QLIE v2 raw payloads, name obfuscation')
# LWG entry names acquire the .wcg extension from the archive reader.
files=[('first.wcg',b'synthetic WCG payload'),('second.wcg',b'another payload')];index=b'';body=b''
for name,data in files:
 raw=name[:-4].encode('cp932');index+=s.pack('<iiBII',-3,4,32,len(body),len(data))+s.pack('B',len(raw))+raw;body+=data
put('standard.lwg','LWG',s.pack('<IIIIII',0x1474c,1,1,len(files),0,len(index))+index+s.pack('<I',len(body))+body,files,'raw extraction only; payloads are not encoded images')
# ZIP uses the standard library as an independent writer.
for method,label in [(zipfile.ZIP_STORED,'stored'),(zipfile.ZIP_DEFLATED,'deflate')]:
 data=io.BytesIO()
 with zipfile.ZipFile(data,'w')as archive:
  for name,content in FILES:
   entry=zipfile.ZipInfo(name,date_time=(2020,1,1,0,0,0));entry.compress_type=method
   archive.writestr(entry,content)
 put('zip-'+label+'.zip','ZIP',data.getvalue(),FILES,label+', UTF-8 names, empty entry')
# Majiro versions differ in hash width and whether sizes are explicit.
for version in (1,2,3):
 names=b''.join(name.encode('cp932')+b'\0'for name,data in FILES)
 entry_size=4*(version+1);names_offset=28+(len(FILES)+(version==1))*entry_size
 data_offset=names_offset+len(names);table=b'';body=b''
 for name,data in FILES:
  offset=data_offset+len(body)
  table+=s.pack('<II',0,offset)if version==1 else s.pack('<III',0,offset,len(data))if version==2 else s.pack('<QII',0,offset,len(data))
  body+=data
 if version==1:table+=s.pack('<II',0,data_offset+len(body))
 header=f'MajiroArcV{version}.000\0'.encode()+s.pack('<III',len(FILES),names_offset,data_offset)
 put(f'majiro-v{version}.arc','MAJIRO',header+table+names+body,FILES,f'Majiro v{version}, CP932 names, clear payloads')
# Historical plugin fixtures use independently encoded layouts and literal-only LZSS.
def lzss_literals(data):
 return b''.join(bytes([(1<<len(data[i:i+8]))-1])+data[i:i+8]for i in range(0,len(data),8))
short_files=[('hello.txt',FILES[0][1]),('日本語.bin',FILES[1][1]),('empty.txt',b'')]
for packed in (False,True):
 body=b'';index=b''
 for name,data in short_files:
  payload=b'LZ'+b'\0'*4+s.pack('<I',len(data))+lzss_literals(data)if packed and data else data
  index+=name.encode('cp932').ljust(12,b'\0')+s.pack('<I',len(payload));body+=payload
 header=b'DL1.0\x1a\0\0'+s.pack('<HIH',len(short_files),16+len(body),0)
 put('legacy-'+('lzss'if packed else'raw')+'.dl1','DL1',header+body+index,short_files,'LZSS literal entries'if packed else'raw, CP932 names, empty entry')
index=b'';body=b'';offset=4+len(FILES)*48
for name,data in FILES:
 index+=name.encode('cp932').ljust(32,b'\0')+s.pack('<IIII',0,len(data),0,offset+len(body));body+=data
put('legacy-alpha.pak','PAK/ALPHA',s.pack('<I',len(FILES))+index+body,FILES,'raw, nested paths, CP932, empty entry')
for packed in (False,True):
 index=b'';body=b'';offset=12+len(FILES)*268
 for name,data in FILES:
  payload=lzss_literals(data)if packed else data
  index+=name.encode('cp932').ljust(256,b'\0')+s.pack('<III',offset+len(body),len(data),len(payload));body+=payload
 put('legacy-asura-'+('lzss'if packed else'raw')+'.dat','PAK/ASURA',b'AsuraPak'+s.pack('<I',len(FILES))+index+body,FILES,'LZSS literal entries'if packed else'raw, nested paths, empty entry')
(ROOT/'legacy555.mbp').write_bytes(s.pack('<IIHH',2,1,0x7c00,0x03e0))
cbf=bytearray(0x24);cbf[:6]=b'MA-CBF';s.pack_into('<II',cbf,0x10,2,2)
(ROOT/'legacy555.cbf').write_bytes(cbf+s.pack('<HHHH',0x7c00,0x03e0,0x001f,0x7fff))
(ROOT/'literal.drg').write_bytes(s.pack('<IHHB',0x4c4c5546^0xffffffff,2,1,7)+bytes([0,0,255,0,255,0,0,0]))
agf=bytearray(0x80);agf[:4]=b'AGF\0';s.pack_into('<I',agf,4,1);s.pack_into('<I',agf,0xC,0x80);s.pack_into('<II',agf,0x1C,2,1)
(ROOT/'literal.agf').write_bytes(agf+s.pack('<I',0x201)+bytes([0,0,255,128,0,255,0,255]))
def png_chunk(tag,data):return s.pack('>I',len(data))+tag+data+s.pack('>I',zlib.crc32(tag+data))
for label,color_type,payload,extra in [
 ('rgb',2,b'\xff\0\0\0\xff\0',b''),
 ('rgba',6,b'\xff\0\0\x80\0\xff\0\xff',b''),
 ('indexed',3,b'\0\1',png_chunk(b'PLTE',b'\xff\0\0\0\xff\0')+png_chunk(b'tRNS',b'\x80\xff'))]:
 png=b'\x89PNG\r\n\x1a\n'+png_chunk(b'IHDR',s.pack('>IIBBBBB',2,1,8,color_type,0,0,0))+extra+png_chunk(b'IDAT',zlib.compress(b'\0'+payload))+png_chunk(b'IEND',b'')
 (ROOT/('colors-'+label+'.png')).write_bytes(png)
if '--webp'in sys.argv:
 subprocess.run(['ffmpeg','-hide_banner','-loglevel','error','-y','-i',str(ROOT/'colors-rgba.png'),
  '-frames:v','1','-c:v','libwebp','-lossless','1',str(ROOT/'slice.webp')],check=True)
if (ROOT/'slice.webp').exists():
 webp=(ROOT/'slice.webp').read_bytes();encoded=bytearray(webp)
 for i in range(16):encoded[i]=(encoded[i]+0x77*i)&255
 (ROOT/'neko-strips.png').write_bytes(s.pack('<16i',*[64+len(webp)*i for i in range(8)],*[len(webp)]*8)+encoded*8)
bbm_pixels=bytes([0,0,255,0,255,0,255,0,0,255,255,255])*4
bbm=b'BM'+s.pack('<IHHI',54+len(bbm_pixels),0,0,54)+s.pack('<IiiHHIIiiII',40,4,4,1,24,0,len(bbm_pixels),0,0,0,0)+bbm_pixels
(ROOT/'obfuscated.bbm').write_bytes(bytes(v^255 for v in bbm[:100])+bbm[100:])
pms=bytearray(0x30);pms[:4]=b'PM\1\0';pms[6]=16
s.pack_into('<iiIIII',pms,0x10,-3,4,2,1,0x30,0x34)
(ROOT/'alpha.pms').write_bytes(pms+s.pack('<HH',0xf800,0x07e0)+bytes([128,0xf8,255]))
tile=(ROOT/'colors-rgba.png').read_bytes()
dpng=s.pack('<4sIIII',b'DPNG',0,1,4,2)+s.pack('<iiIIIQ',1,1,2,1,len(tile),0)+tile
(ROOT/'tiles.dpng').write_bytes(dpng)
ihdr=s.pack('>IIBBBBB',2,1,8,6,0,0,0)
idat=zlib.compress(bytes([0,0,0,255,128,0,255,0,255]))
(ROOT/'alpha.cwp').write_bytes(b'CWDP'+ihdr+s.pack('>I',zlib.crc32(b'IHDR'+ihdr))
 +s.pack('>I',len(idat))+idat+s.pack('>I',zlib.crc32(b'IDAT'+idat))+b'\0')
hzc=bytearray(0x2c);hzc[:4]=b'hzc1';hzc[0xC:0x10]=b'NVSG'
s.pack_into('<II',hzc,4,12,0x20);s.pack_into('<HHHhh',hzc,0x12,0,2,1,-2,3);s.pack_into('<I',hzc,0x20,2)
frames=[('two-frames#000',bytes([0,0,255,0,255,0])),('two-frames#001',bytes([255,0,0,255,255,255]))]
put('two-frames.hzc','HZC/MULTI',hzc+zlib.compress(b''.join(data for name,data in frames)),frames,'two zlib-compressed BGR frames')
index=gx4_index(FILES[:2])
put('serialized.gx4','DAT/GX4LIB',s.pack('<I',len(index))+index+b''.join(data for name,data in FILES[:2]),FILES[:2],'NRBF generic header records, clear entries')
put('serializedDAT.bytes','BYTES/UNITY',b''.join(data for name,data in FILES),FILES,'NRBF companion list index with unused array capacity')
(ROOT/'serializedINF.bytes').write_bytes(unity_index(FILES))
(ROOT/'gx4-wrong-type.nrbf').write_bytes(gx4_index(FILES[:2],root_name='System.IO.FileInfo'))
(ROOT/'gx4-large-array.nrbf').write_bytes(gx4_index([],array_count=0x40000))
(ROOT/'resources32.exe').write_bytes(executable())
(ROOT/'resources64.exe').write_bytes(executable(True))
for version in (1, 3):
 put(f'rgss-v{version}.rgssad','RGSSAD',rgss(FILES,version),FILES,f'v{version}, UTF-8 names, encrypted entries')
put('android-iris.dat','DAT/IRIS',iris(FILES),[('assets/'+n,d)for n,d in FILES],'UTF-16 names and padded payloads')
for packed in (False,True):
 cab_files=[FILES[0],('data.bin',bytes(range(256))*256 if packed else FILES[1][1]),FILES[2]]
 put('cab-'+('mszip'if packed else'stored')+'.cab','CAB',cabinet(cab_files,packed),cab_files,
  'MSZIP with dictionary across three blocks, checksums and reserved fields'if packed else'stored, checksums and reserved fields')
put('cab-utf8.cab','CAB',cabinet(FILES),FILES,'Nested UTF-8 names; portable-only because the legacy wrapper drops directories',portable=True)
if '--crypto'in sys.argv or not (ROOT/'side-b.dat').exists():
 from fixture_sideb import sideb
 sideb_data=sideb(FILES)
else:
 sideb_data=(ROOT/'side-b.dat').read_bytes()
put('side-b.dat','DAT/SIDEB',sideb_data,FILES,'Rijndael 256-bit blocks, CBC zero padding and zlib')
(ROOT/'hibiki-init.dat').write_bytes(hibiki(FILES,bytes(range(16))))
(ROOT/'hibiki-scheme.json').write_text(json.dumps({'KnownSchemes':{'Synthetic':{'ContentKey':'AAECAwQFBgcICQoLDA0ODw=='}}})+'\n')
igs_files=[FILES[0],('sub/名前が長い合成ファイル.bin',bytes(range(256))*2),FILES[2]]
for label,page,password in [('rc4-1024',1024,'igs sample'),('rc4-4096',4096,'igs samp1e'),('rc4-65536',65536,'igs sample'),('plain',4096,None)]:
 archive,index=igs(igs_files,page,password,full_prefix=label!='plain')
 put('igs-'+label+'/standard.dat','DAT/IGS',archive,igs_files,
  f'{label} SQLite index; AES-CBC prefixes, clear tails, UTF-8 key folding and empty entry',portable=True)
 (ROOT/('igs-'+label)/'index.db').write_bytes(index)
(ROOT/'tiles.ipt').write_text('ipt = { mode="cut", base={ x=0,y=0,w=2,h=1,"unused" }, '
  +'{id=1,file="colors-rgba",x=0,y=0} }',encoding='utf-8')
atx=io.BytesIO()
with zipfile.ZipFile(atx,'w')as archive:
 for name,data in [('atlas.json',json.dumps({'Canvas':{'Width':4,'Height':2},'Block':[{'Mesh':[
  {'texNo':0,'width':2,'height':1,'viewX':0,'viewY':0,'srcOffsetX':1,'srcOffsetY':1}]}]}).encode()),
  ('tex0.png',(ROOT/'colors-rgba.png').read_bytes())]:
  info=zipfile.ZipInfo(name,date_time=(2020,1,1,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED
  archive.writestr(info,data)
(ROOT/'tiles.atx').write_bytes(atx.getvalue())
# LIM's raw BGR565 mode; no compressor or commercial image is used.
lim=s.pack('<2sHHHIIHH',b'LM',0x12,16,0,2,1,0xf800,0x07e0)
(ROOT/'raw565.lim').write_bytes(lim)
wav=io.BytesIO()
with wave.open(wav,'wb') as output:
 output.setnchannels(1);output.setsampwidth(2);output.setframerate(22050)
 output.writeframes(b''.join(s.pack('<h',int(math.sin(i*2*math.pi*440/22050)*8000))for i in range(2205)))
(ROOT/'tone.wav').write_bytes(wav.getvalue())
if '--audio'in sys.argv:
 for suffix,codec in [('mp3','libmp3lame'),('ogg','libvorbis'),('aiff','pcm_s16be'),('opus','libopus')]:
  subprocess.run(['ffmpeg','-hide_banner','-loglevel','error','-y','-i',str(ROOT/'tone.wav'),'-map_metadata','-1','-fflags','+bitexact','-flags:a','+bitexact','-c:a',codec,str(ROOT/('tone.'+suffix))],check=True)
for label,key in [('a',bytes(range(16))),('b',bytes(range(16,32)))]:
 game=ROOT/('rpg-'+label);(game/'data').mkdir(parents=True,exist_ok=True)
 (game/'data/System.json').write_text(json.dumps({'encryptionKey':key.hex()}))
 for folder,source,target in [('img/pictures','colors-rgba.png','colors.rpgmvp'),('audio/bgm','tone.ogg','tone.rpgmvo')]:
  if not (ROOT/source).exists():continue
  destination=game/folder;destination.mkdir(parents=True,exist_ok=True)
  data=bytearray((ROOT/source).read_bytes())
  for i in range(16):data[i]^=key[i]
  (destination/target).write_bytes(b'RPGMV'+b'\0'*11+data)
bmp_pixels=bytes([0,0,255,0,255,0,0,0,255,0,0,255,255,255,0,0])
bmp=b'BM'+s.pack('<IHHI',54+len(bmp_pixels),0,0,54)+s.pack('<IiiHHIIiiII',40,2,-2,1,24,0,len(bmp_pixels),0,0,0,0)+bmp_pixels
(ROOT/'top-down.bmp').write_bytes(bmp)
(ROOT/'manifest.json').write_text(json.dumps({'schema':1,'provenance':'Synthetic, generated by tools/generate-fixtures.py; MIT license.','archives':manifest,'portable_archives':portable_manifest,
 'images':[{'file':'legacy555.mbp','format':'MBP','width':2,'height':1,'offset_x':0,'offset_y':0,
            'bgra32':[0,0,255,255,0,255,0,255]},
           {'file':'literal.drg','format':'DRG','width':2,'height':1,'offset_x':0,'offset_y':0,
            'bgra32':[0,0,255,255,0,255,0,255]},
           {'file':'legacy555.cbf','format':'CBF','width':2,'height':2,'offset_x':0,'offset_y':0,
            'bgra32':[255,0,0,255,255,255,255,255,0,0,255,255,0,255,0,255]},
           {'file':'literal.agf','format':'AGF/AOI','width':2,'height':1,'offset_x':0,'offset_y':0,
            'bgra32':[0,0,255,128,0,255,0,255]},
           {'file':'obfuscated.bbm','format':'BBM','width':4,'height':4,'offset_x':0,'offset_y':0,
            'bgra32':[0,0,255,255,0,255,0,255,255,0,0,255,255,255,255,255]*4},
           {'file':'alpha.pms','format':'PMS','width':2,'height':1,'offset_x':-3,'offset_y':4,
            'bgra32':[0,0,255,128,0,255,0,255]},
           {'file':'tiles.dpng','format':'DPNG','width':4,'height':2,'offset_x':0,'offset_y':0,
            'bgra32':[0]*20+[0,0,255,128,0,255,0,255]+[0]*4},
           {'file':'alpha.cwp','format':'CWP','width':2,'height':1,'offset_x':0,'offset_y':0,
            'bgra32':[0,0,255,255,0,255,0,255]},
           {'file':'two-frames.hzc','format':'HZC','width':2,'height':1,'offset_x':-2,'offset_y':3,
            'bgra32':[0,0,255,255,0,255,0,255]},
           {'file':'tiles.ipt','format':'IPT','width':2,'height':1,'offset_x':0,'offset_y':0,
            'bgra32':[0,0,255,128,0,255,0,255]},
           {'file':'tiles.atx','format':'ATX','width':4,'height':2,'offset_x':0,'offset_y':0,
            'bgra32':[0]*20+[0,0,255,128,0,255,0,255]+[0]*4}]},ensure_ascii=False,indent=2)+'\n')
print(f'Generated {len(manifest)} archive variants and one LIM image')
