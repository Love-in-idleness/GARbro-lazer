#!/usr/bin/env python3
"""Compare extraction of a pinned public release against Python's ZIP reader."""
import argparse
import hashlib
import json
import shutil
import subprocess
import tempfile
import urllib.request
import zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
URL='https://www.renpy.org/dl/8.5.3/renpy-8.5.3-sdk.zip'
SHA256='ff57648f9c04f27e381c48af6d8e3ee3cdec296bed4d3831f47f09b0a71b505e'


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream,'sha256').hexdigest()


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet',default='dotnet')
    parser.add_argument('--cache',type=Path,default=ROOT/'artifacts/public-corpus-cache')
    parser.add_argument('--report',type=Path,default=ROOT/'artifacts/public-corpus.json')
    args=parser.parse_args()
    args.cache.mkdir(parents=True,exist_ok=True)
    package=args.cache/'renpy-8.5.3-sdk.zip'
    if not package.exists() or digest(package)!=SHA256:
        partial=package.with_suffix('.partial')
        with urllib.request.urlopen(URL,timeout=45) as source,partial.open('wb') as destination:
            shutil.copyfileobj(source,destination)
        if digest(partial)!=SHA256:
            partial.unlink()
            raise ValueError('Public corpus SHA-256 does not match the official release.')
        partial.replace(package)
    cli=ROOT/'modern/GARbro.Cli/bin/Release/net10.0/garbro.dll'
    with tempfile.TemporaryDirectory(prefix='garbro-public-corpus-') as folder:
        output=Path(folder)
        subprocess.run([args.dotnet,str(cli),'extract',str(package),'-o',str(output),'--json'],
                       stdout=subprocess.DEVNULL,check=True,timeout=180)
        files=0;total=0
        with zipfile.ZipFile(package) as archive:
            for info in archive.infolist():
                if info.is_dir():continue
                path=(output/info.filename).resolve()
                if not path.is_relative_to(output.resolve()) or not path.is_file():
                    raise ValueError('Missing or escaping corpus entry: '+info.filename)
                with archive.open(info) as reference:
                    expected=hashlib.file_digest(reference,'sha256').hexdigest()
                if path.stat().st_size!=info.file_size or digest(path)!=expected:
                    raise ValueError('Corpus digest mismatch: '+info.filename)
                files+=1;total+=info.file_size
    report=dict(source=URL,source_sha256=SHA256,format='ZIP',files=files,
                unpacked_bytes=total,all_sha256_match=True,reference='Python zipfile',
                provenance='RenPy official SDK 8.5.3; downloaded only for validation, not redistributed.')
    args.report.parent.mkdir(parents=True,exist_ok=True)
    args.report.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(f'Public RenPy SDK corpus: {files} files, {total} bytes; all SHA-256 digests match.')


if __name__=='__main__':main()
