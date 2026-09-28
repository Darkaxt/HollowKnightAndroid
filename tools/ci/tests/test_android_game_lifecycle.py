import pathlib
import re
import unittest


REPO_ROOT = pathlib.Path(__file__).resolve().parents[3]
SHELL_ROOT = REPO_ROOT / "tools" / "depot-to-apk" / "shell"
PLAYER_ACTIVITY = SHELL_ROOT / "PlayerActivity.java"
SECONDARY_DISPLAY = SHELL_ROOT / "SecondaryDisplay.java"


def method_body(source: str, signature: str) -> str:
    match = re.search(signature + r"\s*\{", source)
    if match is None:
        return ""
    start = source.find("{", match.start())
    depth = 0
    for index in range(start, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[start + 1:index]
    return ""


class AndroidGameLifecycleTest(unittest.TestCase):
    def test_unity_stops_before_secondary_lifecycle(self):
        source = PLAYER_ACTIVITY.read_text(encoding="utf-8")
        on_stop = method_body(source, r"@Override\s+protected\s+void\s+onStop\s*\(\s*\)")

        self.assertTrue(on_stop, "PlayerActivity.onStop is missing")
        player_stop = on_stop.index("mUnityPlayer.onStop()")
        secondary_stop = on_stop.index("secondaryDisplay.onStop()")
        self.assertLess(
            player_stop,
            secondary_stop,
            "Unity must stop before secondary lifecycle bookkeeping",
        )

    def test_secondary_deactivation_fades_without_detaching_surface(self):
        source = SECONDARY_DISPLAY.read_text(encoding="utf-8")
        set_enabled = method_body(
            source, r"\svoid\s+setEnabledOnUiThread\s*\(\s*boolean\s+value\s*\)"
        )
        set_visible = method_body(
            source, r"\svoid\s+setSurfaceVisible\s*\(\s*boolean\s+visible\s*\)"
        )
        update_surface = method_body(source, r"\svoid\s+updateSurface\s*\(\s*\)")

        self.assertTrue(set_enabled, "SecondaryDisplay.setEnabledOnUiThread is missing")
        self.assertTrue(set_visible, "SecondaryDisplay.setSurfaceVisible is missing")
        self.assertTrue(update_surface, "SecondaryDisplay.updateSurface is missing")
        self.assertNotIn("View.GONE", source)
        self.assertNotIn("View.INVISIBLE", source)
        self.assertIn("setSurfaceVisible(false)", set_enabled)
        self.assertIn("root.setAlpha", set_visible)
        self.assertIn("surface.setAlpha", set_visible)
        self.assertNotIn("setVisibility", set_visible)
        self.assertNotIn("View.GONE", set_visible)
        self.assertIn("setSurfaceVisible(visible)", update_surface)
        self.assertNotIn("View.GONE", update_surface)

    def test_secondary_stop_preserves_unity_owned_surface(self):
        source = SECONDARY_DISPLAY.read_text(encoding="utf-8")
        on_stop = method_body(source, r"\svoid\s+onStop\s*\(\s*\)")

        self.assertTrue(on_stop, "SecondaryDisplay.onStop is missing")
        self.assertIn("started = false", on_stop)
        self.assertIn("handler.removeCallbacks(monitor)", on_stop)
        self.assertIn("touches.setSurface(0, 0, false)", on_stop)
        self.assertNotIn("root.setVisibility", on_stop)
        self.assertNotIn("updateSurface()", on_stop)


if __name__ == "__main__":
    unittest.main()
