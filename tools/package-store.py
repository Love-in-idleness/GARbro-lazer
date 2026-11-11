#!/usr/bin/env python3
"""Create a self-contained, unsigned x64/ARM64 MSIX upload for Partner Center signing."""
import argparse
import importlib.util
import json
import os
import shutil
import subprocess
import zipfile
from pathlib import Path
from installer_support import ROOT, msix_manifest, store_identity, store_version, versions, windows_icon_assets, index_windows_icons
from package_layout import compact_application, install_media
import importlib


def sdk_tool(name):
    sdk=Path(os.environ['ProgramFiles(x86)'])/'Windows Kits/10/bin'
    for version in sorted(sdk.iterdir(),reverse=True):
        path=version/'x64'/name
        if path.exists():return path
    raise RuntimeError('Windows SDK tool is missing: '+name)


def build_architecture(rid,version,output):
    stage=output/('store-stage-'+rid)
    if stage.exists():shutil.rmtree(stage)
    stage.mkdir(parents=True)
    app=stage/'app'
    for project in ('GARbro.Desktop','GARbro.Worker','GARbro.Cli'):
        subprocess.run(['dotnet','publish',str(ROOT/'modern'/project/(project+'.csproj')),'-c','Release','-r',rid,
            '--self-contained','true','-p:RuntimeFrameworkVersion=10.0.14','-o',str(app),'-p:Version='+version,'-p:UseSharedCompilation=false','-m:1','-nr:false','--nologo'],check=True)
    runtime_report=output/('runtime-compaction-'+rid+'.json')
    subprocess.run(['dotnet',str(ROOT/'tools/RuntimeCompactor/bin/Release/net10.0/RuntimeCompactor.dll'),str(app),str(runtime_report)],check=True)
    runtime=json.loads(runtime_report.read_text())
    compact_application(app)
    helpers=importlib.import_module('build-media').build(rid,ROOT/'artifacts/media')
    install_media(helpers,app/'media')
    # Each apphost, including the packaged decoder, must use the local runtime.
    for name in ('GARbro.Desktop','GARbro.Worker','garbro'):
        runtime=json.loads((app/(name+'.runtimeconfig.json')).read_text())['runtimeOptions']
        if 'framework' in runtime or 'frameworks' in runtime or not runtime.get('includedFrameworks'):
            raise RuntimeError('Store apphost is not self-contained: '+name)
        if not (app/'coreclr.dll').is_file():raise RuntimeError('Store runtime is missing')
    spec=importlib.util.spec_from_file_location('package_release',ROOT/'tools/package-release.py')
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);module.licenses(stage)
    windows_icon_assets(stage/'Assets')
    (stage/'AppxManifest.xml').write_text(msix_manifest(version,rid,'store'),encoding='utf-8')
    index_windows_icons(stage)
    # Both the GUI and decoder retain package identity when launching the worker apphost.
    (app/'store-channel.json').write_text(json.dumps(store_identity(),indent=2)+'\n')
    packages=output/'store-packages';packages.mkdir(exist_ok=True)
    package=packages/f'garbro-{version}-store-{rid}.msix'
    subprocess.run([str(sdk_tool('makeappx.exe')),'pack','/d',str(stage),'/p',str(package),'/o'],check=True)
    with zipfile.ZipFile(package) as contents:
        installed=sum(file.file_size for file in contents.infolist())
    report=dict(architecture=rid,packageBytes=package.stat().st_size,installedBytes=installed,
        runtimeCompactionBytesSaved=runtime['bytesSaved'],runtimeInventoryVerified=runtime['metadataAndResourcesPreserved'],
        targetBytes=100_000_000,downloadTargetMet=package.stat().st_size<=100_000_000,installedTargetMet=installed<=100_000_000)
    (output/('store-size-'+rid+'.json')).write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report))
    return package


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version',default='1.0.6')
    parser.add_argument('--output',type=Path,default=Path('artifacts'))
    args=parser.parse_args();versions(args.version)
    output=args.output.resolve();output.mkdir(parents=True,exist_ok=True)
    identity=store_identity();number=store_version(args.version)
    subprocess.run(['dotnet','build',str(ROOT/'tools/RuntimeCompactor/RuntimeCompactor.fsproj'),'-c','Release',
        '-p:UseSharedCompilation=false','-m:1','-nr:false','--nologo'],check=True)
    for rid in ('win-x64','win-arm64'):build_architecture(rid,args.version,output)
    bundle=output/f'garbro-{args.version}-store.msixbundle'
    subprocess.run([str(sdk_tool('makeappx.exe')),'bundle','/d',str(output/'store-packages'),'/p',str(bundle),'/bv',number,'/o'],check=True)
    upload=output/f'garbro-{args.version}-store.msixupload'
    with zipfile.ZipFile(upload,'w',compression=zipfile.ZIP_DEFLATED)as archive:archive.write(bundle,bundle.name)
    (output/'store-submission.json').write_text(json.dumps(dict(**identity,version=number,applicationVersion=args.version,
        architectures=['x64','arm64'],selfContained=True,submissionState='prepared'),indent=2)+'\n')
    with zipfile.ZipFile(output/'store-listing.zip','w',compression=zipfile.ZIP_DEFLATED) as listing:
        for path in (ROOT/'docs/store').rglob('*'):
            if path.is_file():listing.write(path,path.relative_to(ROOT/'docs'))
        listing.write(ROOT/'docs/privacy.md','privacy.md')
        for language in ('en-US','zh-CN'):
            listing.write(ROOT/f'docs/screenshots/video-{language}.png',f'screenshots/video-{language}.png')
        for language,short in [('en-US','en'),('zh-CN','zh'),('ru-RU','ru'),('ja-JP','ja')]:
            listing.write(ROOT/f'docs/screenshots/desktop-{short}-light.png',f'screenshots/desktop-{language}.png')
    print(upload)


if __name__=='__main__':main()
