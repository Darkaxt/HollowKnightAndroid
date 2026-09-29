import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
SHARED = ROOT / "tools/shared-patches/src/Mods"
HK_MENU = ROOT / "tools/hollow-knight-patches/src/mods/HollowKnightNativeModsMenu.cs"
HK_RUNTIME = ROOT / "tools/hollow-knight-patches/src/mods/HollowKnightModsRuntime.cs"
SS_MENU = ROOT / "tools/silksong-patches/src/mods/SilksongNativeModsMenu.cs"
DORMANT_LOWER = ROOT / "tools/silksong-patches/src/dualscreen/DsModsScreen.cs"
ANDROID_MAIN = ROOT / "src/SilksongLauncher.Launcher/app/src/main"
KOTLIN = ANDROID_MAIN / "kotlin/dev/silksong/launcher"
DEPOT_BUILD = ROOT / "tools/depot-to-apk/build.sh"
PRODUCT_CONTRACT = ROOT / "docs/DUALSCREEN-V3.md"


class PhaseOneModsContractTest(unittest.TestCase):
    def test_runtime_and_native_presenters_have_no_master_product_state(self):
        sources = [
            SHARED / "TweakController.cs",
            SHARED / "TweakMenuModel.cs",
            SHARED / "TweakSession.cs",
            HK_MENU,
            HK_RUNTIME,
            SS_MENU,
        ]
        for path in sources:
            source = path.read_text(encoding="utf-8")
            for forbidden in (
                "MasterEnabled",
                "SetMaster",
                "ToggleMaster",
                "StageDisabledMaster",
                "PersistMaster",
                "MASTER MODS",
                "ButtonRole.Master",
            ):
                self.assertNotIn(forbidden, source, f"{forbidden} remains in {path}")
        self.assertFalse(DORMANT_LOWER.exists(), "dormant lower-display Mods presenter must be removed")

    def test_launcher_routes_mod_package_management_without_builtin_config_writer(self):
        launcher = (KOTLIN / "LauncherActivity.kt").read_text(encoding="utf-8")
        settings = (KOTLIN / "SettingsActivity.kt").read_text(encoding="utf-8")
        manifest = (ANDROID_MAIN / "AndroidManifest.xml").read_text(encoding="utf-8")
        ids = (ANDROID_MAIN / "res/values/ids.xml").read_text(encoding="utf-8")
        depot_build = DEPOT_BUILD.read_text(encoding="utf-8")

        self.assertIn("Intent(this, ModsActivity::class.java)", launcher)
        self.assertIn("Intent(this, ModsActivity::class.java)", settings)
        self.assertIn("Intent(this, SkinsActivity::class.java)", launcher)
        self.assertIn("Intent(this, SkinsActivity::class.java)", settings)
        self.assertIn("dev.silksong.launcher.ModsActivity", manifest)
        self.assertIn("dev.silksong.launcher.ModsActivity", depot_build)
        self.assertNotIn("BuiltInModsActivity", launcher + settings + manifest + depot_build)
        self.assertNotIn("btn_mods_master", ids)
        self.assertNotIn("btn_reset_mods", ids)
        self.assertNotIn("builtin_mods_list", ids)

        removed = [
            KOTLIN / "BuiltInModsActivity.kt",
            KOTLIN / "builtinmods/BuiltInModsController.kt",
            KOTLIN / "builtinmods/BuiltInModCatalog.kt",
            KOTLIN / "builtinmods/LineModStateStore.kt",
        ]
        for path in removed:
            self.assertFalse(path.exists(), f"obsolete launcher config surface remains: {path}")

    def test_product_contract_describes_no_master_and_package_only_launcher_ownership(self):
        source = PRODUCT_CONTRACT.read_text(encoding="utf-8")

        self.assertIn("test_native_mods_catalog.py", source)
        self.assertIn("launcher mod-package screen", source)
        self.assertNotIn("test_builtin_mods_catalog.py", source)
        self.assertNotIn("master", source.lower())
        self.assertNotIn("master/reset/Back", source)


if __name__ == "__main__":
    unittest.main()
