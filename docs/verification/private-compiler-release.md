# Private compiler prerequisite for signed APKs

The release workflow has two jobs in the same run/attempt:

1. `patch-profiles` runs on a one-job Windows runner with the lawful original
   game assemblies and authenticated Android player inputs already on that host.
   It compiles/verifies both profiles, runs the full CI input-contract suite with
   private logs, and re-verifies the physical gate after the suite.
2. `release` runs on GitHub-hosted Ubuntu only after that dependency succeeds.
   Before decoding the persistent signing key and before building the APK,
   `workflow_gate.py` checks GitHub's actual same-attempt job/step outcomes and
   exact source identity. Signing and Gradle never run on the private compiler
   host; it receives no signing secrets.

A caller-authored/uploaded JSON, previous run, failed/skipped step, or dry-run
exception cannot substitute for this dependency. No game DLL, raw compiler
receipt, private test log, or private path belongs in an Actions artifact/cache.
Only the verified content-free APK is uploaded.

## Disposable runner admission

Supply a fresh `compiler_runner` dispatch label matching
`dualsouls-compile-<32 lowercase hexadecimal characters>`. Register the Windows
runner with only that label, no default catch-all labels, `--ephemeral`, and no
service. Do not start a general unattended runner for a public repository.

Before starting it, verify the reviewed repository has only the manual release
workflow, freeze the exact dispatched commit/run/attempt, and copy
`private_runner_guard.ps1` beside a private `expected-job.json`. Set the runner
process's `ACTIONS_RUNNER_HOOK_JOB_STARTED` to that reviewed copy. The expectation
contains exactly these string fields from the intended GitHub context:

- `GITHUB_REPOSITORY`: `Darkaxt/HollowKnightAndroid`
- `GITHUB_WORKFLOW_REF`: the release workflow at the dispatched branch
- `GITHUB_WORKFLOW_SHA` and `GITHUB_SHA`: the exact reviewed commit
- `GITHUB_JOB`: `patch-profiles`
- `GITHUB_RUN_ID` and `GITHUB_RUN_ATTEMPT`: the intended run/attempt
- `GITHUB_EVENT_NAME`: `workflow_dispatch`

The hook rejects any different first job before checkout/workflow steps. Keep
its expectation and hook outside the checkout, and do not allow workflow input
to select their paths. Ephemeral alone is not first-job isolation.

Provide `DUALSOULS_INPUT_CONTRACT` and its reviewed
`DUALSOULS_INPUT_CONTRACT_SHA256` in the runner process environment, not public
repository variables. The existing contract schema and physical profile/player
identity checks are unchanged. Also provide an existing absolute
`DUALSOULS_PRIVATE_ROOT` outside the checkout and runner `_work/_temp` tree.
GitHub's runner empties `RUNNER_TEMP` at job teardown, including failed jobs;
therefore compiler/test witnesses use this separate private root instead.
The workflow masks that root and never uploads it. Each disposable runner owns
one fresh root; do not reuse previous outputs as a new run's evidence.

Input consumers are read-only; compiler/weave output is separate. Scrub unrelated
host credentials before starting the runner.
After terminal job completion/failure, verify runner registration is gone and
retain the required private evidence. No persistent service or licensed-input
upload is part of this procedure.
