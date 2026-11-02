#!/usr/bin/env python3
"""Show the public media URLs in composed AppStream metadata for publication checks."""
import gzip
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
for path in Path(sys.argv[1]).glob('files/share/app-info/xmls/*.xml*'):
    data=gzip.decompress(path.read_bytes()) if path.suffix=='.gz' else path.read_bytes()
    root=ET.fromstring(data)
    print('Composed media URLs:',path.name)
    for node in root.findall('.//icon[@type="remote"]')+root.findall('.//screenshot/image'):
        print(node.text)
