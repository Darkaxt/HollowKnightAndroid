import pathlib
import re
import unittest


REPO_ROOT = pathlib.Path(__file__).resolve().parents[3]
SPEC = REPO_ROOT / "docs" / "superpowers" / "specs" / "2026-08-31-dual-souls-ui-port-design.md"
PLAN = REPO_ROOT / "docs" / "superpowers" / "plans" / "2026-08-31-dual-souls-ui-port.md"
MATRIX = REPO_ROOT / "docs" / "verification" / "dual-souls-ui-port-matrix.md"
DUALSCREEN_SOURCES = (
    REPO_ROOT / "tools" / "silksong-patches" / "src" / "dualscreen"
)
DUAL_SCREEN = DUALSCREEN_SOURCES / "DualScreenV2.cs"
PRESENTATION = DUALSCREEN_SOURCES / "DsPresentation.cs"
PORT_RUNTIME = DUALSCREEN_SOURCES / "DsPortRuntime.cs"
PORT_LAYERS = DUALSCREEN_SOURCES / "DsPortLayers.cs"

REQUIREMENTS = tuple(f"DSUI-{number:02d}" for number in range(1, 11))
REFERENCE_MODULES = (
    "HKDualScreen.cs",
    "Bottom.Layering.cs",
    "Bottom.Frame.cs",
    "Bottom.Hud.cs",
    "Bottom.Inventory.cs",
    "Bottom.Charms.cs",
    "Bottom.Map.cs",
    "Bottom.Select.cs",
    "Bottom.Tweaks.cs",
)
STATUS_ROWS = {
    "Authored shell": "REJECTED_PROTOTYPE",
    "Dual Souls composition port": ("NOT_STARTED", "IN-PROGRESS"),
}
VALID_DISPOSITION = re.compile(
    r"^(?:RETAIN_INFRASTRUCTURE|REWRITE_PORT|TEMPORARY_REFERENCE)\b|"
    r"^DELETE_AFTER_STAGE_\d+\b"
)
ACCEPTING_SHELL_STATUSES = {"COMPLETE", "HOST-COMPLETE", "PROVEN", "ACCEPTED"}
STATUS_CLAIM = re.compile(
    r"(?:"
    r"\bcomplete(?:s|d)?\b|\baccept(?:s|ed)?\b|\bproduction[- ]ready\b|"
    r"\b(?:visual(?:ly)?\s+)?prov(?:e|es|ed|en)\b.{0,48}"
    r"\b(?:Dual\s+Souls\s+)?(?:UI\s+)?port\b|"
    r"\b(?:Dual\s+Souls\s+)?(?:UI\s+)?port\b.{0,48}"
    r"\bvisually\s+prov(?:e|es|ed|en)\b|"
    r"\bvisual\s+proof\b.{0,48}\b(?:port|parity)\b|"
    r"\bparity\b.{0,32}\b(?:complete|proven)\b"
    r")",
    re.IGNORECASE,
)
NEGATED_STATUS_CLAIM = re.compile(
    r"\b(?:"
    r"(?:do|does|did|must|is|are|was|were|has|have|had|can|could|will|would|"
    r"should|may|might)\s+not|cannot|can['’]t|doesn['’]t|didn['’]t|"
    r"isn['’]t|wasn['’]t|hasn['’]t|mustn['’]t|won['’]t|wouldn['’]t|"
    r"shouldn['’]t|never"
    r")\s+(?:be\s+|been\s+)?(?:"
    r"complete(?:s|d)?\b|accept(?:s|ed)?\b|production[- ]ready\b|"
    r"(?:visual(?:ly)?\s+)?prov(?:e|es|ed|en)\b.{0,48}"
    r"\b(?:Dual\s+Souls\s+)?(?:UI\s+)?port\b"
    r")",
    re.IGNORECASE,
)
SHELL_REFERENCE = re.compile(
    r"(?:"
    r"(?:current\s+)?(?:independently\s+)?authored\s+(?:companion\s+)?shell|"
    r"current\s+(?:companion\s+)?shell|DsShell|"
    r"redesigned\s+companion(?:\s+(?:UI|shell))?"
    r")",
    re.IGNORECASE,
)
REJECTION_MARKER = re.compile(
    r"\b(?:REJECTED(?:_PROTOTYPE)?|UI-REJECTED)\b",
    re.IGNORECASE,
)


