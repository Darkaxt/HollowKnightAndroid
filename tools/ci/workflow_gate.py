"""Require the fresh private compiler job in this GitHub workflow attempt.

Physical compiler witnesses stay on the private runner. The hosted signing job
trusts GitHub's completed dependency, never uploaded or caller-authored receipts.
"""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import urllib.request

REPOSITORY = 'Darkaxt/HollowKnightAndroid'
WORKFLOW = '.github/workflows/release.yml'
COMPILER_JOB = 'Exact patch profiles'
REQUIRED_STEPS = ('Compile and verify exact patch profiles', 'Test release verification helper',
                  'Verify completed compiler gate')


def require_context(repository, source, run, attempt):
    if (repository != REPOSITORY or not re.fullmatch('[0-9a-f]{40}', source) or
            not re.fullmatch('[1-9][0-9]*', run) or not re.fullmatch('[1-9][0-9]*', attempt)):
        raise ValueError('Not an exact release workflow context')
    return int(run), int(attempt)


def require_dependency(run, jobs, source, run_id, attempt):
    if (type(run.get('id')) is not int or run['id'] != run_id or
            type(run.get('run_attempt')) is not int or run['run_attempt'] != attempt or
            run.get('head_sha') != source or run.get('event') != 'workflow_dispatch' or
            run.get('path') != WORKFLOW):
        raise ValueError('Wrong workflow source, run or attempt')
    items = jobs.get('jobs', [])
    if type(jobs.get('total_count')) is not int or jobs['total_count'] != len(items):
        raise ValueError('Incomplete workflow job list')
    matches = [job for job in items if job.get('name') == COMPILER_JOB]
    if len(matches) != 1:
        raise ValueError('Require exactly one private compiler dependency')
    job = matches[0]
    if (type(job.get('run_id')) is not int or job['run_id'] != run_id or
            type(job.get('run_attempt')) is not int or job['run_attempt'] != attempt or
            job.get('head_sha') != source or job.get('status') != 'completed' or
            job.get('conclusion') != 'success' or type(job.get('id')) is not int):
        raise ValueError('Private compiler dependency did not succeed in this attempt')
    for name in REQUIRED_STEPS:
        steps = [step for step in job.get('steps', []) if step.get('name') == name]
        if (len(steps) != 1 or steps[0].get('status') != 'completed' or
                steps[0].get('conclusion') != 'success'):
            raise ValueError('Fresh compiler or input-contract test step did not succeed')
    return job['id']


def github(route, token):
    request = urllib.request.Request('https://api.github.com/repos/' + REPOSITORY + '/' + route,
                                     headers={'Authorization': 'Bearer ' + token,
                                              'Accept': 'application/vnd.github+json',
                                              'X-GitHub-Api-Version': '2022-11-28'})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def verify_job(repo):
    source = os.environ.get('GITHUB_SHA', '')
    run_id, attempt = require_context(os.environ.get('GITHUB_REPOSITORY', ''), source,
                                      os.environ.get('GITHUB_RUN_ID', ''),
                                      os.environ.get('GITHUB_RUN_ATTEMPT', ''))
    if (subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD'], text=True).strip() != source or
            subprocess.call(['git', '-C', str(repo), 'diff', '--quiet', 'HEAD']) != 0):
        raise ValueError('Signing checkout differs from dispatched source')
    token = os.environ.get('GH_TOKEN', '')
    if not token:
        raise ValueError('Missing workflow metadata credential')
    route = f'actions/runs/{run_id}/attempts/{attempt}'
    job_id = require_dependency(github(route, token),
                                github(route + '/jobs?per_page=100', token), source, run_id, attempt)
    print(f'Fresh same-attempt private compiler job {job_id} verified for {source}.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('verify-job',))
    parser.add_argument('--repo', type=Path, required=True)
    args = parser.parse_args()
    try:
        verify_job(args.repo)
    except Exception as error:
        # Never dump API bodies, credentials, environment or private compiler paths.
        print('[workflow gate] rejected: ' + (str(error) if isinstance(error, ValueError)
                                            else type(error).__name__), file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
