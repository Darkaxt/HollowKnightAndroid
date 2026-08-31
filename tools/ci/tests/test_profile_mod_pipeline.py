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
        self.assertIn("text(profile.displayName", source)

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

    def test_built_in_mod_core_is_staged_for_both_game_profiles(self):
        source = (REPO_ROOT / "src" / "SilksongLauncher.Launcher" / "app" / "build.gradle.kts").read_text(encoding="utf-8")

        self.assertGreaterEqual(source.count('from(rootProject.file("../../tools/shared-patches/src"))'), 2)
        self.assertIn('into("src/shared")', source)

    def test_silksong_second_screen_registers_a_persistent_mods_modal(self):
        shell = (REPO_ROOT / "tools" / "silksong-patches" / "src" / "dualscreen" / "DsShell.cs").read_text(encoding="utf-8")
        modal = (REPO_ROOT / "tools" / "silksong-patches" / "src" / "dualscreen" / "DsModsScreen.cs")

        self.assertTrue(modal.is_file())
        modal_source = modal.read_text(encoding="utf-8")
        self.assertIn("new DsModsScreen", shell)
        self.assertIn("ModsOpen", shell)
        self.assertIn("CompanionShellLayout.Create", shell)
        self.assertIn("CompanionHitTarget.Mods", shell)
        self.assertNotIn("ModsButtonW", shell)
        self.assertNotIn('"MODS", DsTheme.BodySize', shell)
        self.assertIn("void GuardMods", shell)
        self.assertIn("GuardMods(() => _mods.Tick())", shell)
        self.assertRegex(shell, r"GuardMods\(\(\) => _mods\.OnGesture\([a-zA-Z]+\)\)")
        self.assertIn("new TweakController", modal_source)
        self.assertIn("new SilksongTweakAdapter", modal_source)
        self.assertIn("new UnityTweakStore", modal_source)
        self.assertIn("SetMaster", modal_source)
        self.assertIn("Reset", modal_source)

    def test_built_in_tweaks_do_not_use_native_memory_offsets(self):
        roots = [
            REPO_ROOT / "tools" / "shared-patches" / "src",
            REPO_ROOT / "tools" / "silksong-patches" / "src" / "mods",
        ]
        source = "\n".join(
            path.read_text(encoding="utf-8")
            for root in roots
            for path in root.rglob("*.cs")
        ).lower()

        for forbidden in ("intptr", "marshal.", "unsafe", "processmemory", "readprocessmemory", "writeprocessmemory"):
            self.assertNotIn(forbidden, source)

    def test_master_defaults_off_and_persistence_is_game_qualified(self):
        source = (REPO_ROOT / "tools" / "shared-patches" / "src" / "Mods" / "TweakController.cs").read_text(encoding="utf-8")

        self.assertIn('"dualsouls.mods." + adapter.GameId + "."', source)
        self.assertIn("MasterEnabled = false", source)
        self.assertNotIn("MasterEnabled = true;\n        public", source)


if __name__ == "__main__":
    unittest.main()
