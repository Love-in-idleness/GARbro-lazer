#!/usr/bin/env python3
"""Prepare deterministic portable data from this repository's legacy Windows export."""
import argparse
import gzip
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def prepare(source_bytes, report):
    source = json.loads(source_bytes.decode('utf-8-sig'))
    if source.get('PortableSchemaVersion') != 2:
        raise ValueError('Export schema 2 is required to preserve private cipher fields')
    if report['failed'] or report['version'] != source['Version']:
        raise ValueError('Resolve all import failures and match source versions before bundling data')
    tags = sorted(report['applied'])
    if not tags or len(tags) != len(set(tags)):
        raise ValueError('The import report must contain unique applied scheme groups')
    if set(tags) | set(report['unported']) != set(source['SchemeMap']):
        raise ValueError('The import report must account for every source scheme group')
    if set(tags) & set(report['unported']):
        raise ValueError('A scheme group cannot be both applied and unported')
    data = {'PortableSchemaVersion': 2, 'Version': source['Version'],
            'SchemeMap': {tag: source['SchemeMap'][tag] for tag in tags},
            'GameMap': source['GameMap']}
    raw = json.dumps(data, ensure_ascii=False, separators=(',', ':')).encode()
    metadata = {'schema': 1, 'version': source['Version'], 'portable_schema': 2,
                'source_sha256': hashlib.sha256(source_bytes).hexdigest(),
                'json_sha256': hashlib.sha256(raw).hexdigest(),
                'applied': tags, 'unported': sorted(report['unported'])}
    return gzip.compress(raw, compresslevel=9, mtime=0), metadata


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('export', type=Path)
    parser.add_argument('--report', type=Path, required=True,
                        help='Successful import report from the target portable catalog')
    args = parser.parse_args()
    encoded, metadata = prepare(args.export.read_bytes(),
                               json.loads(args.report.read_text(encoding='utf-8-sig')))
    output = ROOT/'modern/GameData/Formats.json.gz'
    output.write_bytes(encoded)
    output.with_name('schemes.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2)+'\n')
    print('Prepared', len(metadata['applied']), 'scheme groups;', len(encoded),
          'compressed bytes; source version', metadata['version'])


if __name__ == '__main__':
    main()
