#!/usr/bin/env python3
"""Two-phase, serial-scoped Android evidence collection for Dual Souls live gates."""

import argparse
import hashlib
import hmac
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys
import time


SUPPORTED_ACTIONS = {"tap", "swipe", "keyevent", "observe"}
ALLOWED_KEYEVENTS = {
    "KEYCODE_BACK",
    "KEYCODE_BUTTON_A",
    "KEYCODE_BUTTON_B",
    "KEYCODE_DPAD_DOWN",
    "KEYCODE_DPAD_LEFT",
    "KEYCODE_DPAD_RIGHT",
    "KEYCODE_DPAD_UP",
}
MAX_CAPTURE_AGE_SECONDS = 180.0
SAFE_STEP_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]*$")


def resolve_capture_command(windows=None):
    """Resolve the extensionless helper without relying on Windows PATHEXT."""
    windows = os.name == "nt" if windows is None else windows
    candidates = []
    for directory in os.environ.get("PATH", "").split(os.pathsep):
        if not directory:
            continue
        base = pathlib.Path(directory)
        candidates.extend((base / "android-capture", base / "android-capture.py"))
        if windows:
            candidates.extend((base / "android-capture.cmd", base / "android-capture.exe"))
    for candidate in candidates:
        if not candidate.is_file():
            continue
        resolved = str(candidate.resolve())
        if candidate.suffix.lower() == ".py" or candidate.suffix == "":
            try:
                with candidate.open("r", encoding="utf-8") as source:
                    first_line = source.readline().lower()
            except (OSError, UnicodeError):
                first_line = ""
            if "python" in first_line:
                return [sys.executable, resolved]
        return [resolved]
    raise FileNotFoundError("android-capture was not found on PATH")


def _require_text(value, field):
    if not isinstance(value, str) or not value.strip():
        raise ValueError(field + " must be non-empty text")
    return value.strip()


def _bounded_int(value, field, minimum=0, maximum=10000):
    if isinstance(value, bool) or not isinstance(value, int) or not minimum <= value <= maximum:
        raise ValueError(f"{field} must be an integer from {minimum} through {maximum}")
    return value


def validate_action(action):
    if not isinstance(action, dict):
        raise ValueError("action must be an object")
    action_type = action.get("type")
    if action_type not in SUPPORTED_ACTIONS:
        raise ValueError("unsupported action type: " + str(action_type))
    if action_type == "observe":
        if set(action) != {"type"}:
            raise ValueError("observe actions do not accept extra fields")
        return

    allowed = {"type", "display"}
    _bounded_int(action.get("display"), "action.display", 0, 32)
    if action_type == "tap":
        allowed.update({"x", "y"})
        _bounded_int(action.get("x"), "action.x")
        _bounded_int(action.get("y"), "action.y")
    elif action_type == "swipe":
        allowed.update({"x1", "y1", "x2", "y2", "duration_ms"})
        for name in ("x1", "y1", "x2", "y2"):
            _bounded_int(action.get(name), "action." + name)
        _bounded_int(action.get("duration_ms"), "action.duration_ms", 50, 5000)
    elif action_type == "keyevent":
        allowed.add("keycode")
        if action.get("keycode") not in ALLOWED_KEYEVENTS:
            raise ValueError("unsupported keyevent: " + str(action.get("keycode")))
    extras = set(action) - allowed
    if extras:
        raise ValueError("unsupported action fields: " + ", ".join(sorted(extras)))


def load_plan(path):
    path = pathlib.Path(path)
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("version") != 1:
        raise ValueError("plan.version must be 1")
    _require_text(data.get("name"), "plan.name")
    steps = data.get("steps")
    if not isinstance(steps, list) or not steps:
        raise ValueError("plan.steps must be a non-empty array")
    seen = set()
    for step in steps:
        if not isinstance(step, dict):
            raise ValueError("each step must be an object")
        step_id = _require_text(step.get("id"), "step.id")
        if not SAFE_STEP_ID.fullmatch(step_id):
            raise ValueError("step.id must be a safe slug")
        if step_id in seen:
            raise ValueError("duplicate step id: " + step_id)
        seen.add(step_id)
        _require_text(step.get("title"), "step.title")
        _require_text(step.get("expected_before"), "step.expected_before")
        _require_text(step.get("expected_after"), "step.expected_after")
        validate_action(step.get("action"))
    return data


