import json
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[3]
RUNTIME = (
    REPO_ROOT
    / "tools"
    / "shared-patches"
    / "src"
    / "Input"
    / "ControllerRecovery.cs"
)


def entry_points(relative_path):
    data = json.loads((REPO_ROOT / relative_path).read_text(encoding="utf-8"))
    return {
        (
            entry.get("nameSpace", ""),
            entry["className"],
            entry["methodName"],
            entry["loadTypes"],
        )
        for entry in data["entryPoints"]
    }


class ControllerRecoverySourceTests(unittest.TestCase):
    def test_both_games_bootstrap_shared_controller_recovery(self):
        expected = ("DualSouls.Controllers", "ControllerRecovery", "Bootstrap", 0)

        for relative_path in (
            "tools/hollow-knight-patches/entrypoints.json",
            "tools/silksong-patches/entrypoints.json",
        ):
            with self.subTest(entrypoints=relative_path):
                self.assertIn(expected, entry_points(relative_path))

    def test_runtime_reconciles_public_input_system_and_incontrol_devices(self):
        source = RUNTIME.read_text(encoding="utf-8")

        for required in (
            "InputSystem.devices",
            "InputManager.Devices",
            "NewUnityInputDevice",
            "ControllerBindingPlanner.Create",
            "InputManager.AttachDevice",
            "InputManager.DetachDevice",
            "UnityInputSystem.onDeviceChange += OnDeviceChange",
            "UnityInputSystem.onDeviceChange -= OnDeviceChange",
            "InputDeviceChange.Removed",
        ):
            self.assertIn(required, source)

        for forbidden in (
            "System.Reflection",
            "GetField(",
            "GetMethod(",
            "Marshal.",
            "PlayerData",
        ):
            self.assertNotIn(forbidden, source)


if __name__ == "__main__":
    unittest.main()
