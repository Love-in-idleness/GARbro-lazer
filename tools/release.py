#!/usr/bin/env python3
"""Validate a release and prepare checksums for the tested platform packages."""
import argparse
import hashlib
import json
import os
import re
import shutil
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]


def release_plan(tag):
    if not re.fullmatch(r'v\d+\.\d+\.\d+(?:-(?:alpha|beta|rc|preview)\.\d+)?',tag):
        raise ValueError('Expected a version such as v1.0.0 or v1.0.0-rc.1')
    notes=Path('docs/releases')/(tag+'.md')
    if not (ROOT/notes).is_file() or not (ROOT/notes).read_text(encoding='utf-8').strip():
        raise ValueError('Release notes must exist: '+str(notes))
    return dict(tag=tag,version=tag[1:],notes=notes.as_posix(),prerelease='true'if'-'in tag else'false')


def prepare_assets(plan,artifacts,revision):
    if not re.fullmatch(r'[0-9a-f]{40}|[0-9a-f]{64}',revision):
        raise ValueError('A full source commit SHA is required')
    packages=[artifacts/('garbro-'+plan['version']+'-'+rid+suffix)for rid,suffix in
              [('linux-x64','.tar.gz'),('win-x64','.zip'),('osx-arm64','.tar.gz'),
               ('linux-x64','.deb'),('win-x64','.msix'),('win-x64','.cer'),
               ('linux-arm64','.tar.gz'),('linux-arm64','.deb'),('win-arm64','.zip'),
               ('win-arm64','.msix'),('win-arm64','.cer'),
               ('store','.msixupload'),('store','.msixbundle'),
               ('osx-x64','.tar.gz'),('osx-x64','.dmg'),('osx-arm64','.dmg'),
               ('linux-x64','.flatpak'),('linux-arm64','.flatpak')]]
    packages.extend(artifacts/name for name in ('store-submission.json','store-listing.zip','flathub-submission.zip'))
    if any(not path.is_file()or not path.stat().st_size for path in packages):
        raise ValueError('All desktop architectures, Store upload, installers and certificates must exist and be nonempty')
    shutil.copy2(ROOT/'docs/compatibility/portable-status.json',artifacts/'compatibility.json')
    for name in ('Formats.json.gz','schemes.json'):
        shutil.copy2(ROOT/'modern/GameData'/name,artifacts/name)
    records=[]
    for path in sorted(packages+[artifacts/name for name in ('compatibility.json','Formats.json.gz','schemes.json')]):
        with path.open('rb')as stream:digest=hashlib.file_digest(stream,'sha256').hexdigest()
        records.append(dict(name=path.name,size=path.stat().st_size,sha256=digest))
    manifest=artifacts/'release.json'
    manifest.write_text(json.dumps(dict(version=plan['version'],commit=revision,assets=records),indent=2)+'\n',encoding='utf-8')
    digest=hashlib.sha256(manifest.read_bytes()).hexdigest()
    lines=[record['sha256']+'  '+record['name']for record in records]+[digest+'  release.json']
    (artifacts/'SHA256SUMS').write_text('\n'.join(lines)+'\n',encoding='utf-8')


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tag',default=os.environ.get('RELEASE_TAG_INPUT'))
    parser.add_argument('--artifacts',type=Path)
    parser.add_argument('--revision')
    args=parser.parse_args()
    tag=args.tag or (ROOT/'release/version.txt').read_text(encoding='utf-8').strip()
    plan=release_plan(tag)
    if args.artifacts:prepare_assets(plan,args.artifacts,args.revision or'')
    if 'GITHUB_OUTPUT'in os.environ:
        with open(os.environ['GITHUB_OUTPUT'],'a',encoding='utf-8')as output:
            for key,value in plan.items():output.write(key+'='+value+'\n')
    print(json.dumps(plan))


if __name__=='__main__':main()
