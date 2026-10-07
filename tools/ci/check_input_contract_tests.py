"""Run the full input-contract suite privately on the exact compiler runner."""
import os
from pathlib import Path
import sys

from compile_receipt import admit_contract, require_process, run_process


def main():
    repo = Path(__file__).resolve().parents[2]
    contract = admit_contract(repo, os.environ['DUALSOULS_INPUT_CONTRACT'])
    private = Path(os.environ['DUALSOULS_PRIVATE_ROOT'])
    if not private.is_absolute() or not private.is_dir() or private.resolve().is_relative_to(repo):
        raise ValueError('Private witness root must be outside checkout')
    temp = private / 'test-temp'
    temp.mkdir(exist_ok=False)
    os.environ.update(DUALSOULS_RETAIN_TEST_FIXTURES='1', DUALSOULS_TEMP_ROOT=str(temp),
                      TMPDIR=str(temp), TEMP=str(temp), TMP=str(temp))
    for profile, player, depot in [('hollow-knight', 'UNITY_PLAYER_61', 'HK_ORIGINAL_DEPOT'),
                                  ('silksong', 'UNITY_PLAYER_50', 'SS_ORIGINAL_DEPOT')]:
        os.environ[player] = contract['profiles'][profile]['player']
        os.environ[depot] = contract['profiles'][profile]['depot']
    # Test failures may contain environment or private paths. Never stream raw
    # suite output to public Actions logs; keep the actual process witness local.
    for key in list(os.environ):
        if any(word in key.upper() for word in ('API_KEY', 'TOKEN', 'PASSWORD', 'SECRET',
                                               'CREDENTIAL', 'COOKIE', 'AUTH_KEY')):
            os.environ.pop(key)
    process = run_process([sys.executable, '-B', '-m', 'unittest', 'discover', 'tools/ci/tests', '-v'],
                          repo, temp / 'input-contract-tests')
    require_process(process)
    print('Private full input-contract suite completed successfully.')
    return 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except Exception:
        print('Private input-contract suite rejected; inspect retained local witnesses.', file=sys.stderr)
        raise SystemExit(1)
