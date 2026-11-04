#!/usr/bin/env python3
"""Install the actual DEB in disposable Debian 13 and Ubuntu 24.04 desktops."""
import argparse
import os
import shutil
import subprocess
import tempfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('package',type=Path)
    parser.add_argument('--distro',choices=['debian','ubuntu','both'],default='both')
    args=parser.parse_args()
    targets=[('debian:13-slim','debian','13'),('ubuntu:24.04','ubuntu','24.04')]
    for base,distro,version in targets:
        if args.distro not in ('both',distro):continue
        with tempfile.TemporaryDirectory(prefix='garbro-deb-check-')as directory:
            context=Path(directory)
            for source,name in [(args.package,'package.deb'),(ROOT/'tools/installers/Dockerfile','Dockerfile'),
                (ROOT/'tools/installers/probe.py','probe.py'),(ROOT/'tools/association_probe.py','association_probe.py'),
                (ROOT/'tests/fixtures/generated/standard.xfl','standard.xfl')]:shutil.copy2(source,context/name)
            tag='garbro-installer-test:'+distro
            command=['docker','build','--build-arg','BASE='+base,'--build-arg','DIST='+distro,'--build-arg','DIST_VERSION='+version,'-t',tag,str(context)]
            if os.environ.get('CODEX_PROXY_CERT'):command[2:2]=['--secret','id=proxy_ca,src='+os.environ['CODEX_PROXY_CERT']]
            subprocess.run(command,check=True)
            subprocess.run(['docker','run','--rm',tag],check=True)


if __name__=='__main__':main()
