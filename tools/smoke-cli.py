#!/usr/bin/env python3
"""Exercise the built CLI's public commands, JSON and exit codes."""
import argparse
import json
import os
import subprocess
import shutil
import hashlib
import tempfile
import struct
import zlib
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet',default='dotnet')
parser.add_argument('--cli',type=Path,help='Use a published or unpacked CLI instead of build output')
args=parser.parse_args()
CLI=args.cli.resolve()if args.cli else ROOT/'modern/GARbro.Cli/bin/Release/net11.0/garbro.dll'
FIX=ROOT/'tests/fixtures/generated'
state=tempfile.TemporaryDirectory(prefix='garbro-cli-state-')
env=dict(os.environ,GARBRO_SETTINGS_PATH=str(Path(state.name)/'settings.json'))
def run(*arguments,code=0,cwd=ROOT):
 result=subprocess.run([args.dotnet,str(CLI),'--json',*map(str,arguments)],capture_output=True,text=True,cwd=cwd,env=env)
 if result.returncode!=code:raise AssertionError((arguments,result.returncode,result.stdout,result.stderr))
 return json.loads(result.stdout)
formats=run('formats')
assert len(formats)==1248
assert any(f['tag']=='XP3'for f in formats)
assert any(f['tag']=='DL1'and f['assembly']=='ArcLegacy.Portable'for f in formats)
assert any(f['tag']=='DAT/IGS'and f['assembly']=='ArcExtra.Portable'for f in formats)
schemes=run('schemes',CLI.parent/'GameData/Formats.json.gz','--check')
scheme_manifest=json.loads((CLI.parent/'GameData/schemes.json').read_text())
assert scheme_manifest==json.loads((ROOT/'modern/GameData/schemes.json').read_text())
assert schemes['version']==scheme_manifest['version'] and not schemes['failed'] and not schemes['unported'],schemes
assert sorted(schemes['applied'])==scheme_manifest['applied'],schemes
assert run('probe',FIX/'standard.xp3','--scheme','none')[0]['format']=='XP3'
assert run('probe',FIX/'raw565.lim')[0]['format']=='LIM'
assert run('probe',FIX/'colors-rgba.png','--format','PNG')[0]['format']=='PNG'
run('probe',FIX/'colors-rgba.png','--format','XFL',code=1)
assert {f['tag'] for f in run('formats','--type','video')}=={'MP4','MKV'}
assert len(run('list',FIX/'pf8.pfs')[0]['entries'])==3
assert run('formats','--timeout','0.01',code=130)['error']=='WorkerLimitException'
assert run('formats','--max-memory','1',code=2)['error']=='ArgumentOutOfRangeException'
for language,word in [('en-US','Commands'),('zh-CN','命令'),('ru-RU','Команды'),('ja-JP','コマンド')]:
 for arguments in [('--language',language,'help'),('help','--language',language)]:
  help=subprocess.check_output([args.dotnet,str(CLI),*arguments],text=True)
  assert word in help,(language,help)
 assert run('--language',language,'list',FIX/'standard.xfl')==run('list',FIX/'standard.xfl','--language',language)
 error=subprocess.run([args.dotnet,str(CLI),'extract','--language',language],text=True,capture_output=True)
 assert error.returncode==2 and error.stderr.strip()
 for command in ('convert','extract'):
  help=subprocess.check_output([args.dotnet,str(CLI),'help',command,'--language='+language],text=True)
  assert 'WEBP' in help,(language,command,help)
