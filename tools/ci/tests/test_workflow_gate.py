import copy
import importlib.util
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[3]
HELPER = ROOT / 'tools/ci/workflow_gate.py'
SOURCE = 'a' * 40


def records():
    run = {'id': 123, 'run_attempt': 2, 'head_sha': SOURCE,
           'event': 'workflow_dispatch', 'path': '.github/workflows/release.yml'}
    job = {'id': 456, 'run_id': 123, 'run_attempt': 2, 'head_sha': SOURCE,
           'name': 'Exact patch profiles', 'status': 'completed', 'conclusion': 'success',
           'steps': [{'name': name, 'status': 'completed', 'conclusion': 'success'}
                     for name in ('Compile and verify exact patch profiles',
                                  'Test release verification helper',
                                  'Verify completed compiler gate')]}
    return run, {'total_count': 1, 'jobs': [job]}


class WorkflowDependencyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        spec = importlib.util.spec_from_file_location('workflow_gate', HELPER)
        cls.gate = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.gate)

    def test_same_run_attempt_exact_source_completed_compiler_is_accepted(self):
        run, jobs = records()
        self.assertEqual(456, self.gate.require_dependency(run, jobs, SOURCE, 123, 2))

    def test_run_identity_event_and_workflow_must_match(self):
        for field, value in [('id', 124), ('run_attempt', 1), ('head_sha', 'b' * 40),
                             ('event', 'pull_request'), ('path', '.github/workflows/other.yml')]:
            with self.subTest(field=field):
                run, jobs = records()
                run[field] = value
                with self.assertRaises(ValueError):
                    self.gate.require_dependency(run, jobs, SOURCE, 123, 2)

    def test_job_identity_and_completed_success_are_required(self):
        for field, value in [('run_id', 124), ('run_attempt', 1), ('head_sha', 'b' * 40),
                             ('name', 'caller success'), ('status', 'in_progress'),
                             ('conclusion', 'skipped'), ('conclusion', 'failure'),
                             ('conclusion', 'cancelled')]:
            with self.subTest(field=field, value=value):
                run, jobs = records()
                jobs['jobs'][0][field] = value
                with self.assertRaises(ValueError):
                    self.gate.require_dependency(run, jobs, SOURCE, 123, 2)

    def test_missing_duplicate_and_truncated_job_lists_fail_closed(self):
        run, jobs = records()
        for changed in [{'total_count': 0, 'jobs': []},
                        {'total_count': 2, 'jobs': jobs['jobs'] * 2},
                        {'total_count': 2, 'jobs': jobs['jobs']}]:
            with self.subTest(jobs=changed), self.assertRaises(ValueError):
                self.gate.require_dependency(run, changed, SOURCE, 123, 2)

    def test_each_actual_compiler_and_input_test_step_must_complete_successfully(self):
        run, jobs = records()
        for index in range(3):
            for field, value in [('conclusion', 'failure'), ('conclusion', 'skipped'),
                                 ('status', 'in_progress')]:
                changed = copy.deepcopy(jobs)
                changed['jobs'][0]['steps'][index][field] = value
                with self.subTest(index=index, field=field, value=value), self.assertRaises(ValueError):
                    self.gate.require_dependency(run, changed, SOURCE, 123, 2)
            for replacement in [[], jobs['jobs'][0]['steps'][:index] + jobs['jobs'][0]['steps'][index + 1:],
                                jobs['jobs'][0]['steps'] + [jobs['jobs'][0]['steps'][index]]]:
                changed = copy.deepcopy(jobs)
                changed['jobs'][0]['steps'] = replacement
                with self.subTest(index=index, steps=replacement), self.assertRaises(ValueError):
                    self.gate.require_dependency(run, changed, SOURCE, 123, 2)

    def test_context_does_not_accept_other_repositories_or_non_release_identifiers(self):
        for repo, source, run, attempt in [('elsewhere/project', SOURCE, '123', '2'),
                                          ('Darkaxt/HollowKnightAndroid', 'bad', '123', '2'),
                                          ('Darkaxt/HollowKnightAndroid', SOURCE, '0', '2'),
                                          ('Darkaxt/HollowKnightAndroid', SOURCE, '123', '-1')]:
            with self.subTest(repo=repo, source=source, run=run, attempt=attempt), self.assertRaises(ValueError):
                self.gate.require_context(repo, source, run, attempt)

    def test_hosted_release_has_unconditional_private_compile_dependency(self):
        workflow = (ROOT / '.github/workflows/release.yml').read_text(encoding='utf8')
        self.assertIn('  patch-profiles:\n', workflow)
        release = workflow.split('  release:\n', 1)[1]
        self.assertIn('    needs: patch-profiles\n', release)
        self.assertNotIn('always()', release)
        self.assertNotIn('continue-on-error:', workflow)
        self.assertEqual(2, workflow.count('tools/ci/workflow_gate.py verify-job'))
        private = workflow.split('  patch-profiles:\n', 1)[1].split('  release:\n', 1)[0]
        self.assertIn('runs-on: ${{ inputs.compiler_runner }}', private)
        self.assertNotIn('secrets.', private)
        self.assertNotIn('upload-artifact', private)
        self.assertNotIn('docker run', private)
        self.assertNotIn('gradle', private.lower())
        self.assertIn('tools/ci/check_input_contract_tests.py', private)


if __name__ == '__main__':
    unittest.main()