def read(path: pathlib.Path) -> str:
    return path.read_text(encoding="utf-8")


def normalize_cell(cell: str) -> str:
    return cell.replace("`", "").replace("*", "").strip()


def markdown_tables(source: str):
    table = []
    for line in source.splitlines():
        stripped = line.strip()
        if stripped.startswith("|"):
            table.append(tuple(cell.strip() for cell in stripped.strip("|").split("|")))
        elif table:
            yield table
            table = []
    if table:
        yield table


def tables_with_header(source: str, first_column_header: str):
    return [
        table
        for table in markdown_tables(source)
        if table and normalize_cell(table[0][0]) == first_column_header
    ]


def prose_paragraphs(source: str):
    paragraph = []
    for line in source.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("|"):
            if paragraph:
                yield " ".join(paragraph)
                paragraph = []
            continue
        paragraph.append(stripped)
    if paragraph:
        yield " ".join(paragraph)


def has_positive_acceptance_claim(text: str) -> bool:
    without_negated_claims = NEGATED_STATUS_CLAIM.sub("", text)
    return bool(STATUS_CLAIM.search(without_negated_claims))


def shell_acceptance_violations(source: str):
    violations = []
    for paragraph in prose_paragraphs(source):
        if SHELL_REFERENCE.search(paragraph) and has_positive_acceptance_claim(
            paragraph
        ):
            violations.append(paragraph)

    for table in markdown_tables(source):
        for row in table:
            row_text = " | ".join(row)
            if not SHELL_REFERENCE.search(row_text):
                continue

            normalized_cells = [normalize_cell(cell) for cell in row]
            row_is_rejected = bool(REJECTION_MARKER.search(" ".join(normalized_cells)))
            subject_references_shell = bool(SHELL_REFERENCE.search(row[0]))
            accepting_status = any(
                cell.upper() in ACCEPTING_SHELL_STATUSES for cell in normalized_cells
            )
            if accepting_status and (subject_references_shell or not row_is_rejected):
                violations.append(row_text)

            violations.extend(
                cell
                for cell in row
                if SHELL_REFERENCE.search(cell) and has_positive_acceptance_claim(cell)
            )

    return violations


