#!/usr/bin/env python3
"""Generate Android associations from the same conservative desktop whitelist."""
import argparse
import xml.etree.ElementTree as ET
from pathlib import Path
from installer_support import associations


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--test', default='false')
    args = parser.parse_args()
    android = 'http://schemas.android.com/apk/res/android'
    ET.register_namespace('android', android)
    def attr(name): return '{'+android+'}'+name
    manifest = ET.parse(args.base)
    app = manifest.getroot().find('application')
    app.set(attr('debuggable'), 'true' if args.test.lower() == 'true' else 'false')
    activity = ET.SubElement(app, 'activity', {attr('name'): 'org.garbro.garbro.MainActivity',
        attr('exported'): 'true', attr('label'): 'GARbro',
        attr('configChanges'): 'density|keyboardHidden|orientation|screenSize|uiMode'})
    launcher = ET.SubElement(activity, 'intent-filter')
    ET.SubElement(launcher, 'action', {attr('name'): 'android.intent.action.MAIN'})
    ET.SubElement(launcher, 'category', {attr('name'): 'android.intent.category.LAUNCHER'})
    share = ET.SubElement(activity, 'intent-filter')
    for action in ('SEND', 'SEND_MULTIPLE'):
        ET.SubElement(share, 'action', {attr('name'): 'android.intent.action.'+action})
    ET.SubElement(share, 'category', {attr('name'): 'android.intent.category.DEFAULT'})
    ET.SubElement(share, 'data', {attr('mimeType'): '*/*'})
    intent = ET.SubElement(activity, 'intent-filter')
    ET.SubElement(intent, 'action', {attr('name'): 'android.intent.action.VIEW'})
    ET.SubElement(intent, 'category', {attr('name'): 'android.intent.category.DEFAULT'})
    for extension in associations():
        ET.SubElement(intent, 'data', {attr('mimeType'): 'application/x-garbro-'+extension})
    ET.SubElement(app, 'service', {attr('name'): 'org.garbro.garbro.DecoderService', attr('exported'): 'false', attr('process'): ':decoder'})
    ET.SubElement(app, 'service', {attr('name'): 'org.garbro.garbro.JobCoordinatorService', attr('exported'): 'false', attr('foregroundServiceType'): 'dataSync'})
    args.output.parent.mkdir(parents=True, exist_ok=True)
    data = ET.tostring(manifest.getroot(), encoding='utf-8', xml_declaration=True)
    if not args.output.exists() or args.output.read_bytes() != data:
        args.output.write_bytes(data)


if __name__ == '__main__': main()
