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

    def test_signing_workflow_uses_the_guard_and_never_invokes_the_lab_module(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")

        self.assertIn("tools/ci/release_contract.py select-apk", workflow)
        self.assertIn("tools/ci/release_contract.py check-package", workflow)
        self.assertNotIn(":emulator-test-app", workflow)

    def assert_exact_profile_control_flow(self, workflow):
        steps = re.split(r"(?m)^      - ", workflow)[1:]
        gate = next((i for i, s in enumerate(steps)
                     if 'name: Compile and verify exact patch profiles\n' in s), None)
        self.assertIsNotNone(gate, 'Signing is reachable without exact-profile compiles')
        step = steps[gate]
        self.assertNotRegex(step, r'(?m)^        (if|continue-on-error):')
        self.assertIn('tools/ci/check_patch_profiles.ps1', step)
        self.assertIn('verify-gate', step)
        self.assertIn('$GITHUB_RUN_ID:$GITHUB_RUN_ATTEMPT:$GITHUB_SHA', step)
        for name in ('Prepare signing key', 'Build the image', 'Build the APK',
                     'Upload the build artefact'):
            index = next(i for i, s in enumerate(steps) if 'name: ' + name + '\n' in s)
            self.assertLess(gate, index)
        for name in ('Prepare signing key', 'Build the APK'):
            step = next(s for s in steps if 'name: ' + name + '\n' in s)
            self.assertIn('verify-gate', step)
            self.assertLess(step.index('verify-gate'), step.index('printf') if name == 'Prepare signing key'
                            else step.index('docker run'))
        checkout = next(s for s in steps if 'actions/checkout@' in s)
        self.assertIn('ref: ${{ github.sha }}', checkout)
        self.assertNotIn('download-artifact', steps[gate])

    def test_exact_profiles_are_unconditional_before_signing_and_artifacts(self):
        self.assert_exact_profile_control_flow(WORKFLOW.read_text(encoding="utf-8"))

    def test_profile_gate_control_flow_rejects_bypass_and_stale_run_mutations(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        marker = '      - name: Compile and verify exact patch profiles\n'
        variants = [workflow.replace(marker, marker + '        if: ${{ !inputs.dry_run }}\n'),
                    workflow.replace(marker, marker + '        continue-on-error: true\n'),
                    workflow.replace('tools/ci/check_patch_profiles.ps1', 'download-artifact'),
                    workflow.replace('ref: ${{ github.sha }}', 'ref: stale-checkpoint'),
                    workflow.replace('$GITHUB_RUN_ID:$GITHUB_RUN_ATTEMPT:$GITHUB_SHA', 'old-run-token')]
        for changed in variants:
            with self.subTest(changed=hash(changed)), self.assertRaises(AssertionError):
                self.assert_exact_profile_control_flow(changed)

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
                       INPUT_DRY_RUN=dry_run, SENTINEL=sentinel.as_posix())
            result = subprocess.run([bash, '-c', script + '\nprintf reached > "$SENTINEL"\n'],
                                    env=env, capture_output=True, text=True)
            (root / (dry_run + '.stdout')).write_text(result.stdout)
            (root / (dry_run + '.stderr')).write_text(result.stderr)
            self.assertNotEqual(0, result.returncode)
            self.assertIn('[receipt] rejected:', result.stderr)
            self.assertFalse(sentinel.exists())

    def test_workflow_forwards_admitted_roots_to_full_input_contract_suite(self):
        import ast
        workflow = WORKFLOW.read_text(encoding='utf-8')
        step = workflow.split('      - name: Test release verification helper\n', 1)[1].split('      # ', 1)[0]
        self.assertIn("python3 -B - <<'PY'", step)
        script = textwrap.dedent(step.split("python3 -B - <<'PY'\n", 1)[1].split('          PY', 1)[0])
        tree = ast.parse(script)
        self.assertIn('admit_contract(Path.cwd(), os.environ["DUALSOULS_INPUT_CONTRACT"])', script)
        for profile, player, depot in [('hollow-knight', 'UNITY_PLAYER_61', 'HK_ORIGINAL_DEPOT'),
                                      ('silksong', 'UNITY_PLAYER_50', 'SS_ORIGINAL_DEPOT')]:
            self.assertIn(f'("{profile}", "{player}", "{depot}")', script)
        self.assertIn('env[player] = contract["profiles"][profile]["player"]', script)
        self.assertIn('env[depot] = contract["profiles"][profile]["depot"]', script)
        self.assertIn('DUALSOULS_RETAIN_TEST_FIXTURES="1"', script)
        self.assertIn('DUALSOULS_TEMP_ROOT=temp, TMPDIR=temp, TEMP=temp, TMP=temp', script)
        calls = [n for n in ast.walk(tree) if isinstance(n, ast.Call) and
                 isinstance(n.func, ast.Attribute) and n.func.attr == 'call']
        self.assertEqual(1, len(calls))
        self.assertIsInstance(calls[0].args[0], ast.List)
        self.assertEqual(['-B', '-m', 'unittest', 'discover', 'tools/ci/tests', '-v'],
                         [n.value for n in calls[0].args[0].elts[1:]])
        self.assertIn('raise SystemExit(subprocess.call(', script)
        self.assertIn('env=env', script)

    def test_apk_shell_ignores_empty_resource_directories(self):
        script = BUILD_SCRIPT.read_text(encoding="utf-8")

        self.assertIn('for f in "$d"/*; do', script)
        self.assertIn('[[ -f "$f" ]] || continue', script)
        self.assertNotIn('cp -f "$d"/* "$sh/res/$(basename "$d")/"', script)

    def test_modern_round_icon_uses_the_same_adaptive_artwork(self):
        regular = (ADAPTIVE_ICON_DIR / "ic_launcher.xml").read_text(encoding="utf-8")
        round_icon = (ADAPTIVE_ICON_DIR / "ic_launcher_round.xml").read_text(
            encoding="utf-8"
        )

        for layer in ("background", "foreground"):
            reference = re.search(
                rf'<{layer} android:drawable="([^"]+)"', regular
            )
            candidate = re.search(
                rf'<{layer} android:drawable="([^"]+)"', round_icon
            )
            self.assertIsNotNone(reference)
            self.assertIsNotNone(candidate)
            self.assertEqual(reference.group(1), candidate.group(1))

    def test_apk_shell_exposes_launcher_as_its_only_launcher_entry_point(self):
        script = BUILD_SCRIPT.read_text(encoding="utf-8")

        launcher = re.search(
            r'<activity android:name="dev\.silksong\.launcher\.LauncherActivity"'
            r'(?P<body>.*?)</activity>',
            script,
            re.DOTALL,
        )
        self.assertIsNotNone(launcher)
        self.assertIn('android:exported="true"', launcher.group("body"))
        self.assertIn('android.intent.action.MAIN', launcher.group("body"))
        self.assertIn('android.intent.category.LAUNCHER', launcher.group("body"))

        setup = re.search(
            r'<activity android:name="dev\.silksong\.launcher\.SetupActivity"'
            r'(?P<body>.*?)/>',
            script,
            re.DOTALL,
        )
        self.assertIsNotNone(setup)
        self.assertIn('android:exported="false"', setup.group("body"))
        self.assertNotIn('android.intent.action.MAIN', setup.group("body"))

    def test_apk_shell_registers_every_launcher_library_activity(self):
        script = BUILD_SCRIPT.read_text(encoding="utf-8")
        manifest = LAUNCHER_MANIFEST.read_text(encoding="utf-8")
        library_activities = set(
            re.findall(r'<activity\s+android:name="([^"]+)"', manifest)
        )

        self.assertTrue(library_activities)
        for activity in sorted(library_activities):
            with self.subTest(activity=activity):
                self.assertIn(f'<activity android:name="{activity}"', script)

    def test_on_device_object_names_use_android_shell_portable_sanitising(self):
        script = IL2CPP_BUILD_SCRIPT.read_text(encoding="utf-8")
        portable = "safe=${rel//[!A-Za-z0-9._-]/_}"

        self.assertNotIn("safe=${rel//[^", script)
        self.assertEqual(3, script.count(portable))

    def test_interrupted_il2cpp_conversion_cannot_publish_partial_output(self):
        source = IL2CPP_CONVERTER.read_text(encoding="utf-8")

        self.assertIn('File(root, "convert.complete")', source)
        self.assertIn("completionMarker(root).readText().trim()", source)
        self.assertIn("file.exists() && !file.delete()", source)
        self.assertIn("if (!part.renameTo(marker))", source)
        self.assertLess(
            source.index("invalidateCompletion(root)"),
            source.index('send(Progress("Preparing the converter"'),
        )
        self.assertGreater(
            source.index("markComplete(root)"),
            source.index("if (!result.ok)"),
        )


if __name__ == "__main__":
    unittest.main()
