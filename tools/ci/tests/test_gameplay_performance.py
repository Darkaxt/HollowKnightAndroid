"""Focused actual-body gameplay work regressions, not a native performance benchmark."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]


class GameplayPerformanceTest(unittest.TestCase):
    def test_production_body_regressions(self):
        source = Path(os.environ.get('GAMEPLAY_PERF_SOURCE_ROOT', ROOT))
        parent = Path(os.environ.get('GAMEPLAY_PERF_OUTPUT_ROOT', tempfile.gettempdir()))
        parent.mkdir(parents=True, exist_ok=True)
        stage = Path(tempfile.mkdtemp(prefix='gameplay-performance-', dir=parent))
        generated = subprocess.run([
            sys.executable, '-B', str(ROOT / 'tools/shared-patches-tests/generate_gameplay_perf_fixture.py'),
            str(stage / 'generated'), '--source-root', str(source)],
            capture_output=True, text=True, timeout=30)
        (stage / 'generator.log').write_text(generated.stdout + generated.stderr, encoding='utf-8')
        self.assertEqual(generated.returncode, 0, generated.stdout + generated.stderr)
        shutil.copyfile(ROOT / 'tools/shared-patches-tests/GameplayPerformanceFixture.cs', stage / 'Engine.cs')
        shutil.copyfile(ROOT / 'tools/shared-patches-tests/GameplayDiscoveryFixture.cs', stage / 'Discovery.cs')
        (stage / 'Perf.csproj').write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
            '<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
            '<EnableNETAnalyzers>false</EnableNETAnalyzers><Nullable>disable</Nullable>'
            '<TreatWarningsAsErrors>true</TreatWarningsAsErrors><NoWarn>CS0649;CS0414</NoWarn>'
            '</PropertyGroup></Project>', encoding='utf-8')
        built = subprocess.run(['dotnet', 'build', str(stage / 'Perf.csproj'), '-c', 'Release', '--nologo', '-v:q'],
            capture_output=True, text=True, timeout=90)
        (stage / 'build.log').write_text(built.stdout + built.stderr, encoding='utf-8')
        self.assertEqual(built.returncode, 0, built.stdout + built.stderr)
        ran = subprocess.run(['dotnet', str(stage / 'bin/Release/net8.0/Perf.dll')],
            capture_output=True, text=True, timeout=30)
        (stage / 'execution.log').write_text(ran.stdout + ran.stderr, encoding='utf-8')
        receipt = dict(source_root=str(source), stage=str(stage), generator_exit=generated.returncode,
            build_exit=built.returncode, execution_exit=ran.returncode,
            scope='Unchanged selected production bodies, modeled engine/data boundaries. Not native timing or FPS.')
        (stage / 'execution.json').write_text(json.dumps(receipt, indent=2), encoding='utf-8')
        self.assertEqual(ran.returncode, 0, ran.stdout + ran.stderr)
        results = json.loads(ran.stdout)
        self.assertGreaterEqual(len(results), 6)
        for name, result in results.items():
            with self.subTest(case=name):
                self.assertEqual(result, 'PASS')


if __name__ == '__main__':
    unittest.main()
