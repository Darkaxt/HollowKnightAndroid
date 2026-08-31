import pathlib
import unittest


REPO_ROOT = pathlib.Path(__file__).parents[3]
SCRIPT = REPO_ROOT / "tools" / "ondevice-il2cpp" / "build-il2cpp.sh"


class NativeBuildContractTest(unittest.TestCase):
    def test_hash_manifest_normalizes_msys_binary_path_markers(self):
        source = SCRIPT.read_text(encoding="utf-8")

        self.assertIn("normalize_sha256_manifest", source)
        self.assertIn("sed 's/^\\([0-9a-fA-F][0-9a-fA-F]*\\) \\*/\\1  /'", source)
        self.assertIn("xargs sha256sum | normalize_sha256_manifest", source)

    def test_first_build_does_not_grep_an_empty_manifest_per_source(self):
        source = SCRIPT.read_text(encoding="utf-8")

        self.assertIn('if [ -s "$MANIFEST" ]; then', source)
        self.assertIn('old=$(grep -F "  $f" "$MANIFEST"', source)

    def test_object_names_are_sanitized_without_per_file_processes(self):
        source = SCRIPT.read_text(encoding="utf-8")

        self.assertIn('safe=${rel//[^A-Za-z0-9._-]/_}', source)
        self.assertNotIn("echo \"${2#$3/}\" | tr", source)
        self.assertNotIn('out=$(obj_name ', source)

    def test_linker_uses_a_response_file_for_the_object_list(self):
        source = SCRIPT.read_text(encoding="utf-8")

        self.assertIn('LINK_OBJECTS="obj/.link-objects.rsp"', source)
        self.assertIn('printf \'%s\\n\' "$o" >> "$LINK_OBJECTS"', source)
        self.assertIn('\"@$LINK_OBJECTS\" "$BASELIB"', source)
        self.assertNotIn('-o "$ROOT/libil2cpp.so" obj/*.o "$BASELIB"', source)


if __name__ == "__main__":
    unittest.main()
