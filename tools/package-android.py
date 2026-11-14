#!/usr/bin/env python3
"""Build standalone Android APKs; publication requires the project's persistent signing key."""
import argparse
import os
import shutil
import subprocess
from pathlib import Path
from installer_support import android_version

ROOT=Path(__file__).resolve().parents[1]


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version',default=os.environ.get('GARBRO_PACKAGE_VERSION','0.3.0-preview.1'))
    parser.add_argument('--rid',choices=['android-arm64','android-x64'],default='android-arm64')
    parser.add_argument('--test',action='store_true')
    parser.add_argument('--sign',action='store_true')
    parser.add_argument('--output',type=Path,default=Path('artifacts'))
    parser.add_argument('--sdk',default=os.environ.get('ANDROID_SDK_ROOT')or os.environ.get('ANDROID_HOME'))
    parser.add_argument('--java',default=os.environ.get('JAVA_HOME'))
    args=parser.parse_args();code=android_version(args.version)
    if args.sign and (not os.environ.get('ANDROID_KEYSTORE_PATH')or not os.environ.get('ANDROID_KEYSTORE_PASSWORD')):
        parser.error('Persistent Android signing credentials are required; never publish a temporary-key APK')
    stage=args.output.resolve()/('android-stage-'+args.rid+('-test'if args.test else''));stage.mkdir(parents=True,exist_ok=True)
    command=['dotnet','publish',str(ROOT/'modern/GARbro.Android/GARbro.Android.csproj'),'-c','Release','-r',args.rid,
        '-o',str(stage),'-m:1','-nr:false','-p:UseSharedCompilation=false','-p:Version='+args.version,
        '-p:ApplicationVersion='+str(code),'-p:ApplicationDisplayVersion='+args.version,
        '-p:GarbroAndroidTest='+str(args.test).lower(),'-p:ApplicationId=org.garbro.garbro'+('.test'if args.test else''),'--nologo']
    if args.sdk:command.append('-p:AndroidSdkDirectory='+args.sdk)
    if args.java:command.append('-p:JavaSdkDirectory='+args.java)
    if args.sign:command += ['-p:AndroidKeyStore=true','-p:AndroidSigningKeyStore='+os.environ['ANDROID_KEYSTORE_PATH'],
        '-p:AndroidSigningKeyAlias=garbro','-p:AndroidSigningStorePass=env:ANDROID_KEYSTORE_PASSWORD','-p:AndroidSigningKeyPass=env:ANDROID_KEYSTORE_PASSWORD']
    subprocess.run(command,cwd=ROOT,check=True)
    candidates=list(stage.glob('*-Signed.apk'))
    if len(candidates)!=1:raise RuntimeError('Expected exactly one signed APK')
    target=args.output/('garbro-'+args.version+'-'+args.rid+('-test'if args.test else'')+'.apk')
    shutil.copy2(candidates[0],target)
    subprocess.run(['python3',str(ROOT/'tools/check-apk.py'),str(target),'--abi','arm64-v8a'if args.rid=='android-arm64'else'x86_64'],check=True)
    if args.sdk:
        build_tools=sorted((Path(args.sdk)/'build-tools').iterdir())[-1]
        subprocess.run([str(build_tools/'zipalign'),'-c','-P','16','4',str(target)],check=True)
        subprocess.run([str(build_tools/'apksigner'),'verify','--verbose',str(target)],check=True)
    print(target)


if __name__=='__main__':main()
