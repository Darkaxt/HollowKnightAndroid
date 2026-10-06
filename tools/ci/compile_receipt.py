"""Local compiler witnesses and a fail-closed, same-checkout release prerequisite.

Receipts are integrity records from this trusted execution, not attestations of
arbitrary downloaded JSON. Raw logs, private paths and binary stages stay local.
"""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import datetime

PROFILES = ('hollow-knight', 'silksong')
ROLES = {'source', 'reference', 'analyzer', 'analyzer-dependency', 'additional', 'config', 'embedded', 'sourcelink', 'incremental', 'import', 'assets'}


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def parse_json(text):
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError('Duplicate JSON key')
            result[key] = value
        return result
    def constant(value):
        raise ValueError('Nonfinite JSON number: ' + value)
    return json.loads(text, object_pairs_hook=pairs, parse_constant=constant)


def load(path):
    p = Path(path)
    if p.stat().st_size > 32 * 1024 * 1024:
        raise ValueError('Receipt exceeds size budget')
    return parse_json(p.read_text(encoding='utf-8-sig'))


def write(path, data):
    with Path(path).open('x', encoding='utf-8') as stream:
        json.dump(data, stream, indent=2, allow_nan=False)


def identity(path):
    p = Path(path).absolute()
    if p.is_symlink() or not p.is_file() or any(c in str(p) for c in '\n\r|'):
        raise ValueError('Invalid file identity: ' + str(p))
    digest = hashlib.sha256()
    with p.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return {'path': str(p), 'size': p.stat().st_size, 'sha256': digest.hexdigest()}


def verify_identity(item):
    if (not isinstance(item, dict) or type(item.get('size')) is not int or
            item['size'] < 0 or not re.fullmatch('[0-9a-f]{64}', item.get('sha256', ''))):
        raise ValueError('Malformed file identity')
    if identity(item['path']) != item:
        raise ValueError('File changed: ' + item['path'])


def token_valid(token):
    if not re.fullmatch('[A-Za-z0-9][A-Za-z0-9:._-]{0,159}', token):
        raise ValueError('Invalid execution token')


def process_identity(process):
    if os.name == 'nt':
        from ctypes import wintypes
        kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        kernel.GetProcessTimes.argtypes = [wintypes.HANDLE] + [ctypes.POINTER(wintypes.FILETIME)] * 4
        times = [wintypes.FILETIME() for _ in range(4)]
        if not kernel.GetProcessTimes(int(process._handle), *[ctypes.byref(t) for t in times]):
            raise OSError(ctypes.get_last_error(), 'GetProcessTimes')
        creation = str((times[0].dwHighDateTime << 32) | times[0].dwLowDateTime)
    else:
        stat = Path('/proc', str(process.pid), 'stat').read_text()
        creation = Path('/proc/sys/kernel/random/boot_id').read_text().strip() + ':' + stat.rsplit(')', 1)[1].split()[19]
    return {'pid': process.pid, 'creation': creation}


def require_process(record):
    if (type(record.get('exit')) is not int or record['exit'] != 0 or
            record.get('status') != 'exited_waited' or record.get('timedOut') is not False or
            record.get('capturedBeforeWait') is not True or
            type(record.get('child', {}).get('pid')) is not int or
            not record.get('child', {}).get('creation')):
        raise ValueError('Process did not complete with witnessed success')


def run_process(argv, cwd, directory):
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=False)
    executable = shutil.which(str(argv[0]))
    if not executable:
        raise ValueError('Required executable is absent')
    argv = [str(Path(executable).resolve()), *map(str, argv[1:])]
    record = {'argv': argv, 'cwd': str(Path(cwd).absolute()), 'started': now(),
              'parentPid': os.getpid(), 'executable': identity(argv[0]),
              'status': 'started', 'timedOut': False}
    with (directory / 'stdout.log').open('xb') as stdout, (directory / 'stderr.log').open('xb') as stderr:
        process = subprocess.Popen(argv, cwd=cwd, stdout=stdout, stderr=stderr)
        try:
            record['child'] = process_identity(process)
            record['capturedBeforeWait'] = True
            write(directory / 'started.json', record)
        finally:
            record['exit'] = process.wait()
    record.update(status='exited_waited', finished=now(),
                  stdout=identity(directory / 'stdout.log'), stderr=identity(directory / 'stderr.log'))
    write(directory / 'process.json', record)
    return record


