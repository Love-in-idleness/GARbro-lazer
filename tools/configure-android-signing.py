#!/usr/bin/env python3
"""Import the private Android signing backup into Actions secrets using a maintainer's gh login."""
import argparse
import base64
import getpass
import subprocess
from pathlib import Path


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('keystore',type=Path)
    parser.add_argument('--repo',default='Love-in-idleness/GARbro-lazer')
    args=parser.parse_args()
    password=getpass.getpass('PKCS12 password: ')
    if not password:parser.error('A password is required')
    # The secret values travel on stdin, never argv or normal command output.
    for name,value in [('ANDROID_KEYSTORE_BASE64',base64.b64encode(args.keystore.read_bytes())),('ANDROID_KEYSTORE_PASSWORD',password.encode())]:
        subprocess.run(['gh','secret','set',name,'--repo',args.repo],input=value,check=True)
    print('Android publication secrets configured.')


if __name__=='__main__':main()
