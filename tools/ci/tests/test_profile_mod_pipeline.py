import pathlib
import unittest


REPO_ROOT = pathlib.Path(__file__).parents[3]
LAUNCHER = REPO_ROOT / "src" / "SilksongLauncher.Launcher" / "app" / "src" / "main" / "kotlin" / "dev" / "silksong" / "launcher"


class ProfileModPipelineContractTest(unittest.TestCase):
    def test_clean_host_test_gate_builds_the_weaver_first(self):
        source = (REPO_ROOT / "Makefile").read_text(encoding="utf-8")

        self.assertIn("test: weaver ##", source)
        self.assertRegex(source, r"(?s)\.PHONY:.*\bweaver\b")

    def test_setup_builds_and_converts_mod_support_in_the_selected_profile(self):
        source = (LAUNCHER / "SetupActivity.kt").read_text(encoding="utf-8")

        self.assertIn("PackageCompiler.compileShims(", source)
        self.assertIn("Il2cppConverter.isStale(profile, out, mods, assets)", source)
        self.assertIn("mods = mods,", source)
        self.assertIn("assets = assets,", source)

    def test_setup_heading_is_not_hard_coded_to_silksong(self):
        source = (LAUNCHER / "SetupActivity.kt").read_text(encoding="utf-8")

        self.assertNotIn('"Silksong Android"', source)
        self.assertNotIn('"Porting Silksong"', source)
        self.assertIn('"Building ${profile.displayName}"', source)
        self.assertIn("getString(R.string.launcher_app_name)", source)

    def test_launcher_and_mod_screen_use_the_selected_profile_build_root(self):
        launcher = (LAUNCHER / "LauncherActivity.kt").read_text(encoding="utf-8")
        mods_activity = (LAUNCHER / "ModsActivity.kt").read_text(encoding="utf-8")

        self.assertIn("val out = buildPaths.buildRoot", launcher)
        self.assertNotIn("Il2cppConverter.rootFor(this)", launcher)
        self.assertIn("ProfileBuildPaths(", mods_activity)
        self.assertIn("buildPaths.buildRoot", mods_activity)
        self.assertNotIn("Il2cppConverter.rootFor", mods_activity)

    def test_runtime_registration_keeps_the_selected_profile_patch(self):
        source = (LAUNCHER / "PlayerImage.kt").read_text(encoding="utf-8")

        self.assertIn("PackageCompiler.patchAssembly(profile, root)", source)
        self.assertIn("patchAssemblyName", source)
        self.assertIn('register("BepInEx", "BepInEx.Bootstrap", "Chainloader", "Start", 2)', source)


if __name__ == "__main__":
    unittest.main()
