import importlib.util
import pathlib
import unittest


MODULE_PATH = pathlib.Path(__file__).parents[1] / "verify_apk_signing.py"


class VerifyApkSigningContractTest(unittest.TestCase):
    def load_module(self):
        self.assertTrue(
            MODULE_PATH.is_file(),
            "the release workflow needs a reusable APK signer-pin verifier",
        )
        spec = importlib.util.spec_from_file_location("verify_apk_signing", MODULE_PATH)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module

    def test_normalizes_and_extracts_apksigner_sha256(self):
        module = self.load_module()
        expected = "324b3a3e854b69d567d1527ae52e96a1051adf13550b485e320f8ce8cf678c38"
        apksigner_output = (
            "Verifies\n"
            "Signer #1 certificate DN: CN=HollowKnightAndroid\n"
            "Signer #1 certificate SHA-256 digest: "
            "32:4B:3A:3E:85:4B:69:D5:67:D1:52:7A:E5:2E:96:A1:05:1A:DF:13:55:0B:48:5E:32:0F:8C:E8:CF:67:8C:38\n"
        )

        self.assertEqual(expected, module.normalize_certificate_sha256(expected.upper()))
        self.assertEqual(expected, module.extract_certificate_sha256(apksigner_output))

    def test_rejects_missing_or_malformed_certificate_digests(self):
        module = self.load_module()

        with self.assertRaisesRegex(ValueError, "64 hexadecimal"):
            module.normalize_certificate_sha256("1234")
        with self.assertRaisesRegex(ValueError, "did not report"):
            module.extract_certificate_sha256("Verifies\n")


if __name__ == "__main__":
    unittest.main()
