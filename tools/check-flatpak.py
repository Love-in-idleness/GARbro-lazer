#!/usr/bin/env python3
"""Check Flatpak identity, permission boundaries and bilingual application metadata."""
import json
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
APP='io.github.love_in_idleness.GARbro-no-wpf'

def check():
    manifest=json.loads((ROOT/f'packaging/flatpak/{APP}.json').read_text())
    metadata=ET.parse(ROOT/f'packaging/flatpak/{APP}.metainfo.xml').getroot()
    assert manifest['app-id']==metadata.findtext('id')==APP
    assert metadata.findtext('launchable')==APP+'.desktop'
    assert manifest['runtime-version']=='26.08'
    assert '--socket=wayland' in manifest['finish-args']
    assert '--socket=fallback-x11' in manifest['finish-args']
    assert not any(arg.startswith(('--filesystem=','--socket=session-bus','--socket=system-bus','--device=all')) for arg in manifest['finish-args'])
    assert '--share=network' not in manifest['build-options'].get('build-args',[])
    assert metadata.findtext('summary') and metadata.find('summary[@{http://www.w3.org/XML/1998/namespace}lang="zh_CN"]') is not None
    assert metadata.find('content_rating') is not None
    assert metadata.findtext('metadata_license')=='CC0-1.0'
    return APP

if __name__=='__main__':print('Verified Flatpak preparation:',check())
