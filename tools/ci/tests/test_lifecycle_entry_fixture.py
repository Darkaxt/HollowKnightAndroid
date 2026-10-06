"""Authenticate lifecycle entry bodies; graphics and Android remain unmodeled."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
TOOLS = ROOT / "tools/shared-patches-tests"


class LifecycleEntryFixtureTest(unittest.TestCase):
    def test_shared_presentation_replay_uses_only_the_selected_snapshot(self):
        temp = Path(os.environ["DUALSOULS_HOST_TEST_TEMP"])
        directory = Path(tempfile.mkdtemp(prefix="shared-replay-", dir=temp))
        shared = directory / "shared"
        shared.mkdir()
        try:
            original = ROOT / "tools/shared-patches/src/dualscreen/DirectDisplayPresentation.cs"
            selected = shared / original.name
            selected.write_bytes(b"// Independent replay snapshot identity.\n" + original.read_bytes())
            output, manifest = directory / "ss.cs", directory / "ss.json"
            args = [sys.executable, str(TOOLS / "generate_ss_shell_fixture.py"), str(output),
                    "--shared-source-root", str(shared), "--manifest", str(manifest)]
            subprocess.run(args, check=True, capture_output=True, text=True)
            records = json.loads(manifest.read_text(encoding="utf-8"))["bodies"]
            record = next(r for r in records if r["member"] == "DirectDisplayPresentation")
            self.assertEqual(selected.resolve(), Path(record["source"]).resolve())
            self.assertEqual(hashlib.sha256(selected.read_bytes()).hexdigest(), record["source_file_sha256"])
            self.assertNotEqual(hashlib.sha256(original.read_bytes()).hexdigest(), record["source_file_sha256"])
            # A missing selected snapshot must fail, never fall back to current
            # shared production while still claiming an immutable replay.
            args[args.index("--shared-source-root") + 1] = str(directory / "missing")
            failed = subprocess.run(args, capture_output=True, text=True)
            self.assertNotEqual(0, failed.returncode)
            self.assertIn("FileNotFoundError", failed.stderr)
        finally:
            for path in shared.iterdir():
                self.assertTrue(path.is_file())
                path.unlink()
            shared.rmdir()
            for path in directory.iterdir():
                self.assertTrue(path.is_file())
                path.unlink()
            directory.rmdir()

    def test_complete_hk_entry_and_ss_presentation_bodies_are_unchanged(self):
        temp = Path(os.environ["DUALSOULS_HOST_TEST_TEMP"])
        directory = Path(tempfile.mkdtemp(prefix="lifecycle-identity-", dir=temp))
        try:
            for generator, label in (("generate_hk_pause_fixture.py", "hk"),
                                     ("generate_ss_shell_fixture.py", "ss")):
                output = directory / (label + ".cs")
                manifest = directory / (label + ".json")
                args = [sys.executable, str(TOOLS / generator), str(output)]
                args += [str(manifest)] if label == "hk" else ["--manifest", str(manifest)]
                subprocess.run(args, check=True, capture_output=True, text=True)
                data = json.loads(manifest.read_text(encoding="utf-8"))
                self.assertEqual(hashlib.sha256(output.read_bytes()).hexdigest(), data["output_sha256"])
                records = data.get("methods", data.get("bodies"))
                for record in records:
                    if "source_file_sha256" in record:
                        self.assertEqual(hashlib.sha256(Path(record["source"]).read_bytes()).hexdigest(),
                                         record["source_file_sha256"])
                    if "body_identical" in record:
                        self.assertTrue(record["body_identical"])
                required = ({"Boot", "EnsureStarted", "Start", "TickError", "BindDirectDisplay",
                             "OnDirectPanelGeometry", "ShutdownDirectDisplayAndRestore",
                             "RetryPendingDirectDisplayRestore", "CompleteDirectDisplayTeardown"}
                            if label == "hk" else
                            {"DualScreenV2", "DsPresentation", "DirectDisplayPresentation",
                             "DirectDisplayCanvasScaler", "DsTouch.MapToCanvas"})
                by_name = {r.get("method", r.get("member")): r for r in records}
                self.assertTrue(required.issubset(by_name))
                # Re-extract independently using the token-aware source member parser,
                # and authenticate the entire executable body, not tokens alone.
                spec = importlib.util.spec_from_file_location("lifecycle_parser", TOOLS / "generate_ss_shell_fixture.py")
                parser = importlib.util.module_from_spec(spec)
                spec.loader.exec_module(parser)
                text = output.read_text(encoding="utf-8")
                for name in required:
                    record = by_name[name]
                    source = Path(record["source"]).read_text(encoding="utf-8")
                    if label == "hk":
                        original = parser.member(parser.class_text(source, "HKDualScreen"), name)
                    elif name == "DsTouch.MapToCanvas":
                        original = parser.member(parser.class_text(source, "DsTouch"), "MapToCanvas")
                    else:
                        original = parser.class_text(source, name)
                    body = original[original.index("{"):]
                    self.assertIn(body, text)
                    self.assertEqual(hashlib.sha256(body.encode()).hexdigest(), record["body_utf8_lf_sha256"])
        finally:
            for path in directory.iterdir():
                self.assertTrue(path.is_file())
                path.unlink()
            directory.rmdir()


if __name__ == "__main__":
    unittest.main()
