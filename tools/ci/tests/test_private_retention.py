import importlib.util
import os
import pathlib
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parents[3]
GOLDEN = 'tools/skin-goldens/v1/transactions.json'


class PrivateCompilerRetentionTests(unittest.TestCase):
    def test_fresh_checkout_preserves_committed_golden_bytes_and_is_clean(self):
        with tempfile.TemporaryDirectory() as folder:
            repo = pathlib.Path(folder)
            def git(*args, **kwargs):
                return subprocess.check_output(['git', '-C', str(repo), *args], **kwargs)
            git('init', '-q')
            git('config', 'core.autocrlf', 'false')
            (repo / '.gitattributes').write_bytes((ROOT / '.gitattributes').read_bytes())
            golden = repo / GOLDEN
            golden.parent.mkdir(parents=True)
            original = subprocess.check_output(['git', '-C', str(ROOT), 'show', 'HEAD:' + GOLDEN])
            golden.write_bytes(original)
            git('add', '.gitattributes')
            # Reproduce the existing CRLF blob, not a newly normalized substitute.
            blob = git('hash-object', '-w', '--no-filters', GOLDEN).decode().strip()
            git('update-index', '--add', '--cacheinfo', '100644', blob, GOLDEN)
            git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid',
                'commit', '-qm', 'Fixture')
            golden.unlink()
            git('checkout-index', '-f', GOLDEN)
            self.assertEqual(original, golden.read_bytes())
            self.assertEqual(b'', git('diff', 'HEAD', '--name-only'))

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

    def test_private_witnesses_are_not_written_to_runner_auto_cleaned_temp(self):
        workflow = (ROOT / '.github/workflows/release.yml').read_text(encoding='utf8')
        private = workflow.split('  patch-profiles:\n', 1)[1].split('  release:\n', 1)[0]
        self.assertIn('$env:DUALSOULS_PRIVATE_ROOT', private)
        self.assertNotIn('Join-Path $env:RUNNER_TEMP', private)
        helper = (ROOT / 'tools/ci/check_input_contract_tests.py').read_text(encoding='utf8')
        self.assertIn("os.environ['DUALSOULS_PRIVATE_ROOT']", helper)
        self.assertNotIn("os.environ['RUNNER_TEMP']", helper)


if __name__ == '__main__':
    unittest.main()
