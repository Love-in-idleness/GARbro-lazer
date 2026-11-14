import importlib.util
import unittest
from pathlib import Path

spec=importlib.util.spec_from_file_location('media_build',Path(__file__).resolve().parents[1]/'build-media.py')
media=importlib.util.module_from_spec(spec);spec.loader.exec_module(media)

class MediaBuildCacheTests(unittest.TestCase):
    def test_mac_only_architecture_check_change_reuses_windows_but_not_mac(self):
        current=Path(media.__file__).read_text()
        old=current.replace("['lipo', str(source/name), '-verify_arch',", "['lipo', str(source/name), '-info',")
        for rid in ('win-x64','win-arm64'):self.assertTrue(media.compatible_builder(old,rid))
        self.assertFalse(media.compatible_builder(old,'osx-arm64'))
    def test_windows_compiler_and_common_build_changes_invalidate_cache(self):
        current=Path(media.__file__).read_text()
        self.assertFalse(media.compatible_builder(current.replace("'--extra-cflags=-MT'","'--extra-cflags=-MD'"),'win-arm64'))
        self.assertFalse(media.compatible_builder(current.replace("'--enable-static'","'--enable-shared'"),'win-x64'))
    def test_malformed_recipe_is_not_reused(self):
        self.assertFalse(media.compatible_builder('invalid python','win-x64'))
    def test_windows_shared_libraries_do_not_reuse_the_old_static_executables(self):
        current=Path(media.__file__).read_text()
        old=current.replace("['--enable-shared', '--disable-static'] if rid.startswith('win-') else ['--disable-shared', '--enable-static']", "['--disable-shared', '--enable-static']")
        for rid in ('win-x64','win-arm64'):self.assertFalse(media.compatible_builder(old,rid))
