import importlib.util
import pathlib
import re
import tempfile
import unittest
import os
import shutil
import shlex
import subprocess
import sys
import textwrap


ROOT = pathlib.Path(__file__).resolve().parents[3]
HELPER = ROOT / "tools" / "ci" / "release_contract.py"
WORKFLOW = ROOT / ".github" / "workflows" / "release.yml"
BUILD_SCRIPT = ROOT / "tools" / "depot-to-apk" / "build.sh"
LAUNCHER_MANIFEST = (
    ROOT
    / "src"
    / "SilksongLauncher.Launcher"
    / "app"
    / "src"
    / "main"
    / "AndroidManifest.xml"
)
IL2CPP_BUILD_SCRIPT = ROOT / "tools" / "ondevice-il2cpp" / "build-il2cpp.sh"
IL2CPP_CONVERTER = (
    ROOT
    / "src"
    / "SilksongLauncher.Launcher"
    / "app"
    / "src"
    / "main"
    / "kotlin"
    / "dev"
    / "silksong"
    / "launcher"
    / "Il2cppConverter.kt"
)
ADAPTIVE_ICON_DIR = (
    ROOT
    / "tools"
    / "depot-to-apk"
    / "shell"
    / "res"
    / "mipmap-anydpi-v26"
)


def load_helper():
    spec = importlib.util.spec_from_file_location("release_contract", HELPER)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"could not load {HELPER}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class ReleasePipelineContractTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.contract = load_helper()

    def test_asset_discovery_accepts_only_the_exact_versioned_dual_souls_apk(self):
        with tempfile.TemporaryDirectory() as directory:
            build = pathlib.Path(directory)
            expected = build / "DualSouls-1.2.3.apk"
            expected.write_bytes(b"production")

            self.assertEqual(expected, self.contract.select_release_apk(build, "1.2.3"))

            (build / "emulator-test-app-debug.apk").write_bytes(b"lab")
            with self.assertRaisesRegex(ValueError, "unexpected APK"):
                self.contract.select_release_apk(build, "1.2.3")

    def test_asset_discovery_rejects_non_release_versions_and_missing_assets(self):
        with tempfile.TemporaryDirectory() as directory:
            build = pathlib.Path(directory)
            for version in ("0.1-lab", "latest", "1.2", "1.2.3/other"):
                with self.subTest(version=version):
                    with self.assertRaisesRegex(ValueError, "version"):
                        self.contract.select_release_apk(build, version)
            with self.assertRaisesRegex(ValueError, "expected release APK"):
                self.contract.select_release_apk(build, "1.2.3")

    def test_package_guard_rejects_the_lab_suffix(self):
        self.contract.require_production_package("io.github.darkaxt.dualsouls")
        for package_name in (
            "io.github.darkaxt.dualsouls.emutest",
            "io.github.darkaxt.dualsouls.debug",
            "com.jakobkhansen.silksong",
        ):
            with self.subTest(package_name=package_name):
                with self.assertRaisesRegex(ValueError, "production package"):
                    self.contract.require_production_package(package_name)

    def assert_exact_profile_control_flow(self, workflow):
        private, release = workflow.split('  patch-profiles:\n', 1)[1].split('  release:\n', 1)
        self.assertIn('    needs: patch-profiles\n', release)
        self.assertNotIn('always()', release)
        self.assertNotIn('continue-on-error:', workflow)
        self.assertNotIn('secrets.', private)
        self.assertNotIn('upload-artifact', private)
        steps = re.split(r'(?m)^      - ', private)[1:]
        gate = next((s for s in steps if 'name: Compile and verify exact patch profiles\n' in s), None)
        self.assertIsNotNone(gate, 'Signing is reachable without exact-profile compiles')
        self.assertNotRegex(gate, r'(?m)^        (if|continue-on-error):')
        self.assertIn('tools/ci/check_patch_profiles.ps1', gate)
        self.assertIn('verify-gate', gate)
        token = '$($env:GITHUB_RUN_ID):$($env:GITHUB_RUN_ATTEMPT):$($env:GITHUB_SHA)'
        self.assertIn(token, gate)
        final = next(s for s in steps if 'name: Verify completed compiler gate\n' in s)
        self.assertNotRegex(final, r'(?m)^        (if|continue-on-error):')
        self.assertIn('verify-gate', final)
        self.assertIn(token, final)
        for name in ('Prepare signing key', 'Build the APK'):
            step = next(s for s in re.split(r'(?m)^      - ', release)[1:]
                        if 'name: ' + name + '\n' in s)
            self.assertIn('workflow_gate.py verify-job', step)
            self.assertIn('GH_TOKEN: ${{ github.token }}', step)
            self.assertLess(step.index('verify-job'), step.index('printf') if name == 'Prepare signing key'
                            else step.index('docker run'))
        for section in (private, release):
            checkout = next(s for s in re.split(r'(?m)^      - ', section)[1:] if 'actions/checkout@' in s)
            self.assertIn('ref: ${{ github.sha }}', checkout)
        self.assertNotIn('download-artifact', private)

    def test_actual_presigning_verifier_blocks_sandbox_sentinel_for_both_dry_run_modes(self):
        workflow = WORKFLOW.read_text(encoding='utf-8')
        signing = workflow.split('      - name: Prepare signing key\n', 1)[1]
        script = signing.split('        run: |\n', 1)[1].split('          for name ', 1)[0]
        script = textwrap.dedent(script).replace('python3 -B', shlex.quote(pathlib.Path(sys.executable).as_posix()) + ' -B')
        bash = shutil.which('bash')
        self.assertIsNotNone(bash)
        root = pathlib.Path(tempfile.mkdtemp(prefix='profile-sentinel-', dir=os.environ.get('DUALSOULS_TEMP_ROOT')))
        for dry_run in ('false', 'true'):
            sentinel = root / ('sentinel-' + dry_run)
            env = dict(os.environ, GITHUB_WORKSPACE=ROOT.as_posix(), RUNNER_TEMP=root.as_posix(),
                       GITHUB_RUN_ID='123', GITHUB_RUN_ATTEMPT='1', GITHUB_SHA='a' * 40,
                       GITHUB_REPOSITORY='Darkaxt/HollowKnightAndroid', GH_TOKEN='',
                       INPUT_DRY_RUN=dry_run, SENTINEL=sentinel.as_posix())
            result = subprocess.run([bash, '-c', script + '\nprintf reached > "$SENTINEL"\n'],
                                    env=env, capture_output=True, text=True)
            (root / (dry_run + '.stdout')).write_text(result.stdout)
            (root / (dry_run + '.stderr')).write_text(result.stderr)
            self.assertNotEqual(0, result.returncode)
            self.assertIn('[workflow gate] rejected:', result.stderr)
            self.assertFalse(sentinel.exists())


if __name__ == "__main__":
    unittest.main()
