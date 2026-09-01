import inspect
import os
import pathlib
import re
import subprocess
import tempfile
import unittest


REPO_ROOT = pathlib.Path(__file__).resolve().parents[3]
SPEC = REPO_ROOT / "docs" / "superpowers" / "specs" / "2026-08-31-dual-souls-ui-port-design.md"
PLAN = REPO_ROOT / "docs" / "superpowers" / "plans" / "2026-08-31-dual-souls-ui-port.md"
MATRIX = REPO_ROOT / "docs" / "verification" / "dual-souls-ui-port-matrix.md"
SOURCE_AUDIT = REPO_ROOT / "docs" / "verification" / "dualscreen-source-audit.md"
DUALSCREEN_SOURCES = (
    REPO_ROOT / "tools" / "silksong-patches" / "src" / "dualscreen"
)
DUAL_SCREEN = DUALSCREEN_SOURCES / "DualScreenV2.cs"
PRESENTATION = DUALSCREEN_SOURCES / "DsPresentation.cs"
PORT_RUNTIME = DUALSCREEN_SOURCES / "DsPortRuntime.cs"
PORT_LAYERS = DUALSCREEN_SOURCES / "DsPortLayers.cs"
PORT_UTIL = DUALSCREEN_SOURCES / "DsPortUtil.cs"
RESIDENT_UI = DUALSCREEN_SOURCES / "DsResidentUi.cs"
PORT_FRAME = DUALSCREEN_SOURCES / "DsPortFrame.cs"
PORT_FRAME_STATE = DUALSCREEN_SOURCES / "DsPortFrameState.cs"


def portable_temp_parent():
    runner_temp = os.environ.get("RUNNER_TEMP")
    if runner_temp:
        candidate = pathlib.Path(runner_temp)
        if candidate.is_dir() and os.access(candidate, os.W_OK):
            return candidate
    if os.name == "nt":
        d_temp = pathlib.Path("D:/Temp")
        if d_temp.is_dir() and os.access(d_temp, os.W_OK):
            return d_temp
    return None

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


