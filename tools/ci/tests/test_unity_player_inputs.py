"""Authenticated player input gate tests (no compile).

Executable member/checker cases require UNITY_PLAYER_50, UNITY_PLAYER_61,
HK_ORIGINAL_DEPOT and SS_ORIGINAL_DEPOT, pointing to read-only authentic inputs.
Fixtures and all child command receipts are retained, never cleaned by this module.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
HELPER = ROOT / "tools/ci/verify_unity_player.py"
MANIFEST = ROOT / "tools/ci/unity-player-members.json"
PROFILES = {"hollow-knight": ("6000.0.61f1", "UNITY_PLAYER_61", "HK_ORIGINAL_DEPOT"),
            "silksong": ("6000.0.50f1", "UNITY_PLAYER_50", "SS_ORIGINAL_DEPOT")}


class UnityPlayerInputContracts(unittest.TestCase):
    def fixture(self):
        return Path(tempfile.mkdtemp(prefix="player-member-test-"))

    def document(self):
        self.assertTrue(MANIFEST.is_file(), "Authenticated repository player manifest is missing")
        return json.loads(MANIFEST.read_text(encoding="utf-8"))

    def authentic(self, profile):
        name = PROFILES[profile][1]
        self.assertIn(name, os.environ, f"Set {name} to authenticated Android managed members")
        path = Path(os.environ[name])
        self.assertTrue(path.is_dir(), str(path))
        return path

    def run_command(self, argv, *, cwd=None, env=None):
        root = Path(os.environ.get("DUALSOULS_TEST_COMMAND_EVIDENCE", str(self.fixture())))
        root.mkdir(parents=True, exist_ok=True)
        token = uuid.uuid4().hex
        log = root / (token + ".log")
        with log.open("xb") as stream:
            proc = subprocess.Popen(argv, cwd=cwd or ROOT, env=env, stdout=stream, stderr=subprocess.STDOUT)
            timed_out = False
            try:
                code = proc.wait(timeout=30)
            except subprocess.TimeoutExpired:
                timed_out = True
                proc.kill()
                code = proc.wait()
        raw = log.read_bytes()
        with (root / (token + ".json")).open("x", encoding="utf-8") as stream:
            json.dump({"argv": argv, "cwd": str(cwd or ROOT), "pid": proc.pid,
                       "exit": code, "timed_out": timed_out, "child_status": "exited_waited", "log": str(log),
                       "log_sha256": hashlib.sha256(raw).hexdigest()}, stream, indent=2)
        self.assertFalse(timed_out, f"Child command timed out; retained log: {log}")
        return code, raw.decode("utf-8", errors="replace")

    def helper(self, profile, path, *, helper=HELPER, extra=()):
        self.assertTrue(helper.is_file(), "Repository player-member verifier is missing")
        return self.run_command([sys.executable, "-B", str(helper), "--profile", profile,
                                 "--player", str(path), *extra])

    def test_repository_helper_exists(self):
        self.assertTrue(HELPER.is_file(), "Repository player-member verifier is missing")

    def test_manifest_binds_only_exact_compiler_consumed_player_members(self):
        doc = self.document()
        self.assertEqual(1, doc["schemaVersion"])
        self.assertEqual(set(PROFILES), set(doc["profiles"]))
        project = ET.parse(ROOT / "tools/hollow-knight-patches/HollowKnightPatches.csproj")
        hk = {Path(n.text.split("/")[-1]).name for n in project.iter("HintPath")
              if n.text.startswith("$(UnityManaged)/")}
        ss_source = (ROOT / "tools/silksong-patches/check.ps1").read_text()
        ss_list = ss_source.split("$engine = @(", 1)[1].split(") | ForEach-Object", 1)[0]
        ss = {name + ".dll" for name in re.findall(r"'(UnityEngine\.[^']+)'", ss_list)}
        self.assertEqual(5, len(hk))
        self.assertEqual(18, len(ss))
        for profile, expected in (("hollow-knight", hk), ("silksong", ss)):
            authority = doc["profiles"][profile]
            self.assertEqual(PROFILES[profile][0], authority["unityVersion"])
            self.assertEqual(expected, {m["file"] for m in authority["members"]})
            self.assertEqual(len(expected), len(authority["members"]))
            for member in authority["members"]:
                self.assertEqual("./Variations/il2cpp/Managed/" + member["file"], member["archiveMember"])
                self.assertIs(type(member["size"]), int)
                self.assertGreater(member["size"], 0)
                self.assertRegex(member["sha256"], r"^[0-9a-f]{64}$")
        core = {p: next(m["sha256"] for m in doc["profiles"][p]["members"]
                       if m["file"] == "UnityEngine.CoreModule.dll") for p in PROFILES}
        self.assertEqual("4cfe70da33508fc85098cc33e64b778deffc3a00050da2d0031b227652fed8aa", core["hollow-knight"])
        self.assertEqual("618f0a5627321cd3241523257941a1fa9c686538f2548094f42a29977e63c2fd", core["silksong"])

    def test_missing_player_and_unknown_profile_fail_closed(self):
        for profile in (*PROFILES, "caller-selected-profile"):
            with self.subTest(profile=profile):
                code, text = self.helper(profile, self.fixture() / "absent")
                self.assertNotEqual(0, code, text)
                self.assertIn("Unity player input rejected", text)

    def test_no_caller_selected_authority_or_version(self):
        for option in ("--manifest", "--expected-sha256", "--unity-version"):
            with self.subTest(option=option):
                code, text = self.helper("hollow-knight", self.fixture(), extra=(option, "forged"))
                self.assertNotEqual(0, code, text)
                self.assertIn("unrecognized arguments", text)

    def test_each_required_member_absent_size_or_hash_mismatch_rejects(self):
        doc = self.document()
        for profile in PROFILES:
            authentic = self.authentic(profile)
            members = doc["profiles"][profile]["members"]
            for member in members:
                for mode in ("absent", "size", "hash"):
                    with self.subTest(profile=profile, file=member["file"], mode=mode):
                        player = self.fixture()
                        for pin in members:
                            if pin["file"] == member["file"] and mode == "absent":
                                continue
                            data = (authentic / pin["file"]).read_bytes()
                            self.assertEqual(pin["size"], len(data))
                            self.assertEqual(pin["sha256"], hashlib.sha256(data).hexdigest())
                            if pin["file"] == member["file"]:
                                data = data + b"x" if mode == "size" else bytes([data[0] ^ 1]) + data[1:]
                            (player / pin["file"]).write_bytes(data)
                        code, text = self.helper(profile, player)
                        self.assertNotEqual(0, code, text)
                        self.assertIn(member["file"], text)
                        self.assertIn("Unity player input rejected", text)

    def test_wrong_profile_version_rejects_authentic_other_player(self):
        for profile, other in (("hollow-knight", "silksong"), ("silksong", "hollow-knight")):
            with self.subTest(profile=profile):
                code, text = self.helper(profile, self.authentic(other))
                self.assertNotEqual(0, code, text)
                self.assertIn(PROFILES[profile][0], text)
                self.assertIn("UnityEngine.CoreModule.dll", text)

    def test_malformed_ambiguous_or_forged_repository_authority_rejects(self):
        doc = self.document()
        variants = {"missing": None, "malformed": "{", "duplicate-root":
                    MANIFEST.read_text().replace('"schemaVersion":', '"schemaVersion": 1, "schemaVersion":', 1)}
        for name, edit in (
            ("wrong-version", lambda d: d["profiles"]["hollow-knight"].update(unityVersion="6000.0.50f1")),
            ("duplicate-member", lambda d: d["profiles"]["hollow-knight"]["members"].append(d["profiles"]["hollow-knight"]["members"][0])),
            ("absent-required-member", lambda d: d["profiles"]["silksong"]["members"].pop()),
            ("malformed-digest", lambda d: d["profiles"]["hollow-knight"]["members"][0].update(sha256="bad")),
            ("string-size", lambda d: d["profiles"]["hollow-knight"]["members"][0].update(size="1")),
            ("traversal", lambda d: d["profiles"]["hollow-knight"]["members"][0].update(file="../UnityEngine.CoreModule.dll")),
            ("caller-forged-hash", lambda d: d["profiles"]["hollow-knight"]["members"][0].update(sha256=hashlib.sha256(b"forged").hexdigest())),
            ("forged-provenance", lambda d: d["profiles"]["silksong"]["provenance"].update(bindingSha256="0" * 64)),
            ("empty-profiles", lambda d: d.update(profiles={})),
        ):
            changed = json.loads(json.dumps(doc)); edit(changed); variants[name] = json.dumps(changed)
        for name, text in variants.items():
            with self.subTest(authority=name):
                root = self.fixture()
                helper = root / "verify_unity_player.py"
                helper.write_bytes(HELPER.read_bytes())
                if text is not None:
                    (root / "unity-player-members.json").write_text(text, encoding="utf-8")
                code, output = self.helper("hollow-knight", root / "absent", helper=helper)
                self.assertNotEqual(0, code, output)
                self.assertIn("Unity player authority is invalid", output)

    def test_checkers_reject_wrong_player_before_work_or_compiler(self):
        pwsh = shutil.which("pwsh")
        self.assertIsNotNone(pwsh, "PowerShell 7 is required")
        for profile, other in (("hollow-knight", "silksong"), ("silksong", "hollow-knight")):
            with self.subTest(profile=profile):
                self.assertIn(PROFILES[profile][2], os.environ)
                depot = Path(os.environ[PROFILES[profile][2]])
                self.assertTrue((depot / "Assembly-CSharp.dll").is_file())
                root = self.fixture()
                trap = root / "dotnet.cmd"
                trap.write_text('@exit /b 93\n', encoding="ascii")
                env = dict(os.environ, PATH=str(root) + os.pathsep + os.environ["PATH"],
                           DUALSOULS_TEMP_ROOT=str(root))
                output, work = root / "must-not-create-output", root / "must-not-create-work"
                command = [pwsh, "-NoLogo", "-NoProfile", "-NonInteractive", "-File",
                           str(ROOT / f"tools/{profile}-patches/check.ps1"), "-Depot", str(depot),
                           "-Player", str(self.authentic(other)), "-Output", str(output), "-RetainArtifacts"]
                if profile == "silksong":
                    command += ["-Work", str(work)]
                code, text = self.run_command(command, env=env)
                self.assertNotEqual(0, code, text)
                self.assertIn("Unity player input rejected", text)
                self.assertIn(PROFILES[profile][0], text)
                self.assertNotIn("compiling", text)
                self.assertNotIn("patch compile", text)
                self.assertFalse(output.exists(), text)
                self.assertFalse(work.exists(), text)


if __name__ == "__main__":
    unittest.main()