def _sha256_file(path):
    digest = hashlib.sha256()
    with pathlib.Path(path).open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _authorization_token(serial, pending):
    material = json.dumps(
        {
            "serial": serial,
            "step": pending["step_id"],
            "prepared_at": pending["prepared_at"],
            "plan_sha256": pending["plan_sha256"],
            "action": pending["action"],
            "up": _sha256_file(pending["captures"]["up"]),
            "down": _sha256_file(pending["captures"]["down"]),
        },
        sort_keys=True,
    ).encode("utf-8")
    return hashlib.sha256(material).hexdigest()


class SubprocessRunner:
    def capture_command(self):
        return resolve_capture_command()

    def run(self, command):
        completed = subprocess.run(
            command,
            check=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )
        return completed.stdout


class ValidationSession:
    def __init__(self, root, runner=None, now=None, sleeper=None):
        self.root = pathlib.Path(root)
        self.state_path = self.root / "session.json"
        self.runner = runner or SubprocessRunner()
        self.now = now or time.time
        self.sleeper = sleeper or time.sleep
        self.state = json.loads(self.state_path.read_text(encoding="utf-8"))
        self.plan = load_plan(self.root / "plan.json")

    @classmethod
    def create(cls, root, plan_path, serial, package, runner=None, now=None, sleeper=None):
        root = pathlib.Path(root)
        if root.exists() and any(root.iterdir()):
            raise ValueError("session directory must be absent or empty")
        root.mkdir(parents=True, exist_ok=True)
        plan = load_plan(plan_path)
        shutil.copyfile(plan_path, root / "plan.json")
        state = {
            "version": 1,
            "plan": plan["name"],
            "serial": _require_text(serial, "serial"),
            "package": _require_text(package, "package"),
            "started_at": (now or time.time)(),
            "pending": None,
            "steps": {},
        }
        (root / "evidence").mkdir()
        (root / "session.json").write_text(json.dumps(state, indent=2) + "\n", encoding="utf-8")
        return cls(root, runner=runner, now=now, sleeper=sleeper)

    def _save(self):
        self.state_path.write_text(json.dumps(self.state, indent=2) + "\n", encoding="utf-8")

    def _step(self, step_id):
        for step in self.plan["steps"]:
            if step["id"] == step_id:
                return step
        raise ValueError("unknown step id: " + step_id)

    def _check_device(self):
        output = self.runner.run(["adb", "-s", self.state["serial"], "get-state"]).strip()
        if output != "device":
            raise RuntimeError("target device is not ready: " + output)

    def _check_foreground(self, action):
        if action["type"] == "observe":
            return
        target_display = action["display"]
        output = self.runner.run(
            ["adb", "-s", self.state["serial"], "shell", "dumpsys", "window", "displays"]
        )
        current_display = None
        current_focus_package = None
        focused_app_package = None
        for line in output.splitlines():
            display_match = re.search(r"\bmDisplayId=(\d+)\b", line)
            if display_match:
                current_display = int(display_match.group(1))
            if current_display != target_display:
                continue
            focus_kind = None
            if "mCurrentFocus=" in line:
                focus_kind = "current"
            elif "mFocusedApp=" in line:
                focus_kind = "app"
            if focus_kind is None:
                continue
            component = re.search(
                r"\bu\d+\s+([A-Za-z0-9_.]+)(?:/[A-Za-z0-9_.$]+)?(?:\}|\s|$)",
                line,
            )
            if component and focus_kind == "current":
                current_focus_package = component.group(1)
            elif component:
                focused_app_package = component.group(1)
        foreground_package = current_focus_package or focused_app_package
        if foreground_package != self.state["package"]:
            raise RuntimeError(
                f"expected package is not foreground on display {target_display}; "
                "capture and inspect the UI again"
            )

    def _capture(self, step_id, phase):
        capture_command = (
            self.runner.capture_command()
            if hasattr(self.runner, "capture_command")
            else ["android-capture"]
        )
        output = self.runner.run(
            capture_command + ["--serial", self.state["serial"], "--screens", "both"]
        )
        payload = json.loads(output)
        if payload.get("serial") != self.state["serial"]:
            raise RuntimeError("android-capture returned evidence from another device")
        copied = {}
        for screen in ("up", "down"):
            source = pathlib.Path(payload.get("outputs", {}).get(screen, ""))
            if not source.is_file():
                raise RuntimeError("android-capture did not produce the " + screen + " screen")
            destination = self.root / "evidence" / f"{step_id}-{phase}-{screen}{source.suffix or '.png'}"
            shutil.copyfile(source, destination)
            copied[screen] = str(destination.resolve())
        return copied

    def prepare(self, step_id):
        if self.state["pending"] is not None:
            raise ValueError("review or discard the pending prepared action first")
        step = self._step(step_id)
        self._check_device()
        captures = self._capture(step_id, "before")
        prepared_at = self.now()
        action = json.loads(json.dumps(step["action"], sort_keys=True))
        pending = {
            "step_id": step_id,
            "prepared_at": prepared_at,
            "plan_sha256": _sha256_file(self.root / "plan.json"),
            "action": action,
            "captures": captures,
        }
        token = _authorization_token(self.state["serial"], pending)
        pending["token"] = token
        self.state["pending"] = pending
        self.state["steps"][step_id] = {"status": "prepared", "before": captures}
        self._save()
        return {
            "step_id": step_id,
            "title": step["title"],
            "expected_before": step["expected_before"],
            "action": action,
            "captures": captures,
            "token": token,
            "expires_in_seconds": MAX_CAPTURE_AGE_SECONDS,
        }

    def discard(self, note):
        pending = self.state.get("pending")
        if pending is None:
            raise ValueError("no prepared action is pending")
        step_state = self.state["steps"][pending["step_id"]]
        step_state["status"] = "discarded"
        step_state["note"] = _require_text(note, "note")
        self.state["pending"] = None
        self._save()

    def _action_command(self, action):
        base = ["adb", "-s", self.state["serial"], "shell", "input"]
        action_type = action["type"]
        if action_type == "observe":
            return None
        base.extend(["-d", str(action["display"])])
        if action_type == "tap":
            return base + ["tap", str(action["x"]), str(action["y"])]
        if action_type == "swipe":
            return base + [
                "swipe",
                str(action["x1"]),
                str(action["y1"]),
                str(action["x2"]),
                str(action["y2"]),
                str(action["duration_ms"]),
            ]
        return base + ["keyevent", action["keycode"]]

    def _invalidate_pending(self, status, note):
        pending = self.state["pending"]
        step_state = self.state["steps"][pending["step_id"]]
        step_state["status"] = status
        step_state["note"] = note
        self.state["pending"] = None
        self._save()

    def execute(self, token):
        pending = self.state.get("pending")
        if pending is None:
            raise ValueError("no prepared action is pending")
        if token != pending["token"]:
            raise ValueError("prepared action token does not match")
        if _sha256_file(self.root / "plan.json") != pending.get("plan_sha256"):
            self._invalidate_pending("authorization_failed", "The validation plan changed after preparation.")
            raise ValueError("validation plan changed after prepare")
        try:
            validate_action(pending.get("action"))
            authorized = _authorization_token(self.state["serial"], pending)
        except Exception as failure:
            self._invalidate_pending("authorization_failed", str(failure))
            raise ValueError("prepared action authorization could not be verified") from failure
        if not hmac.compare_digest(pending["token"], authorized):
            self._invalidate_pending("authorization_failed", "The prepared action or evidence changed.")
            raise ValueError("prepared action authorization does not match")
        if self.now() - pending["prepared_at"] > MAX_CAPTURE_AGE_SECONDS:
            self._invalidate_pending("stale", "The inspected pre-action capture expired.")
            raise ValueError("prepared capture is stale; capture and inspect the UI again")
        try:
            self._check_device()
            self._check_foreground(pending["action"])
        except Exception as failure:
            self._invalidate_pending("precondition_failed", str(failure))
            raise

        step = self._step(pending["step_id"])
        step_state = self.state["steps"][step["id"]]
        step_state["status"] = "executing"
        step_state["action"] = pending["action"]
        self.state["pending"] = None
        self._save()

        try:
            command = self._action_command(pending["action"])
            if command is not None:
                self.runner.run(command)
            self.sleeper(1.0)
            captures = self._capture(step["id"], "after")
            diagnostics_path = self.root / "evidence" / f"{step['id']}-logcat.txt"
            diagnostics = self.runner.run(
                [
                    "adb", "-s", self.state["serial"], "logcat", "-d", "-v", "threadtime", "-t", "300",
                    "SilksongLauncher:V", "DualScreen:V", "Unity:V", "AndroidRuntime:E", "*:S",
                ]
            )
            diagnostics_path.write_text(diagnostics, encoding="utf-8")
        except Exception as failure:
            step_state["status"] = "execution_failed"
            step_state["note"] = str(failure)
            self._save()
            raise

        self.state["steps"][step["id"]] = {
            "status": "awaiting_review",
            "action": pending["action"],
            "before": pending["captures"],
            "after": captures,
            "diagnostics": str(diagnostics_path.resolve()),
        }
        self._save()
        return {
            "step_id": step["id"],
            "title": step["title"],
            "expected_after": step["expected_after"],
            "captures": captures,
            "diagnostics": str(diagnostics_path.resolve()),
            "status": "awaiting_review",
        }

    def review(self, step_id, verdict, note):
        if verdict not in {"pass", "fail", "blocked"}:
            raise ValueError("verdict must be pass, fail, or blocked")
        note = _require_text(note, "note")
        step_state = self.state["steps"].get(step_id)
        if step_state is None or step_state.get("status") != "awaiting_review":
            raise ValueError("step has no completed evidence awaiting review: " + step_id)
        step_state["status"] = verdict
        step_state["note"] = note
        self._save()

    def render_report(self):
        lines = [
            "# " + self.plan["name"] + " validation report",
            "",
            "- Serial: `" + self.state["serial"] + "`",
            "- Package: `" + self.state["package"] + "`",
            "",
        ]
        for step in self.plan["steps"]:
            evidence = self.state["steps"].get(step["id"], {})
            status = evidence.get("status", "not_run").upper()
            lines.append(f"## {status} — {step['title']}")
            lines.append("")
            lines.append("- Expected before: " + step["expected_before"])
            lines.append("- Expected after: " + step["expected_after"])
            if evidence.get("note"):
                lines.append("- Review: " + evidence["note"])
            for phase in ("before", "after"):
                for screen, path in evidence.get(phase, {}).items():
                    relative = pathlib.Path(path).relative_to(self.root.resolve())
                    lines.append(f"- {phase.title()} {screen}: `{relative.as_posix()}`")
            if evidence.get("diagnostics"):
                relative = pathlib.Path(evidence["diagnostics"]).relative_to(self.root.resolve())
                lines.append("- Diagnostics: `" + relative.as_posix() + "`")
            lines.append("")
        return "\n".join(lines)

    def write_report(self):
        report_path = self.root / "report.md"
        report_path.write_text(self.render_report(), encoding="utf-8")
        return report_path


