import hashlib
import importlib.util
import json
import os
import pathlib
import re
import tempfile
import unittest

ROOT = pathlib.Path(__file__).parents[3]
GENERATOR = ROOT / "tools/shared-patches-tests/generate_skin_unity_fixture.py"


class SkinUnityFixtureTest(unittest.TestCase):
    def test_complete_conditional_bodies_and_exact_manifest_identities(self):
        spec = importlib.util.spec_from_file_location("skin_unity_fixture", GENERATOR)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory(dir=os.environ["DUALSOULS_HOST_TEST_TEMP"]) as temp:
            output = pathlib.Path(temp) / "SkinUnityFixture.g.cs"
            module.generate(ROOT, output)
            text = output.read_text(encoding="utf-8")
            manifest = json.loads(output.with_suffix(".manifest.json").read_text(encoding="utf-8"))
            sha = lambda data: hashlib.sha256(data).hexdigest()
            self.assertEqual(sha(output.read_bytes()), manifest["output_sha256"])
            self.assertEqual(2, len(manifest["bodies"]))
            for record in manifest["bodies"]:
                source = (ROOT / record["source"]).read_text(encoding="utf-8")
                self.assertEqual(sha((ROOT / record["source"]).read_bytes()), record["source_sha256"])
                bodies = re.findall(r"namespace DualSouls\.Skins\.(?:HollowKnight|Silksong)\.Runtime\s*\{(.*?)\n\}", source, re.S)
                self.assertEqual([sha(body.encode()) for body in bodies], record["namespace_bodies_sha256"])
                for body in bodies:
                    self.assertIn(body, text)  # Whole declarations, not selected method-token proof.
                self.assertTrue(record["body_identical"])
            self.assertNotIn("#if", text)
            self.assertIn("IReadOnlyList<SkinSlot> Discover()", text)
            self.assertIn("session.TickTeardown();", text)
            self.assertIn("session.Dispose();", text)


if __name__ == "__main__":
    unittest.main()