def csharp_method_body(source: str, signature_pattern: str) -> str:
    match = re.search(signature_pattern + r"\s*\{", source)
    if match is None:
        return ""
    start = source.find("{", match.start())
    depth = 0
    for index in range(start, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[start + 1:index]
    return ""


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

    def test_hud_contract_is_one_hollow_knight_layout_populated_by_silksong(self):
        spec = " ".join(read(SPEC).split())
        plan = " ".join(read(PLAN).split())
        matrix = " ".join(read(MATRIX).split())
        source_audit = " ".join(read(SOURCE_AUDIT).split())

        for required in (
            "The result is one composed HUD",
            "Hollow Knight UI assets or widgets must not be",
            "Silksong's original HUD layout must not",
            "`Bottom.Hud` remains authoritative for",
            "layout, ownership, routing, transitions, lifecycle, and consequences",
            "Each semantic Silksong element—health, Silk, currencies, Crests/Tools",
            "must occupy or extend the corresponding Hollow Knight design slot",
            "every existing driver instance must remain attached and running on that same live object",
            "never by removing or freezing gameplay-HUD drivers",
            "separate static/status chrome may clone resident visual donors",
            "pause, inventory, or full dual-screen off/display unavailable",
            "separate companion page toggle does not return the live gameplay HUD",
            "direct-transport safety, not behavior attributed to `RelayerHud`",
            "must not roll back driver-owned health, Silk, currency, active, or visual state",
        ):
            self.assertIn(required, spec)

        for required in (
            "Hollow Knight owns visible behavior, state transitions, and their consequences",
            "an intact Silksong HUD layout, duplicate",
            "stacked HUDs, or Hollow Knight widgets/art dumped over Silksong elements",
            "Do not create a second gameplay HUD",
            "exact Hollow Knight HUD region/slot geometry populated by resident "
            "Silksong health, Silk, currency, area, and equipped Crest/Tool elements",
            "same-instance routing into Hollow Knight slots",
            "beneath each proven semantic anchor",
            "Reject cloned or mirrored gameplay HUDs",
            "live object to leave the top screen only by being routed to the bottom",
            "move the one live object or required live subtree",
            "Keep the same object and driver instances",
            "Direct route-back occurs on pause, inventory, or full dual-screen-off/display loss",
            "separate companion-page toggle leaves the live gameplay HUD on the bottom",
            "original parent, sibling index, moved-root local transform (position, rotation, and scale)",
            "every descendant layer changed by the adapter",
            "Do not change driver-owned active or visual state",
            "Never restore an old active/visual value",
            "static chrome follows the oracle's separate clone/renderer-pool behavior",
        ):
            self.assertIn(required, plan)

        self.assertIn(
            "move the same live Silksong semantic objects/subtrees into Hollow Knight slots",
            matrix,
        )
        self.assertIn(
            "move the one live HUD down and clean the top screen",
            source_audit,
        )
        for required in (
            "same instance ID and native driver instances",
            "original parent, sibling index, moved-root local position/rotation/scale",
            "every descendant layer it changes",
            "Direct route-back occurs on pause, inventory, or full dual-screen-off/display loss",
            "separate companion-page toggle leaves the live gameplay HUD on the bottom",
            "Restoration must cover only adapter-mutated routing properties",
            "separate area/equipped/FPS/battery/status chrome may clone resident Silksong visual donors",
            "Runtime proof of same-instance routing, reassertion, and exact restoration remains pending",
        ):
            self.assertIn(required, source_audit)
        self.assertIn(
            "Clone-and-mirror is not an equivalent implementation",
            source_audit,
        )
        self.assertIn(
            "A cloned/mirrored gameplay HUD, intact Silksong layout, duplicate "
            "stacked HUD, or Hollow Knight art/widgets overlaid on Silksong is rejected",
            matrix,
        )
        for required in (
            "original drivers and instance IDs",
            "restore only adapter-mutated parent/sibling/moved-root-transform/layer properties",
            "separate companion-page toggle leaves it routed",
            "Proactively restore still-valid objects before scene/routing-rig teardown as transport safety",
            "routing-only restoration, and side-by-side evidence remain unproved",
        ):
            self.assertIn(required, matrix)
        self.assertNotIn(
            "preserve main-screen HUD behavior unless the user explicitly enables",
            plan,
        )
        self.assertNotIn(
            "must clone, re-parent, re-layer, and drive resident game UI objects",
            source_audit,
        )

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

    def test_stage_two_frame_sources_exist(self):
        for source in (PORT_UTIL, RESIDENT_UI, PORT_FRAME, PORT_FRAME_STATE):
            with self.subTest(source=source.name):
                self.assertTrue(source.is_file(), f"missing Stage 2 source: {source.name}")

    def test_resident_adapter_uses_exact_inventory_apis_and_reports_provenance(self):
        if not RESIDENT_UI.is_file():
            self.skipTest("DsResidentUi.cs is introduced by Stage 2")
        source = read(RESIDENT_UI)
        self.assertIn("Resources.FindObjectsOfTypeAll<InventoryPaneList>()", source)
        self.assertRegex(source, r"GetPane\s*\(\s*InventoryPaneList\.PaneTypes\s+role\s*\)")
        self.assertIn("_paneList.GetPane(role)", source)
        self.assertIn('GetField("currentPaneText"', source)
        identities = (
            ("CloneTopOrnament", "_UIManager/UICanvas/OptionsMenuScreen/TopFleur",
             "Warning_Fleur0008", "959f, 106f, 959f, 106f"),
            ("CloneBottomOrnament", "_UIManager/UICanvas/KeepResPrompt/BottomFleur",
             "bottom_fleur0008", "355f, 134f, 303f, 66f"),
            ("CloneSelectedTopFleur", "_UIManager/UICanvas/PauseMenuScreen/TopFleur",
             "pause_top_fleur0000", "426f, 123f, 426f, 123f"),
            ("CloneSelectedBottomFleur", "_UIManager/UICanvas/PauseMenuScreen/BottomFleur",
             "bottom_fleur0000", "355f, 134f, 355f, 134f"),
        )
        for method, path, sprite, dimensions in identities:
            with self.subTest(path=path, sprite=sprite):
                self.assertEqual(1, source.count(f'"{path}"'))
                self.assertEqual(1, source.count(f'"{sprite}"'))
                clone_method = csharp_method_body(
                    source, rf"public\s+GameObject\s+{method}\s*\([^)]*\)\s*=>"
                )
                if not clone_method:  # expression-bodied method has no braces
                    clone_method = re.search(
                        rf"public\s+GameObject\s+{method}\s*\([^)]*\)\s*=>"
                        rf"(?P<body>.*?);", source, re.DOTALL
                    ).group("body")
                self.assertIn(dimensions, re.sub(r"\s+", " ", clone_method))
        self.assertIn("Resources.FindObjectsOfTypeAll<Image>()", source)
        self.assertRegex(source, r"source\.sprite\.name\s*!=\s*exactSpriteName")
        self.assertIn("Vector2 spriteSize = source.sprite.rect.size;", source)
        self.assertIn("var sourceRect = source.transform as RectTransform;", source)
        self.assertIn("Vector2 sourceSize = sourceRect.sizeDelta;", source)
        self.assertRegex(source, r"spriteSize\.x\s*-\s*expectedSpriteWidth")
        self.assertRegex(source, r"spriteSize\.y\s*-\s*expectedSpriteHeight")
        self.assertRegex(source, r"sourceSize\.x\s*-\s*expectedRectWidth")
        self.assertRegex(source, r"sourceSize\.y\s*-\s*expectedRectHeight")
        self.assertIn("_imageIndex.TryGetValue(exactSourcePath", source)
        self.assertIn("clone.GetComponent<Image>()", source)
        self.assertRegex(source, r"CapabilityGap\s*\(")
        self.assertRegex(source, r"ResidentProvenance\s*\(")
        self.assertNotRegex(source, r"(?:fallback|substitute).*(?:sprite|ornament)")

    def test_frame_uses_semantic_bottom_tabs_and_resident_text(self):
        if not PORT_FRAME.is_file():
            self.skipTest("DsPortFrame.cs is introduced by Stage 2")
        source = read(PORT_FRAME)
        order = re.search(
            r"ApprovedPageOrder\s*=\s*\{(?P<body>.*?)\};", source, re.DOTALL
        )
        self.assertIsNotNone(order)
        self.assertEqual(
            ["Inventory", "Loadout", "Tasks", "Journal", "Map"],
            re.findall(r"DsPageRole\.(\w+)", order.group("body")),
        )
        self.assertIn("pane.DisplayName", source)
        self.assertIn("ClonePaneName", source)
        self.assertIn("BuildBottomCentredTabs", source)
        self.assertIn("SelectedTopFleur", source)
        self.assertIn("SelectedBottomFleur", source)
        self.assertNotIn("UnityEngine.UI.Button", source)

    def test_native_tab_selection_alpha_matches_dual_souls(self):
        source = read(PORT_FRAME)
        alpha_body = csharp_method_body(source, r"void\s+ApplyTabSelectionAlpha\s*\(\s*\)")
        self.assertTrue(alpha_body, "missing tab selection alpha method")
        self.assertIn("for (int i = 0; i < _tabs.Count; i++)", alpha_body)
        self.assertIn("var tab = _tabs[i];", alpha_body)
        self.assertRegex(
            alpha_body,
            r"(?s)var\s+color\s*=\s*text\.color\s*;.*?"
            r"var\s+alpha\s*=\s*DsPortFrameState\.LabelAlpha\s*\(\s*"
            r"tab\.Role\s*==\s*_selected\s*\)\s*;.*?"
            r"text\.color\s*=\s*new\s+Color\s*\(\s*color\.r\s*,\s*color\.g\s*,\s*"
            r"color\.b\s*,\s*alpha\s*\)\s*;",
        )

        build_body = csharp_method_body(source, r"void\s+TryBuild\s*\(\s*\)")
        self.assertTrue(build_body)
        self.assertLess(
            build_body.index("BuildBottomCentredTabs();"),
            build_body.index("ApplyTabSelectionAlpha();"),
        )
        select_body = csharp_method_body(
            source, r"public\s+void\s+Select\s*\(\s*DsPageRole\s+role\s*\)"
        )
        self.assertTrue(select_body)
        self.assertLess(
            select_body.index("_selected = role;"),
            select_body.index("ApplyTabSelectionAlpha();"),
        )

    def test_static_frame_clones_are_sanitized_before_activation(self):
        source = read(PORT_UTIL)
        body = csharp_method_body(
            source,
            r"public\s+static\s+GameObject\s+CloneStaticResidentVisual\s*\([^)]*\)",
        )
        self.assertTrue(body, "missing static-frame clone lifecycle")
        staging_inactive = body.index("staging.SetActive(false);")
        instantiate = re.search(
            r"Object\.Instantiate\s*\(\s*source\s*,\s*staging\.transform\s*,\s*false\s*\)",
            body,
        )
        self.assertIsNotNone(instantiate, "clone must be born under inactive staging")
        clone_inactive = body.index("clone.SetActive(false);")
        required_policy = (
            "GetComponentsInChildren<MonoBehaviour>(true)",
            "behaviour.GetType() == retainedVisualType",
            "retainedCount != 1",
            "Object.DestroyImmediate(behaviour);",
            "var remainingBehaviours = clone.GetComponentsInChildren<MonoBehaviour>(true);",
            "remainingBehaviours.Length != 1",
            "remainingBehaviours[0].GetType() != retainedVisualType",
            "retained.enabled = true;",
            "retained.gameObject.SetActive(true);",
            "GetComponentsInChildren<Renderer>(true)",
            "renderer.gameObject.SetActive(true);",
            "renderer.enabled = true;",
        )
        for statement in required_policy:
            self.assertIn(statement, body)
        self.assertNotIn("IsAssignableFrom", body)
        self.assertNotIn("behaviour.enabled = false;", body)
        mono_scan = body.index("var behaviours = clone.GetComponentsInChildren<MonoBehaviour>(true);")
        retained_check = body.index("behaviour.GetType() == retainedVisualType")
        count_check = body.index("retainedCount != 1")
        destroy_other = body.index("Object.DestroyImmediate(behaviour);")
        verify_scan = body.index(
            "var remainingBehaviours = clone.GetComponentsInChildren<MonoBehaviour>(true);"
        )
        verify_exact = body.index("remainingBehaviours[0].GetType() != retainedVisualType")
        retained_enable = body.index("retained.enabled = true;")
        retained_active = body.index("retained.gameObject.SetActive(true);")
        explicit_disable_points = [
            body.index(f"GetComponentsInChildren<{component}>(true)")
            for component in ("Animator", "Animation", "AudioSource", "Collider2D")
        ]
        renderer_scan = body.index("GetComponentsInChildren<Renderer>(true)")
        renderer_active = body.index("renderer.gameObject.SetActive(true);")
        renderer_enable = body.index("renderer.enabled = true;")
        final_inactive = body.rindex("clone.SetActive(false);")
        reparent = body.index("clone.transform.SetParent(parent, false);")
        activate = body.index("clone.SetActive(true);")
        self.assertLess(staging_inactive, instantiate.start())
        self.assertLess(instantiate.end(), clone_inactive)
        self.assertLess(clone_inactive, mono_scan)
        self.assertLess(mono_scan, retained_check)
        self.assertLess(retained_check, count_check)
        self.assertLess(count_check, destroy_other)
        self.assertLess(destroy_other, verify_scan)
        self.assertLess(verify_scan, verify_exact)
        self.assertLess(verify_exact, retained_enable)
        self.assertLess(retained_enable, retained_active)
        self.assertTrue(
            all(retained_active < point < reparent for point in explicit_disable_points)
        )
        self.assertLess(max(explicit_disable_points), renderer_scan)
        self.assertLess(renderer_scan, renderer_active)
        self.assertLess(renderer_active, renderer_enable)
        self.assertLess(renderer_enable, final_inactive)
        self.assertLess(final_inactive, reparent)
        self.assertLess(reparent, activate)
        self.assertIn("catch", body)
        self.assertRegex(body, r"if\s*\(\s*clone\s*!=\s*null\s*\)\s*Object\.Destroy\s*\(\s*clone\s*\)")
        self.assertRegex(body, r"if\s*\(\s*staging\s*!=\s*null\s*\)\s*Object\.Destroy\s*\(\s*staging\s*\)")
        self.assertNotIn("CloneResidentVisual", source)

        resident = read(RESIDENT_UI)
        pane_clone = csharp_method_body(
            resident, r"public\s+GameObject\s+ClonePaneName\s*\([^)]*\)"
        )
        self.assertIn("typeof(PaneText)", pane_clone)
        image_clone = csharp_method_body(
            resident, r"GameObject\s+CloneResidentImage\s*\([^)]*\)"
        )
        self.assertIn("typeof(Image)", image_clone)

    def test_frame_state_harness_uses_portable_temp_parent(self):
        harness = inspect.getsource(
            self.test_pure_frame_state_executes_repeated_selection_boundaries_and_interruptions
        )
        self.assertIn("portable_temp_parent()", harness)
        self.assertNotIn('pathlib.Path("D:/Temp")', harness)
        selector = inspect.getsource(portable_temp_parent)
        self.assertIn('os.environ.get("RUNNER_TEMP")', selector)
        self.assertIn('os.name == "nt"', selector)
        self.assertIn('pathlib.Path("D:/Temp")', selector)
        self.assertGreaterEqual(selector.count("is_dir()"), 2)
        self.assertGreaterEqual(selector.count("os.access"), 2)
        self.assertIn("return None", selector)

    def test_resident_lookup_is_unique_exact_loaded_and_revision_cached(self):
        source = read(RESIDENT_UI)
        self.assertIn("scene.IsValid() || !scene.isLoaded", source)
        self.assertNotIn("EndsWith", source)
        self.assertIn("_imageIndex.TryGetValue(exactSourcePath", source)
        self.assertIn("matches.Count != 1", source)
        self.assertIn("duplicate", source)
        self.assertIn("_imageIndexBuilt", source)
        self.assertIn("_attemptedImageRoles", source)
        self.assertIn("_refreshAttempted", source)
        clone_body = csharp_method_body(source, r"GameObject\s+CloneResidentImage\s*\([^)]*\)")
        self.assertTrue(clone_body)
        self.assertNotIn("Resources.FindObjectsOfTypeAll<Image>()", clone_body)
        index_body = csharp_method_body(source, r"void\s+BuildImageIndex\s*\(\s*\)")
        self.assertEqual(1, index_body.count("Resources.FindObjectsOfTypeAll<Image>()"))
        forget_body = csharp_method_body(source, r"public\s+void\s+Forget\s*\(\s*\)")
        self.assertIn("_attemptedImageRoles.Clear();", forget_body)
        self.assertIn("_imageIndex.Clear();", forget_body)
        self.assertIn("_imageIndexBuilt = false;", forget_body)
        self.assertIn("_refreshAttempted = false;", forget_body)

    def test_frame_discovery_runs_once_per_ingame_source_transition(self):
        source = read(PORT_FRAME)
        tick = csharp_method_body(source, r"public\s+void\s+Tick\s*\(\s*float\s+dt\s*\)")
        self.assertTrue(tick)
        self.assertIn("DsGameData.InGame", tick)
        self.assertIn("inGame != _lastInGame", tick)
        self.assertIn("_buildAttempted", tick)
        self.assertNotIn("Time.frameCount % 30", tick)
        try_build = csharp_method_body(source, r"void\s+TryBuild\s*\(\s*\)")
        self.assertNotIn("Resources.FindObjectsOfTypeAll", try_build)
        invalidate = csharp_method_body(
            source, r"public\s+void\s+InvalidateResidentSources\s*\(\s*\)"
        )
        self.assertIn("_buildAttempted = false;", invalidate)
        self.assertIn("_resident.Forget();", invalidate)

    def test_pure_frame_state_executes_repeated_selection_boundaries_and_interruptions(self):
        self.assertTrue(PORT_FRAME_STATE.is_file(), "missing pure production frame state")
        with tempfile.TemporaryDirectory(dir=portable_temp_parent()) as directory:
            work = pathlib.Path(directory)
            (work / "FrameStateHost.csproj").write_text(
                """<Project Sdk=\"Microsoft.NET.Sdk\">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <Nullable>disable</Nullable><ImplicitUsings>disable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=\"Program.cs\" />
    <Compile Include=\"%s\" Link=\"DsPortFrameState.cs\" />
  </ItemGroup>
</Project>
""" % PORT_FRAME_STATE,
                encoding="utf-8",
            )
            (work / "Program.cs").write_text(
                """using System;
static class Program
{
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    static void Main()
    {
        var state = DsPortFrameState.Initial(5, 0);
        Need(DsPortFrameState.LabelAlpha(true) == 1f, "selected alpha");
        Need(DsPortFrameState.LabelAlpha(false) == 0.6f, "inactive alpha");
        Need(DsPortFrameState.ContainsHit(0.2f, 0.2f, 0.4f), "left boundary");
        Need(DsPortFrameState.ContainsHit(0.4f, 0.2f, 0.4f), "right boundary");
        Need(!DsPortFrameState.ContainsHit(0.199f, 0.2f, 0.4f), "outside boundary");

        state = DsPortFrameState.BeginSelection(state, 4);
        Need(state.Direction == 1 && state.SelectedIndex == 4, "forward selection");
        Need(DsPortFrameState.IsHostActive(state, 0), "first outgoing");
        Need(DsPortFrameState.IsHostActive(state, 4), "first incoming");
        Need(!DsPortFrameState.IsHostActive(state, 2), "unrelated host");

        state = DsPortFrameState.BeginSelection(state, 1);
        Need(state.Direction == -1 && state.SelectedIndex == 1, "interrupted reverse");
        Need(!DsPortFrameState.IsHostActive(state, 0), "stale outgoing removed");
        Need(DsPortFrameState.IsHostActive(state, 4), "current outgoing");
        Need(DsPortFrameState.IsHostActive(state, 1), "new incoming");

        state = DsPortFrameState.CompleteSelection(state);
        for (int i = 0; i < 5; i++)
            Need(DsPortFrameState.IsHostActive(state, i) == (i == 1), "settled host " + i);
    }
}
""",
                encoding="utf-8",
            )
            run = subprocess.run(
                ["dotnet", "run", "--project", str(work / "FrameStateHost.csproj"),
                 "-c", "Release", "--nologo", "-v", "quiet",
                 "-p:UseSharedCompilation=false"],
                cwd=work,
                text=True,
                capture_output=True,
            )
            self.assertEqual(0, run.returncode, run.stdout + run.stderr)

        frame = read(PORT_FRAME)
        for decision in (
            "DsPortFrameState.LabelAlpha", "DsPortFrameState.ContainsHit",
            "DsPortFrameState.BeginSelection", "DsPortFrameState.IsHostActive",
            "DsPortFrameState.CompleteSelection",
        ):
            self.assertIn(decision, frame)

    def test_frame_owns_masks_positions_page_cache_and_horizontal_slide_state(self):
        if not PORT_FRAME.is_file():
            self.skipTest("DsPortFrame.cs is introduced by Stage 2")
        source = read(PORT_FRAME)
        for role in ("ContentMask", "StatusAnchor", "ModsAnchor"):
            with self.subTest(role=role):
                self.assertIn(role, source)
        self.assertRegex(source, r"Dictionary\s*<\s*DsPageRole\s*,\s*RectTransform\s*>")
        self.assertIn("GetOrCreatePageHost", source)
        self.assertIn("BeginHorizontalSlide", source)
        self.assertIn("Mathf.Pow(1f - _slideT, 3f)", source)
        self.assertRegex(source, r"new\s+Vector2\s*\([^,]+,\s*0f\s*\)")

    def test_frame_geometry_comes_from_native_glyph_and_sprite_bounds(self):
        source = read(PORT_FRAME)
        resident = read(RESIDENT_UI)
        self.assertIn("ForceMeshUpdate(true)", source)
        self.assertRegex(source, r"Renderer\.bounds|renderer\.bounds")
        self.assertIn("LayoutNativeTabLabels", source)
        self.assertIn("AlignTabBaselines", source)
        self.assertIn("ScaleResidentAspect", source)
        self.assertIn("PositionSelectedFleursFromGlyphBounds", source)
        self.assertIn("UpdateDynamicInnerEdges", source)
        self.assertIn("InnerTop", source)
        self.assertIn("InnerBottom", source)
        self.assertIn("sprite.rect.size", resident)

    def test_renderer_composition_has_explicit_sorting_and_functional_covers(self):
        util = read(PORT_UTIL)
        frame = read(PORT_FRAME)
        layers = read(PORT_LAYERS)
        self.assertIn("NormalizeRenderers", util)
        self.assertRegex(util, r"sortingLayerID\s*=\s*0\s*;")
        self.assertRegex(util, r"sortingOrder\s*=\s*baseOrder")
        self.assertIn("DsRendererMaskCover", util)
        self.assertIn("MeshRenderer", util)
        self.assertIn("SetRect", util)
        self.assertIn("UpdateRendererMasks", frame)
        self.assertEqual(4, len(re.findall(r"_rendererMasks\[\d\]\.SetRect", frame)))
        self.assertIn("ConfigureCanvasOrder(Pages, PAGE_RENDER_ORDER)", layers)
        self.assertIn("ConfigureCanvasOrder(Frame, FRAME_RENDER_ORDER)", layers)
        self.assertIn("ConfigureCanvasOrder(HUD, HUD_RENDER_ORDER)", layers)
        self.assertIn("canvas.overrideSorting = true", layers)
        self.assertLess(layers.index("Pages.SetSiblingIndex(1)"),
                        layers.index("Frame.SetSiblingIndex(2)"))
        self.assertLess(layers.index("Frame.SetSiblingIndex(2)"),
                        layers.index("HUD.SetSiblingIndex(3)"))

    def test_interrupted_slide_normalizes_all_hosts_before_new_state(self):
        source = read(PORT_FRAME)
        begin = re.search(
            r"public\s+void\s+BeginHorizontalSlide\s*\([^)]*\)\s*"
            r"\{(?P<body>.*?)\n\s*\}", source, re.DOTALL
        )
        self.assertIsNotNone(begin)
        self.assertIn("NormalizePageHostsForInterruptedSlide(from);", begin.group("body"))
        normalize = re.search(
            r"void\s+NormalizePageHostsForInterruptedSlide\s*\([^)]*\)\s*"
            r"\{(?P<body>.*?)\n\s*\}", source, re.DOTALL
        )
        self.assertIsNotNone(normalize)
        body = normalize.group("body")
        self.assertIn("foreach (var pair in _pageHosts)", body)
        self.assertIn("pair.Value.anchoredPosition = Vector2.zero", body)
        self.assertRegex(body, r"SetActive\s*\(\s*pair\.Key\s*==\s*current\s*\)")
        tick = csharp_method_body(source, r"void\s+TickHorizontalSlide\s*\([^)]*\)")
        completion = tick.index("DsPortFrameState.CompleteSelection(_selectionState)")
        settle = tick.index("SetOnlySelectedPageActive(", completion)
        self.assertLess(completion, settle)

    def test_runtime_owns_frame_lifecycle_and_scene_invalidation(self):
        source = read(PORT_RUNTIME)
        self.assertRegex(source, r"\bDsPortFrame\s+_frame\s*;")
        self.assertRegex(source, r"_frame\s*=\s*new\s+DsPortFrame\s*\(")
        self.assertRegex(source, r"_frame\.Tick\s*\(")
        self.assertRegex(source, r"_frame\.InvalidateResidentSources\s*\(")
        self.assertRegex(source, r"_frame\.Dispose\s*\(")

    def test_production_port_sources_do_not_draw_authored_frame_substitutes(self):
        violations = []
        rejected = (
            "DsWidgets.Fleur", "DsTheme.White", "DsTheme.Disc",
            "AddComponent<Image>", "AddComponent<UnityEngine.UI.Image>",
            "DsShell", "DsScreens",
            "Inventory/Border/Inv_Border_", "Sprite.Create(",
        )
        sources = list(DUALSCREEN_SOURCES.glob("DsPort*.cs")) + [RESIDENT_UI]
        for path in sorted(set(sources)):
            source = read(path)
            for token in rejected:
                if token in source:
                    violations.append(f"{path.name}: {token}")
        self.assertEqual([], violations)


if __name__ == "__main__":
    unittest.main()
