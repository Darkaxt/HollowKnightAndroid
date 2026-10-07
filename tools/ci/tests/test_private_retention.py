import pathlib
import subprocess
import tempfile
import unittest

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
