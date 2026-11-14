import importlib.util
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
from zipfile import ZipFile

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
from installer_support import msix_manifest, store_identity, store_version

spec = importlib.util.spec_from_file_location('repair_store', TOOLS/'repair-store-release.py')
repair = importlib.util.module_from_spec(spec); spec.loader.exec_module(repair)
spec = importlib.util.spec_from_file_location('package_store', TOOLS/'package-store.py')
packaging = importlib.util.module_from_spec(spec); spec.loader.exec_module(packaging)


class StoreBundleTests(unittest.TestCase):
    def test_build_reports_compaction_separately_from_apphost_runtime_configuration(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            def run(command, **kwargs):
                if command[1] == 'publish':
                    app = Path(command[command.index('-o') + 1]); app.mkdir(exist_ok=True)
                    (app/'coreclr.dll').write_bytes(b'runtime')
                    for name in ('GARbro.Desktop', 'GARbro.Worker', 'garbro'):
                        (app/(name+'.runtimeconfig.json')).write_text(json.dumps({'runtimeOptions': {'includedFrameworks': [{'name': 'Microsoft.NETCore.App', 'version': '10.0.14'}]}}))
                elif command[1].endswith('RuntimeCompactor.dll'):
                    Path(command[3]).write_text(json.dumps({'bytesSaved': 12345, 'metadataAndResourcesPreserved': True}))
                elif command[0] == 'makeappx.exe':
                    with ZipFile(command[command.index('/p') + 1], 'w') as package:
                        package.writestr('app/coreclr.dll', b'runtime')
                else:
                    self.fail('Unexpected external command: '+repr(command))
            fake_spec = SimpleNamespace(loader=SimpleNamespace(exec_module=lambda module: None))
            with patch.object(packaging.subprocess, 'run', side_effect=run), \
                    patch.object(packaging, 'sdk_tool', return_value=Path('makeappx.exe')), \
                    patch.object(packaging, 'install_media'), patch.object(packaging, 'windows_icon_assets'), \
                    patch.object(packaging, 'index_windows_icons'), \
                    patch.dict(sys.modules, {'build-media': SimpleNamespace(build=lambda *args: root)}), \
                    patch.object(packaging.importlib.util, 'spec_from_file_location', return_value=fake_spec), \
                    patch.object(packaging.importlib.util, 'module_from_spec', return_value=SimpleNamespace(licenses=lambda stage: None)):
                package = packaging.build_architecture('win-x64', '1.0.6', root)
            report = json.loads((root/'store-size-win-x64.json').read_text())
            self.assertEqual(12345, report['runtimeCompactionBytesSaved'])
            self.assertTrue(report['runtimeInventoryVerified'])
            self.assertEqual(7, report['installedBytes'])
            self.assertEqual(package.stat().st_size, report['packageBytes'])

    def test_verification_accepts_shared_and_legacy_bundles_but_rejects_duplicate_runtimes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            submission = dict(**store_identity(), version=store_version())
            for layout in ('shared', 'legacy', 'mixed', 'missing'):
                bundle = root/(layout+'.msixbundle'); upload = root/(layout+'.msixupload')
                with ZipFile(bundle, 'w') as destination:
                    destination.writestr('AppxMetadata/AppxBundleManifest.xml',
                        '<Bundle><Identity Version="'+submission['version']+'"/></Bundle>')
                    for rid in ('win-x64', 'win-arm64'):
                        payload = io.BytesIO()
                        with ZipFile(payload, 'w') as package:
                            package.writestr('AppxManifest.xml', msix_manifest('1.0.5', rid, 'store'))
                            folders = {'shared': ('app',), 'legacy': ('desktop', 'cli'),
                                'mixed': ('app', 'desktop', 'cli'), 'missing': ('desktop',)}[layout]
                            for folder in folders: package.writestr(folder+'/coreclr.dll', b'runtime')
                        destination.writestr(rid+'.msix', payload.getvalue())
                with ZipFile(upload, 'w') as destination: destination.write(bundle, bundle.name)
                with self.subTest(layout=layout):
                    if layout in ('mixed', 'missing'):
                        with self.assertRaises(ValueError): repair.verify_bundle(bundle, upload, submission)
                    else:
                        repair.verify_bundle(bundle, upload, submission)
