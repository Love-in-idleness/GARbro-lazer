#!/usr/bin/env python3
"""Generate checksummed offline NuGet sources and a commit-pinned submission manifest."""
import argparse
import hashlib
import json
import re
import subprocess
import urllib.request
from pathlib import Path
from patched_dependencies import local_packages

ROOT=Path(__file__).resolve().parents[1]
APP='io.github.love_in_idleness.GARbro-lazer'

def package_records(assets, local=()):
    packages={name.lower():library for name,library in assets['libraries'].items() if library['type']=='package'}
    # SDK runtime/apphost packs are download-only dependencies and do not appear
    # under libraries. A clean offline sandbox still needs their archives.
    for framework in assets['project']['frameworks'].values():
        for dependency in framework.get('downloadDependencies',[]):
            match=re.fullmatch(r'\[([^,]+),\s*([^\]]+)\]',dependency['version'])
            if not match or match[1]!=match[2]:raise ValueError('Expected a pinned SDK pack version')
            identity=dependency['name'].lower()+'/'+match[1]
            packages.setdefault(identity,{'path':identity})
    # Local patched packages travel with the pinned source tree. They must not
    # acquire invented nuget.org URLs in an offline-source manifest.
    return ((name, library) for name, library in packages.items() if name not in local)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--revision',required=True,help='Full reviewed source commit, never a moving branch')
    parser.add_argument('--in-sdk',action='store_true',help='Resolve with the installed Flatpak SDK, as required for submission')
    parser.add_argument('--output',type=Path,default=Path('artifacts/flathub'))
    args=parser.parse_args()
    if not re.fullmatch('[0-9a-f]{40}',args.revision):parser.error('A full source commit is required')
    args.output.mkdir(parents=True,exist_ok=True);sources={};local=local_packages(ROOT)
    for rid,arch in [('linux-x64','x86_64'),('linux-arm64','aarch64')]:
        for project in ('GARbro.Desktop','GARbro.Worker','GARbro.Cli'):
            host=['dotnet']
            if args.in_sdk:
                host=['flatpak','run','--share=network','--filesystem='+str(ROOT),'--command=sh',
                      '--env=NUGET_PACKAGES='+str(ROOT/'artifacts/flatpak-nuget-cache'),
                      '--runtime=org.freedesktop.Sdk//26.08','org.freedesktop.Sdk.Extension.dotnet10//26.08',
                      '-c','export DOTNET_ROOT=/usr/lib/sdk/dotnet10/lib; exec /usr/lib/sdk/dotnet10/bin/dotnet "$@"','--']
            subprocess.run([*host,'restore',str(ROOT/f'modern/{project}/{project}.csproj'),'-r',rid,
                '-p:SelfContained=true','-p:RuntimeFrameworkVersion=10.0.14','-p:NuGetAudit=false'],cwd=ROOT,check=True)
            assets=json.loads((ROOT/f'modern/{project}/obj/project.assets.json').read_text())
            for identity,library in package_records(assets,local):
                name,version=identity.lower().split('/');filename=f'{name}.{version}.nupkg'
                # NuGet's contentHash excludes signature metadata; Flatpak checks
                # the complete downloaded ZIP. Hash the archive itself instead.
                cached=next((Path(base)/library['path']/filename for base in assets['packageFolders']
                             if (Path(base)/library['path']/filename).is_file()),None)
                url=f'https://api.nuget.org/v3-flatcontainer/{name}/{version}/{filename}'
                previous=next((item for (file,_),item in sources.items() if file==filename),None)
                if previous is not None:
                    if arch not in previous['only-arches']:previous['only-arches'].append(arch)
                    continue
                with cached.open('rb') if cached else urllib.request.urlopen(url,timeout=90) as archive:
                    digest=hashlib.file_digest(archive,'sha512').hexdigest()
                key=(filename,digest)
                if key not in sources:sources[key]={'type':'file','url':url,
                    'sha512':digest,'dest':'nuget-sources','dest-filename':filename,'only-arches':[]}
                if arch not in sources[key]['only-arches']:sources[key]['only-arches'].append(arch)
    (args.output/'nuget-sources.json').write_text(json.dumps(sorted(sources.values(),key=lambda s:s['dest-filename']),indent=2)+'\n')
    manifest=json.loads((ROOT/f'packaging/flatpak/{APP}.json').read_text())
    next(module for module in manifest['modules'] if module['name']=='garbro')['sources'][0]={'type':'git','url':'https://github.com/Love-in-idleness/GARbro-lazer.git','commit':args.revision}
    (args.output/(APP+'.json')).write_text(json.dumps(manifest,indent=2)+'\n')
    (args.output/'flathub.json').write_bytes((ROOT/'packaging/flatpak/flathub.json').read_bytes())
    print(f'Prepared {len(sources)} pinned NuGet sources; run Flatpak validation before submission.')

if __name__=='__main__':main()
