import unittest

from test_profile_mod_pipeline import (
    REPO_ROOT, SKIN_FORBIDDEN_SEAMS, SKIN_SOURCE_ROOTS,
    skin_source_inventory, skin_source_violations,
)


class SkinSourceSafetyTest(unittest.TestCase):
    def test_each_prohibited_seam_reports_exact_file_and_line(self):
        cases = {
            "native-interop": "IntPtr address; unsafe { }",
            "process-memory": "ReadProcessMemory(handle);",
            "native-offset": "var il2cpp_offset = 12;",
            "player-data-injection": "class PlayerData { int __dsSkin; }",
            "player-data-write": "playerData.health = 0;",
            "save-content-api": "GameManager.instance.SaveGame();",
            "save-content-path": 'File.ReadAllBytes("user1.dat");',
            "gameplay-death-command": "HeroController.UnsafeInstance.Die();",
        }
        self.assertEqual(set(SKIN_FORBIDDEN_SEAMS), set(cases))
        for seam, source in cases.items():
            with self.subTest(seam=seam):
                self.assertIn(f"skins/injected.cs:2: {seam}", skin_source_violations("skins/injected.cs", "\n" + source))

    def test_existing_typed_reads_bridge_and_library_authority_are_allowed(self):
        source = '''
            var manager = GameManager.UnsafeInstance;
            var slot = manager.playerData.profileID;
            if (manager.playerData.permadeathMode == 0) Observe();
            var occurrence = manager.__dsNormalDeathOccurrence;
            var library = File.ReadAllText("library.json");
            // IntPtr ReadProcessMemory SaveGame user1.dat
            /* unsafe { hero.Die(); } */
        '''
        self.assertEqual([], skin_source_violations("allowed.cs", source))

    def test_guard_inventory_covers_paired_runtime_death_loader_and_transport(self):
        paths = {path.relative_to(REPO_ROOT).as_posix() for path in skin_source_inventory()}
        for profile, name in (("hollow-knight", "HollowKnight"), ("silksong", "Silksong")):
            for suffix in ("SkinRuntime.cs", "SkinDeathAdapter.cs", "SkinLibrary.cs"):
                self.assertIn(f"tools/{profile}-patches/src/skins/runtime/{name}{suffix}", paths)
        self.assertIn("tools/shared-patches/src/skins/runtime/SkinRuntimeSession.cs", paths)
        self.assertTrue(any("Transport" in path for path in paths), paths)
        for root in SKIN_SOURCE_ROOTS:
            self.assertTrue(any(path.startswith(root + "/") for path in paths))


if __name__ == "__main__":
    unittest.main()
