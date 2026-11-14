#!/usr/bin/env python3
"""Account for the entire frozen catalog without treating compilation as extraction support."""
import argparse
import json
import subprocess
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet',default='dotnet')
args=parser.parse_args()
source=json.loads((ROOT/'docs/compatibility/baseline-source.json').read_text())['formats']
legacy=json.loads((ROOT/'docs/compatibility/baseline-runtime.json').read_text())
result=subprocess.run([args.dotnet,str(ROOT/'modern/GARbro.Cli/bin/Release/net10.0/garbro.dll'),'formats','--json'],capture_output=True,text=True,check=True)
compiled=json.loads(result.stdout)
manifest=json.loads((ROOT/'tests/fixtures/generated/manifest.json').read_text())
variants={}
for archive in manifest['archives']+manifest.get('portable_archives',[]):
 if archive['file']not in ('unsafe.xfl','collision.xfl'):variants.setdefault(archive['format'],[]).append(archive['file'])
variants['DAT/hibiki']=['hibiki-init.dat']
image_fixtures={item['format']:item['file']for item in manifest.get('images',[])}
image_fixtures['PNG/NekoNyan']='neko-strips.png'
image_fixtures.update({'WEBP':'slice.webp','RPGMVP':'rpg-a/img/pictures/colors.rpgmvp'})
audio_fixtures={'WAV':'tone.wav','MP3':'tone.mp3','OGG':'tone.ogg','AIFF':'tone.aiff',
 'OPUS':'tone.opus','RPGMVO':'rpg-a/audio/bgm/tone.rpgmvo'}
rows=[]
for item in compiled:
 assembly={'GARbro.Core':'GameRes'}.get(item['assembly'],item['assembly'].removesuffix('.Portable'))
 matches=[s for s in source if s['assembly']==assembly and s['class']==item['class'] and (s['tag']==item['tag'] or s['tag']is None)]
 if len(matches)!=1:raise SystemExit('Unresolved portable export identity: '+item['class'])
 original=matches[0]
 canonical=original['contract'].removesuffix('Format').lower()
 limitations={'WMA':['Decoding requires Windows Media Foundation.'],
  'LAY/MAGES':['Fractional target tile coordinates require a resampling backend; integer-aligned composition is implemented.'],
  'PNG/NekoNyan':['The WebP-strip fixture is verified against synthetic pixels in the portable runtime; the original Windows native WebP backend is not compared.']}
 limitations['CAB']=['Only stored and MSZIP folders are implemented. Quantum, LZX, split blocks and multi-cabinet sets report unsupported. Non-UTF-8 names use the current culture ANSI code page.']
 limitations['DAT/hibiki']=['Requires separately supplied hibiki_works.json or an imported scheme. Legacy BinaryFormatter auxiliary files are not loaded. Only the synthetic portable fixture is verified.']
 limitations['DAT/IGS']=['Uses open-source SQLite3 Multiple Ciphers RC4 compatibility mode. Synthetic plain and fully encrypted SQLite indexes are verified; mixed plaintext-page-1 databases and actual game indexes are unverified. Active journals are rejected.']
 if item['tag']in ('WEBP','RPGMVP','RPGMVO','OPUS'):
  limitations[item['tag']]=['Synthetic portable decoding is verified; the historical Windows backend is not compared for this fixture.']
 rows.append({'limitations':limitations.get(item['tag'],[]),'baseline_assembly':original['assembly'],'class':item['class'],'tag':item['tag'],'type':canonical,'resource_type':item['type'],
  'build':'verified','recognition':'fixture-verified'if item['tag']in variants or item['tag']in image_fixtures or item['tag']in audio_fixtures or item['tag']=='LIM'else 'not-tested',
  'list':'fixture-verified'if item['tag']in variants else 'not-applicable'if canonical!='archive'else 'not-tested',
  'raw_extract':'fixture-verified'if item['tag']in variants else 'not-applicable'if canonical!='archive'else 'not-tested',
  'conversion':'fixture-verified'if item['tag']in image_fixtures or item['tag']in audio_fixtures or item['tag']in ('LIM','PNG','BMP','TIFF')else 'not-tested',
  'evidence':['tests/GARbro.Core.Tests/HibikiTests.cs']if item['tag']=='DAT/hibiki'else ['tests/GARbro.Core.Tests/ExperimentalFormatTests.cs']if item['tag']in ('WEBP','RPGMVP','RPGMVO','OPUS')else ['tests/GARbro.Core.Tests/ArchiveFixtureTests.cs']if item['tag']in variants or item['tag']in image_fixtures or item['tag']=='LIM'else ['tests/GARbro.Core.Tests/AudioTests.cs']if item['tag']in audio_fixtures else ['tests/GARbro.Core.Tests/ImagingTests.cs']if item['tag']in ('PNG','BMP','TIFF')else [],
  'fixtures':variants.get(item['tag'],[image_fixtures[item['tag']]]if item['tag']in image_fixtures else ['raw565.lim']if item['tag']=='LIM'else [audio_fixtures[item['tag']]]if item['tag']in audio_fixtures else [])})
for row, item in zip(rows, compiled):
 if row['type']=='archive' and item['canWrite']:
  row['creation']='fixture-verified'
  row['creation_evidence']=['tests/GARbro.Core.Tests/ResourceBrowserTests.cs:EveryDeclaredArchiveWriterCreatesAnArchiveThatCanBeListed']
  row['limitations'].append('Creation/read-back covers a small synthetic file with explicit required keys; not every option or real-game variant.')
source_release={(r['assembly'],r['class'],r['tag'])for r in source if not r['condition']}
legacy_release={(r['assembly'],r['class'],r['tag'])for r in legacy}
if source_release!=legacy_release:raise SystemExit('Source/runtime baseline difference is not accounted for')
output={'schema':1,'baseline_commit':'698ccc57d766f4afab5be4ec9a1936dd830dac12',
 'runtime_snapshot_commit':'1bb36b9dd964af05585e16d0924fa8bcc635409f','source_declarations':len(source),'legacy_release_exports':len(legacy),
 'portable_exports':len(rows),'default_status':{'build':'not-ported','recognition':'not-tested','list':'not-tested','raw_extract':'not-tested','conversion':'not-tested'},
 'note':'Defaults apply to every baseline-source.json export not overridden below. Fixture verification covers the named synthetic variants only.',
 'debug_only':[{'assembly':r['assembly'],'class':r['class'],'tag':r['tag']}for r in source if r['condition']],
 'not_ported':[{'assembly':r['assembly'],'class':r['class'],'tag':r['tag'],
  'reason':'No portable implementation.'}
  for r in legacy if (r['assembly'],r['class'],r['tag'])not in {(row['baseline_assembly'],row['class'],row['tag'])for row in rows}],
 'compiled':rows}
(ROOT/'docs/compatibility/portable-status.json').write_text(json.dumps(output,ensure_ascii=False,indent=2)+'\n')
print(f'{len(legacy)} release exports accounted for; {len(rows)} compile in the portable runtime')