def verify_process(record):
    require_process(record)
    for field in ('executable', 'stdout', 'stderr'):
        verify_identity(record[field])


def file_set(root, suffixes=None):
    paths = sorted(p for p in Path(root).rglob('*') if p.is_file() and
                   (suffixes is None or p.suffix.lower() in suffixes))
    if len(paths) > 20000:
        raise ValueError('File closure exceeds budget')
    return [identity(p) for p in paths]


def read_inputs(path):
    result = {role: {} for role in ROLES}
    for line in Path(path).read_text(encoding='utf-8-sig').splitlines():
        role, digest, name = line.split('|')
        if role not in ROLES or not re.fullmatch('[0-9A-Fa-f]{64}', digest):
            raise ValueError('Malformed compiler input')
        item = identity(name)
        if item['sha256'] != digest.lower():
            raise ValueError('Compiler input changed: ' + name)
        key = os.path.normcase(str(Path(name).absolute()))
        if key in result[role] and result[role][key] != item:
            raise ValueError('Ambiguous compiler input')
        result[role][key] = item
    return {role: list(items.values()) for role, items in result.items()}


def argument_paths(arguments, prefix, base):
    paths = []
    for arg in arguments:
        if arg.startswith(prefix):
            value = arg[len(prefix):]
            # These profile projects use ordinary references, never aliases.
            for name in re.findall(r'"[^"]*"|[^,]+', value):
                p = Path(name.strip('"'))
                paths.append(os.path.normcase(str((p if p.is_absolute() else base / p).resolve())))
    return set(paths)


def verify_build(build):
    if (set(build) != {'schemaVersion', 'kind', 'token', 'inputs', 'arguments', 'selection', 'tools',
                       'compilerTask', 'compilerTool', 'process', 'sdkProbe', 'witnesses', 'output', 'publishedOutputs'} or
            build.get('kind') != 'compiler-build' or type(build.get('schemaVersion')) is not int or build['schemaVersion'] != 1):
        raise ValueError('Unknown compiler receipt')
    token_valid(build['token'])
    if set(build['inputs']) != ROLES:
        raise ValueError('Incomplete compiler input roles')
    verify_process(build['process'])
    verify_process(build['sdkProbe'])
    for item in build['witnesses'] + build['tools'] + build['publishedOutputs'] + [build['output'], build['compilerTask'], build['compilerTool']]:
        verify_identity(item)
    matches = [x for x in build['publishedOutputs'] if Path(x['path']).name == Path(build['output']['path']).name]
    if len(matches) != 1 or matches[0]['sha256'] != build['output']['sha256']:
        raise ValueError('Fresh published DLL differs from compiler output')
    for key in ('compilerTask', 'compilerTool'):
        if build[key] not in build['tools']:
            raise ValueError('Executed compiler missing from tool closure')
    witnesses = {Path(x['path']).name: Path(x['path']) for x in build['witnesses']}
    if set(witnesses) != {'before.tsv', 'after.tsv', 'arguments.txt', 'selection.txt', 'output.tsv'} or len(build['witnesses']) != 5:
        raise ValueError('Missing or duplicate Csc witness')
    if (read_inputs(witnesses['before.tsv']) != build['inputs'] or
            read_inputs(witnesses['after.tsv']) != build['inputs'] or
            witnesses['arguments.txt'].read_text(encoding='utf-8-sig').splitlines() != build['arguments']):
        raise ValueError('Compiler receipt does not agree with Csc witnesses')
    selection = witnesses['selection.txt'].read_text(encoding='utf-8-sig').splitlines()
    if len(selection) != 8 or selection[-1] != build['token'] or build['selection'] != {
            'sdk': selection[3], 'runtime': selection[4], 'framework': selection[5], 'project': selection[6]}:
        raise ValueError('Compiler selection receipt disagreement')
    digest, path = witnesses['output.tsv'].read_text(encoding='utf-8-sig').strip().split('|')
    if identity(path) != build['output'] or digest.lower() != build['output']['sha256']:
        raise ValueError('Compiler output witness disagreement')
    for role, items in build['inputs'].items():
        if role not in ROLES:
            raise ValueError('Unsupported compiler input role')
        for item in items:
            verify_identity(item)
    args = build['arguments']
    if not args or any(a.startswith('@') for a in args):
        raise ValueError('Empty or unsupported response arguments')
    base = Path(build['selection']['project']).parent
    sources = {os.path.normcase(x['path']) for x in build['inputs']['source']}
    source_args = [Path(a.strip('"')) for a in args
                   if not a.startswith('-') and not re.match(r'^/[A-Za-z][A-Za-z0-9+-]*(?::|$)', a)]
    actual_sources = {os.path.normcase(str((p if p.is_absolute() else base / p).resolve())) for p in source_args}
    if not sources or sources != actual_sources:
        raise ValueError('Csc source argument/input disagreement')
    for role, prefix in [('reference', '/reference:'), ('analyzer', '/analyzer:'),
                         ('additional', '/additionalfile:'), ('config', '/analyzerconfig:'),
                         ('embedded', '/embed:'), ('sourcelink', '/sourcelink:')]:
        expected = {os.path.normcase(x['path']) for x in build['inputs'][role]}
        if expected != argument_paths(args, prefix, base):
            raise ValueError('Csc ' + role + ' argument/input disagreement')
    if any(a.startswith(('/resource:', '/linkresource:', '/win32res:', '/keyfile:')) for a in args):
        raise ValueError('Unsupported compiler-bearing argument')


