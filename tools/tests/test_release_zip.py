import importlib.util
import os
from pathlib import Path
import sys
import tempfile
import unittest
from zipfile import ZipFile

TOOLS=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(TOOLS))
spec=importlib.util.spec_from_file_location('package_release',TOOLS/'package-release.py')
release=importlib.util.module_from_spec(spec);spec.loader.exec_module(release)


class ReleaseZipTests(unittest.TestCase):
    def test_old_dependency_dates_do_not_break_the_windows_archive(self):
        with tempfile.TemporaryDirectory()as directory:
            root=Path(directory);stage=root/'garbro-test-win-arm64'
            dependency=stage/'desktop/Avalonia.Wayland.dll'
            dependency.parent.mkdir(parents=True);dependency.write_bytes(b'dependency')
            os.utime(dependency,(0,0))
            archive=root/'release.zip';release.write_zip(stage,archive)
            with ZipFile(archive)as packaged:
                name='garbro-test-win-arm64/desktop/Avalonia.Wayland.dll'
                self.assertEqual(b'dependency',packaged.read(name))
                self.assertEqual((1980,1,1),packaged.getinfo(name).date_time[:3])
