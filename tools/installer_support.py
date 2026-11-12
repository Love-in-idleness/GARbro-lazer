"""Shared installer identity, file associations and monotonic package versions."""
import json
import base64
import hashlib
import os
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP_ID = 'org.garbro.GARbro'
PUBLISHER = 'CN=GARbro-no-wpf-Test'
IDENTITY = 'LoveInIdleness.GARbro'
COMMON = set('zip cab rar 7z tar gz bz2 xz iso dat bin exe dll com bat cmd ps1 vbs js lnk url msi msix appx reg scr cpl so dylib desktop sh py txt json xml csv html htm pdf bmp png jpg jpeg gif tif tiff tga ico icns svg webp avif pcx ppm pgm pbm wav mp3 ogg opus flac aif aiff wma mid midi m4a mp4 avi mkv webm mov'.split())
EXCLUDED_ASSOCIATIONS = (COMMON - set('zip cab rar 7z tar gz bz2 xz iso dat bin'.split())) | set('app jar md psb psd mpg mpeg'.split())


def versions(version):
    match = re.fullmatch(r'(\d+)\.(\d+)\.(\d+)(?:-(alpha|beta|preview|rc)\.(\d+))?', version)
    if not match:
        raise ValueError('Installer version must be SemVer with an optional alpha/beta/preview/rc number')
    major, minor, patch = map(int, match.group(1, 2, 3))
    if max(major, minor, patch) > 65535:
        raise ValueError('MSIX version components must not exceed 65535')
    channel, number = match.group(4, 5)
    if channel:
        number = int(number)
        if not 1 <= number <= 9999:
            raise ValueError('Prerelease number must be between 1 and 9999')
        revision = {'alpha': 10000, 'beta': 20000, 'preview': 30000, 'rc': 40000}[channel] + number
        deb = f'{major}.{minor}.{patch}~{channel}.{number}'
    else:
        revision, deb = 65535, version
    return deb, f'{major}.{minor}.{patch}.{revision}'


def android_version(version):
    versions(version)
    match = re.fullmatch(r'(\d+)\.(\d+)\.(\d+)(?:-(alpha|beta|preview|rc)\.(\d+))?', version)
    major, minor, patch = map(int, match.group(1, 2, 3))
    channel, number = match.group(4, 5)
    if major > 20 or minor > 99 or patch > 99 or number and int(number) > 999:
        raise ValueError('Android versions require major <= 20, minor/patch <= 99 and prerelease <= 999')
    revision = {'alpha':1000, 'beta':2000, 'preview':3000, 'rc':4000}.get(channel, 9999)
    return ((major * 100 + minor) * 100 + patch) * 10000 + revision + (int(number) if number else 0)


def associations():
    baseline = json.loads((ROOT/'docs/compatibility/baseline-runtime.json').read_text())
    known = {ext.lower() for item in baseline for ext in item['extensions']}
    selected = json.loads((ROOT/'tools/installers/associations.json').read_text())['extensions']
    if any(ext not in known or ext in EXCLUDED_ASSOCIATIONS or not re.fullmatch(r'\w{1,32}', ext) for ext in selected):
        raise ValueError('Invalid game-resource file association')
    return sorted(set(selected))


def association_token(extension):
    return extension.encode('idna').decode('ascii')


def association_mimes(extensions):
    aliases = json.loads((ROOT/'tools/installers/associations.json').read_text())['mimeAliases']
    return sorted({'application/octet-stream'} | {'application/x-garbro-'+association_token(ext) for ext in extensions} |
                  {mime for ext in extensions for mime in aliases.get(ext, [])})


def mime_document(extensions):
    xml = ET.Element('mime-info', xmlns='http://www.freedesktop.org/standards/shared-mime-info')
    for ext in extensions:
        item = ET.SubElement(xml, 'mime-type', type='application/x-garbro-'+association_token(ext))
        ET.SubElement(item, 'comment').text = 'GARbro game resource (.'+ext+')'
        for language,label in [('zh_CN','GARbro 游戏资源'),('ru','Игровые ресурсы GARbro'),('ja','GARbro ゲームリソース')]:
            ET.SubElement(item, 'comment', {'{http://www.w3.org/XML/1998/namespace}lang':language}).text = label+' (.'+ext+')'
        ET.SubElement(item, 'sub-class-of', type='application/octet-stream')
        # Keep existing MIME recognition and defaults; desktop_entry also handles their types.
        ET.SubElement(item, 'glob', pattern='*.'+ext, weight='10')
    return ET.ElementTree(xml)