def run_build(repo, directory, token, arguments):
    repo, directory = Path(repo).absolute(), Path(directory).absolute()
    token_valid(token)
    directory.mkdir(parents=True, exist_ok=False)
    target = repo / 'tools/ci/compiler-receipt.targets'
    dotnet = shutil.which('dotnet')
    if not dotnet:
        raise ValueError('dotnet absent')
    home = Path(dotnet).resolve().parent
    # Resolve the candidate SDK before the build, then require the actual Csc
    # task selection to agree. This avoids hashing unrelated installed SDKs.
    probe = run_process(['dotnet', '--version'], repo, directory / 'sdk-selection')
    require_process(probe)
    candidate_version = Path(probe['stdout']['path']).read_text(encoding='utf-8-sig').strip()
    candidate = home / 'sdk' / candidate_version
    if not candidate.is_dir():
        raise ValueError('Selected SDK is not in resolved dotnet home')
    before_tools = [identity(p) for p in sorted(candidate.iterdir()) if p.is_file() and p.suffix.lower() in {'.dll', '.json', '.exe'}]
    for folder in ('Roslyn', 'Sdks'):
        before_tools += file_set(candidate / folder, {'.dll', '.exe', '.json', '.props', '.targets', '.so', '.dylib'})
    before_tools += file_set(candidate, {'.props', '.targets'})
    before_tools += file_set(home / 'shared/Microsoft.NETCore.App', {'.dll', '.json', '.so', '.dylib'})
    before_tools += file_set(home / 'host', {'.dll', '.json', '.so', '.dylib'})
    before_tools = list({x['path']: x for x in before_tools}.values())
    # Current projects have one NuGet package; unknown package closure is rejected.
    nuget = Path(os.environ.get('NUGET_PACKAGES', str(Path.home() / '.nuget/packages')))
    package_before = file_set(nuget / 'mono.cecil/0.11.6')
    raw = directory / 'csc'
    raw.mkdir()
    args = [*arguments, '-v:diagnostic', '-maxcpucount:1',
            '-p:ProvideCommandLineArgs=true', f'-p:CustomAfterMicrosoftCommonTargets={target}',
            f'-p:CompilerReceiptDirectory={raw}', f'-p:CompilerReceiptToken={token}']
    process = run_process(['dotnet', *args], repo, directory / 'process')
    require_process(process)
    witnesses = [identity(raw / n) for n in ('before.tsv', 'after.tsv', 'arguments.txt', 'selection.txt', 'output.tsv')]
    before = read_inputs(raw / 'before.tsv')
    after = read_inputs(raw / 'after.tsv')
    if before != after:
        raise ValueError('Compiler input closure changed')
    selection = (raw / 'selection.txt').read_text(encoding='utf-8-sig').splitlines()
    if len(selection) != 8 or selection[-1] != token:
        raise ValueError('Compiler selection/token missing')
    sdk, sdk_roots, roslyn, sdk_version, runtime, framework, project, _ = selection
    diagnostic = Path(process['stdout']['path']).read_text(encoding='utf-8-sig', errors='replace')
    task_assemblies = re.findall(r'Using "Csc" task from assembly "([^"]+)"', diagnostic)
    if len(set(task_assemblies)) != 1 or 'Task "Csc" skipped' in diagnostic:
        raise ValueError('Actual executed Csc task witness absent')
    task = Path(task_assemblies[0]).resolve()
    resolved_roslyn = Path(roslyn).resolve()
    if (sdk_version != candidate_version or Path(sdk).resolve() != candidate.resolve() or
            task.parent not in (resolved_roslyn, resolved_roslyn / 'bincore')):
        raise ValueError('Unexpected resolved compiler task')
    if not re.search(r'Done executing task "Csc"\.', diagnostic):
        raise ValueError('Csc successful execution witness absent')
    compiler_paths = re.findall(r'PathToTool=([^\r\n]+?)\s+\(TaskId:', diagnostic)
    if compiler_paths:
        compiler = Path(compiler_paths[-1]).resolve()
    else:
        compiler = resolved_roslyn / 'bincore/csc.dll'
        if str(compiler).lower() not in diagnostic.lower():
            raise ValueError('Executed compiler tool witness absent')
    if compiler.parent != resolved_roslyn / 'bincore' or compiler.name not in ('csc.dll', 'csc.exe'):
        raise ValueError('Unexpected executed compiler tool')
    selected_roots = [Path(sdk).absolute(), home / 'shared/Microsoft.NETCore.App' / runtime, home / 'host']
    tools = [x for x in before_tools if any(Path(x['path']).is_relative_to(root) for root in selected_roots)]
    if not all(any(x['path'] == str(p) for x in tools) for p in (task, compiler)):
        raise ValueError('Executed compiler not in frozen tool closure')
    for x in tools:
        verify_identity(x)
    for asset in before['assets']:
        assets = load(asset['path'])
        for name, entry in assets.get('libraries', {}).items():
            if entry.get('type') == 'package' and name.lower() != 'mono.cecil/0.11.6':
                raise ValueError('Unsupported NuGet tool dependency: ' + name)
        if any(e.get('type') == 'package' for e in assets.get('libraries', {}).values()):
            if not package_before:
                raise ValueError('Package tool closure was not present before compile')
            for x in package_before:
                verify_identity(x)
            tools.extend(package_before)
    digest, output_path = (raw / 'output.tsv').read_text(encoding='utf-8-sig').strip().split('|')
    output = identity(output_path)
    if output['sha256'] != digest.lower():
        raise ValueError('Compiler output changed')
    output_options = [i for i, value in enumerate(arguments) if value in ('-o', '--output')]
    if len(output_options) != 1 or output_options[0] + 1 >= len(arguments):
        raise ValueError('Require one explicit compiler output directory')
    published = Path(arguments[output_options[0] + 1])
    published_outputs = [identity(p) for p in sorted(published.iterdir()) if p.is_file() and p.suffix.lower() in ('.dll', '.json')]
    build = {'schemaVersion': 1, 'kind': 'compiler-build', 'token': token,
             'inputs': before, 'arguments': (raw / 'arguments.txt').read_text(encoding='utf-8-sig').splitlines(),
             'selection': {'sdk': sdk_version, 'runtime': runtime, 'framework': framework, 'project': project},
             'tools': tools, 'compilerTask': identity(task), 'compilerTool': identity(compiler),
             'process': process, 'sdkProbe': probe, 'witnesses': witnesses, 'output': output,
             'publishedOutputs': published_outputs}
    verify_build(build)
    write(directory / 'build-success.json', build)
    return build


