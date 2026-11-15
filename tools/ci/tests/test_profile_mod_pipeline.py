import hashlib
import json
import pathlib
import re
import subprocess
import tempfile
import unittest


REPO_ROOT = pathlib.Path(__file__).parents[3]
LAUNCHER = REPO_ROOT / "src" / "SilksongLauncher.Launcher" / "app" / "src" / "main" / "kotlin" / "dev" / "silksong" / "launcher"


SKIN_SOURCE_ROOTS = (
    "tools/shared-patches/src/skins",
    "tools/hollow-knight-patches/src/skins",
    "tools/silksong-patches/src/skins",
)
SKIN_FORBIDDEN_SEAMS = {
    "native-interop": r"\b(?:IntPtr|UIntPtr|Marshal|DllImport|unsafe)\b",
    "process-memory": r"\b(?:ReadProcessMemory|WriteProcessMemory|OpenProcess|VirtualAlloc|ProcessMemory|GetProcAddress)\b",
    "native-offset": r"\b(?:il2cpp|native)[_\w]*(?:offset|address)\b",
    "player-data-injection": r"\b(?:class\s+PlayerData|PlayerData\s*\.\s*__\w+|playerData\s*\.\s*__\w+)",
    "player-data-write": r"\bplayerData\s*\.\s*\w+\s*(?:=(?!=)|\+=|-=|\+\+|--)",
    "save-content-api": r"\b(?:SaveGame|LoadGame|ClearSaveFile|ReadSaveFile|WriteSaveFile|SaveData|LoadSaveData)\b",
    "save-content-path": r"(?:user|save)\d*\.(?:dat|sav)\b",
    "gameplay-death-command": r"\b(?:hero|currentHero|HeroController(?:\.(?:instance|SilentInstance|UnsafeInstance))?)\s*\.\s*(?:Die|Kill|TakeDamage|SetHealth)\s*\(",
}


def skin_source_inventory(root=REPO_ROOT):
    return sorted(path for relative in SKIN_SOURCE_ROOTS for path in (root / relative).rglob("*.cs"))