def mac_document_types(extensions, filename_types=None):
    if filename_types is None:
        if sys.platform == 'darwin':
            from macos_association_probe import filename_types
        else:
            filename_types = lambda extension: []
    aliases = json.loads((ROOT/'tools/installers/associations.json').read_text())['utiAliases']
    types, imports = [], []
    for ext in extensions:
        identifier = aliases.get(ext, 'org.garbro.resource.'+association_token(ext))
        types.append(identifier)
        # Existing types (for example MacBinary .bin) take precedence over imported
        # declarations. Advertise their Viewer role too, preserving default handlers.
        types.extend(value for value in filename_types(ext) if value and not value.startswith('dyn.'))
        if ext not in aliases:
            imports.append({'UTTypeIdentifier':identifier, 'UTTypeDescription':'Game resource (.'+ext+')',
                'UTTypeConformsTo':['public.data'], 'UTTypeTagSpecification':{'public.filename-extension':[ext]}})
    return ([{'CFBundleTypeName':'Game resources','CFBundleTypeRole':'Viewer','LSHandlerRank':'Alternate',
              'CFBundleTypeExtensions':extensions,'LSItemContentTypes':sorted(set(types))}], imports)


def desktop_entry(extensions):
    mime = ';'.join(association_mimes(extensions))+';'
    return ('[Desktop Entry]\nType=Application\nName=GARbro\n'
            'Comment=Browse, preview and extract game resources\n'
            'Comment[zh_CN]=浏览、预览与提取游戏资源\n'
            'Comment[ru]=Просмотр и извлечение игровых ресурсов\n'
            'Comment[ja]=ゲームのリソースを閲覧・プレビュー・抽出\n'
            'Exec=garbro-desktop %F\nTryExec=garbro-desktop\n'
            f'Icon={APP_ID}\nTerminal=false\nCategories=Utility;Graphics;\n'
            f'StartupWMClass=GARbro.Desktop\nMimeType={mime}\n')


def build_deb(stage, output, version, rid='linux-x64'):
    architecture = {'linux-x64': 'amd64', 'linux-arm64': 'arm64'}[rid]
    deb_version, _ = versions(version)
    root = output/f'deb-root-{version}-{architecture}'
    if root.exists(): shutil.rmtree(root)
    app = root/'opt/garbro'
    shutil.copytree(stage/'app', app/'app')
    docs = root/'usr/share/doc/garbro'
    docs.mkdir(parents=True)
    for name in ('LICENSE', 'third-party.md', 'dependencies.json', 'compatibility.md', 'migration-status.md', 'linux-desktop.md', 'install.md', 'desktop-parity.md'):
        shutil.copy2(stage/name, docs/name)
    for file in list(stage.glob('manual.*.md')) + list(stage.glob('troubleshoot.*.md')): shutil.copy2(file, docs/file.name)
    shutil.copytree(stage/'licenses', docs/'licenses')
    shutil.copytree(stage/'compatibility', docs/'compatibility')
    binary = root/'usr/bin'; binary.mkdir(parents=True)
    (binary/'garbro').symlink_to('/opt/garbro/app/garbro')
    (binary/'garbro-desktop').symlink_to('/opt/garbro/app/GARbro.Desktop')
    entries = root/'usr/share/applications'; entries.mkdir(parents=True)
    extensions = associations()
    (entries/(APP_ID+'.desktop')).write_text(desktop_entry(extensions))
    integration=root/'usr/share/garbro';integration.mkdir(parents=True)
    shutil.copy2(ROOT/'tools/installers/associations.json',integration/'associations.json')
    shutil.copy2(ROOT/'tools/installers/update_associations.py',integration/'update_associations.py')
    mime = root/'usr/share/mime/packages'; mime.mkdir(parents=True)
    mime_document(extensions).write(mime/(APP_ID+'.xml'), encoding='utf-8', xml_declaration=True)
    for size in (16, 32, 48, 64, 128, 256, 512):
        icon = root/f'usr/share/icons/hicolor/{size}x{size}/apps'; icon.mkdir(parents=True)
        shutil.copy2(ROOT/f'modern/GARbro.UI/Assets/app-icon-{size}.png', icon/(APP_ID+'.png'))
    control = root/'DEBIAN'; control.mkdir()
    depends = ('dotnet-runtime-10.0 (>= 10.0), libc6 (>= 2.35), libstdc++6, libfontconfig1, libfreetype6, '
               'libx11-6, libx11-xcb1, libxcb1, libxcb-render0, libxcb-shm0, libxcb-xfixes0, '
               'libice6, libsm6, libegl1, libgles2, libwayland-client0, libwayland-egl1, libwayland-cursor0, libxkbcommon0, '
               'libgomp1, libpulse0, libsdl2-2.0-0 (>= 2.0.22), ffmpeg, libglib2.0-bin, desktop-file-utils, shared-mime-info, xdg-utils, python3 (>= 3.9)')
    (control/'control').write_text(f'Package: garbro\nVersion: {deb_version}\nArchitecture: {architecture}\n'
        'Section: graphics\nPriority: optional\nMaintainer: GARbro-lazer contributors\n'
        f'Depends: {depends}\nRecommends: xdg-desktop-portal\n'
        'Homepage: https://github.com/Love-in-idleness/GARbro-lazer\n'
        'Description: Game resource browser and extractor\n'
        ' Browse game archives, preview resources and safely extract their contents.\n')
    for name in ('postinst', 'postrm'):
        script = control/name
        script.write_text('#!/bin/sh\nset -e\n'+
            ('python3 /usr/share/garbro/update_associations.py\n' if name=='postinst' else '')+
            'if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database /usr/share/applications || true; fi\n'
            'if command -v update-mime-database >/dev/null 2>&1; then update-mime-database /usr/share/mime || true; fi\n')
        script.chmod(0o755)
    for file in root.rglob('*'):
        if file.is_dir(): file.chmod(0o755)
        elif not file.is_symlink() and file not in (control/'postinst', control/'postrm'):
            file.chmod(0o755 if file.name in ('garbro', 'GARbro.Desktop', 'GARbro.Worker') else 0o644)
    package = output/f'garbro-{version}-{rid}.deb'
    subprocess.run(['dpkg-deb', '--build', '--root-owner-group', str(root), str(package)], check=True)
    return package


