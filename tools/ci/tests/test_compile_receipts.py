import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
HELPER = ROOT / 'tools/ci/compile_receipt.py'
TARGET = ROOT / 'tools/ci/compiler-receipt.targets'


def helper():
    spec = importlib.util.spec_from_file_location('compile_receipt', HELPER)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class CompilerReceiptTests(unittest.TestCase):
    def test_actual_compiler_receipt_seam_exists(self):
        self.assertTrue(TARGET.is_file(), 'Real CoreCompile has no receipt target')
        self.assertTrue(HELPER.is_file(), 'No compiler-bound receipt verifier')

    def test_strict_json_rejects_duplicate_keys_and_nonfinite_numbers(self):
        c = helper()
        for text in ('{"a":1,"a":2}', '{"a":NaN}', '{"a":Infinity}'):
            with self.subTest(text=text), self.assertRaises(ValueError):
                c.parse_json(text)

    def test_process_success_never_accepts_prose_null_timeout_or_unknown_identity(self):
        c = helper()
        valid = {'exit': 0, 'status': 'exited_waited', 'timedOut': False,
                 'child': {'pid': 1, 'creation': '123'}, 'capturedBeforeWait': True}
        c.require_process(valid)
        for key, value in (('exit', True), ('exit', None), ('exit', 1),
                           ('timedOut', True), ('status', 'started'),
                           ('child', {}), ('capturedBeforeWait', False)):
            with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                c.require_process(dict(valid, **{key: value}))

    def test_missing_contract_cannot_create_output_or_success(self):
        c = helper()
        base = Path(tempfile.mkdtemp(prefix='receipt-contract-', dir=os.environ.get('DUALSOULS_TEMP_ROOT')))
        output = base / 'not-created'
        with self.assertRaises((ValueError, FileNotFoundError)):
            c.check_profiles(ROOT, base / 'missing.json', output, 'host-test')
        self.assertFalse(output.exists())

    def test_real_compile_records_generated_sources_references_arguments_and_output(self):
        c = helper()
        base = Path(tempfile.mkdtemp(prefix='receipt-real-', dir=os.environ.get('DUALSOULS_TEMP_ROOT')))
        project = base / 'source with spaces'
        project.mkdir()
        source = project / 'Class with spaces, comma.cs'
        source.write_text('public class ReceiptFixture { public int Value => 42; }')
        csproj = project / 'Fixture.csproj'
        csproj.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                          '<TargetFramework>netstandard2.1</TargetFramework>'
                          '<SourceControlInformationFeatureSupported>true</SourceControlInformationFeatureSupported>'
                          '</PropertyGroup><ItemGroup><EmbeddedFiles Include="Class with spaces, comma.cs" />'
                          '<SourceRoot Include="$(MSBuildProjectDirectory)/" SourceLinkUrl="https://example.invalid/*" />'
                          '</ItemGroup></Project>')
        build = c.run_build(ROOT, base / 'receipt', 'host-test', [
            'build', str(csproj), '-c', 'Release', '-o', str(base / 'bin'),
            '--nologo', '-nodeReuse:false', '-p:UseSharedCompilation=false'])
        c.verify_build(build)
        roles = build['inputs']
        self.assertTrue(any(x['path'].endswith('Class with spaces, comma.cs') for x in roles['source']))
        self.assertTrue(any(x['path'].endswith('Class with spaces, comma.cs') for x in roles['embedded']))
        self.assertTrue(any('AssemblyInfo.cs' in x['path'] for x in roles['source']))
        self.assertTrue(any(x['path'].endswith('netstandard.dll') for x in roles['reference']))
        self.assertTrue(roles['sourcelink'])
        self.assertTrue(build['arguments'])
        self.assertGreater(build['output']['size'], 0)
        self.assertTrue(build['tools'])
        self.assertEqual('exited_waited', build['process']['status'])
        for change in ({'schemaVersion': True}, {'extra': 'unknown'}, {'arguments': []},
                       {'inputs': {}}, {'tools': []}, {'publishedOutputs': []}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                c.verify_build(dict(build, **change))
        source.write_text('public class Changed {}')
        with self.assertRaisesRegex(ValueError, 'changed'):
            c.verify_build(build)

    def test_weaver_style_net8_analyzer_metadata_is_settled_before_snapshot(self):
        c = helper()
        base = Path(tempfile.mkdtemp(prefix='receipt-net8-', dir=os.environ.get('DUALSOULS_TEMP_ROOT')))
        project = base / 'Metadata.csproj'
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                           '<TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType>'
                           '<UseAppHost>false</UseAppHost><ImplicitUsings>enable</ImplicitUsings>'
                           '</PropertyGroup></Project>')
        (base / 'Program.cs').write_text('public static class Program { public static int Main() => 0; }')
        build = c.run_build(ROOT, base / 'receipt', 'host-test', [
            'build', str(project), '-c', 'Release', '-o', str(base / 'bin'),
            '-nodeReuse:false', '-p:UseSharedCompilation=false'])
        c.verify_build(build)
        self.assertTrue(build['inputs']['analyzer'])
        self.assertTrue(any(x['path'].endswith('.globalconfig') for x in build['inputs']['config']))
        self.assertTrue(any('GlobalUsings.g.cs' in x['path'] for x in build['inputs']['source']))

    def test_silksong_native_receipt_call_preserves_msbuild_colon_arguments(self):
        c = helper()
        base = Path(tempfile.mkdtemp(prefix='receipt-pwsh-', dir=os.environ.get('DUALSOULS_TEMP_ROOT')))
        (base / 'PatchCheck.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                                               '<TargetFramework>netstandard2.1</TargetFramework>'
                                               '</PropertyGroup></Project>')
        (base / 'Program.cs').write_text('public class Program {}')
        receipts = base / 'receipts'
        receipts.mkdir()
        source = (ROOT / 'tools/silksong-patches/check.ps1').read_text()
        invocation = source.split("$log = Join-Path $work 'build.log'\nif ($ReceiptDirectory) {\n", 1)[1].split('    $exitCode = $LASTEXITCODE', 1)[0]
        quote = lambda p: "'" + str(p).replace("'", "''") + "'"
        script = (f'$repo={quote(ROOT)}; $work={quote(base)}; $ReceiptDirectory={quote(receipts)}; '
                  '$RunToken="host-test";\n' + invocation + '\nexit $LASTEXITCODE')
        process = c.run_process(['pwsh', '-NoProfile', '-NonInteractive', '-Command', script], ROOT, base / 'pwsh')
        c.require_process(process)
        build = c.load(receipts / 'patch/build-success.json')
        self.assertIn('-nodeReuse:false', build['process']['argv'])
        self.assertIn('-p:UseSharedCompilation=false', build['process']['argv'])

    def test_skipped_compiler_cannot_emit_success_receipt(self):
        c = helper()
        base = Path(tempfile.mkdtemp(prefix='receipt-skip-', dir=os.environ.get('DUALSOULS_TEMP_ROOT')))
        p = base / 'Fixture.csproj'
        p.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                     '<TargetFramework>netstandard2.1</TargetFramework>'
                     '</PropertyGroup></Project>')
        (base / 'Fixture.cs').write_text('public class Fixture {}')
        with self.assertRaises(ValueError):
            c.run_build(ROOT, base / 'receipt', 'host-test', [
                'build', str(p), '-nodeReuse:false', '-p:UseSharedCompilation=false',
                '-p:SkipCompilerExecution=true', '-o', str(base / 'bin')])
        self.assertFalse((base / 'receipt/build-success.json').exists())
        self.assertIn('Receipt requires real, unshared compiler execution',
                      (base / 'receipt/process/stdout.log').read_text(errors='replace'))

    def test_gate_rejects_compile_only_silksong_and_wrong_correlation(self):
        c = helper()
        for gate in ({}, {'schemaVersion': 1, 'kind': 'both-profile-gate',
                         'token': 'host-test', 'sourceRevision': 'a' * 40,
                         'profiles': [{'profile': 'silksong', 'compile': {'status': 'success'}}]}):
            with self.subTest(gate=gate), self.assertRaises(ValueError):
                c.verify_gate_data(ROOT, gate, 'a' * 40, 'host-test')


if __name__ == '__main__':
    unittest.main()
