import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('prepare_schemes', Path(__file__).resolve().parents[1]/'prepare-schemes.py')
prepare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prepare)


class PrepareSchemesTests(unittest.TestCase):
    def setUp(self):
        self.source = {'Version': 148, 'PortableSchemaVersion': 2,
                       'SchemeMap': {'DXA': {'private_field': 'retained'}, 'unused': {}},
                       'GameMap': {'game.exe': 'sample'}}
        self.report = {'version': 148, 'applied': ['DXA'], 'unported': ['unused'], 'failed': {}}

    def export(self):
        return json.dumps(self.source).encode('utf-8-sig')

    def test_output_is_deterministic_and_bound_to_exported_fields(self):
        data, metadata = prepare.prepare(self.export(), self.report)
        self.assertEqual(data, prepare.prepare(self.export(), self.report)[0])
        raw = gzip.decompress(data)
        self.assertEqual('retained', json.loads(raw)['SchemeMap']['DXA']['private_field'])
        self.assertEqual(hashlib.sha256(raw).hexdigest(), metadata['json_sha256'])
        self.assertEqual(hashlib.sha256(self.export()).hexdigest(), metadata['source_sha256'])
        self.assertEqual(['unused'], metadata['unported'])

    def test_legacy_public_property_export_is_rejected(self):
        for version in (None, 1, 3):
            self.source['PortableSchemaVersion'] = version
            with self.subTest(version=version), self.assertRaises(ValueError):
                prepare.prepare(self.export(), self.report)

    def test_partial_or_mismatched_import_is_rejected(self):
        for change in ({'failed': {'DXA': 'bad key'}}, {'version': 147},
                       {'applied': []}, {'applied': ['DXA', 'DXA']},
                       {'unported': []}, {'unported': ['unused', 'DXA']},
                       {'applied': ['unknown']}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                prepare.prepare(self.export(), dict(self.report, **change))