def store_identity():
    identity = json.loads((ROOT/'tools/installers/store-identity.json').read_text())
    alphabet = '0123456789abcdefghjkmnpqrstvwxyz'
    standard = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
    encoded = base64.b32encode(hashlib.sha256(identity['publisher'].encode('utf-16le')).digest()[:8]).decode().rstrip('=')
    family = identity['name'] + '_' + ''.join(alphabet[standard.index(c)] for c in encoded)
    if family != identity['packageFamilyName']:
        raise ValueError('Store identity does not match the registered PFN')
    return identity


def store_version(application_version=None):
    number = (ROOT/'release/store-version.txt').read_text().strip()
    if not re.fullmatch(r'\d+\.\d+\.\d+\.0', number) or any(int(part) > 65535 for part in number.split('.')):
        raise ValueError('Store versions require four components and a reserved zero revision')
    if application_version is not None:
        minimum=application_version.split('-',1)[0]
        if not re.fullmatch(r'\d+\.\d+\.\d+',minimum):raise ValueError('Invalid application version')
        if tuple(map(int,number.split('.')[:3])) < tuple(map(int,minimum.split('.'))):
            raise ValueError('Store package version is behind the application version; update release/store-version.txt')
    return number


def msix_manifest(version, rid='win-x64', channel='preview'):
    architecture = {'win-x64': 'x64', 'win-arm64': 'arm64'}[rid]
    _, number = versions(version)
    identity = store_identity() if channel == 'store' else {'name': IDENTITY, 'publisher': PUBLISHER, 'publisherDisplayName': 'GARbro-lazer'}
    if channel == 'store': number = store_version(version)
    namespaces = {'': 'http://schemas.microsoft.com/appx/manifest/foundation/windows10',
        'uap': 'http://schemas.microsoft.com/appx/manifest/uap/windows10',
        'uap3': 'http://schemas.microsoft.com/appx/manifest/uap/windows10/3',
        'uap5': 'http://schemas.microsoft.com/appx/manifest/uap/windows10/5',
        'rescap': 'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities'}
    def tag(prefix, name): return '{'+namespaces[prefix]+'}'+name
    for prefix, uri in namespaces.items(): ET.register_namespace(prefix, uri)
    root = ET.Element(tag('', 'Package'), IgnorableNamespaces='uap uap3 uap5 rescap')
    ET.SubElement(root, tag('', 'Identity'), Name=identity['name'], Publisher=identity['publisher'], Version=number, ProcessorArchitecture=architecture)
    properties = ET.SubElement(root, tag('', 'Properties'))
    for key, value in [('DisplayName','GARbro' if channel == 'store' or '-' not in version else 'GARbro Preview'),('PublisherDisplayName',identity['publisherDisplayName']),('Logo','Assets/StoreLogo.png')]: ET.SubElement(properties, tag('', key)).text = value
    resources = ET.SubElement(root, tag('', 'Resources'))
    for language in ('en-us','zh-cn','ru-ru','ja-jp'): ET.SubElement(resources, tag('', 'Resource'), Language=language)
    dependencies = ET.SubElement(root, tag('', 'Dependencies'))
    ET.SubElement(dependencies, tag('', 'TargetDeviceFamily'), Name='Windows.Desktop', MinVersion='10.0.19041.0', MaxVersionTested='10.0.26100.0')
    apps = ET.SubElement(root, tag('', 'Applications'))
    app = ET.SubElement(apps, tag('', 'Application'), Id='GARbro', Executable='app/GARbro.Desktop.exe', EntryPoint='Windows.FullTrustApplication')
    ET.SubElement(app, tag('uap', 'VisualElements'), DisplayName='GARbro', Description='Game resource browser',
        BackgroundColor='transparent', Square150x150Logo='Assets/Square150x150Logo.png', Square44x44Logo='Assets/Square44x44Logo.png')
    extensions = ET.SubElement(app, tag('', 'Extensions'))
    # A standard desktop association inherits the application's full-trust target.
    extension = ET.SubElement(extensions, tag('uap', 'Extension'), Category='windows.fileTypeAssociation')
    association = ET.SubElement(extension, tag('uap3', 'FileTypeAssociation'), Name='garbroresources', Parameters='"%1"', MultiSelectModel='Player')
    ET.SubElement(association, tag('uap', 'DisplayName')).text = 'GARbro game resources'
    types = ET.SubElement(association, tag('uap', 'SupportedFileTypes'))
    for ext in associations(): ET.SubElement(types, tag('uap', 'FileType')).text = '.'+ext
    alias = ET.SubElement(extensions, tag('uap5', 'Extension'), Category='windows.appExecutionAlias', Executable='app/garbro.exe', EntryPoint='Windows.FullTrustApplication')
    ET.SubElement(ET.SubElement(alias, tag('uap5', 'AppExecutionAlias')), tag('uap5', 'ExecutionAlias'), Alias='garbro.exe')
    capabilities = ET.SubElement(root, tag('', 'Capabilities'))
    ET.SubElement(capabilities, tag('rescap', 'Capability'), Name='runFullTrust')
    return ET.tostring(root, encoding='unicode', xml_declaration=False)


