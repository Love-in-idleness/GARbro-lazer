#!/usr/bin/env python3
"""Same-machine Windows GUI measurements; UI action through layout and a DWM barrier."""
import argparse, hashlib, json, os, platform, shutil, statistics, subprocess
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--v101',type=Path,required=True);p.add_argument('--v102',type=Path,required=True)
p.add_argument('--output',type=Path,default=ROOT/'artifacts/comparison');p.add_argument('--trials',type=int,default=5)
a=p.parse_args();out=a.output.resolve();out.mkdir(parents=True,exist_ok=True)
def run(args,**kw):subprocess.run([str(v)for v in args],check=True,**kw)
if os.name!='nt':raise SystemExit('WPF comparison requires an actual Windows desktop.')
fixtures=out/'inputs';fixtures.mkdir();(fixtures/'empty').mkdir();(fixtures/'images').mkdir()
for suffix in ('a','b'):
 shutil.copy2(ROOT/'tests/fixtures/generated/standard.xfl',fixtures/f'sample-{suffix}.xfl')
 shutil.copy2(ROOT/'tests/fixtures/generated/colors-rgb.png',fixtures/f'images/sample-{suffix}.png')
 directory=fixtures/f'directory-{suffix}';directory.mkdir()
 for i in range(10000):(directory/f'entry-{i:05}.txt').touch()
# Build the same instrumentation against each unmodified application source tree.
legacy=a.v102.resolve()/'bin/Release'
run(['powershell','-NoProfile','-File',ROOT/'tools/performance/export-wpf-resources.ps1','-Directory',legacy])
run(['msbuild',ROOT/'tools/performance/WpfBench.csproj','/p:Configuration=Release','/p:GarbroLegacyDirectory='+str(legacy)])
config=legacy/'GARbro.GUI.exe.config'
if config.exists():shutil.copy2(config,legacy/'GARbro.WpfBenchmark.exe.config')
executables={'WPF':legacy/'GARbro.WpfBenchmark.exe'}
run([executables['WPF'],fixtures/'empty',fixtures,'archive',out/'preflight-WPF.json'],timeout=60,cwd=legacy)
for name,source in [('1.0.1',a.v101.resolve()),('1.0.2',a.v102.resolve())]:
 destination=out/('driver-'+name);shutil.copytree(ROOT/'tools/performance/modern',destination,ignore=shutil.ignore_patterns('bin','obj'))
 run(['dotnet','build',destination/'ModernBench.csproj','-c','Release','--disable-build-servers','-m:1',f'-p:GarbroSourceDirectory={source}'])
 executables[name]=destination/'bin/Release/net10.0/ModernBench.exe'
results=[]
for trial in range(a.trials):
 names=list(executables);names=names[trial%3:]+names[:trial%3]
 for name in names:
  for scenario in ('archive','directory','image'):
   report=out/f'{name}-{scenario}-{trial}.json'
   args=[executables[name],fixtures,scenario,report]
   if name=='WPF':args.insert(1,fixtures/'empty')
   run(args,timeout=180,cwd=executables[name].parent)
   record=json.loads(report.read_text(encoding='utf-8-sig'));record.update(label=name,trial=trial)
   if name!='WPF' and not record['version'].startswith(name):raise ValueError('Wrong application assembly: '+record['version'])
   results.append(record)
summary={}
for name in executables:
 summary[name]={}
 for scenario in ('archive','directory','image'):
  records=[r for r in results if r['label']==name and r['scenario']==scenario]
  summary[name][scenario]={'firstMedianMs':statistics.median(r['firstMs']for r in records),
   'warmMedianMs':statistics.median(v for r in records for v in r['warmMs']),
   'startupMedianMs':statistics.median(r['startupMs']for r in records),
   'uiDelayP95MedianMs':statistics.median(r['uiDelayP95Ms']for r in records),
   'uiDelayMaxMs':max(r['uiDelayMaxMs']for r in records)}
cpu=subprocess.check_output(['powershell','-NoProfile','-Command','(Get-CimInstance Win32_Processor | Select-Object -First 1).Name'],text=True).strip()
metadata={'platform':platform.platform(),'machine':platform.machine(),'cpu':cpu,'trials':a.trials,'warmMeasurementsPerTrial':7,
 'v101Commit':subprocess.check_output(['git','-C',str(a.v101),'rev-parse','HEAD'],text=True).strip(),
 'v102Commit':subprocess.check_output(['git','-C',str(a.v102),'rev-parse','HEAD'],text=True).strip(),
 'fixtures':{n:hashlib.sha256((ROOT/'tests/fixtures/generated'/n).read_bytes()).hexdigest()for n in ('standard.xfl','colors-rgb.png')},
 'method':'Action to validated view plus common layout/17 ms/DwmFlush barrier. Process-cold, OS file caches retained. Alternate identical A/B inputs to prevent WPF same-path no-op. Image timings include native selection debounce. Startup is measured from driver entry to ready window; WPF initializes formats at startup, modern versions keep their normal deferred/prewarm policy.'}
(out/'comparison.json').write_text(json.dumps({'environment':metadata,'summary':summary,'samples':results},indent=2),encoding='utf-8')
lines=['# WPF / 1.0.1 / 1.0.2 GUI time comparison','',f"Windows x64; {cpu}; {a.trials} fresh processes per scenario, 7 warm measurements per process.",'',
 '| Operation (ms, median) | WPF | 1.0.1 | 1.0.2 |','| --- | ---: | ---: | ---: |']
for scenario,title in [('archive','Small XFL archive'),('directory','10,000-file directory'),('image','Small PNG preview')]:
 for field,label in [('firstMedianMs','first'),('warmMedianMs','warm')]:
  lines.append('| '+title+' ('+label+') | '+' | '.join(f'{summary[n][scenario][field]:.1f}'for n in ('WPF','1.0.1','1.0.2'))+' |')
lines+=['',metadata['method'],'','Raw samples and exact source/fixture hashes: `comparison.json`.']
(out/'comparison.md').write_text('\n'.join(lines)+'\n',encoding='utf-8');print('\n'.join(lines))
