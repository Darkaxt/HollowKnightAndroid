import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
HOOK = ROOT / 'tools/ci/private_runner_guard.ps1'


def expected_context():
    return {'GITHUB_REPOSITORY': 'Darkaxt/HollowKnightAndroid',
            'GITHUB_WORKFLOW_REF': 'Darkaxt/HollowKnightAndroid/.github/workflows/release.yml@refs/heads/checkpoint/test',
            'GITHUB_WORKFLOW_SHA': 'a' * 40, 'GITHUB_JOB': 'patch-profiles',
            'GITHUB_RUN_ID': '123', 'GITHUB_RUN_ATTEMPT': '1',
            'GITHUB_SHA': 'a' * 40, 'GITHUB_EVENT_NAME': 'workflow_dispatch'}


class PrivateRunnerGuardTests(unittest.TestCase):
    def run_hook(self, expected, context):
        root = Path(tempfile.mkdtemp(prefix='runner-guard-', dir=os.environ.get('DUALSOULS_TEMP_ROOT')))
        script = root / HOOK.name
        shutil.copyfile(HOOK, script)
        if expected is not None:
            (root / 'expected-job.json').write_text(json.dumps(expected), encoding='utf8')
        env = {k: v for k, v in os.environ.items()
               if not any(word in k.upper() for word in ('TOKEN', 'SECRET', 'PASSWORD', 'API_KEY',
                                                        'CREDENTIAL', 'COOKIE', 'AUTH_KEY'))}
        env.update({key: str(value) for key, value in context.items()})
        return subprocess.run(['pwsh', '-NoLogo', '-NoProfile', '-NonInteractive', '-File', str(script)],
                              env=env, capture_output=True, text=True)

    def test_only_exact_intended_context_is_admitted(self):
        expected = expected_context()
        result = self.run_hook(expected, expected)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn('Intended exact-source compiler job admitted', result.stdout)

    def test_each_different_job_context_is_rejected_before_repository_steps(self):
        expected = expected_context()
        for name in expected:
            with self.subTest(name=name):
                actual = dict(expected, **{name: 'unintended'})
                result = self.run_hook(expected, actual)
                self.assertNotEqual(0, result.returncode)
                self.assertNotIn('job admitted', result.stdout)
                self.assertIn('rejected an unintended', result.stderr)

    def test_missing_or_caller_redefined_expectation_is_not_an_admission(self):
        expected = expected_context()
        invalid = [None, dict(expected, GITHUB_EVENT_NAME='pull_request'),
                   dict(expected, GITHUB_JOB='other'), dict(expected, extra='claimed success'),
                   dict(expected, GITHUB_RUN_ID=123)]
        for changed in invalid:
            with self.subTest(expected=changed):
                result = self.run_hook(changed, expected if changed is None else changed)
                self.assertNotEqual(0, result.returncode)
                self.assertNotIn('job admitted', result.stdout)


if __name__ == '__main__':
    unittest.main()