def build_msix(stage, output, version, rid='win-x64'):
    architecture = {'win-x64': 'x64', 'win-arm64': 'arm64'}[rid]
    root = output/f'msix-root-{version}-{architecture}'
    if root.exists(): shutil.rmtree(root)
    root.mkdir()
    for folder in ('app', 'licenses', 'compatibility'): shutil.copytree(stage/folder, root/folder)
    for name in ('LICENSE','third-party.md','dependencies.json','compatibility.md'): shutil.copy2(stage/name, root/name)
    windows_icon_assets(root/'Assets')
    (root/'AppxManifest.xml').write_text(msix_manifest(version, rid), encoding='utf-8')
    index_windows_icons(root)
    subprocess.run(['powershell','-NoProfile','-ExecutionPolicy','Bypass','-File',str(ROOT/'tools/package-msix.ps1'),
        '-Stage',str(root),'-Output',str(output),'-Version',version,'-Architecture',architecture], check=True)
    return output/f'garbro-{version}-{rid}.msix'


def windows_icon_assets(assets):
    """Provide shell icons without Windows' coloured application plate, at native sizes."""
    assets.mkdir()
    for name, size in [('StoreLogo',50),('Square150x150Logo',150),('Square44x44Logo',44)]:
        shutil.copy2(ROOT/f'modern/GARbro.UI/Assets/app-icon-{size}.png', assets/(name+'.png'))
    for size in (16,24,32,48,64,256):
        for suffix in ('', '_altform-unplated', '_altform-lightunplated'):
            shutil.copy2(ROOT/f'modern/GARbro.UI/Assets/app-icon-{size}.png',
                assets/f'Square44x44Logo.targetsize-{size}{suffix}.png')


def index_windows_icons(stage):
    """Index shell qualifiers so Windows actually selects the unplated assets."""
    sdk=Path(os.environ['ProgramFiles(x86)'])/'Windows Kits/10/bin'
    makepri=next((version/'x64/makepri.exe' for version in sorted(sdk.iterdir(),reverse=True)
        if (version/'x64/makepri.exe').is_file()),None)
    if makepri is None:raise RuntimeError('Windows SDK MakePri is missing')
    resource=stage/'resources.pri'
    subprocess.run([str(makepri),'new','/cf',str(ROOT/'tools/installers/priconfig.xml'),'/pr',str(stage),
        '/mn',str(stage/'AppxManifest.xml'),'/of',str(resource),'/o'],check=True)
    with tempfile.TemporaryDirectory(prefix='garbro-icon-index-')as directory:
        dump=Path(directory)/'resources.xml'
        subprocess.run([str(makepri),'dump','/if',str(resource),'/of',str(dump),'/dt','detailed','/o'],check=True)
        data=dump.read_text(encoding='utf-8-sig')
        for variant in ('_altform-unplated.png','_altform-lightunplated.png'):
            if variant not in data:raise RuntimeError('The Windows shell icon qualifier was not indexed: '+variant)
