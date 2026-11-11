import gzip
import hashlib
import json
import lzma
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from package_layout import compact_application, install_media


class PackageLayoutTests(unittest.TestCase):
    def test_compaction_retains_runtime_and_exact_multilingual_file_lists(self):
        with tempfile.TemporaryDirectory() as temporary:
            app = Path(temporary); data = app/'GameData'; data.mkdir()
            names = 'file.png\n中文/背景.png\nРусский/音声.ogg\n日本語/画像.webp\n'.encode()
            (data/'game.lst').write_bytes(names)
            for name in ('coreclr.dll', 'GARbro.Worker.dll', 'garbro.dll', 'GARbro.Desktop.dll', 'game.xml'):
                (app/name).write_bytes(b'required')
            for name in ('libSkiaSharp.pdb', 'garbro.xml', 'mscordbi.dll', 'mscordaccore_amd64.dll', 'createdump.exe'):
                (app/name).write_bytes(b'diagnostics')
            compact_application(app)
            self.assertEqual(names, gzip.decompress((data/'game.lst.gz').read_bytes()))
            self.assertFalse((data/'game.lst').exists())
            self.assertEqual({'coreclr.dll', 'GARbro.Worker.dll', 'garbro.dll', 'GARbro.Desktop.dll', 'game.xml', 'GameData'}, {p.name for p in app.iterdir()})

    def test_media_keeps_the_complete_pinned_source_once_and_reuses_its_archive(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); media = root/'win-x64'; media.mkdir()
            original = b'complete corresponding source\0' * 1024
            compressed = gzip.compress(original, mtime=0)
            digest = hashlib.sha256(compressed).hexdigest()
            (media/'ffmpeg-source.tar.gz').write_bytes(compressed)
            (media/'media-codecs.json').write_text(json.dumps({'sha256': digest}))
            for name in ('ffmpeg.exe', 'ffprobe.exe', 'COPYING.LGPLv2.1', 'LICENSE.md', 'build-media.py'):
                (media/name).write_bytes(b'keep')
            for app in (root/'first', root/'second'):
                install_media(media, app/'media')
                target = app/'media'; metadata = json.loads((target/'source-archive.json').read_text())
                data = (target/metadata['file']).read_bytes()
                self.assertEqual(original, lzma.decompress(data))
                self.assertEqual(digest, metadata['originalSha256'])
                self.assertEqual(hashlib.sha256(data).hexdigest(), metadata['sha256'])
                self.assertEqual(hashlib.sha256(original).hexdigest(), metadata['tarSha256'])
                self.assertFalse((target/'ffmpeg-source.tar.gz').exists())
                self.assertTrue((target/'COPYING.LGPLv2.1').is_file())
            self.assertEqual(1, len(list(root.glob('ffmpeg-source-*.tar.xz'))))
            (media/'ffmpeg-source.tar.gz').write_bytes(b'corrupted')
            with self.assertRaises(ValueError): install_media(media, root/'bad')
            self.assertFalse((root/'bad').exists())