assert 'version' in run('--version')
assert any(f['tag']=='WEBP' and f['canWrite'] for f in run('--timeout','30','formats','--type=image'))
run('convert','--nested','unused.png','-o','unused.png',code=2)
run('extract','unused.xfl','--discard-child-archives','-o','unused',code=2)
run('extract','unused.xfl','-o','first','--output','second',code=2)
with tempfile.TemporaryDirectory(prefix='garbro-cli-')as folder:
 root=Path(folder)
 run('extract',FIX/'standard.xp3','--scheme','none','-o',root/'output')
 assert (root/'output/hello.txt').read_text()=='Hello, GARbro!\n'
 for fixture in ('cab-mszip.cab','igs-rc4-4096/standard.dat'):
  destination=root/fixture.replace('/','-')
  run('extract',FIX/fixture,'-o',destination)
  assert (destination/'hello.txt').read_text()=='Hello, GARbro!\n'
 run('convert',FIX/'raw565.lim','-o',root/'image.png')
 assert (root/'image.png').read_bytes().startswith(b'\x89PNG\r\n\x1a\n')
 run('convert',FIX/'obfuscated.bbm','-o',root/'bbm.png')
 assert (root/'bbm.png').read_bytes().startswith(b'\x89PNG\r\n\x1a\n')
 run('convert',FIX/'colors-rgba.png','--to=webp','-o',root/'image.webp')
 webp=(root/'image.webp').read_bytes();assert webp[:4]==b'RIFF' and webp[8:12]==b'WEBP'
 assert run('probe',root/'image.webp')[0]['format']=='WEBP'
 run('convert',FIX/'tone.aiff','-o',root/'voice.flac')
 assert run('probe',root/'voice.flac')[0]['type']=='audio'
 run('convert',FIX/'movie.mpg','--to=mp4','-o',root/'movie.mp4')
 assert run('probe',root/'movie.mp4')[0]['type']=='video'
 # -- protects filenames beginning with a dash, including in the supervised child.
 literal=root/'-literal.xfl';shutil.copy2(FIX/'standard.xfl',literal)
 assert run('list','--',literal.name,cwd=root)[0]['format']=='XFL'
 before=hashlib.sha256((FIX/'colors-rgba.png').read_bytes()).hexdigest()
 run('convert',FIX/'colors-rgba.png','-o',FIX/'colors-rgba.png','--overwrite','replace',code=5)
 assert hashlib.sha256((FIX/'colors-rgba.png').read_bytes()).hexdigest()==before
 images=root/'images';images.mkdir();shutil.copy2(FIX/'colors-rgba.png',images/'image.png')
 report=run('extract',images,'--recursive','--image-format','webp','--discard-original-media','-o',root/'converted')
 assert len(report[0]['files'])==1 and (root/'converted/image.webp').exists()
 assert not (root/'converted/image.png').exists() and (images/'image.png').exists()
 # Repeated archive names in different disk subfolders retain their relative paths.
 disks=root/'scan'
 for name in ('first','second'):
  (disks/name).mkdir(parents=True);shutil.copy2(FIX/'standard.xfl',disks/name/'data.xfl')
 (disks/'readme.txt').write_text('ordinary file, not an archive')
 scanned=run('extract',disks,'--recursive','-o',root/'scanned')
 assert len(scanned)==2
 for name in ('first','second'):assert (root/'scanned'/name/'data.xfl.unpacked/hello.txt').read_text()=='Hello, GARbro!\n'
 # The output budget is shared across positional archive inputs.
 first=root/'one.xfl';second=root/'two.xfl'
 shutil.copy2(FIX/'standard.xfl',first);shutil.copy2(FIX/'standard.xfl',second)
 size=sum(e['size'] for e in run('list',first)[0]['entries'])
 run('extract',first,second,'-o',root/'limited','--max-total',size+1,code=3)
 run('extract',FIX/'unsafe.xfl','-o',root/'unsafe',code=3)
 assert not (root/'escape.txt').exists()
 run('extract',FIX/'standard.xp3','-o',root/'missing-key',code=4)
 # Validate collisions before the first output write and protect the archive itself.
 for side in ('a','b'):
  (root/side).mkdir(); shutil.copy2(FIX/'standard.xfl',root/side/'same.xfl')
 run('extract',root/'a/same.xfl',root/'b/same.xfl','-o',root/'same-output',code=5)
 assert not (root/'same-output').exists()
 payload=root/'a/source.xfl'; payload.write_text('same-name entry')
 archive=root/'b/source.xfl'
 run('create',payload,'-o',archive)
 original=archive.read_bytes()
 run('extract',archive,'-o',archive.parent,'--overwrite','replace',code=5)
 assert archive.read_bytes()==original
 plain=run('extract',FIX/'standard.xfl','-o',root/'plain')[0]
 nested=run('extract',FIX/'standard.xfl','-o',root/'nested','--format','XFL','--nested')[0]
 assert set(plain)==set(nested) and nested['format']=='XFL'
 assert (root/'nested/hello.txt').exists()
 assert all(not Path(f['path']).is_absolute() for f in nested['files'])
 run('extract',payload,'-o',root/'unknown-nested','--nested',code=1)
 assert not (root/'unknown-nested').exists()
 # Convert an archive entry with an image offset and report skipped output.
 offset_file=root/'offset.png'
 png=(FIX/'colors-rgba.png').read_bytes()
 chunk=b'oFFs'+struct.pack('>iiB',2,1,0)
 offset_file.write_bytes(png[:33]+struct.pack('>I',9)+chunk+struct.pack('>I',zlib.crc32(chunk))+png[33:])
 media=root/'media.xfl'
 shutil.copy2(FIX/'tone.wav',root/'tone.wav')
 run('create',offset_file,root/'tone.wav','-o',media)
 converted=root/'offset-result.png'
 assert run('convert',media,'--format','XFL','--entry','offset.png','--adjust-offset','-o',converted)['written']
 assert struct.unpack('>II',converted.read_bytes()[16:24])==(4,2)
 assert run('convert',media,'--entry','tone.wav','-o',root/'entry.flac')['written']
 assert not run('convert',offset_file,'-o',converted,'--overwrite','skip')['written']
 assert struct.unpack('>II',converted.read_bytes()[16:24])==(4,2)
 script=ROOT/'tests/fixtures/scripts-native/rscript19.gsc'
 packed=root/'scripts.xfl'
 run('create',script,'-o',packed,'--script-format','export','--discard-original-media')
 assert [entry['name'] for entry in run('list',packed)[0]['entries']]==['rscript19.tsc']
 run('extract',packed,'-o',root/'scripts-out','--script-format','GSC','--discard-original-media')
 assert (root/'scripts-out/rscript19.gsc').read_bytes()==script.read_bytes()
 same_dir_script=root/'script.gsc';shutil.copy2(script,same_dir_script)
 same_dir_output=root/'script.tsc';same_dir_output.write_text('existing text')
 assert not run('convert',same_dir_script,'-o',same_dir_output,'--overwrite','skip')['written']
 assert same_dir_output.read_text()=='existing text'
 # Validation does not change the saved database, and an import survives a new process.
 database=root/'schemes.json'
 database.write_text(json.dumps({'PortableSchemaVersion':2,'Version':152,'SchemeMap':{
  'XP3':{'$type':'GameRes.Formats.KiriKiri.Xp3Scheme, ArcFormats','NoCryptTitles':[],
  'KnownSchemes':{'CLI synthetic':{'$type':'GameRes.Formats.KiriKiri.XorCrypt, ArcFormats','m_key':227}}}}}))
 run('schemes',database,'--check')
 run('create',payload,'--scheme','CLI synthetic','-o',root/'unsaved.xp3',code=4)
 run('schemes',database)
 run('create',payload,'--scheme','CLI synthetic','-o',root/'saved.xp3')
 database.write_text('{"Version":153,"SchemeMap":{"XP3":{}}}')
 run('schemes',database,code=3)
 run('create',payload,'--scheme','CLI synthetic','-o',root/'retained.xp3')
 assert run('schemes','--reset')['reset']
 run('create',payload,'--scheme','CLI synthetic','-o',root/'reset.xp3',code=4)
state.cleanup()
print('CLI smoke checks passed')
