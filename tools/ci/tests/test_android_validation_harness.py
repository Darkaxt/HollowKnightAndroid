import importlib.util
import json
import os
import pathlib
import tempfile
import unittest
from unittest import mock


MODULE_PATH = pathlib.Path(__file__).parents[2] / "android-validation" / "dualsouls_validate.py"


class FakeRunner:
    def __init__(self, capture_paths):
        self.capture_paths = capture_paths
        self.calls = []

    def run(self, command):
        self.calls.append(command)
        if command[0] == "android-capture":
            return json.dumps(
                {
                    "serial": "thor",
                    "outputs": {
                        "up": str(self.capture_paths["up"]),
                        "down": str(self.capture_paths["down"]),
                    },
                }
            )
        if command[-1] == "get-state":
            return "device\n"
        if "dumpsys" in command:
            return (
                "Display: mDisplayId=0\n"
                "  mCurrentFocus=Window{10 u0 com.ayn.launcher/.MainActivity}\n"
                "Display: mDisplayId=4\n"
                "  mCurrentFocus=Window{42 u0 io.github.darkaxt.dualsouls/.GameActivity}\n"
            )
        if "logcat" in command:
            return "09-13 DualScreen: validation\n"
        return ""


class FailingAfterInputRunner(FakeRunner):
    def __init__(self, capture_paths):
        super().__init__(capture_paths)
        self.capture_count = 0

    def run(self, command):
        if command[0] == "android-capture":
            self.capture_count += 1
            if self.capture_count == 2:
                raise RuntimeError("post capture failed")
        return super().run(command)


class WrongForegroundRunner(FakeRunner):
    def run(self, command):
        if "dumpsys" in command:
            self.calls.append(command)
            return (
                "Display: mDisplayId=0\n"
                "  mCurrentFocus=Window{10 u0 io.github.darkaxt.dualsouls/.LauncherActivity}\n"
                "Display: mDisplayId=4\n"
                "  mCurrentFocus=Window{42 u0 com.example.other/.MainActivity}\n"
            )
        return super().run(command)


class OverlayFocusRunner(FakeRunner):
    def run(self, command):
        if "dumpsys" in command:
            self.calls.append(command)
            return (
                "Display: mDisplayId=4\n"
                "  mCurrentFocus=Window{10 u0 com.android.systemui/.DialogActivity}\n"
                "  mFocusedApp=ActivityRecord{42 u0 io.github.darkaxt.dualsouls/.GameActivity}\n"
            )
        return super().run(command)


class BarePackageFocusRunner(FakeRunner):
    def run(self, command):
        if "dumpsys" in command:
            self.calls.append(command)
            return (
                "Display: mDisplayId=4 (organized)\n"
                "  mCurrentFocus=Window{10 u0 io.github.darkaxt.dualsouls}\n"
                "  mFocusedApp=ActivityRecord{42 u0 rip.moth.cocoonshell/.ExternalDisplayActivity}\n"
            )
        return super().run(command)


