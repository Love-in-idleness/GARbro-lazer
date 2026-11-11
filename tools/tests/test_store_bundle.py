import importlib.util
import io
import sys
import tempfile
import unittest
from pathlib import Path
from zipfile import ZipFile

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
from installer_support import msix_manifest, store_identity, store_version

spec = importlib.util.spec_from_file_location('repair_store', TOOLS/'repair-store-release.py')
repair = importlib.util.module_from_spec(spec); spec.loader.exec_module(repair)


class StoreBundleTests(unittest.TestCase):
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