def tracked(repo):
    names = subprocess.check_output(['git', '-C', str(repo), 'ls-files', '-z']).decode().split('\0')
    # These mandatory consumers are frozen even in a pre-publication host run.
    names += ['tools/ci/compile_receipt.py', 'tools/ci/compiler-receipt.targets', 'tools/ci/check_patch_profiles.ps1']
    return {n: identity(Path(repo) / n)['sha256'] for n in sorted(set(names)) if n}


def revision(repo):
    return subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD'], text=True).strip()


def admit_contract(repo, path):
    contract = load(path)
    trusted = os.environ.get('DUALSOULS_INPUT_CONTRACT_SHA256', '')
    if not re.fullmatch('[0-9a-f]{64}', trusted) or identity(path)['sha256'] != trusted:
        raise ValueError('Provisioning contract lacks reviewed administrator identity')
    if set(contract) != {'schemaVersion', 'source', 'profiles'} or type(contract['schemaVersion']) is not int or contract['schemaVersion'] != 1 or contract['source'] != 'owner-provisioned-read-only':
        raise ValueError('Unreviewed provisioning contract')
    # A machine-local mount is an administrative provisioning decision, not a
    # command-line claim. Release supplies its reviewed mount path, never a URL.
    profiles = contract['profiles']
    if set(profiles) != set(PROFILES):
        raise ValueError('Provision exactly both profiles')
    for profile, roots in profiles.items():
        if set(roots) != {'depot', 'player'}:
            raise ValueError('Unexpected provision keys')
        for name in roots.values():
            if not Path(name).is_absolute() or not Path(name).is_dir():
                raise ValueError('Provisioned root is absent or relative')
        sys.path.insert(0, str(Path(repo) / 'tools/ci'))
        import verify_unity_player
        verify_unity_player.verify_player(profile, Path(roots['player']))
        game = identity(Path(roots['depot']) / 'Assembly-CSharp.dll')
        if profile == 'hollow-knight':
            manifest = load(Path(repo) / 'src/SilksongLauncher.Launcher/app/src/main/assets/profiles/hollow-knight-1.5.12620.json')
            if manifest['profileId'] != profile or manifest['gameVersion'] != '1.5.12620':
                raise ValueError('Wrong Hollow Knight authority')
            entries = [x for x in manifest['requiredFiles'] if x['relativePath'] == 'Managed/Assembly-CSharp.dll']
            if len(entries) != 1 or entries[0]['action'] != 'copy' or entries[0]['size'] != game['size'] or entries[0]['sha256'] != game['sha256']:
                raise ValueError('Hollow Knight original input mismatch')
        else:
            authority = Path(repo) / 'tools/bundle-surgery/BridgeSilksongNormalDeath.cs'
            if identity(authority)['sha256'] != 'c33c6fa2bb4c9791a6c10c8b653a68aeffecfc468d05dcc5dc2945f6c613ee7b':
                raise ValueError('Silksong original authority changed')
            text = authority.read_text()
            pins = re.findall(r'(?m)^[ \t]*internal const string PINNED_ASSEMBLY_SHA256 = "([0-9a-f]{64})";', text)
            versions = re.findall(r'(?m)^[ \t]*internal const string PINNED_GAME_VERSION = "([^"]+)";', text)
            if len(pins) != 1 or versions != ['1.0.29980'] or pins[0] != game['sha256']:
                raise ValueError('Silksong original input mismatch')
    return contract


