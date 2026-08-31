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


if __name__ == "__main__":
    unittest.main()
