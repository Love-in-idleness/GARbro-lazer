#!/usr/bin/env python3
"""Validate APK ABI, native 16 KiB ELF alignment and exclusion of desktop native libraries."""
import argparse
import struct
import zipfile
from pathlib import Path


def check_apk(path,abi):
    native=[]
    with zipfile.ZipFile(path)as apk:
        for name in apk.namelist():
            if not name.startswith('lib/')or not name.endswith('.so'):continue
            if name.split('/')[1]!=abi:raise ValueError('Unexpected native ABI: '+name)
            if 'Magick' in name or 'SDL' in name:raise ValueError('Desktop native library leaked into APK: '+name)
            data=apk.read(name)
            if data[:5]!=b'\x7fELF\x02':raise ValueError('Expected a 64-bit ELF: '+name)
            machine=struct.unpack_from('<H',data,18)[0]
            if machine!=({'arm64-v8a':183,'x86_64':62}[abi]):raise ValueError('ELF machine does not match ABI: '+name)
            offset=struct.unpack_from('<Q',data,32)[0];size,count=struct.unpack_from('<HH',data,54)
            for i in range(count):
                at=offset+i*size
                if struct.unpack_from('<I',data,at)[0]==1 and struct.unpack_from('<Q',data,at+48)[0]<16384:
                    raise ValueError('Native library is not 16 KiB aligned: '+name)
            native.append(name)
        if not native:raise ValueError('APK contains no native libraries')
    return native


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('apk',type=Path)
    parser.add_argument('--abi',choices=['arm64-v8a','x86_64'],default='arm64-v8a')
    args=parser.parse_args();print('Validated',len(check_apk(args.apk,args.abi)),'native APK libraries')


if __name__=='__main__':main()