def _parser():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)

    init = commands.add_parser("init", help="create a validation evidence session")
    init.add_argument("--session", required=True, type=pathlib.Path)
    init.add_argument("--plan", required=True, type=pathlib.Path)
    init.add_argument("--serial", required=True)
    init.add_argument("--package", required=True)

    prepare = commands.add_parser("prepare", help="capture both screens before one action")
    prepare.add_argument("--session", required=True, type=pathlib.Path)
    prepare.add_argument("--step", required=True)

    discard = commands.add_parser("discard", help="discard a prepared action without sending input")
    discard.add_argument("--session", required=True, type=pathlib.Path)
    discard.add_argument("--note", required=True)

    execute = commands.add_parser("execute", help="execute one inspected prepared action")
    execute.add_argument("--session", required=True, type=pathlib.Path)
    execute.add_argument("--token", required=True)

    review = commands.add_parser("review", help="record an evidence-backed verdict")
    review.add_argument("--session", required=True, type=pathlib.Path)
    review.add_argument("--step", required=True)
    review.add_argument("--verdict", required=True, choices=("pass", "fail", "blocked"))
    review.add_argument("--note", required=True)

    report = commands.add_parser("report", help="write the current Markdown report")
    report.add_argument("--session", required=True, type=pathlib.Path)
    return parser


def main(argv=None):
    args = _parser().parse_args(argv)
    if args.command == "init":
        session = ValidationSession.create(args.session, args.plan, args.serial, args.package)
        result = {"session": str(session.root.resolve()), "plan": session.plan["name"]}
    else:
        session = ValidationSession(args.session)
        if args.command == "prepare":
            result = session.prepare(args.step)
        elif args.command == "discard":
            session.discard(args.note)
            result = {"status": "discarded"}
        elif args.command == "execute":
            result = session.execute(args.token)
        elif args.command == "review":
            session.review(args.step, args.verdict, args.note)
            result = {"step_id": args.step, "status": args.verdict}
        else:
            result = {"report": str(session.write_report().resolve())}
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
