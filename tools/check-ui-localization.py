#!/usr/bin/env python3
"""Check all four shared catalogs, placeholders, application keys and offline help."""
import json
import re
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
LANGUAGES=('en-US','zh-CN','ru-RU','ja-JP')

def check():
    resources={}
    for language in LANGUAGES:
        def unique(pairs):
            result={}
            for key,value in pairs:
                if key in result:raise ValueError('Duplicate resource key: '+key)
                if not isinstance(value,str)or not value.strip():raise ValueError('Empty translation: '+key)
                result[key]=value
            return result
        resources[language]=json.loads((ROOT/f'modern/GARbro.Application/Locales/{language}.json').read_text(),object_pairs_hook=unique)
    english=resources['en-US']
    for language,strings in resources.items():
        if set(strings)!=set(english):raise ValueError(f'{language}: missing/extra keys: {set(strings)^set(english)}')
        for key,value in strings.items():
            placeholders=lambda s:set(re.findall(r'\{\d+(?:,[+-]?\d+)?(?::[^}]+)?\}',s))
            if placeholders(value)!=placeholders(english[key]):raise ValueError(f'{language}: placeholders differ: {key}')
    missing=[]
    for directory in ('GARbro.UI','GARbro.Android','GARbro.Cli'):
        for file in (ROOT/'modern'/directory).glob('*.cs'):
            source=file.read_text()
            keys=re.findall(r'\b(?:Text|text)\["([^"\n]+)"\]',source)
            keys+=re.findall(r'\b(?:Bind|Label|Button|Item|Group|IconButton|ActionButton)\("([^"\n]+)"',source)
            keys+=re.findall(r'\b(?:Text|text)\.Format\("([^"\n]+)"',source)
            for key in keys:
                if key not in english:missing.append(str(file.relative_to(ROOT))+': '+key)
    if missing:raise ValueError('Untranslated application keys:\n'+'\n'.join(missing))
    for language in LANGUAGES:
        for name in ('manual','troubleshoot'):
            if not (ROOT/f'docs/{name}.{language}.md').read_text().strip():raise ValueError('Offline help is empty')
    return len(english)

if __name__=='__main__':print('Verified',check(),'shared keys in four languages and offline documents')
