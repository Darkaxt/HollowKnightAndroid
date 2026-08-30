import pathlib
import unittest


REPO_ROOT = pathlib.Path(__file__).parents[3]
PACKAGE_ID = "io.github.darkaxt.dualsouls"
APP_LABEL = "Dual Souls"
APK_STEM = "DualSouls"


def read(relative_path: str) -> str:
    return (REPO_ROOT / relative_path).read_text(encoding="utf-8")


class ProductIdentityContractTest(unittest.TestCase):
    def test_build_and_local_device_defaults_use_the_product_identity(self):
        build = read("tools/depot-to-apk/build.sh")
        dev = read("tools/depot-to-apk/dev.sh")
        makefile = read("Makefile")

        self.assertIn(f'PKG="${{PKG:-{PACKAGE_ID}}}"', build)
        self.assertIn(f'APP_LABEL="${{APP_LABEL:-{APP_LABEL}}}"', build)
        self.assertIn(f'APK_NAME="${{APK_NAME:-{APK_STEM}-$VERSION_NAME.apk}}"', build)
        self.assertIn(f'PKG="${{PKG:-{PACKAGE_ID}}}"', dev)
        self.assertIn(f'APK_NAME="${{APK_NAME:-{APK_STEM}-$VERSION_NAME.apk}}"', dev)
        self.assertIn(f"PKG     ?= {PACKAGE_ID}", makefile)
        self.assertIn(f"APK     ?= $(APK_DIR)/{APK_STEM}-$(VERSION).apk", makefile)

    def test_launcher_resources_use_the_product_label(self):
        strings = read(
            "src/SilksongLauncher.Launcher/app/src/main/res/values/strings.xml"
        )

        self.assertIn(
            f'<string name="launcher_app_name">{APP_LABEL}</string>', strings
        )
        self.assertIn(f'<string name="launcher_title">{APP_LABEL}</string>', strings)

    def test_release_workflow_asserts_and_names_the_product_identity(self):
        workflow = read(".github/workflows/release.yml")
        contract = read("tools/ci/release_contract.py")

        self.assertIn(f'PRODUCTION_PACKAGE = "{PACKAGE_ID}"', contract)
        self.assertIn(
            'tools/ci/release_contract.py check-package --package "$package"',
            workflow,
        )
        self.assertIn(
            f'out="{APK_STEM}-${{{{ steps.version.outputs.version }}}}.apk"',
            workflow,
        )
        self.assertIn(
            f"## {APP_LABEL} ${{{{ steps.version.outputs.version }}}}", workflow
        )


if __name__ == "__main__":
    unittest.main()
