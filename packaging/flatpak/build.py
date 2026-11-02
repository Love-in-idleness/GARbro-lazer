#!/usr/bin/env python3
"""Offline build inside the Freedesktop SDK; host runtimes are never used."""
import importlib.util
import os
import platform
import shutil
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

root=Path.cwd()
rid={'x86_64':'linux-x64','aarch64':'linux-arm64'}[platform.machine()]
version=os.environ.get('GARBRO_PACKAGE_VERSION','1.0.1')
for project,folder in [('GARbro.Desktop','desktop'),('GARbro.Worker','desktop'),('GARbro.Cli','cli')]:
    path=f'modern/{project}/{project}.csproj'
    options=['-r',rid,'-p:SelfContained=true','-p:RuntimeFrameworkVersion=10.0.14','-p:NuGetAudit=false']
    subprocess.run(['dotnet','restore',path,*options,'--source',str(root/'nuget-sources')],check=True)
    subprocess.run(['dotnet','publish',path,*options,'--no-restore','-c','Release','-o',f'/app/lib/garbro/{folder}',
                    '-p:Version='+version,'-m:1','-nr:false','-p:UseSharedCompilation=false'],check=True)
    # SDL is provided by the Freedesktop runtime, as distribution libraries are
    # used in the DEB. Never ship the NuGet SDL build on Linux.
    (Path('/app/lib/garbro')/folder/'libSDL2-2.0.so').unlink(missing_ok=True)
spec=importlib.util.spec_from_file_location('installer_support',root/'tools/installer_support.py')
installer=importlib.util.module_from_spec(spec);spec.loader.exec_module(installer)
app='io.github.love_in_idleness.GARbro-no-wpf'
data=Path('/app/share');binary=Path('/app/bin');binary.mkdir(parents=True,exist_ok=True)
for name,target in [('garbro-desktop','desktop/GARbro.Desktop'),('garbro','cli/garbro')]:
    (binary/name).symlink_to('/app/lib/garbro/'+target)
entries=data/'applications';entries.mkdir(parents=True,exist_ok=True)
entry=installer.desktop_entry(installer.associations()).replace(installer.APP_ID,app)
(entries/(app+'.desktop')).write_text(entry)
mime=data/'mime/packages';mime.mkdir(parents=True,exist_ok=True)
xml=ET.Element('mime-info',xmlns='http://www.freedesktop.org/standards/shared-mime-info')
for ext in installer.associations():
    node=ET.SubElement(xml,'mime-type',type='application/x-garbro-'+ext)
    ET.SubElement(node,'comment').text='GARbro game resource (.'+ext+')'
    ET.SubElement(node,'glob',pattern='*.'+ext,weight='10')
ET.ElementTree(xml).write(mime/(app+'.xml'),encoding='utf-8',xml_declaration=True)
meta=data/'metainfo';meta.mkdir(parents=True,exist_ok=True)
shutil.copy2(root/'packaging/flatpak'/ (app+'.metainfo.xml'),meta)
fonts=Path('/app/etc/fonts');fonts.mkdir(parents=True,exist_ok=True)
shutil.copy2(root/'packaging/flatpak/fontconfig.conf',fonts/'local.conf')
for size in (16,32,48,64,128,256,512):
    icons=data/f'icons/hicolor/{size}x{size}/apps';icons.mkdir(parents=True,exist_ok=True)
    shutil.copy2(root/f'modern/GARbro.UI/Assets/app-icon-{size}.png',icons/(app+'.png'))
import sys
sys.path.insert(0,str(root/'tools'))
spec=importlib.util.spec_from_file_location('package_release',root/'tools/package-release.py')
package=importlib.util.module_from_spec(spec);spec.loader.exec_module(package)
notices=data/'doc'/app;notices.mkdir(parents=True,exist_ok=True);package.licenses(notices)
