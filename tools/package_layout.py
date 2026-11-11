"""Keep one application payload and remove files that are not needed at runtime."""
import gzip
import hashlib
import json
import lzma
import os
import shutil
from pathlib import Path


def compact_application(directory):
    directory = Path(directory)
    for file in directory.rglob('*'):
        if file.is_file() and (file.suffix.lower() == '.pdb' or
                file.name.lower().startswith(('mscordaccore', 'libmscordaccore', 'microsoft.diasymreader.native.')) or
                file.name.lower() in ('mscordbi.dll', 'libmscordbi.so', 'libmscordbi.dylib', 'createdump', 'createdump.exe') or
                file.suffix.lower() == '.xml' and file.with_suffix('.dll').is_file()):
            # Symbols and debugger helpers are not used for startup or decoding.
            file.unlink()
    for file in (directory/'GameData').glob('*.lst'):
        with file.open('rb') as source, file.with_suffix('.lst.gz').open('wb') as target:
            with gzip.GzipFile(filename='', mode='wb', fileobj=target, compresslevel=9, mtime=0) as compressed:
                shutil.copyfileobj(source, compressed)
        file.unlink()


def install_media(source, destination):
    """Retain the full LGPL source once, with denser lossless archive compression."""
    source, destination = Path(source), Path(destination)
    recipe = json.loads((source/'media-codecs.json').read_text())
    original = source/'ffmpeg-source.tar.gz'
    with original.open('rb') as stream:
        if hashlib.file_digest(stream, 'sha256').hexdigest() != recipe['sha256']:
            raise ValueError('FFmpeg corresponding-source checksum mismatch')
    cache = source.parent/('ffmpeg-source-'+recipe['sha256']+'.tar.xz')
    if not cache.is_file():
        temporary = cache.with_suffix('.'+str(os.getpid())+'.tmp')
        try:
            with gzip.open(original, 'rb') as stream, lzma.open(temporary, 'wb', preset=9) as target:
                shutil.copyfileobj(stream, target)
            temporary.replace(cache)
        finally:
            temporary.unlink(missing_ok=True)
    with gzip.open(original, 'rb') as stream:
        original_tar = hashlib.file_digest(stream, 'sha256').hexdigest()
    with lzma.open(cache, 'rb') as stream:
        if hashlib.file_digest(stream, 'sha256').hexdigest() != original_tar:
            raise ValueError('Recompressed FFmpeg source differs from the pinned source')
    shutil.copytree(source, destination, ignore=shutil.ignore_patterns('ffmpeg-source.tar.gz'))
    archive = destination/'ffmpeg-source.tar.xz'
    shutil.copy2(cache, archive)
    with archive.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    (destination/'source-archive.json').write_text(json.dumps(dict(file=archive.name, sha256=digest,
        tarSha256=original_tar, originalSha256=recipe['sha256']), indent=2)+'\n')
