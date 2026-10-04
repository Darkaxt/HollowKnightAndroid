"""Build-consumer guardrails; executable Kotlin/native weaving gates complement these."""
from pathlib import Path
from contextlib import contextmanager
import json
import os
import re
import shlex
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]


@contextmanager
def fixture_directory(prefix):
    # Admitted evidence runs retain every fixture; normal test runs keep their legacy cleanup.
    if os.environ.get("DUALSOULS_RETAIN_TEST_FIXTURES") == "1":
        yield tempfile.mkdtemp(prefix=prefix)
    else:
        with tempfile.TemporaryDirectory(prefix=prefix) as tmp:
            yield tmp


def body(text, signature):
    start = text.index(signature)
    opening = text.index("{", text.index("\n    ) {", start)) if "fun " in signature else text.index("{", start)
    depth, end = 1, opening + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


class HollowKnightRequiredWeaveContracts(unittest.TestCase):
    def test_make_check_requires_and_forwards_explicit_depot(self):
        make = shutil.which("make")
        self.assertIsNotNone(make, "GNU make is required for the executable check-wrapper contract")
        env = dict(os.environ)
        for key in ("DEPOT", "MAKEFLAGS", "MFLAGS"):
            env.pop(key, None)
        with fixture_directory(prefix="dualsouls-check-wrapper-") as tmp:
            root = Path(tmp)
            (root / "Makefile").write_bytes((ROOT / "Makefile").read_bytes())
            cases = (
                ("missing", None),
                ("blank", " \t "),
                ("managed", (root / "originalManaged").as_posix()),
                ("spaces", (root / "original Managed").as_posix()),
            )
            for name, depot in cases:
                with self.subTest(depot=name):
                    command = [make, "-n", "-s", "--no-print-directory", "check"]
                    if depot is not None:
                        command.append(f"DEPOT={depot}")
                    result = subprocess.run(command, cwd=root, env=env, capture_output=True,
                                            text=True, encoding="utf-8", timeout=10)
                    text = result.stdout + result.stderr
                    if name in ("missing", "blank"):
                        self.assertEqual(2, result.returncode, text)
                        self.assertRegex(text, r"usage: make check DEPOT=\S+")
                        self.assertNotIn("pwsh", text)
                    else:
                        self.assertEqual(0, result.returncode, text)
                        lines = [line.strip() for line in result.stdout.splitlines()
                                 if line.strip().startswith("pwsh ")]
                        self.assertEqual(1, len(lines), text)
                        self.assertEqual(
                            ["pwsh", "-NoProfile", "-File", "tools/silksong-patches/check.ps1",
                             "-Depot", depot],
                            shlex.split(lines[0]), text,
                        )
                        self.assertIn(f'-Depot "{depot}"', lines[0])

    def test_real_conversion_consumer_propagates_required_failures(self):
        source = (ROOT / "src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/Mods.kt").read_text()
        method = body(source, "    suspend fun weaveBuiltin(")
        self.assertIn('File(assemblies, "HollowKnightPatches.dll").isFile', method)
        failure = body(method, "            if (!result.ok)")
        self.assertRegex(failure, r"if \(requiredHollowKnight\).*throw IOException")
        caught = body(method, "        } catch (t: Throwable)")
        self.assertRegex(caught, r"if \(requiredHollowKnight\).*throw IOException")

    def test_exact_check_exercises_actual_weaver_not_only_patch_compile(self):
        source = (ROOT / "tools/hollow-knight-patches/check.ps1").read_text()
        self.assertIn("'ModWeaver.dll'", source)
        self.assertIn("builtin --assemblies", source)
        self.assertRegex(source, re.compile(r'if \(\$LASTEXITCODE -ne 0\).*?mandatory', re.S))
        self.assertIn("-p:BaseIntermediateOutputPath=", source)

    def test_exact_ss_check_can_retain_registered_compiler_inputs(self):
        source = (ROOT / "tools/silksong-patches/check.ps1").read_text()
        self.assertIn("[switch]$RetainArtifacts", source)
        self.assertIn("[string]$Work", source)
        self.assertIn("Refusing to overwrite existing patch-check work", source)
        self.assertRegex(source, re.compile(r"finally\s*\{\s*if \(\$RetainArtifacts\).*?else\s*\{.*?Remove-Item", re.S))

    def test_existing_converter_runs_builtin_before_conversion(self):
        source = (ROOT / "src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/Il2cppConverter.kt").read_text()
        self.assertIn("Mods.weaveBuiltin(context, root, asmDir(root), assets)", source)
        self.assertLess(source.index("Mods.weaveBuiltin(context, root, asmDir(root), assets)"), source.index('argv += "--assembly='))


