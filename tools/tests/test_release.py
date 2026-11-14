import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

spec=importlib.util.spec_from_file_location('garbro_release',Path(__file__).resolve().parents[1]/'release.py')
release=importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temporary=tempfile.TemporaryDirectory()
        self.root=Path(self.temporary.name)
        self.original_root=release.ROOT
        release.ROOT=self.root
        (self.root/'docs/releases').mkdir(parents=True)
        (self.root/'docs/releases/v1.2.3.md').write_text('Verified release notes')
        (self.root/'docs/compatibility').mkdir()
        (self.root/'docs/compatibility/portable-status.json').write_text('{"schema":1}')
        (self.root/'modern/GameData').mkdir(parents=True)
        for name in ('Formats.json.gz','schemes.json'):(self.root/'modern/GameData'/name).write_bytes(b'database')
        (self.root/'tools/installers').mkdir(parents=True)
        self.identity=json.loads((self.original_root/'tools/installers/store-identity.json').read_text())
        (self.root/'tools/installers/store-identity.json').write_text(json.dumps(self.identity))
        self.artifacts=self.root/'artifacts'
        self.artifacts.mkdir()

    def tearDown(self):
        release.ROOT=self.original_root
        self.temporary.cleanup()

    def test_version_cannot_escape_notes_directory(self):
        for tag in ('../v1.2.3','v1.2.3\nunsafe','v1.2.3;echo unsafe','latest'):
            with self.subTest(tag=tag),self.assertRaises(ValueError):release.release_plan(tag)

    def test_notes_are_required(self):
        with self.assertRaises(ValueError):release.release_plan('v2.0.0')

    def test_missing_platform_fails_before_writing_a_manifest(self):
        plan=release.release_plan('v1.2.3')
        (self.artifacts/'garbro-1.2.3-linux-x64.tar.gz').write_bytes(b'package')
        with self.assertRaises(ValueError):release.prepare_assets(plan,self.artifacts,'a'*40)
        self.assertFalse((self.artifacts/'release.json').exists())

    def test_all_assets_are_bound_to_the_source_commit_and_checksums(self):
        plan=release.release_plan('v1.2.3')
        for suffix in ('linux-x64.tar.gz','win-x64.zip','osx-arm64.tar.gz','linux-x64.deb','win-x64.msix','win-x64.cer','linux-arm64.tar.gz','linux-arm64.deb','win-arm64.zip','win-arm64.msix','win-arm64.cer','store.msixupload','store.msixbundle','osx-x64.tar.gz','osx-x64.dmg','osx-arm64.dmg','linux-x64.flatpak','linux-arm64.flatpak'):
            (self.artifacts/('garbro-1.2.3-'+suffix)).write_bytes(suffix.encode())
        for name in ('store-listing.zip','flathub-submission.zip'):(self.artifacts/name).write_bytes(b'submission')
        (self.artifacts/'store-submission.json').write_text(json.dumps(dict(**self.identity,version='1.2.3.0',applicationVersion='1.2.3',selfContained=True,architectures=['x64','arm64'])))
        release.prepare_assets(plan,self.artifacts,'a'*40)
        manifest=json.loads((self.artifacts/'release.json').read_text())
        self.assertEqual('a'*40,manifest['commit'])
        self.assertEqual(24,len(manifest['assets']))
        checksums=(self.artifacts/'SHA256SUMS').read_text().splitlines()
        self.assertEqual(25,len(checksums))
        for line in checksums:
            digest,name=line.split('  ')
            self.assertEqual(digest,hashlib.sha256((self.artifacts/name).read_bytes()).hexdigest())

    def test_store_version_cannot_lag_behind_the_application(self):
        path=self.artifacts/'store-submission.json'
        data=dict(**self.identity,version='1.0.3.0',applicationVersion='1.0.4',selfContained=True,architectures=['x64','arm64'])
        path.write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError,'behind'):release.validate_store_submission(path,'1.0.4')
        data['version']='1.0.4.0';path.write_text(json.dumps(data))
        self.assertEqual('1.0.4.0',release.validate_store_submission(path,'1.0.4')['version'])



if __name__=='__main__':unittest.main()
