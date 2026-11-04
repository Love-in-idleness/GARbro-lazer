#!/usr/bin/env python3
"""Build unsigned framework-dependent preview archives for one explicit RID."""
import argparse
import json
import os
import re
import plistlib
import shutil
import subprocess
import tarfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path
from installer_support import build_deb, build_msix
from installer_support import associations, mac_document_types

ROOT=Path(__file__).resolve().parents[1]


def write_zip(stage, archive):
    # Deterministic dependencies can have a 1980-01-01 UTC timestamp,
    # which is still 1979 in a Windows runner's local timezone.
    with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED,strict_timestamps=False)as destination:
        for file in sorted(stage.rglob('*')):
            if file.is_file():destination.write(file,file.relative_to(stage.parent))


def licenses(destination):
    records={}
    directory=destination/'licenses'
    directory.mkdir(parents=True,exist_ok=True)
    for project in ('GARbro.Cli','GARbro.Desktop'):
        assets=json.loads((ROOT/'modern'/project/'obj/project.assets.json').read_text())
        for name,library in assets['libraries'].items():
            if library['type']!='package':continue
            record={'package':name,'sha512':library.get('sha512')}
            package=next((Path(base)/library['path'] for base in assets['packageFolders'] if (Path(base)/library['path']).exists()),None)
            if package:
                nuspec=next(package.glob('*.nuspec'),None)
                if nuspec:
                    xml=ET.parse(nuspec)
                    license_node=next((n for n in xml.iter() if n.tag.split('}')[-1]=='license'),None)
                    if license_node is not None:record['license']=license_node.text
                    for tag in ('authors','copyright','projectUrl'):
                        node=next((n for n in xml.iter() if n.tag.split('}')[-1]==tag),None)
                        if node is not None:record[tag]=node.text
                    target=directory/name.replace('/','-')/'package.nuspec'
                    target.parent.mkdir(parents=True,exist_ok=True)
                    shutil.copy2(nuspec,target)
                for file in package.rglob('*'):
                    if file.is_file() and ('license' in file.name.lower() or 'licence' in file.name.lower()
                        or file.name.lower().startswith(('notice','copying','copyright'))):
                        target=directory/name.replace('/','-')/file.relative_to(package)
                        target.parent.mkdir(parents=True,exist_ok=True)
                        shutil.copy2(file,target)
            records[name]=record
    (destination/'dependencies.json').write_text(json.dumps(sorted(records.values(),key=lambda r:r['package']),indent=2)+'\n')
    shutil.copy2(ROOT/'LICENSE',destination/'LICENSE')
    shutil.copy2(ROOT/'docs/third-party.md',destination/'third-party.md')
    shutil.copytree(ROOT/'docs/licenses',directory/'source-notices')
    shutil.copy2(ROOT/'docs/compatibility.md',destination/'compatibility.md')
    shutil.copytree(ROOT/'docs/compatibility',destination/'compatibility')
    shutil.copy2(ROOT/'docs/migration-status.md',destination/'migration-status.md')
    for name in ('linux-desktop.md','install.md','desktop-parity.md'):
        shutil.copy2(ROOT/'docs'/name,destination/name)
    for file in list((ROOT/'docs').glob('manual.*.md')) + list((ROOT/'docs').glob('troubleshoot.*.md')): shutil.copy2(file,destination/file.name)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rid',required=True,choices=['linux-x64','linux-arm64','win-x64','win-arm64','osx-x64','osx-arm64'])
    parser.add_argument('--output',default='artifacts')
    parser.add_argument('--dotnet',default='dotnet')
    parser.add_argument('--version',default=os.environ.get('GARBRO_PACKAGE_VERSION','1.0.3'))
    parser.add_argument('--installer',action='store_true',help='Also build DEB or signed test MSIX for the selected architecture')
    args=parser.parse_args()
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?',args.version):parser.error('Invalid package version')
    output=Path(args.output).resolve();stage=output/('garbro-'+args.version+'-'+args.rid)
    if stage.exists():shutil.rmtree(stage)
    stage.mkdir(parents=True)
    for project,folder in [('GARbro.Cli','cli'),('GARbro.Desktop','desktop')]:
        subprocess.run([args.dotnet,'publish',str(ROOT/'modern'/project/(project+'.csproj')),
            '-c','Release','-r',args.rid,'--self-contained','false','-o',str(stage/folder),
            '-p:Version='+args.version,'-m:1','-nr:false','-p:UseSharedCompilation=false','--nologo','-v:q'],cwd=ROOT,check=True)
        if args.rid.startswith('linux-'):
            # Linux follows the distribution's SDL2 ABI and security updates.
            (stage/folder/'libSDL2-2.0.so').unlink(missing_ok=True)
        else:
            helpers=ROOT/'artifacts/media'/args.rid
            if not helpers.is_dir():raise RuntimeError('Build video helpers first: python tools/build-media.py --rid '+args.rid)
            shutil.copytree(helpers,stage/folder/'media')
    if args.rid.startswith('osx-'):
        desktop=stage/'desktop';app=stage/'GARbro.app/Contents';app.mkdir(parents=True)
        resources=app/'Resources';resources.mkdir()
        shutil.move(str(desktop),resources/'app')
        executables=app/'MacOS';executables.mkdir()
        project=ROOT/'modern/GARbro.Desktop/GARbro.Desktop.csproj'
        template=subprocess.check_output([args.dotnet,'msbuild',str(project),'-t:_GetAppHostPaths','-getProperty:AppHostSourcePath',
            '-p:RuntimeIdentifier='+args.rid,'-p:Configuration=Release','-nologo','-v:q'],text=True).strip().splitlines()[-1]
        if not Path(template).is_file():raise RuntimeError('Cannot locate the .NET macOS apphost template')
        subprocess.run([args.dotnet,'msbuild',str(ROOT/'tools/create-mac-host.proj'),'-t:CreateMacHost','-nologo',
            '-p:Template='+template,'-p:Destination='+str(executables/'GARbro.Desktop'),
            '-p:MacManagedAssembly='+str(resources/'app/GARbro.Desktop.dll'),
            '-p:ManagedPath=../Resources/app/GARbro.Desktop.dll','-p:SignHost=true'],check=True)
        (resources/'app/GARbro.Desktop').unlink()
        documents, imported_types = mac_document_types(associations())
        with (app/'Info.plist').open('wb')as stream:
            plistlib.dump({'CFBundleIdentifier':'org.garbro.desktop','CFBundleName':'GARbro',
                'CFBundleDisplayName':'GARbro','CFBundleExecutable':'GARbro.Desktop','CFBundlePackageType':'APPL',
                'CFBundleIconFile':'app-icon.icns',
                'CFBundleVersion':args.version.split('-')[0],'CFBundleShortVersionString':args.version.split('-')[0],'NSHighResolutionCapable':True,
                'LSMinimumSystemVersion':'14.0',
                'CFBundleLocalizations':['en','zh-Hans','ru','ja'],
                'CFBundleDocumentTypes':documents,'UTImportedTypeDeclarations':imported_types},stream)
        shutil.copy2(ROOT/'modern/GARbro.UI/Assets/app-icon.icns',resources/'app-icon.icns')
        # Managed PE assemblies belong in Resources, not the Mach-O code directory.
        # Sign every native helper/library before sealing the outer application.
        magic={b'\xfe\xed\xfa\xce',b'\xce\xfa\xed\xfe',b'\xfe\xed\xfa\xcf',b'\xcf\xfa\xed\xfe',b'\xca\xfe\xba\xbe',b'\xbe\xba\xfe\xca',b'\xca\xfe\xba\xbf',b'\xbf\xba\xfe\xca'}
        for file in sorted(app.rglob('*')):
            if file.is_file():
                with file.open('rb')as stream:header=stream.read(4)
                if header in magic:subprocess.run(['codesign','--force','--sign','-',str(file)],check=True)
        subprocess.run(['codesign','--force','--deep','--sign','-',str(stage/'GARbro.app')],check=True)
    licenses(stage)
    (stage/'README.txt').write_text(f'GARbro {args.version} ({args.rid})\nRequires the .NET 10 runtime.\n'
        'This archive is unsigned. See compatibility.md for verified formats and known limits.\n'
        'Run cli/garbro for commands, or the desktop executable / macOS app bundle.\n')
    archive=output/(stage.name+('.zip'if args.rid.startswith('win-')else '.tar.gz'))
    if archive.suffix=='.zip':
        write_zip(stage,archive)
    else:
        with tarfile.open(archive,'w:gz')as z:z.add(stage,arcname=stage.name)
    print(archive)
    if args.installer:
        if args.rid.startswith('linux-'): print(build_deb(stage,output,args.version,args.rid))
        elif args.rid.startswith('win-'): print(build_msix(stage,output,args.version,args.rid))
        elif args.rid.startswith('osx-'):
            dmg=output/(stage.name+'.dmg')
            dmg.unlink(missing_ok=True)
            disk=output/('dmg-root-'+args.rid)
            if disk.exists():shutil.rmtree(disk)
            shutil.copytree(stage,disk/'GARbro')
            (disk/'Applications').symlink_to('/Applications',target_is_directory=True)
            subprocess.run(['hdiutil','create','-volname','GARbro','-srcfolder',str(disk),'-ov','-format','UDZO',str(dmg)],check=True)
            print(dmg)

if __name__=='__main__':main()
