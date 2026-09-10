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
    def test_corrective_authority_requires_bounded_reference_not_h5(self):
        spec = read(SPEC)
        plan = read(PLAN)
        self.assertIn("2026-09-10 corrective authority — visible HUD first", spec)
        self.assertIn("supersedes any stage ordering", spec)
        self.assertIn("one evidence bundle", spec)
        self.assertIn("exact-build capture", spec)
        self.assertIn("source-proven or runtime-observed", spec)
        self.assertIn("H4/H5/H6 are not prerequisites", plan)
        self.assertIn("V0–V5 is the active queue", plan)
        self.assertIn(PLAN.name, spec)

    def test_historical_stage_order_is_preserved_but_v0_v5_is_current(self):
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
        active = plan.split(
            "### 2026-09-10 Task99 recovery override — stop building around the missing HUD",
            1,
        )[1].split("### Task99: functional HUD and native companion pages", 1)[0]
        active_steps = [active.index(f"**V{number} —") for number in range(6)]
        self.assertEqual(sorted(active_steps), active_steps)
        self.assertIn("complete\n  Hollow Knight Mods, skins, topology and H5 closure are not prerequisites", active)
        self.assertIn("Do not advance to native pages while the live HUD gate is red", active)

    def test_historical_silksong_deferral_is_preserved_but_hud_override_is_current(self):
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
        current = matrix.split("## Task99 signed live result — 2026-09-10 corrective reset", 1)[1]
        self.assertIn("first ordinary-gameplay capture invalidates Task99 Batch A acceptance", current)
        self.assertIn("plan V0–V5", current)
        self.assertIn("H4/H5/H6 closure", current)
        self.assertIn("host-only result accepts a visible", matrix)
        historical = matrix.split(
            "## Historical 2026-09-08 execution override — superseded 2026-09-10",
            1,
        )[1]
        for owner in ("Task99 Batch A", "Task99 Batch B", "Task100", "Task101", "Task102"):
            self.assertIn(owner, historical)
        self.assertIn("does not prove Unity driver behavior or visual fit", historical)
        self.assertIn("does not apply to the V3–V4 integrated Android gate", historical)
        self.assertIn("Historical DELETE_AFTER rows are future dispositions", historical)
        self.assertIn("## 2026-09-08 execution addendum — Silksong host continuation", read(PLAN))

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

    def test_corrective_device_gate_allows_bounded_gameplay_with_safety_guards(self):
        plan = read(PLAN)
        spec = read(SPEC)
        self.assertIn("Capture both physical displays in ordinary gameplay", plan)
        self.assertIn("disposable test slot", plan)
        self.assertIn("thread/session-specific device lease", plan)
        self.assertIn("delete only the recorded test slot", plan)
        self.assertIn("Creating one disposable new-game save", spec)
        self.assertIn("remove only that test save", spec)
        self.assertIn("No assistant-driven movement, jump, attack", spec)
        self.assertIn("ordinary gameplay state", spec)

        # Retain the former synthetic-only gate text and traceability as historical
        # evidence, but do not let it override the dated corrective authority.
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
