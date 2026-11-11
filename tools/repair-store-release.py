#!/usr/bin/env python3
"""Replace Store assets while retaining unchanged application packages and hashes."""
import argparse
import hashlib
import io
import json
import os
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path
from release import release_plan, validate_store_submission


def gh(*args):
    return subprocess.check_output(['gh',*args],text=True)


def digest(path):
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()


def verify_bundle(bundle,upload,submission):
    seen=set()
    with zipfile.ZipFile(bundle) as archive:
        manifest=ET.fromstring(archive.read('AppxMetadata/AppxBundleManifest.xml'))
        if manifest.find('{*}Identity').get('Version')!=submission['version']:raise ValueError('Bundle version mismatch')
        for name in archive.namelist():
            if not name.endswith('.msix'):continue
            with zipfile.ZipFile(io.BytesIO(archive.read(name))) as package:
                root=ET.fromstring(package.read('AppxManifest.xml'));identity=root.find('{*}Identity')
                for key,expected in [('Name',submission['name']),('Publisher',submission['publisher']),('Version',submission['version'])]:
                    if identity.get(key)!=expected:raise ValueError('Store manifest mismatch: '+key)
                if root.find('{*}Properties/{*}PublisherDisplayName').text!=submission['publisherDisplayName']:raise ValueError('Publisher display name mismatch')
                seen.add(identity.get('ProcessorArchitecture'))
                paths={path.replace('\\','/') for path in package.namelist()}
                shared='app/coreclr.dll' in paths
                if shared and any(path.startswith(('desktop/','cli/')) for path in paths):
                    raise ValueError('Store payload mixes shared and duplicate application directories')
                for folder in ('app',) if shared else ('desktop','cli'):
                    if folder+'/coreclr.dll' not in paths:raise ValueError('Bundled Store runtime missing')
    if seen!={'x64','arm64'}:raise ValueError('Both Store architectures are required')
    with zipfile.ZipFile(upload) as archive:
        if archive.namelist()!=[bundle.name]:raise ValueError('Upload must contain the expected bundle')
        with archive.open(bundle.name) as stream:
            if hashlib.file_digest(stream,'sha256').hexdigest()!=digest(bundle):raise ValueError('Upload bundle differs from the verified bundle')


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tag',required=True)
    parser.add_argument('--assets',type=Path,required=True)
    parser.add_argument('--store-run',type=int,required=True)
    args=parser.parse_args();plan=release_plan(args.tag)
    repo=os.environ['GITHUB_REPOSITORY'];revision=os.environ['GITHUB_SHA'];directory=args.assets.resolve()
    if len(revision)!=40 or any(c not in '0123456789abcdef' for c in revision):raise ValueError('Invalid source revision')
    submission=validate_store_submission(directory/'store-submission.json',plan['version'])
    bundle=directory/f"garbro-{plan['version']}-store.msixbundle"
    upload=directory/f"garbro-{plan['version']}-store.msixupload"
    verify_bundle(bundle,upload,submission)
    names=[bundle.name,upload.name,'store-submission.json','store-listing.zip']
    pages=json.loads(gh('api','--paginate','--slurp',f'repos/{repo}/releases?per_page=100'))
    info=next(item for page in pages for item in page if item['tag_name']==args.tag)
    store_revision=json.loads(gh('api',f'repos/{repo}/actions/runs/{args.store_run}'))['head_sha']
    if not info['draft'] or info.get('immutable'):raise ValueError('Repair requires a mutable draft release')
    ref=json.loads(gh('api',f'repos/{repo}/git/ref/tags/{args.tag}'))['object']
    if ref['type']!='commit':raise ValueError('Expected the release commit tag')
    with tempfile.TemporaryDirectory(prefix='garbro-store-repair-') as temporary:
        previous=Path(temporary)
        gh('release','download',args.tag,'--repo',repo,'--pattern','release.json','--pattern','SHA256SUMS','--dir',str(previous))
        old_bytes=(previous/'release.json').read_bytes();manifest=json.loads(old_bytes)
        checks={name:value for value,name in (line.split('  ',1) for line in (previous/'SHA256SUMS').read_text().splitlines())}
        if hashlib.sha256(old_bytes).hexdigest()!=checks['release.json']:raise ValueError('Existing release manifest checksum mismatch')
        old_revision=manifest['commit']
        if manifest['version']!=plan['version'] or ref['sha']!=old_revision:raise ValueError('Release changed during the repair')
        # Reuse binary packages only while all application and decoder source is identical.
        subprocess.run(['git','diff','--exit-code',old_revision,revision,'--','modern','GameRes','ArcFormats','Experimental'],check=True)
        subprocess.run(['git','diff','--exit-code',store_revision,revision,'--','modern','GameRes','ArcFormats','Experimental'],check=True)
        records={item['name']:item for item in manifest['assets']}
        if len(records)!=24 or not set(names)<=set(records):raise ValueError('Unexpected release asset set')
        source_commits=manifest.get('asset_source_commits',{name:old_revision for name in records})
        for name in names:
            path=directory/name
            if not path.is_file() or path.stat().st_size==0:raise ValueError('Missing Store asset: '+name)
            records[name]=dict(name=name,size=path.stat().st_size,sha256=digest(path))
            source_commits[name]=store_revision
        manifest.update(commit=revision,application_source_commit=manifest.get('application_source_commit',old_revision),asset_source_commits=source_commits,assets=[records[name] for name in sorted(records)])
        manifest_path=directory/'release.json';manifest_path.write_text(json.dumps(manifest,indent=2)+'\n')
        lines=[item['sha256']+'  '+item['name'] for item in manifest['assets']]+[digest(manifest_path)+'  release.json']
        (directory/'SHA256SUMS').write_text('\n'.join(lines)+'\n')
        gh('release','upload',args.tag,'--repo',repo,'--clobber',*(str(directory/name) for name in names),str(manifest_path),str(directory/'SHA256SUMS'))
        payload=previous/'tag.json';payload.write_text(json.dumps(dict(sha=revision,force=True)))
        gh('api','--method','PATCH',f'repos/{repo}/git/refs/tags/{args.tag}','--input',str(payload))
        gh('release','edit',args.tag,'--repo',repo,'--target',revision,'--draft=false')
    print('Store release repaired:',args.tag,'package version:',submission['version'],'source:',revision)


if __name__=='__main__':main()
