import pathlib
import re
import unittest


REPO_ROOT = pathlib.Path(__file__).resolve().parents[3]
PLAYER_ACTIVITY = REPO_ROOT / "tools" / "depot-to-apk" / "shell" / "PlayerActivity.java"


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
    def test_unity_stops_before_secondary_surface_is_hidden(self):
        source = PLAYER_ACTIVITY.read_text(encoding="utf-8")
        on_stop = method_body(source, r"@Override\s+protected\s+void\s+onStop\s*\(\s*\)")

        self.assertTrue(on_stop, "PlayerActivity.onStop is missing")
        player_stop = on_stop.index("mUnityPlayer.onStop()")
        secondary_stop = on_stop.index("secondaryDisplay.onStop()")
        self.assertLess(
            player_stop,
            secondary_stop,
            "Unity must stop rendering before the owned secondary SurfaceView is hidden",
        )


if __name__ == "__main__":
    unittest.main()