class ExactAssemblyInputContracts(unittest.TestCase):
    HK_MANIFEST = "src/SilksongLauncher.Launcher/app/src/main/assets/profiles/hollow-knight-1.5.12620.json"
    SS_AUTHORITY = "tools/bundle-surgery/BridgeSilksongNormalDeath.cs"

    @classmethod
    def setUpClass(cls):
        cls.pwsh = shutil.which("pwsh")
        if not cls.pwsh:
            raise RuntimeError("PowerShell 7 (pwsh) is required for executable input-identity tests")

    def invoke(self, profile, error, *, layout="Managed", missing=False,
               explicit=True, authority=None):
        # Isolated checker/authority fixtures; never alter the real authority or game.
        with fixture_directory(prefix="dualsouls-input-identity-") as tmp:
            root = Path(tmp)
            script_path = f"tools/{profile}-patches/check.ps1"
            script = root / script_path
            script.parent.mkdir(parents=True)
            script.write_bytes((ROOT / script_path).read_bytes())
            authority_path = self.HK_MANIFEST if profile == "hollow-knight" else self.SS_AUTHORITY
            target = root / authority_path
            target.parent.mkdir(parents=True, exist_ok=True)
            if authority is None:
                target.write_bytes((ROOT / authority_path).read_bytes())
            elif authority is not False:
                target.write_text(authority, encoding="utf-8")
            if profile == "silksong":
                title_path = "tools/silksong-patches/src/dualscreen/DsTitleCard.cs"
                title = root / title_path
                title.parent.mkdir(parents=True)
                title.write_bytes((ROOT / title_path).read_bytes())
            managed = root / f"{profile}-target" / "game_Data" / "Managed"
            managed.mkdir(parents=True)
            if not missing:
                (managed / "Assembly-CSharp.dll").write_bytes(b"unknown bytes, not a pinned game assembly")
            depot = managed if layout == "Managed" else managed.parent
            work = root / "must-not-create-work"
            output = root / "must-not-create-output"
            command = [self.pwsh, "-NoLogo", "-NoProfile", "-NonInteractive", "-File", str(script),
                       "-Player", str(root / "missing-player"), "-Output", str(output)]
            if explicit:
                command += ["-Depot", str(depot)]
            if profile == "silksong":
                command += ["-Work", str(work)]
            env = dict(os.environ, DUALSOULS_TEMP_ROOT=str(root), SILKSONG_DEPOT=str(managed))
            result = subprocess.run(command, cwd=root, env=env, capture_output=True,
                                    text=True, encoding="utf-8", timeout=30)
            text = result.stdout + result.stderr
            self.assertNotEqual(0, result.returncode, text)
            self.assertIn(error, text, text)
            self.assertNotIn("UnityEngine.CoreModule.dll is missing", text)
            self.assertNotIn("No Android player assemblies", text)
            self.assertNotIn("compiling", text)
            self.assertNotIn("Build succeeded", text)
            self.assertFalse(work.exists(), text)
            self.assertFalse(output.exists(), text)

    def test_unknown_target_bytes_reject_before_engine_or_output(self):
        for profile in ("hollow-knight", "silksong"):
            for layout in ("Managed", "Data"):
                with self.subTest(profile=profile, layout=layout):
                    self.invoke(profile, "Assembly-CSharp.dll input identity mismatch", layout=layout)

    def test_missing_assembly_fails_as_input_not_engine(self):
        for profile in ("hollow-knight", "silksong"):
            with self.subTest(profile=profile):
                self.invoke(profile, "Assembly-CSharp.dll is missing", missing=True)

    def test_silksong_requires_explicit_depot_even_with_environment_candidate(self):
        self.invoke("silksong", "Explicit -Depot is required", explicit=False)

    def test_hk_invalid_manifest_authority_rejects_before_input(self):
        manifest = json.loads((ROOT / self.HK_MANIFEST).read_text())
        entry = next(e for e in manifest["requiredFiles"]
                     if e["relativePath"] == "Managed/Assembly-CSharp.dll")
        minimal = {"profileId": "hollow-knight", "gameVersion": "1.5.12620", "requiredFiles": [entry]}
        variants = {"missing": False, "malformed": "{", "duplicate-entry": json.dumps(dict(minimal, requiredFiles=[entry, entry])),
                    "missing-entry": json.dumps(dict(minimal, requiredFiles=[])),
                    "wrong-profile": json.dumps(dict(minimal, profileId="silksong")),
                    "wrong-version": json.dumps(dict(minimal, gameVersion="1.5.12621")),
                    "duplicate-key": json.dumps(minimal).replace('"profileId":', '"profileId": "silksong", "profileId":')}
        for key, value in (("action", "rewrite"), ("sha256", "not-a-digest"), ("size", "3597312")):
            variants[f"invalid-{key}"] = json.dumps(dict(minimal, requiredFiles=[dict(entry, **{key: value})]))
        for name, authority in variants.items():
            with self.subTest(authority=name):
                self.invoke("hollow-knight", "Hollow Knight input authority is invalid", authority=authority)

    def test_ss_invalid_named_pin_authority_rejects_before_input(self):
        source = (ROOT / self.SS_AUTHORITY).read_text()
        pin_line = next(line for line in source.splitlines() if "const string PINNED_ASSEMBLY_SHA256 =" in line)
        variants = {"missing": False, "missing-pin": source.replace("PINNED_ASSEMBLY_SHA256", "OTHER_HASH"),
                    "duplicate-pin": source + "\n" + pin_line,
                    "malformed-pin": source.replace("1af095416b89f73993058f9cbac3a93959d928314b735cc4acbca7bf1a952d2d", "bad"),
                    "wrong-version": source.replace('"1.0.29980"', '"1.0.29981"')}
        for name, authority in variants.items():
            with self.subTest(authority=name):
                self.invoke("silksong", "Silksong input authority is invalid", authority=authority)

    def test_ss_noncanonical_duplicate_named_pins_reject_before_input(self):
        source = (ROOT / self.SS_AUTHORITY).read_text()
        for pin in ("PINNED_GAME_VERSION", "PINNED_ASSEMBLY_SHA256"):
            pin_line = next(line for line in source.splitlines() if f"const string {pin} =" in line)
            escaped_pin = pin[:-1] + f"\\u{ord(pin[-1]):04x}"
            duplicates = {
                "compact": f'internal const string {pin}="bad";',
                "tabs": f'internal\tconst\tstring\t{pin}\t=\t"bad";',
                "multiline": f'internal const\nstring\n{pin}\n=\n"bad";',
                "same-line": f'internal const string OTHER_AUTHORITY_PIN = "unused"; internal const string {pin}="bad";',
                "malformed-rhs": f'internal const string {pin} = ;',
                "comma-separated": f'internal const string OTHER_AUTHORITY_PIN = "unused", {pin} = "bad";',
                "comma-comment": f'internal const string OTHER_AUTHORITY_PIN = "unused", {pin} /* initializer */ = "bad";',
                "comment-separated": f'internal const string /* type */ {pin} /* initializer */ = "bad";',
                "escaped-identifier": f'internal const string {escaped_pin} = "bad";',
            }
            for form, duplicate in duplicates.items():
                with self.subTest(pin=pin, form=form):
                    authority = source.replace(pin_line, pin_line + "\n" + duplicate, 1)
                    self.invoke("silksong", "Silksong input authority is invalid", authority=authority)

    def test_ss_changed_authority_bytes_reject_before_input(self):
        source = (ROOT / self.SS_AUTHORITY).read_text()
        self.invoke("silksong", "Silksong input authority is invalid",
                    authority=source + "\n// Unrelated comment; original named pin values are unchanged.\n")

    def test_authorities_remain_existing_exact_original_pins(self):
        manifest = json.loads((ROOT / self.HK_MANIFEST).read_text())
        self.assertEqual("hollow-knight", manifest["profileId"])
        self.assertEqual("1.5.12620", manifest["gameVersion"])
        entries = [e for e in manifest["requiredFiles"] if e["relativePath"] == "Managed/Assembly-CSharp.dll"]
        self.assertEqual(1, len(entries))
        self.assertEqual(3597312, entries[0]["size"])
        self.assertEqual("copy", entries[0]["action"])
        self.assertEqual("5c84b8e59dd669c48db1fc426541d0d96643f59d35a72534b21fead7c96a3086", entries[0]["sha256"])
        source = (ROOT / self.SS_AUTHORITY).read_text()
        self.assertEqual(["1.0.29980"], re.findall(r'const string PINNED_GAME_VERSION = "([^"]+)";', source))
        self.assertEqual(["1af095416b89f73993058f9cbac3a93959d928314b735cc4acbca7bf1a952d2d"],
                         re.findall(r'const string PINNED_ASSEMBLY_SHA256 = "([^"]+)";', source))

    def test_player_member_guards_precede_output_and_preserve_game_guards(self):
        for profile in ("hollow-knight", "silksong"):
            with self.subTest(profile=profile):
                source = (ROOT / f"tools/{profile}-patches/check.ps1").read_text()
                self.assertIn("tools/ci/verify_unity_player.py", source)
                invocation = f'--profile {profile} --player $Player'
                self.assertIn(invocation, source)
                guard = source.index(invocation)
                self.assertLess(source.index("Assembly-CSharp.dll input identity mismatch"), guard)
                for marker in ("$taskTempRoot =", "New-Item -ItemType Directory",
                               "dotnet build" if profile == "hollow-knight" else "Start-Process dotnet"):
                    self.assertLess(guard, source.index(marker))
                tail = source[guard:source.index("$taskTempRoot =")]
                self.assertIn("if ($LASTEXITCODE -ne 0)", tail)
                self.assertIn("throw", tail)
                self.assertNotIn("[string]$PlayerManifest", source)
                self.assertNotIn("[string]$ExpectedPlayerHash", source)

    def test_guards_use_named_authorities_before_engine_temp_and_compile(self):
        for profile, authority in (("hollow-knight", "hollow-knight-1.5.12620.json"),
                                   ("silksong", "BridgeSilksongNormalDeath.cs")):
            source = (ROOT / f"tools/{profile}-patches/check.ps1").read_text()
            with self.subTest(profile=profile):
                self.assertIn(authority, source)
                self.assertIn("Get-FileHash -LiteralPath $assembly -Algorithm SHA256", source)
                guard = source.index("Assembly-CSharp.dll input identity mismatch")
                for marker in ("$taskTempRoot =", "New-Item -ItemType Directory", "dotnet build" if profile == "hollow-knight" else "Start-Process dotnet"):
                    self.assertLess(guard, source.index(marker))
                self.assertLess(guard, source.index("Android UnityEngine.CoreModule.dll is missing" if profile == "hollow-knight" else "No Android player assemblies"))
                self.assertNotIn("[string]$ExpectedHash", source)
        ss = (ROOT / "tools/silksong-patches/check.ps1").read_text()
        self.assertIn("PINNED_ASSEMBLY_SHA256", ss)
        self.assertIn("PINNED_GAME_VERSION", ss)
        self.assertNotIn("Any depot copy will do", ss)
        self.assertNotIn("$env:SILKSONG_DEPOT", ss)
        self.assertNotRegex(ss, r"Get-ChildItem[^\n]*-Filter 'Managed'")


if __name__ == "__main__":
    unittest.main()
