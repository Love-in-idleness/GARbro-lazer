"""Read the OS minimum from otool output without mistaking linker versions for it."""
import re


def minimum_system_versions(commands):
    versions=[]
    for kind,section in re.findall(r'cmd LC_(BUILD_VERSION|VERSION_MIN_MACOSX)\b(.*?)(?=Load command|\Z)',commands,re.S):
        field='minos' if kind=='BUILD_VERSION' else 'version'
        versions.extend(re.findall(r'^\s*'+field+r'\s+(\d+\.\d+(?:\.\d+)?)\s*$',section,re.M))
    return versions