class DualSoulsUiPortContractTest(unittest.TestCase):
    def test_authoritative_contract_documents_exist(self):
        for document in (SPEC, PLAN, MATRIX):
            with self.subTest(document=document.relative_to(REPO_ROOT)):
                self.assertTrue(document.is_file(), f"missing contract: {document}")

    def test_spec_and_plan_name_every_requirement(self):
        for document in (SPEC, PLAN):
            source = read(document)
            for requirement in REQUIREMENTS:
                with self.subTest(
                    document=document.relative_to(REPO_ROOT),
                    requirement=requirement,
                ):
                    self.assertIn(requirement, source)

    def test_matrix_covers_every_reference_module(self):
        matrix = read(MATRIX)
        tables = tables_with_header(matrix, "Reference module")
        self.assertEqual(1, len(tables), "matrix needs one reference-module table")
        first_column = [normalize_cell(row[0]) for row in tables[0][2:] if row]

        for module in REFERENCE_MODULES:
            with self.subTest(module=module):
                self.assertEqual(
                    1,
                    first_column.count(module),
                    f"{module} must have exactly one first-column data row",
                )

    def test_matrix_disposes_every_current_dualscreen_source(self):
        matrix = read(MATRIX)
        current_sources = sorted(path.name for path in DUALSCREEN_SOURCES.glob("*.cs"))
        tables = tables_with_header(matrix, "Current file")

        self.assertTrue(current_sources, "no current dualscreen C# sources were found")
        self.assertEqual(1, len(tables), "matrix needs one current-file table")
        rows = tables[0][2:]
        for filename in current_sources:
            with self.subTest(filename=filename):
                matching_rows = [
                    row for row in rows if row and normalize_cell(row[0]) == filename
                ]
                self.assertEqual(
                    1,
                    len(matching_rows),
                    f"{filename} must have exactly one first-column disposition row",
                )
                self.assertGreaterEqual(len(matching_rows[0]), 2)
                disposition = normalize_cell(matching_rows[0][1])
                self.assertRegex(
                    disposition,
                    VALID_DISPOSITION,
                    f"invalid disposition for {filename}: {disposition}",
                )

    def test_matrix_keeps_the_prototype_and_port_status_explicit(self):
        rows = {
            columns[0]: columns[1]
            for line in read(MATRIX).splitlines()
            if line.startswith("|")
            and len(columns := [column.strip() for column in line.strip("|").split("|")])
            >= 2
        }

        authored_shell_state = rows.get("Authored shell", "").strip("`* ")
        composition_port_state = rows.get(
            "Dual Souls composition port", ""
        ).strip("`* ")

        self.assertEqual(STATUS_ROWS["Authored shell"], authored_shell_state)
        self.assertIn(
            composition_port_state,
            STATUS_ROWS["Dual Souls composition port"],
            "Dual Souls composition port must be exactly NOT_STARTED or IN-PROGRESS",
        )

    def test_status_documents_do_not_accept_the_authored_shell_as_the_port(self):
        for document in (
            REPO_ROOT / "README.md",
            REPO_ROOT / "docs" / "verification" / "design-traceability.md",
        ):
            with self.subTest(document=document.relative_to(REPO_ROOT)):
                self.assertEqual(
                    [],
                    shell_acceptance_violations(read(document)),
                    "the authored shell cannot complete or visually prove the port",
                )

    def test_shell_acceptance_detector_rejects_bad_claims_without_cross_cell_noise(self):
        bad_examples = (
            "The current authored shell completes the Dual Souls UI port.",
            "DsShell visually proves the port.",
            "| Component | Status | Evidence |\n"
            "| --- | --- | --- |\n"
            "| Authored shell | COMPLETE | Current render |",
            "| Goal | Status | Evidence |\n"
            "| --- | --- | --- |\n"
            "| UI | PROVEN | Current DsShell output |",
            "| Goal | Status | Evidence |\n"
            "| --- | --- | --- |\n"
            "| UI | IN-PROGRESS | DsShell is production-ready |",
            "| Component | Status | Evidence |\n"
            "| --- | --- | --- |\n"
            "| Authored shell | COMPLETE | Previously REJECTED_PROTOTYPE |",
        )
        for example in bad_examples:
            with self.subTest(example=example):
                self.assertTrue(shell_acceptance_violations(example))

        rejected_cross_cell_example = (
            "| Goal 11: faithful Dual Souls port | Status | Evidence | Acceptance |\n"
            "| --- | --- | --- | --- |\n"
            "| Goal 11 | IN-PROGRESS | The authored shell is REJECTED_PROTOTYPE | "
            "Side-by-side proof is still required |"
        )
        self.assertEqual([], shell_acceptance_violations(rejected_cross_cell_example))

        negated_examples = (
            "The authored shell does not complete the UI port.",
            "The authored shell cannot complete the UI port.",
            "The authored shell doesn't complete the UI port.",
            "The authored shell didn't complete the UI port.",
            "The authored shell must not be accepted.",
            "The authored shell mustn't be accepted.",
            "The current shell is not production-ready.",
            "The current shell isn't production-ready.",
            "The current shell wasn't production-ready.",
            "The authored shell hasn't been accepted.",
            "The authored shell won't be accepted.",
            "The authored shell wouldn't be accepted.",
            "The authored shell shouldn't be accepted.",
            "The authored shell could not be accepted.",
            "The authored shell will not be accepted.",
            "The authored shell would not be accepted.",
            "The authored shell should not be accepted.",
            "DsShell never visually proved the port.",
        )
        for example in negated_examples:
            with self.subTest(example=example):
                self.assertEqual([], shell_acceptance_violations(example))

    def test_stage_one_port_sources_exist(self):
        for source in (PORT_RUNTIME, PORT_LAYERS):
            with self.subTest(source=source.name):
                self.assertTrue(source.is_file(), f"missing Stage 1 source: {source.name}")

    def test_production_entry_uses_the_empty_port_runtime_not_the_authored_shell(self):
        source = read(DUAL_SCREEN)
        self.assertRegex(source, r"\bDsPortRuntime\s+_port\s*;")
        self.assertRegex(
            source,
            r"_port\s*=\s*new\s+DsPortRuntime\s*\(\s*_screen\s*\)\s*;",
        )
        presentation_assignment = source.index("_screen = screen;")
        bringup_yield = source.index("yield return screen.Bringup();")
        ready_guard = source.index("if (!screen.Ready)")
        runtime_construction = source.index("_port = new DsPortRuntime(_screen);")
        self.assertLess(presentation_assignment, bringup_yield)
        self.assertLess(bringup_yield, ready_guard)
        self.assertLess(ready_guard, runtime_construction)
        for rejected in (
            r"\bDsShell\s+_shell\b",
            r"new\s+DsShell\s*\(",
            r"\bRegisterScreens\s*\(",
        ):
            with self.subTest(rejected=rejected):
                self.assertNotRegex(source, rejected)

    def test_no_production_dualscreen_source_constructs_the_dormant_shell(self):
        violations = []
        for path in sorted(DUALSCREEN_SOURCES.glob("*.cs")):
            if path.name == "DsShell.cs":
                continue  # retained dormant type; its declaration is not reachability
            if re.search(r"new\s+DsShell\s*\(", read(path)):
                violations.append(path.name)
        self.assertEqual([], violations, "production sources must not construct DsShell")

    def test_presentation_owns_only_the_proven_content_and_overlay_layers(self):
        source = read(PRESENTATION)
        self.assertRegex(source, r"public\s+const\s+int\s+CONTENT_LAYER\s*=\s*6\s*;")
        self.assertRegex(source, r"public\s+const\s+int\s+OVERLAY_LAYER\s*=\s*3\s*;")
        self.assertRegex(
            source,
            r"OWNED_LAYER_MASK\s*=\s*\(1\s*<<\s*CONTENT_LAYER\)\s*\|\s*"
            r"\(1\s*<<\s*OVERLAY_LAYER\)\s*;",
        )
        self.assertEqual(2, len(re.findall(r"AddComponent<Camera>\s*\(\s*\)", source)))
        self.assertEqual(
            [("CONTENT_LAYER", "6"), ("OVERLAY_LAYER", "3")],
            re.findall(r"const\s+int\s+(\w*LAYER\w*)\s*=\s*(\d+)\s*;", source),
        )

        for camera, layer in (
            ("Camera", "CONTENT_LAYER"),
            ("OverlayCamera", "OVERLAY_LAYER"),
        ):
            with self.subTest(camera=camera):
                block = re.search(
                    rf"(?ms)^\s*{camera}\s*=\s*.*?AddComponent<Camera>\s*\(\s*\)\s*;"
                    rf"(?P<body>.*?)(?=^\s*$)",
                    source,
                )
                self.assertIsNotNone(block, f"missing construction block for {camera}")
                body = block.group("body")
                self.assertRegex(body, rf"\b{camera}\.targetDisplay\s*=\s*DISPLAY\s*;")
                self.assertRegex(body, rf"\b{camera}\.cullingMask\s*=\s*1\s*<<\s*{layer}\s*;")

        self.assertRegex(
            source,
            r"if\s*\(\s*c\s*==\s*null\s*\|\|\s*IsOwnedCamera\s*\(\s*c\s*\)\s*\)\s*continue\s*;",
        )
        self.assertRegex(source, r"c\.cullingMask\s*&=\s*~OWNED_LAYER_MASK\s*;")
        owned_check = re.search(
            r"bool\s+IsOwnedCamera\s*\(\s*Camera\s+camera\s*\)\s*\{(?P<body>.*?)\}",
            source,
            re.DOTALL,
        )
        self.assertIsNotNone(owned_check)
        self.assertIn("camera == Camera", owned_check.group("body"))
        self.assertIn("camera == OverlayCamera", owned_check.group("body"))

    def test_empty_port_layer_roots_preserve_content_and_overlay_roles(self):
        if not PORT_LAYERS.is_file():
            self.skipTest("DsPortLayers.cs is introduced by Stage 1")
        source = read(PORT_LAYERS)
        for root_name in ("Content", "Frame", "Pages", "HUD", "Overlays", "Fade"):
            with self.subTest(root_name=root_name):
                self.assertIn(f'"{root_name}"', source)
        self.assertIn("presentation.Root", source)
        self.assertIn("presentation.OverlayRoot", source)
        self.assertIn("DsPresentation.CONTENT_LAYER", source)
        self.assertIn("DsPresentation.OVERLAY_LAYER", source)
        for rejected in ("DsShell", "DsWidgets", "IDsScreen"):
            with self.subTest(rejected=rejected):
                self.assertNotIn(rejected, source)

    def test_port_runtime_tracks_composition_lifecycle_without_drawing(self):
        if not PORT_RUNTIME.is_file():
            self.skipTest("DsPortRuntime.cs is introduced by Stage 1")
        source = read(PORT_RUNTIME)
        self.assertRegex(source, r"\bDsPortLayers\s+_layers\s*;")
        self.assertIn("SceneManager.GetActiveScene().handle", source)
        self.assertRegex(source, r"SceneRevision\s*\+\+")
        self.assertRegex(source, r"IsIdle\s*=\s*idle\s*;")
        self.assertRegex(source, r"IsVisible\s*=\s*visible\s*;")
        self.assertRegex(source, r"_layers\.SetVisible\s*\(\s*visible\s*\)\s*;")
        self.assertRegex(source, r"if\s*\(\s*_disposed\s*\)\s*return\s*;")
        self.assertNotRegex(source, r"(?:List|Queue)\s*<\s*DsGesture\s*>")

        layer_source = read(PORT_LAYERS)
        self.assertRegex(layer_source, r"public\s+void\s+SetVisible\s*\(\s*bool\s+visible\s*\)")
        self.assertRegex(layer_source, r"\.gameObject\.SetActive\s*\(\s*visible\s*\)")
        for rejected in ("DsShell", "DsWidgets", "IDsScreen"):
            with self.subTest(rejected=rejected):
                self.assertNotIn(rejected, source)

    def test_hotplug_reactivates_and_rechecks_the_existing_presentation(self):
        host = read(DUAL_SCREEN)
        self.assertNotRegex(
            host,
            r"if\s*\(\s*_screen\s*!=\s*null\s*&&\s*_screen\.Ready\s*\)\s*yield\s+break\s*;",
        )
        self.assertRegex(host, r"_screen\.MarkUnavailable\s*\(\s*\)\s*;")

        presentation = read(PRESENTATION)
        self.assertRegex(presentation, r"public\s+void\s+MarkUnavailable\s*\(")
        self.assertRegex(presentation, r"int\s+_availabilityRevision\s*;")
        self.assertIn(
            "int availabilityRevision = _availabilityRevision;",
            presentation,
        )
        self.assertIn(
            "if (availabilityRevision != _availabilityRevision)",
            presentation,
        )
        mark_unavailable = re.search(
            r"public\s+void\s+MarkUnavailable\s*\(\s*\)\s*\{(?P<body>.*?)\n\s*\}",
            presentation,
            re.DOTALL,
        )
        self.assertIsNotNone(mark_unavailable)
        self.assertIn("_availabilityRevision++;", mark_unavailable.group("body"))

        revision_capture = presentation.index(
            "int availabilityRevision = _availabilityRevision;"
        )
        activate = presentation.index("displays[DISPLAY].Activate();")
        settle = presentation.index("while (Time.realtimeSinceStartup < until)", activate)
        refreshed = presentation.index("displays = Display.displays;", settle)
        presence_check = presentation.index("if (displays.Length <= DISPLAY)", refreshed)
        revision_check = presentation.index(
            "if (availabilityRevision != _availabilityRevision)",
            presence_check,
        )
        measure = presentation.index("MeasurePanel(displays[DISPLAY]);", presence_check)
        ready = presentation.index("Ready = true;", measure)
        self.assertLess(revision_capture, activate)
        self.assertLess(activate, settle)
        self.assertLess(settle, refreshed)
        self.assertLess(refreshed, presence_check)
        self.assertLess(presence_check, revision_check)
        self.assertLess(revision_check, measure)
        self.assertLess(measure, ready)

    def test_bringup_serializes_one_retained_presentation_without_timeout(self):
        host = read(DUAL_SCREEN)
        retained = host.index("_screen = screen;")
        yielded = host.index("yield return screen.Bringup();")
        self.assertLess(retained, yielded)

        presentation = read(PRESENTATION)
        self.assertRegex(presentation, r"bool\s+_bringupInProgress\s*;")
        self.assertRegex(
            presentation,
            r"while\s*\(\s*_bringupInProgress\s*\)\s*yield\s+return\s+null\s*;",
        )
        self.assertNotRegex(presentation, r"bringup.{0,80}(?:timeout|deadline)")

    def test_host_active_state_requires_unpaused_present_ready_display(self):
        source = read(DUAL_SCREEN)
        self.assertRegex(source, r"bool\s+_displayPresent\s*;")
        self.assertIn(
            "bool active = !_paused && _displayPresent && _screen != null && _screen.Ready;",
            source,
        )
        self.assertRegex(source, r"_displayPresent\s*=\s*now\s*>\s*DsPresentation\.DISPLAY\s*;")
        self.assertRegex(source, r"if\s*\(\s*DsTouch\.Enabled\s*&&\s*Time\.unscaledTime")
        pause = re.search(
            r"void\s+OnApplicationPause\s*\(\s*bool\s+paused\s*\)\s*\{(?P<body>.*?)\}",
            source,
            re.DOTALL,
        )
        self.assertIsNotNone(pause)
        self.assertIn("ApplyActiveState();", pause.group("body"))
        self.assertNotIn("SetActive(!paused)", pause.group("body"))

    def test_all_empty_port_roots_full_stretch_their_parent(self):
        source = read(PORT_LAYERS)
        self.assertIn("root.anchorMin = Vector2.zero;", source)
        self.assertIn("root.anchorMax = Vector2.one;", source)
        self.assertIn("root.offsetMin = Vector2.zero;", source)
        self.assertIn("root.offsetMax = Vector2.zero;", source)

    def test_stage_one_status_is_bounded_to_the_verified_source_boundary(self):
        plan_rows = [
            line for line in read(PLAN).splitlines() if line.startswith("| 1 |")
        ]
        matrix_rows = [
            line
            for line in read(MATRIX).splitlines()
            if line.startswith("| Stage 1 transport/composition separation |")
        ]
        self.assertEqual(1, len(plan_rows))
        self.assertEqual(1, len(matrix_rows))
        for row in plan_rows + matrix_rows:
            self.assertNotIn("HOST-COMPLETE", row)
            self.assertIn("HOST-VERIFIED-BOUNDARY", row)


if __name__ == "__main__":
    unittest.main()
