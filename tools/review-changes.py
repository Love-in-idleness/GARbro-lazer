#!/usr/bin/env python3
"""Avoid repeating application regressions for documentation/build-only updates."""
import json
import os
import subprocess
from pathlib import Path

event_path=os.environ.get('GITHUB_EVENT_PATH')
event=json.loads(Path(event_path).read_text()) if event_path else {}
pr=event.get('pull_request',{})
head=pr.get('head',{}).get('sha') or os.environ.get('GITHUB_SHA','HEAD')
before=event.get('before')
if not before or set(before)=={'0'}:
    before=pr.get('base',{}).get('sha') if event.get('action')!='synchronize' else head+'^'
comparison=subprocess.run(['git','diff','--quiet',before or head+'^',head,'--',
    'modern','GameRes','ArcFormats','Experimental','tests','Directory.Build.props','Directory.Packages.props','NuGet.Config'])
changed=comparison.returncode!=0 # Missing history conservatively runs regressions.
value='true' if changed else 'false'
if os.environ.get('GITHUB_OUTPUT'):
    with open(os.environ['GITHUB_OUTPUT'],'a') as output:output.write('app='+value+'\n')
print('Application regressions required:',value)