def skin_source_violations(path, source):
    # Lexical regression guard, not a semantic C# security analyzer. Preserve string
    # literals (including save paths), exclude comments, and report file/seam/line.
    tokens = re.compile(r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|//[^\n]*|/\*.*?\*/', re.S)
    code = tokens.sub(lambda match: match.group() if match.group().startswith(('"', '@"')) else
                      re.sub(r"[^\n]", " ", match.group()), source)
    return [f"{path}:{code.count(chr(10), 0, match.start()) + 1}: {seam}"
            for seam, pattern in SKIN_FORBIDDEN_SEAMS.items()
            for match in re.finditer(pattern, code, re.I)]


class ProfileModPipelineContractTest(unittest.TestCase):
    def _package_manifest_fixture(self, root):
        root = pathlib.Path(root)
        packages = root / "packages"
        packages.mkdir()
        authorities = {}
        for name, content in (
            ("bcl.dll", b"bcl"),
            ("core.dll", b"core"),
            ("Assembly-CSharp.dll", b"game"),
        ):
            path = root / name
            path.write_bytes(content)
            authorities[name] = path
        assemblies = {}
        for name in (
            "Unity.InputSystem.dll",
            "HollowKnightPatches.dll",
            "0Harmony.dll",
            "BepInEx.dll",
        ):
            path = packages / name
            path.write_bytes(name.encode("utf-8"))
            assemblies[name] = path

        digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
        values = {
            "schema": "1",
            "profile": "hollow-knight",
            "unityVersion": "6000.0.61f1",
            "roslynVersion": "4.12.0",
            "steamDepotId": "367522",
            "gameVersion": "1.5.12620",
            "unityMscorlibSha256": digest(authorities["bcl.dll"]),
            "androidCoreModuleSha256": digest(authorities["core.dll"]),
            "depotAssemblySha256": digest(authorities["Assembly-CSharp.dll"]),
        }
        values.update({f"assembly.{name}": digest(path) for name, path in assemblies.items()})
        manifest = packages / "dualsouls-package-assemblies-v1.properties"
        manifest.write_text("\n".join(f"{key}={value}" for key, value in values.items()) + "\n", encoding="utf-8")
        return packages, authorities, assemblies, values, manifest

    def _validate_package_manifest(self, packages, authorities):
        script = REPO_ROOT / "tools" / "bepinex-shim" / "package-manifest.ps1"
        quote = lambda value: "'" + str(value).replace("'", "''") + "'"
        command = (
            f". {quote(script)}; "
            "try { Test-ProductionPackageManifest "
            f"-PackageDirectory {quote(packages)} -Profile 'hollow-knight' "
            "-UnityVersion '6000.0.61f1' -RoslynVersion '4.12.0' "
            "-DepotId '367522' -GameVersion '1.5.12620' "
            f"-BclPath {quote(authorities['bcl.dll'])} "
            f"-PlayerCorePath {quote(authorities['core.dll'])} "
            f"-DepotAssemblyPath {quote(authorities['Assembly-CSharp.dll'])} | Out-Null; exit 0 "
            "} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }"
        )
        return subprocess.run(
            ["pwsh", "-NoProfile", "-NonInteractive", "-Command", command],
            text=True,
            capture_output=True,
            check=False,
        )

    def test_package_manifest_validator_accepts_exact_profile_package(self):
        with tempfile.TemporaryDirectory() as temp:
            packages, authorities, _, _, _ = self._package_manifest_fixture(temp)
            result = self._validate_package_manifest(packages, authorities)
            self.assertEqual(0, result.returncode, result.stderr)

    def test_package_manifest_validator_rejects_wrong_profile_and_stale_digest(self):
        with tempfile.TemporaryDirectory() as temp:
            packages, authorities, assemblies, values, manifest = self._package_manifest_fixture(temp)
            manifest.write_text(
                manifest.read_text(encoding="utf-8").replace("profile=hollow-knight", "profile=silksong"),
                encoding="utf-8",
            )
            wrong = self._validate_package_manifest(packages, authorities)
            self.assertNotEqual(0, wrong.returncode)
            self.assertIn("profile does not match", wrong.stderr)

            values["profile"] = "hollow-knight"
            manifest.write_text("\n".join(f"{key}={value}" for key, value in values.items()) + "\n", encoding="utf-8")
            assemblies["BepInEx.dll"].write_bytes(b"changed after manifest publication")
            stale = self._validate_package_manifest(packages, authorities)
            self.assertNotEqual(0, stale.returncode)
            self.assertIn("digest is stale", stale.stderr)

    def test_package_manifest_validator_rejects_opposite_profile_assembly(self):
        with tempfile.TemporaryDirectory() as temp:
            packages, authorities, _, _, _ = self._package_manifest_fixture(temp)
            (packages / "SilksongPatches.dll").write_bytes(b"wrong profile")
            result = self._validate_package_manifest(packages, authorities)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("Opposite-profile package assembly is present", result.stderr)

    def test_package_manifest_validator_rejects_empty_and_missing_selected_patch(self):
        with tempfile.TemporaryDirectory() as temp:
            packages, authorities, assemblies, values, manifest = self._package_manifest_fixture(temp)
            manifest.unlink()
            empty = self._validate_package_manifest(packages, authorities)
            self.assertNotEqual(0, empty.returncode)
            self.assertIn("manifest is missing", empty.stderr)

            manifest.write_text("\n".join(f"{key}={value}" for key, value in values.items()) + "\n", encoding="utf-8")
            assemblies["HollowKnightPatches.dll"].unlink()
            missing = self._validate_package_manifest(packages, authorities)
            self.assertNotEqual(0, missing.returncode)
            self.assertIn("Required production package assembly is missing: HollowKnightPatches.dll", missing.stderr)


if __name__ == "__main__":
    unittest.main()
