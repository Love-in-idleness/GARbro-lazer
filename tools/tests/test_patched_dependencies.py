import importlib.util
import json
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile

ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('patched_dependencies',ROOT/'tools/patched_dependencies.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)


class PatchedDependencyTests(unittest.TestCase):
    def test_the_recorded_backend_is_complete_and_has_no_cursor_theme_assets(self):
        packages=module.local_packages(ROOT)
        self.assertEqual({'avalonia.wayland/12.1.4-garbro.1'},set(packages))
        with ZipFile(next(iter(packages.values())))as archive:
            self.assertEqual([], [name for name in archive.namelist() if name.lower().endswith(('.cur','.ani')) or '/cursors/' in name])
            for name in ('lib/net10.0/Avalonia.Wayland.dll','licenses/MIT.md','source-patches/system-cursors.patch'):
                self.assertIn(name,archive.namelist())
            metadata=ET.fromstring(archive.read('Avalonia.Wayland.nuspec'))
            ns={'n':'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'}
            dependencies={item.attrib['id']:item.attrib['version']for item in metadata.findall('.//n:dependency',ns)}
            self.assertEqual({'Avalonia':'12.1.4','Avalonia.FreeDesktop':'12.1.4','NWayland':'0.11.0'},dependencies)