class AndroidValidationHarnessTest(unittest.TestCase):
    def load_module(self):
        self.assertTrue(MODULE_PATH.is_file(), "the live gate needs a reusable evidence harness")
        spec = importlib.util.spec_from_file_location("dualsouls_validate", MODULE_PATH)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module

    def write_plan(self, root, action=None):
        plan = {
            "version": 1,
            "name": "mods-smoke",
            "steps": [
                {
                    "id": "open-mods",
                    "title": "Open Mods",
                    "expected_before": "Stable ordinary lower HUD",
                    "expected_after": "Mods exclusively owns lower content",
                    "action": action
                    or {"type": "tap", "display": 4, "x": 106, "y": 922},
                }
            ],
        }
        path = root / "plan.json"
        path.write_text(json.dumps(plan), encoding="utf-8")
        return path

    def test_rejects_unbounded_or_shell_actions(self):
        module = self.load_module()
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            plan_path = self.write_plan(root, {"type": "shell", "command": "sendevent ..."})
            with self.assertRaisesRegex(ValueError, "unsupported action type"):
                module.load_plan(plan_path)

    def test_rejects_unsafe_step_ids(self):
        module = self.load_module()
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            plan_path = self.write_plan(root)
            plan = json.loads(plan_path.read_text(encoding="utf-8"))
            plan["steps"][0]["id"] = "../../escape"
            plan_path.write_text(json.dumps(plan), encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "safe slug"):
                module.load_plan(plan_path)

    def test_resolves_extensionless_python_capture_helper_on_windows(self):
        module = self.load_module()
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            helper = root / "android-capture"
            helper.write_text("#!/usr/bin/env python3\n", encoding="utf-8")
            with mock.patch.dict(os.environ, {"PATH": str(root)}):
                command = module.resolve_capture_command(windows=True)

            self.assertEqual(module.sys.executable, command[0])
            self.assertEqual(str(helper.resolve()), command[1])

    def test_prepare_captures_both_screens_and_requires_token_before_input(self):
        module = self.load_module()
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            source_up = root / "source-up.png"
            source_down = root / "source-down.png"
            source_up.write_bytes(b"up")
            source_down.write_bytes(b"down")
            runner = FakeRunner({"up": source_up, "down": source_down})
            plan_path = self.write_plan(root)
            session = module.ValidationSession.create(
                root / "session", plan_path, "thor", "io.github.darkaxt.dualsouls",
                runner=runner, now=lambda: 1000.0,
            )

            prepared = session.prepare("open-mods")

            self.assertEqual("Stable ordinary lower HUD", prepared["expected_before"])
            self.assertTrue(prepared["token"])
            self.assertTrue(pathlib.Path(prepared["captures"]["up"]).is_file())
            self.assertTrue(pathlib.Path(prepared["captures"]["down"]).is_file())
            self.assertIn(
                ["android-capture", "--serial", "thor", "--screens", "both"],
                runner.calls,
            )
            self.assertFalse(any("tap" in call for call in runner.calls))

    def test_execute_rejects_wrong_or_stale_token_without_sending_input(self):
        module = self.load_module()
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            source_up = root / "source-up.png"
            source_down = root / "source-down.png"
            source_up.write_bytes(b"up")
            source_down.write_bytes(b"down")
            runner = FakeRunner({"up": source_up, "down": source_down})
            plan_path = self.write_plan(root)
            clock = [1000.0]
            session = module.ValidationSession.create(
                root / "session", plan_path, "thor", "io.github.darkaxt.dualsouls",
                runner=runner, now=lambda: clock[0],
            )
            prepared = session.prepare("open-mods")

            with self.assertRaisesRegex(ValueError, "token"):
                session.execute("wrong")
            clock[0] = 1201.0
            with self.assertRaisesRegex(ValueError, "stale"):
                session.execute(prepared["token"])

            self.assertFalse(any("tap" in call for call in runner.calls))

    def test_discard_forces_a_fresh_capture_without_sending_input(self):
        module = self.load_module()
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            source_up = root / "source-up.png"
            source_down = root / "source-down.png"
            source_up.write_bytes(b"up")
            source_down.write_bytes(b"down")
            runner = FakeRunner({"up": source_up, "down": source_down})
            plan_path = self.write_plan(root)
            session = module.ValidationSession.create(
                root / "session", plan_path, "thor", "io.github.darkaxt.dualsouls",
                runner=runner, now=lambda: 1000.0,
            )
            session.prepare("open-mods")

            session.discard("UI changed before the action could be reviewed.")

            self.assertFalse(any("tap" in call for call in runner.calls))
            state = json.loads((root / "session" / "session.json").read_text(encoding="utf-8"))
            self.assertIsNone(state["pending"])
            self.assertEqual("discarded", state["steps"]["open-mods"]["status"])

    def test_execute_rejects_plan_changes_after_prepare(self):
        module = self.load_module()
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            source_up = root / "source-up.png"
            source_down = root / "source-down.png"
            source_up.write_bytes(b"up")
            source_down.write_bytes(b"down")
            runner = FakeRunner({"up": source_up, "down": source_down})
            plan_path = self.write_plan(root)
            session_root = root / "session"
            session = module.ValidationSession.create(
                session_root, plan_path, "thor", "io.github.darkaxt.dualsouls",
                runner=runner, now=lambda: 1000.0, sleeper=lambda _: None,
            )
            prepared = session.prepare("open-mods")
            changed = json.loads((session_root / "plan.json").read_text(encoding="utf-8"))
            changed["steps"][0]["action"]["x"] = 999
            (session_root / "plan.json").write_text(json.dumps(changed), encoding="utf-8")

            reloaded = module.ValidationSession(
                session_root, runner=runner, now=lambda: 1000.0, sleeper=lambda _: None
            )
            with self.assertRaisesRegex(ValueError, "plan changed"):
                reloaded.execute(prepared["token"])

            self.assertFalse(any("tap" in call for call in runner.calls))

    def test_execute_accepts_package_only_current_focus_window(self):
        module = self.load_module()
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            source_up = root / "source-up.png"
            source_down = root / "source-down.png"
            source_up.write_bytes(b"up")
            source_down.write_bytes(b"down")
            runner = BarePackageFocusRunner({"up": source_up, "down": source_down})
            plan_path = self.write_plan(root)
            session = module.ValidationSession.create(
                root / "session", plan_path, "thor", "io.github.darkaxt.dualsouls",
                runner=runner, now=lambda: 1000.0, sleeper=lambda _: None,
            )
            prepared = session.prepare("open-mods")

            session.execute(prepared["token"])

            self.assertEqual(1, len([call for call in runner.calls if "tap" in call]))

    def test_execute_rejects_target_when_overlay_owns_current_focus(self):
        module = self.load_module()
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            source_up = root / "source-up.png"
            source_down = root / "source-down.png"
            source_up.write_bytes(b"up")
            source_down.write_bytes(b"down")
            runner = OverlayFocusRunner({"up": source_up, "down": source_down})
            plan_path = self.write_plan(root)
            session = module.ValidationSession.create(
                root / "session", plan_path, "thor", "io.github.darkaxt.dualsouls",
                runner=runner, now=lambda: 1000.0,
            )
            prepared = session.prepare("open-mods")

            with self.assertRaisesRegex(RuntimeError, "foreground"):
                session.execute(prepared["token"])

            self.assertFalse(any("tap" in call for call in runner.calls))

    def test_execute_rejects_a_modified_persisted_action_snapshot(self):
        module = self.load_module()
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            source_up = root / "source-up.png"
            source_down = root / "source-down.png"
            source_up.write_bytes(b"up")
            source_down.write_bytes(b"down")
            runner = FakeRunner({"up": source_up, "down": source_down})
            plan_path = self.write_plan(root)
            session_root = root / "session"
            session = module.ValidationSession.create(
                session_root, plan_path, "thor", "io.github.darkaxt.dualsouls",
                runner=runner, now=lambda: 1000.0, sleeper=lambda _: None,
            )
            prepared = session.prepare("open-mods")
            state_path = session_root / "session.json"
            state = json.loads(state_path.read_text(encoding="utf-8"))
            state["pending"]["action"]["x"] = 999
            state_path.write_text(json.dumps(state), encoding="utf-8")

            reloaded = module.ValidationSession(
                session_root, runner=runner, now=lambda: 1000.0, sleeper=lambda _: None
            )
            with self.assertRaisesRegex(ValueError, "authorization"):
                reloaded.execute(prepared["token"])

            self.assertFalse(any("tap" in call for call in runner.calls))

    def test_post_input_failure_consumes_token_and_cannot_repeat_action(self):
        module = self.load_module()
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            source_up = root / "source-up.png"
            source_down = root / "source-down.png"
            source_up.write_bytes(b"up")
            source_down.write_bytes(b"down")
            runner = FailingAfterInputRunner({"up": source_up, "down": source_down})
            plan_path = self.write_plan(root)
            session = module.ValidationSession.create(
                root / "session", plan_path, "thor", "io.github.darkaxt.dualsouls",
                runner=runner, now=lambda: 1000.0, sleeper=lambda _: None,
            )
            prepared = session.prepare("open-mods")

            with self.assertRaisesRegex(RuntimeError, "post capture failed"):
                session.execute(prepared["token"])
            with self.assertRaisesRegex(ValueError, "no prepared action"):
                session.execute(prepared["token"])

            taps = [call for call in runner.calls if "tap" in call]
            self.assertEqual(1, len(taps))
            state = json.loads((root / "session" / "session.json").read_text(encoding="utf-8"))
            self.assertIsNone(state["pending"])
            self.assertEqual("execution_failed", state["steps"]["open-mods"]["status"])

    def test_execute_fails_closed_when_another_package_is_foreground(self):
        module = self.load_module()
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            source_up = root / "source-up.png"
            source_down = root / "source-down.png"
            source_up.write_bytes(b"up")
            source_down.write_bytes(b"down")
            runner = WrongForegroundRunner({"up": source_up, "down": source_down})
            plan_path = self.write_plan(root)
            session = module.ValidationSession.create(
                root / "session", plan_path, "thor", "io.github.darkaxt.dualsouls",
                runner=runner, now=lambda: 1000.0, sleeper=lambda _: None,
            )
            prepared = session.prepare("open-mods")

            with self.assertRaisesRegex(RuntimeError, "foreground"):
                session.execute(prepared["token"])

            self.assertFalse(any("tap" in call for call in runner.calls))

    def test_execute_uses_serial_and_logical_display_then_captures_after(self):
        module = self.load_module()
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            source_up = root / "source-up.png"
            source_down = root / "source-down.png"
            source_up.write_bytes(b"up")
            source_down.write_bytes(b"down")
            runner = FakeRunner({"up": source_up, "down": source_down})
            plan_path = self.write_plan(root)
            session = module.ValidationSession.create(
                root / "session", plan_path, "thor", "io.github.darkaxt.dualsouls",
                runner=runner, now=lambda: 1000.0, sleeper=lambda _: None,
            )
            prepared = session.prepare("open-mods")

            result = session.execute(prepared["token"])

            self.assertIn(
                ["adb", "-s", "thor", "shell", "input", "-d", "4", "tap", "106", "922"],
                runner.calls,
            )
            captures = [call for call in runner.calls if call[0] == "android-capture"]
            self.assertEqual(2, len(captures))
            self.assertEqual("Mods exclusively owns lower content", result["expected_after"])
            self.assertTrue(pathlib.Path(result["captures"]["down"]).is_file())
            state = json.loads((root / "session" / "session.json").read_text(encoding="utf-8"))
            self.assertIsNone(state["pending"])
            self.assertEqual("awaiting_review", state["steps"]["open-mods"]["status"])

    def test_review_and_report_never_infer_visual_success(self):
        module = self.load_module()
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            source_up = root / "source-up.png"
            source_down = root / "source-down.png"
            source_up.write_bytes(b"up")
            source_down.write_bytes(b"down")
            runner = FakeRunner({"up": source_up, "down": source_down})
            plan_path = self.write_plan(root)
            session = module.ValidationSession.create(
                root / "session", plan_path, "thor", "io.github.darkaxt.dualsouls",
                runner=runner, now=lambda: 1000.0, sleeper=lambda _: None,
            )
            prepared = session.prepare("open-mods")
            session.execute(prepared["token"])

            report_before = session.render_report()
            self.assertIn("AWAITING_REVIEW", report_before)
            self.assertNotIn("PASS — Open Mods", report_before)

            session.review("open-mods", "pass", "No ordinary HUD remained visible.")
            report_after = session.render_report()
            self.assertIn("PASS — Open Mods", report_after)
            self.assertIn("No ordinary HUD remained visible.", report_after)


if __name__ == "__main__":
    unittest.main()