def weaver_runtime(weaver):
    directory = Path(weaver).parent
    return [identity(directory / name) for name in
            ('ModWeaver.deps.json', 'ModWeaver.runtimeconfig.json', 'Mono.Cecil.dll')]


def weave(repo, stage, patch, weaver, token, directory):
    stage, directory = Path(stage), Path(directory)
    directory.mkdir(parents=True, exist_ok=False)
    stage.mkdir(exist_ok=False)
    depot = Path(patch['depot'])
    originals = file_set(depot, {'.dll'})
    for x in originals:
        shutil.copyfile(x['path'], stage / Path(x['path']).name)
    compiled = identity(patch['dll'])
    shutil.copyfile(compiled['path'], stage / Path(compiled['path']).name)
    available = file_set(stage, {'.dll'})
    initial = identity(stage / 'Assembly-CSharp.dll')
    runtime_files = weaver_runtime(weaver)
    first = run_process(['dotnet', str(weaver), 'builtin', '--assemblies', str(stage)], repo, directory / 'first')
    require_process(first)
    first_hash = identity(stage / 'Assembly-CSharp.dll')['sha256']
    second = run_process(['dotnet', str(weaver), 'builtin', '--assemblies', str(stage)], repo, directory / 'second')
    require_process(second)
    second_hash = identity(stage / 'Assembly-CSharp.dll')['sha256']
    if first_hash == initial['sha256'] or first_hash != second_hash:
        raise ValueError('Weave missing or not byte-idempotent')
    for x in originals + runtime_files:
        verify_identity(x)
    return {'token': token, 'compiledPatch': compiled, 'weaver': identity(weaver),
            'weaverRuntime': runtime_files,
            'availableResolverInputs': available, 'originalInputs': originals,
            'initial': initial['sha256'], 'first': first_hash, 'second': second_hash,
            'firstProcess': first, 'secondProcess': second,
            'finalGame': identity(stage / 'Assembly-CSharp.dll')}


