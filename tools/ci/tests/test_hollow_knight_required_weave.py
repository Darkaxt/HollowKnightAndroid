"""Build-consumer guardrails; executable Kotlin/native weaving gates complement these."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[3]


def body(text, signature):
    start = text.index(signature)
    opening = text.index("{", text.index("\n    ) {", start)) if "fun " in signature else text.index("{", start)
    depth, end = 1, opening + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


class HollowKnightRequiredWeaveContracts(unittest.TestCase):
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


if __name__ == "__main__":
    unittest.main()
