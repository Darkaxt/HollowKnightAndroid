import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]


def csharp_rows(path: Path):
    text = path.read_text(encoding="utf-8")
    actionable = text.split("TweakDescriptor.Deferred(", 1)[0]
    return re.findall(
        r'new TweakDescriptor\(\s*"([^"]+)",\s*"([^"]+)",\s*"([^"]+)",'
        r'\s*"([^"]+)",\s*"([^"]+)",\s*new\[\]\s*\{([^}]+)\}\)',
        actionable,
    )


def kotlin_rows(game: str):
    text = (ROOT / "src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/"
            "builtinmods/BuiltInModCatalog.kt").read_text(encoding="utf-8")
    block = text.split(f'val {game} = listOf(', 1)[1].split("\n        )", 1)[0]
    return re.findall(
        r'row\("([^"]+)",\s*"([^"]+)",\s*"([^"]+)",\s*"([^"]+)",'
        r'\s*"([^"]+)",\s*([^\n]+)\)',
        block,
    )


class BuiltInModsCatalogContractTest(unittest.TestCase):
    def test_android_catalog_matches_production_adapters(self):
        pairs = [
            ("hollowKnight", ROOT / "tools/hollow-knight-patches/src/mods/HollowKnightTweakAdapter.cs"),
            ("silksong", ROOT / "tools/silksong-patches/src/mods/SilksongTweakAdapter.cs"),
        ]
        for kotlin_name, csharp_path in pairs:
            expected = [row[:5] + (tuple(re.findall(r'"([^"]+)"', row[5])),) for row in csharp_rows(csharp_path)]
            actual = [row[:5] + (tuple(re.findall(r'"([^"]+)"', row[5])),) for row in kotlin_rows(kotlin_name)]
            self.assertEqual(expected, actual, kotlin_name)

    def test_launcher_catalog_never_exposes_deferred_rows(self):
        kotlin = (ROOT / "src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/"
                  "builtinmods/BuiltInModCatalog.kt").read_text(encoding="utf-8")
        visible = kotlin.split("val deferred", 1)[0]
        self.assertNotIn("HKMOD-", visible)
    def test_builtin_mod_mutations_use_explicit_game_lifecycle_lease_without_process_scanning(self):
        activity = (ROOT / "src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/"
                    "BuiltInModsActivity.kt").read_text(encoding="utf-8")
        launcher = (ROOT / "src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/"
                    "LauncherActivity.kt").read_text(encoding="utf-8")
        startup = (ROOT / "src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/runtime/"
                   "GameProcessStartup.kt").read_text(encoding="utf-8")
        authority = (ROOT / "src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/runtime/"
                     "GameLifecycleAuthority.kt").read_text(encoding="utf-8")

        self.assertIn("GameLifecycleAuthority", activity)
        self.assertNotIn("GameProcessInspector", activity)
        self.assertNotIn("runningAppProcesses", authority)
        self.assertNotIn("/proc", authority)
        self.assertNotIn("game-lifecycle.state", authority)
        self.assertNotIn("writeState", authority)
        self.assertNotIn("writeText", authority)
        self.assertIn("markLaunchPending", launcher)
        on_resume = launcher.split("override fun onResume()", 1)[1].split("// ── Login", 1)[0]
        self.assertIn("GameLifecycleAuthority.clearProcessLaunchPending()", on_resume)
        self.assertLess(
            on_resume.index("clearProcessLaunchPending"),
            on_resume.index("if (returningFromGame)"),
        )
        self.assertIn('check(pendingLaunch == null)', authority)
        self.assertIn("acquireForGame", startup)

    def test_production_runtimes_use_shared_profile_file_and_one_silksong_controller(self):
        hk = (ROOT / "tools/hollow-knight-patches/src/mods/HollowKnightModsRuntime.cs").read_text(encoding="utf-8")
        ss = (ROOT / "tools/silksong-patches/src/mods/SilksongModsRuntime.cs").read_text(encoding="utf-8")
        screen = (ROOT / "tools/silksong-patches/src/dualscreen/DsModsScreen.cs").read_text(encoding="utf-8")
        for source in (hk, ss):
            self.assertIn("LineFileTweakStore.ProfilePath(Application.persistentDataPath", source)
            self.assertNotIn("new PlayerPrefsTweakStore()", source)
        self.assertIn("SilksongModsRuntime.Current.Session.Controller", screen)
        self.assertNotIn("new TweakController(", screen)
        self.assertIn('"RESET ALL MODS"', screen)


if __name__ == "__main__":
    unittest.main()
