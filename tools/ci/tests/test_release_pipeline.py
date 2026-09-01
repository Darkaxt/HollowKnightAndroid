import importlib.util
import pathlib
import re
import tempfile
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[3]
HELPER = ROOT / "tools" / "ci" / "release_contract.py"
WORKFLOW = ROOT / ".github" / "workflows" / "release.yml"
BUILD_SCRIPT = ROOT / "tools" / "depot-to-apk" / "build.sh"
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

    def test_apk_shell_ignores_empty_resource_directories(self):
        script = BUILD_SCRIPT.read_text(encoding="utf-8")

        self.assertIn('for f in "$d"/*; do', script)
        self.assertIn('[[ -f "$f" ]] || continue', script)
        self.assertNotIn('cp -f "$d"/* "$sh/res/$(basename "$d")/"', script)

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
