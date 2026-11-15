import importlib.util
import os
import pathlib
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parents[3]

class PrivateCompilerRetentionTests(unittest.TestCase):
    def test_all_committed_line_endings_match_declared_text_attributes(self):
        entries = subprocess.check_output(['git', '-C', str(ROOT), 'ls-files', '--eol', '-z']).decode()
        incompatible = []
        for entry in entries.split('\0'):
            if not entry:
                continue
            metadata, path = entry.split('\t', 1)
            index, _, attributes = metadata.split(maxsplit=2)
            if index in ('i/crlf', 'i/mixed') and attributes.strip() == 'attr/text eol=lf':
                incompatible.append(path)
        self.assertEqual([], incompatible)

    def test_full_suite_receives_the_declared_host_fixture_root(self):
        spec = importlib.util.spec_from_file_location('private_suite', ROOT / 'tools/ci/check_input_contract_tests.py')
        helper = importlib.util.module_from_spec(spec)
        with mock.patch.object(sys, 'path', [str(ROOT / 'tools/ci'), *sys.path]):
            spec.loader.exec_module(helper)
        contract = {'profiles': {profile: {'player': profile + '-player', 'depot': profile + '-depot'}
                                 for profile in ('hollow-knight', 'silksong')}}
        with tempfile.TemporaryDirectory() as folder:
            observed = {}
            def run(*args):
                observed.update(os.environ)
                return {'fixture': True}
            with mock.patch.dict(os.environ, {'DUALSOULS_INPUT_CONTRACT': 'fixture',
                                             'DUALSOULS_PRIVATE_ROOT': folder}, clear=True), \
                    mock.patch.object(helper, 'admit_contract', return_value=contract), \
                    mock.patch.object(helper, 'run_process', side_effect=run), \
                    mock.patch.object(helper, 'require_process'), mock.patch('builtins.print'):
                self.assertEqual(0, helper.main())
            expected = str(pathlib.Path(folder) / 'test-temp')
            self.assertEqual(expected, observed.get('DUALSOULS_HOST_TEST_TEMP'))
            self.assertEqual(expected, observed['DUALSOULS_TEMP_ROOT'])
            self.assertTrue(pathlib.Path(expected).is_dir())


if __name__ == '__main__':
    unittest.main()
