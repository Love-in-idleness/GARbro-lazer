import importlib.util
import unittest
import tempfile
import struct
import xml.etree.ElementTree as ET
from pathlib import Path

spec=importlib.util.spec_from_file_location('installer_support',Path(__file__).resolve().parents[1]/'installer_support.py')
installer=importlib.util.module_from_spec(spec);spec.loader.exec_module(installer)


class InstallerTests(unittest.TestCase):
    def test_versions_preserve_prerelease_to_stable_upgrade_order(self):
        releases=['0.2.0-alpha.1','0.2.0-beta.1','0.2.0-preview.1','0.2.0-preview.10','0.2.0-rc.1','0.2.0','0.2.1-preview.1']
        windows=[tuple(map(int,installer.versions(v)[1].split('.')))for v in releases]
        self.assertEqual(windows,sorted(windows))
        self.assertEqual('0.2.0~preview.1',installer.versions(releases[2])[0])
        for version in ('../0.2.0','0.2.0-preview.10000','0.2.0\ncommand'):
            with self.subTest(version=version),self.assertRaises(ValueError):installer.versions(version)

    def test_associations_exclude_common_media_and_executable_types(self):
        extensions=installer.associations()
        for extension in ('xp3','rpa','pfs','ypf','lim'):self.assertIn(extension,extensions)
        self.assertFalse(set(extensions)&{'exe','dll','lnk','dat','bin','png','jpg','wav','mp3','zip','txt','md','lib','psb','psd','xfl','pac'})
        desktop=installer.desktop_entry(extensions)
        self.assertIn('Terminal=false\n',desktop)
        self.assertIn('Exec=garbro-desktop %F\n',desktop)

    def test_msix_uses_gui_entry_and_quoted_file_activation(self):
        manifest=installer.msix_manifest('0.2.0-preview.1')
        root=ET.fromstring(manifest)
        for prefix in root.get('IgnorableNamespaces').split():self.assertIn('xmlns:'+prefix+'=',manifest)
        ns={'m':'http://schemas.microsoft.com/appx/manifest/foundation/windows10','u':'http://schemas.microsoft.com/appx/manifest/uap/windows10','u2':'http://schemas.microsoft.com/appx/manifest/uap/windows10/2','u3':'http://schemas.microsoft.com/appx/manifest/uap/windows10/3'}
        self.assertEqual('desktop/GARbro.Desktop.exe',root.find('.//m:Application',ns).get('Executable'))
        self.assertEqual('"%1"',root.find('.//u3:Verb',ns).get('Parameters'))
        self.assertEqual('"%1"',root.find('.//u3:FileTypeAssociation',ns).get('Parameters'))
        self.assertIsNotNone(root.find('.//u3:FileTypeAssociation/u:SupportedFileTypes/u:FileType',ns))
        self.assertIsNotNone(root.find('.//u3:FileTypeAssociation/u2:SupportedVerbs/u3:Verb',ns))
        identity=root.find('m:Identity',ns)
        self.assertEqual(installer.PUBLISHER,identity.get('Publisher'))


class ArchitectureAndStoreTests(unittest.TestCase):
    def test_small_ico_entries_include_transparent_alpha_and_an_and_mask(self):
        data=(installer.ROOT/'modern/GARbro.UI/Assets/app-icon.ico').read_bytes()
        self.assertEqual((0,1),struct.unpack_from('<HH',data))
        for index in range(struct.unpack_from('<H',data,4)[0]):
            width,height,_,_,_,_,length,offset=struct.unpack_from('<BBBBHHII',data,6+16*index)
            if width==0:continue  # 256px PNG
            self.assertEqual(40,struct.unpack_from('<I',data,offset)[0])
            self.assertEqual(0,data[offset+40+3])  # transparent corner
            mask=data[offset+40+width*height*4:offset+length]
            self.assertTrue(mask[0]&0x80)
            self.assertTrue(any(value!=255 for value in mask))

    def test_taskbar_icons_have_unplated_variants_in_both_shell_themes(self):
        with tempfile.TemporaryDirectory()as directory:
            assets=Path(directory)/'Assets';installer.windows_icon_assets(assets)
            for size in (16,24,32,48,64,256):
                source=installer.ROOT/f'modern/GARbro.UI/Assets/app-icon-{size}.png'
                for suffix in ('','_altform-unplated','_altform-lightunplated'):
                    self.assertEqual(source.read_bytes(),(assets/f'Square44x44Logo.targetsize-{size}{suffix}.png').read_bytes())
            for channel in ('preview','store'):
                manifest=ET.fromstring(installer.msix_manifest('1.0.0','win-x64',channel))
                self.assertEqual('transparent',manifest.find('.//{*}VisualElements').get('BackgroundColor'))

    def test_both_windows_architectures_keep_the_preview_identity(self):
        for rid,architecture in [('win-x64','x64'),('win-arm64','arm64')]:
            root=ET.fromstring(installer.msix_manifest('0.3.0-preview.1',rid))
            identity=root.find('{*}Identity')
            self.assertEqual(architecture,identity.get('ProcessorArchitecture'))
            self.assertEqual('LoveInIdleness.GARbro',identity.get('Name'))

    def test_store_identity_matches_the_registered_pfn_and_reserved_revision(self):
        expected=installer.store_identity()
        self.assertEqual('Love-in-idleness.GARbro_j546ht2g1e7f4',expected['packageFamilyName'])
        self.assertEqual('9NP39X1TRVNM',expected['storeId'])
        for rid in ('win-x64','win-arm64'):
            root=ET.fromstring(installer.msix_manifest('0.3.0-preview.1',rid,'store'))
            identity=root.find('{*}Identity')
            self.assertEqual(expected['name'],identity.get('Name'))
            self.assertEqual(expected['publisher'],identity.get('Publisher'))
            self.assertEqual(installer.store_version(),identity.get('Version'))
            self.assertEqual('0',identity.get('Version').split('.')[-1])
            self.assertEqual(expected['publisherDisplayName'],root.find('{*}Properties/{*}PublisherDisplayName').text)

    def test_android_release_order_is_monotonic(self):
        versions=['0.2.0-preview.1','0.3.0-alpha.1','0.3.0-beta.1','0.3.0-preview.1','0.3.0-preview.2','0.3.0-rc.1','0.3.0','0.3.1-alpha.1','0.4.0']
        numbers=[installer.android_version(v)for v in versions]
        self.assertEqual(sorted(numbers),numbers)
        self.assertEqual(len(numbers),len(set(numbers)))
        with self.assertRaises(ValueError):installer.android_version('0.3.0-preview.1000')
