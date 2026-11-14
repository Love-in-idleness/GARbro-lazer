#!/usr/bin/env python3
"""Collect the fully resolved manifest and publication instructions without moving refs."""
import zipfile
from pathlib import Path
root=Path(__file__).resolve().parents[1]
with zipfile.ZipFile(root/'artifacts/flathub-submission.zip','w',compression=zipfile.ZIP_DEFLATED) as archive:
    for path in sorted((root/'artifacts/flathub').glob('*.json')):archive.write(path,path.name)
    archive.write(root/'docs/flathub.md','README.md')