def verify_gate_data(repo, gate, source_revision, token):
    token_valid(token)
    if (set(gate) != {'schemaVersion', 'kind', 'token', 'sourceRevision', 'tracked', 'verifier', 'provisionContract', 'profiles', 'privateInputs'} or
            type(gate.get('schemaVersion')) is not int or gate['schemaVersion'] != 1 or gate.get('kind') != 'both-profile-gate' or
            gate.get('token') != token or gate.get('sourceRevision') != source_revision or
            not re.fullmatch('[0-9a-f]{40}', source_revision) or revision(repo) != source_revision):
        raise ValueError('Gate correlation or schema mismatch')
    if gate.get('tracked') != tracked(repo):
        raise ValueError('Current tracked sources changed')
    verify_identity(gate['verifier'])
    if gate['verifier'] != identity(Path(repo) / 'tools/ci/compile_receipt.py'):
        raise ValueError('Wrong gate verifier')
    verify_identity(gate['provisionContract'])
    contract = admit_contract(repo, gate['provisionContract']['path'])
    current_private = []
    for roots in contract['profiles'].values():
        for root in roots.values():
            current_private.extend(file_set(root, {'.dll'}))
    if gate['privateInputs'] != current_private:
        raise ValueError('Provisioned input closure changed')
    for item in gate['privateInputs']:
        verify_identity(item)
    receipts = gate['profiles']
    if len(receipts) != 2 or {x['profile'] for x in receipts} != set(PROFILES):
        raise ValueError('Require exactly both profiles')
    for p in receipts:
        if set(p) != {'profile', 'checker', 'patch', 'builds', 'weave'}:
            raise ValueError('Unexpected profile receipt')
        verify_process(p['checker'])
        verify_identity(p['patch'])
        for b in p['builds']:
            if b['token'] != token:
                raise ValueError('Stale compiler token')
            verify_build(b)
        if len(p['builds']) != (2 if p['profile'] == 'hollow-knight' else 1):
            raise ValueError('Missing profile/weaver compile')
        roots = contract['profiles'][p['profile']]
        patch_build = p['builds'][0]
        if patch_build['selection']['framework'] != 'netstandard2.1' or '/langversion:9.0' not in patch_build['arguments']:
            raise ValueError('Wrong patch framework or language version')
        definitions = ';'.join(a[len('/define:'):] for a in patch_build['arguments'] if a.startswith('/define:'))
        if not all(name in definitions.split(';') for name in ('UNITY_ANDROID', 'ENABLE_INPUT_SYSTEM')):
            raise ValueError('Missing Android patch compiler definitions')
        sys.path.insert(0, str(Path(repo) / 'tools/ci'))
        import verify_unity_player
        members = verify_unity_player.load_authority()[p['profile']]['members']
        required_refs = [identity(Path(roots['depot']) / 'Assembly-CSharp.dll')]
        required_refs += [identity(Path(roots['player']) / m['file']) for m in members]
        if any(x not in patch_build['inputs']['reference'] for x in required_refs):
            raise ValueError('Compiler did not consume authenticated profile references')
        own = Path(repo) / 'tools' / (p['profile'] + '-patches') / 'src'
        shared = Path(repo) / 'tools/shared-patches/src'
        required_sources = file_set(own, {'.cs'}) + file_set(shared, {'.cs'})
        if any(x not in patch_build['inputs']['source'] for x in required_sources):
            raise ValueError('Compiler omitted current patch sources')
        if p['builds'][0]['output']['sha256'] != p['patch']['sha256']:
            raise ValueError('Compiled patch identity mismatch')
        w = p['weave']
        if (w['token'] != token or w['first'] == w['initial'] or w['first'] != w['second'] or
                w['compiledPatch'] != p['patch']):
            raise ValueError('Missing or partial weave witness')
        stage = Path(w['finalGame']['path']).parent
        if w['originalInputs'] != file_set(roots['depot'], {'.dll'}) or w['initial'] != required_refs[0]['sha256']:
            raise ValueError('Weave original profile input disagreement')
        expected_stage = [{**x, 'path': str(stage / Path(x['path']).name)} for x in w['originalInputs'] + [p['patch']]]
        if sorted(expected_stage, key=lambda x: x['path']) != sorted(w['availableResolverInputs'], key=lambda x: x['path']):
            raise ValueError('Empty, mixed or incomplete resolver stage')
        for name in ('firstProcess', 'secondProcess'):
            verify_process(w[name])
            if w[name]['argv'][1:] != [w['weaver']['path'], 'builtin', '--assemblies', str(stage)]:
                raise ValueError('Weaver command/stage identity mismatch')
        for x in w['originalInputs'] + w['weaverRuntime'] + [w['weaver'], w['finalGame']]:
            verify_identity(x)
        if w['finalGame']['sha256'] != w['second']:
            raise ValueError('Woven artifact changed')
    hk = next(x for x in receipts if x['profile'] == 'hollow-knight')
    ss = next(x for x in receipts if x['profile'] == 'silksong')
    if Path(hk['weave']['finalGame']['path']).parent == Path(ss['weave']['finalGame']['path']).parent:
        raise ValueError('Profile stages must be isolated')
    if (hk['builds'][1]['output']['sha256'] != hk['weave']['weaver']['sha256'] or
            hk['weave']['weaver'] != ss['weave']['weaver'] or
            hk['weave']['weaverRuntime'] != ss['weave']['weaverRuntime'] or
            any(x not in hk['builds'][1]['publishedOutputs'] for x in hk['weave']['weaverRuntime'])):
        raise ValueError('Unrelated weaver artifact')


