import importlib.util
import unittest
from pathlib import Path

spec=importlib.util.spec_from_file_location('macos_support',Path(__file__).resolve().parents[1]/'macos_support.py')
macos=importlib.util.module_from_spec(spec);spec.loader.exec_module(macos)


class MacMinimumTests(unittest.TestCase):
    def test_build_command_uses_os_minimum_and_ignores_linker_version(self):
        output='''Load command 9
      cmd LC_BUILD_VERSION
  cmdsize 32
 platform 1
    minos 14.0
      sdk 26.0
   ntools 1
     tool LD
  version 1267.0
Load command 10
      cmd LC_MAIN
'''
        self.assertEqual(['14.0'],macos.minimum_system_versions(output))
        self.assertEqual(['26.0'],macos.minimum_system_versions(output.replace('minos 14.0','minos 26.0')))

    def test_legacy_and_universal_commands_preserve_each_slice(self):
        self.assertEqual(['14.0','13.5.1'],macos.minimum_system_versions('''Load command 4
cmd LC_VERSION_MIN_MACOSX
version 14.0
sdk 26.0
Load command 8
cmd LC_BUILD_VERSION
minos 13.5.1
sdk 26.0
'''))
