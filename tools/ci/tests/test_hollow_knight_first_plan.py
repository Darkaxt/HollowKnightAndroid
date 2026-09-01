import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[3]
SPEC = ROOT / "docs/superpowers/specs/2026-08-31-dual-souls-ui-port-design.md"
UNIFIED_SPEC = ROOT / "docs/superpowers/specs/2026-08-29-unified-hollow-knight-platform-design.md"
PLAN = ROOT / "docs/superpowers/plans/2026-09-01-hollow-knight-first-dual-souls.md"
OLD_PLAN = ROOT / "docs/superpowers/plans/2026-08-31-dual-souls-ui-port.md"
MATRIX = ROOT / "docs/verification/dual-souls-ui-port-matrix.md"
TRACEABILITY = ROOT / "docs/verification/design-traceability.md"
README = ROOT / "README.md"


def read(path):
    return path.read_text(encoding="utf-8")


class HollowKnightFirstPlanTests(unittest.TestCase):
    def test_authority_requires_the_hollow_knight_reference_before_silksong(self):
        spec = read(SPEC)
        self.assertIn("DSUI-00 — Establish the executable Hollow Knight reference first", spec)
        self.assertIn("No further Silksong composition implementation may advance", spec)
        for required in ("HUD", "pages", "Mods persistence", "skin scanning/application/rotation"):
            self.assertIn(required, spec)
        self.assertIn(PLAN.name, spec)

    def test_execution_order_is_reference_then_extraction_then_silksong(self):
        plan = read(PLAN)
        stages = [
            "## Stage H1:",
            "## Stage H2:",
            "## Stage H3:",
            "## Stage H4:",
            "## Stage H5:",
            "## Stage H6:",
            "## Stage S1:",
            "## Stage S2:",
            "## Stage F1:",
        ]
        offsets = [plan.index(stage) for stage in stages]
        self.assertEqual(sorted(offsets), offsets)
        self.assertLess(plan.index("complete Hollow Knight Dual Souls companion"),
                        plan.index("Extract shared contracts"))
        self.assertLess(plan.index("Extract shared contracts"),
                        plan.index("Resume the Silksong resident-object port"))

    def test_silksong_work_is_preserved_but_deferred_behind_h5_h6(self):
        old_plan = read(OLD_PLAN)
        matrix = read(MATRIX)
        traceability = read(TRACEABILITY)
        self.assertIn("PARKED SUCCESSOR PHASE", old_plan)
        self.assertIn("DEFERRED / AUDIT-COMPLETE", matrix)
        self.assertIn("Hollow Knight H5/H6", matrix)
        self.assertIn("| Goal 11: faithful Dual Souls bottom-screen port | H2–H6, S1–S2 | IN-PROGRESS |", traceability)
        self.assertIn("Silksong adaptation", traceability)
        self.assertIn("stays parked", traceability)
        self.assertIn("| Dual Souls composition port | `IN-PROGRESS` |", matrix)

    def test_every_public_authority_links_the_new_plan(self):
        for document in (UNIFIED_SPEC, OLD_PLAN, MATRIX, README):
            with self.subTest(document=document.name):
                self.assertIn(PLAN.name, read(document))

    def test_plan_requires_stage_by_stage_specification_reconciliation(self):
        plan = read(PLAN)
        self.assertIn("After every stage, re-read the specifications", plan)
        self.assertIn("blockers = 0", plan)
        self.assertIn("tracked_deferrals = 0", plan)
        self.assertIn("update the README", plan)

    def test_every_device_pass_forbids_gameplay_and_save_state_hosts(self):
        plan = read(PLAN)
        spec = read(SPEC)
        self.assertIn("Every device cycle from H2 through release", plan)
        self.assertIn("An in-game room or save is not a permitted validation host", plan)
        self.assertIn("already-loaded gameplay", plan)
        self.assertIn("device passes neither read nor write game saves", plan)
        self.assertIn("device switching does not open or mutate a user save", plan)
        self.assertIn("Every remaining reference-device pass", spec)
        self.assertIn("enter or reuse an in-game room", spec)
        self.assertIn("save reads/writes prevented", spec)
        for document in (plan, spec):
            self.assertNotIn("minimum unmoving static scene needed", document)
            self.assertNotIn("already-present static state", document)

        traceability = read(TRACEABILITY)
        gate4 = next(
            line for line in traceability.splitlines()
            if line.startswith("| Device gate 4:")
        )
        gate5 = next(
            line for line in traceability.splitlines()
            if line.startswith("| Device gate 5:")
        )
        self.assertNotIn("save/reload/resume", gate4)
        self.assertIn("synthetic save fixtures", gate4)
        self.assertNotIn("Stable gameplay", gate5)
        self.assertNotIn("Boss, effects-heavy", gate5)
        self.assertIn("controlled injected render states", gate5)


if __name__ == "__main__":
    unittest.main()