def check_profiles(repo, contract_path, output, token):
    repo, output = Path(repo).absolute(), Path(output).absolute()
    token_valid(token)
    contract = admit_contract(repo, contract_path)
    if output.is_relative_to(repo):
        raise ValueError('Private output must be outside checkout')
    source = revision(repo)
    frozen = tracked(repo)
    if os.environ.get('GITHUB_ACTIONS') == 'true':
        if source != os.environ.get('GITHUB_SHA') or subprocess.call(['git', '-C', str(repo), 'diff', '--quiet', 'HEAD']) != 0:
            raise ValueError('Release checkout differs from dispatched commit')
    private_inputs = []
    for roots in contract['profiles'].values():
        for root in roots.values():
            private_inputs.extend(file_set(root, {'.dll'}))
    output.mkdir(parents=True, exist_ok=False)
    receipts = []
    for profile in PROFILES:
        roots = contract['profiles'][profile]
        folder = output / profile
        folder.mkdir()
        receipt = folder / 'receipts'
        work = folder / 'work'
        script = repo / 'tools' / (profile + '-patches' if profile == 'silksong' else 'hollow-knight-patches') / 'check.ps1'
        dll = work / 'HollowKnightPatches.dll' if profile == 'hollow-knight' else work / 'bin/SilksongPatches.dll'
        args = ['pwsh', '-NoLogo', '-NoProfile', '-NonInteractive', '-File', str(script),
                '-Depot', roots['depot'], '-Player', roots['player'], '-RetainArtifacts',
                '-ReceiptDirectory', str(receipt), '-RunToken', token]
        args += ['-Output' if profile == 'hollow-knight' else '-Work', str(work)]
        process = run_process(args, repo, folder / 'checker')
        require_process(process)
        builds = [load(receipt / 'patch/build-success.json')]
        if profile == 'hollow-knight':
            builds.append(load(receipt / 'weaver/build-success.json'))
            weaver = work / 'weaver/ModWeaver.dll'
            # HK's existing mandatory weave, instrumented at its original seam.
            w = load(receipt / 'weave.json')
        else:
            w = weave(repo, folder / 'stage', {'depot': roots['depot'], 'dll': str(dll)},
                      weaver, token, folder / 'weave')
        receipts.append({'profile': profile, 'checker': process, 'builds': builds,
                         'patch': identity(dll), 'weave': w})
    gate = {'schemaVersion': 1, 'kind': 'both-profile-gate', 'token': token,
            'sourceRevision': source, 'tracked': frozen, 'verifier': identity(Path(__file__)),
            'provisionContract': identity(contract_path), 'profiles': receipts, 'privateInputs': private_inputs}
    verify_gate_data(repo, gate, source, token)
    write(output / 'both-profiles-success.json', gate)
    return gate


