#!/usr/bin/env python3
"""Exercise the built CLI's public commands, JSON and exit codes."""
import argparse
import json
import subprocess
import tempfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet',default='dotnet')
parser.add_argument('--cli',type=Path,help='Use a published or unpacked CLI instead of build output')
args=parser.parse_args()
CLI=args.cli.resolve()if args.cli else ROOT/'modern/GARbro.Cli/bin/Release/net10.0/garbro.dll'
FIX=ROOT/'tests/fixtures/generated'
def run(*arguments,code=0):
 result=subprocess.run([args.dotnet,str(CLI),*map(str,arguments),'--json'],capture_output=True,text=True,cwd=ROOT)
 if result.returncode!=code:raise AssertionError((arguments,result.returncode,result.stdout,result.stderr))
 return json.loads(result.stdout)
formats=run('formats')
assert len(formats)==1247
assert any(f['tag']=='XP3'for f in formats)
assert any(f['tag']=='DL1'and f['assembly']=='ArcLegacy.Portable'for f in formats)
assert any(f['tag']=='DAT/IGS'and f['assembly']=='ArcExtra.Portable'for f in formats)
schemes=run('schemes',CLI.parent/'GameData/Formats.json.gz')
scheme_manifest=json.loads((CLI.parent/'GameData/schemes.json').read_text())
assert schemes['version']==scheme_manifest['version']==148 and not schemes['failed'] and not schemes['unported'],schemes
assert sorted(schemes['applied'])==scheme_manifest['applied'],schemes
assert run('probe',FIX/'standard.xp3','--scheme','none')[0]['format']=='XP3'
assert run('probe',FIX/'raw565.lim')[0]['format']=='LIM'
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
 run('extract',FIX/'unsafe.xfl','-o',root/'unsafe',code=3)
 assert not (root/'escape.txt').exists()
 run('extract',FIX/'standard.xp3','-o',root/'missing-key',code=4)
print('CLI smoke checks passed')