def weave_start(directory, stage, depot, patch, weaver, token):
    token_valid(token)
    record = {'token': token, 'compiledPatch': identity(patch), 'weaver': identity(weaver),
              'weaverRuntime': weaver_runtime(weaver),
              'availableResolverInputs': file_set(stage, {'.dll'}),
              'originalInputs': file_set(depot, {'.dll'}),
              'initial': identity(Path(stage) / 'Assembly-CSharp.dll')['sha256']}
    write(Path(directory) / 'weave-start.json', record)


def weave_end(directory, stage, first_hash):
    directory = Path(directory)
    record = load(directory / 'weave-start.json')
    record['first'] = first_hash.replace('-', '').lower()
    record['finalGame'] = identity(Path(stage) / 'Assembly-CSharp.dll')
    record['second'] = record['finalGame']['sha256']
    if not re.fullmatch('[0-9a-f]{64}', record['first']) or record['first'] == record['initial'] or record['first'] != record['second']:
        raise ValueError('Weave missing or not byte-idempotent')
    for name in ('first', 'second'):
        record[name + 'Process'] = load(directory / name / 'process.json')
        verify_process(record[name + 'Process'])
    for item in record['originalInputs'] + record['weaverRuntime'] + [record['compiledPatch'], record['weaver']]:
        verify_identity(item)
    write(directory / 'weave.json', record)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('run-build', 'run-process', 'check-profiles', 'verify-gate', 'weave-start', 'weave-end'))
    parser.add_argument('--repo', type=Path, required=True)
    parser.add_argument('--directory', type=Path)
    parser.add_argument('--run-token')
    parser.add_argument('--contract', type=Path)
    parser.add_argument('--gate', type=Path)
    parser.add_argument('--source-revision')
    parser.add_argument('--stage', type=Path)
    parser.add_argument('--depot', type=Path)
    parser.add_argument('--patch', type=Path)
    parser.add_argument('--weaver', type=Path)
    parser.add_argument('--first-hash')
    args, argv = parser.parse_known_args()
    argv = argv[1:] if argv[:1] == ['--'] else argv
    if args.command not in ('run-build', 'run-process') and argv:
        parser.error('Unexpected command arguments')
    if args.command == 'run-build':
        run_build(args.repo, args.directory, args.run_token, argv)
    elif args.command == 'run-process':
        record = run_process(argv, args.repo, args.directory)
        require_process(record)
    elif args.command == 'check-profiles':
        check_profiles(args.repo, args.contract, args.directory, args.run_token)
    elif args.command == 'weave-start':
        weave_start(args.directory, args.stage, args.depot, args.patch, args.weaver, args.run_token)
    elif args.command == 'weave-end':
        weave_end(args.directory, args.stage, args.first_hash)
    else:
        verify_gate_data(args.repo, load(args.gate), args.source_revision, args.run_token)
    print('[receipt] verified ' + args.command)


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, KeyError, TypeError) as error:
        print('[receipt] rejected: ' + str(error), file=sys.stderr)
        sys.exit(1)
