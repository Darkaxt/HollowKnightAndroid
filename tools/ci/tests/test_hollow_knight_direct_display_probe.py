import json
import pathlib
import re
import unittest


REPO_ROOT = pathlib.Path(__file__).resolve().parents[3]
PROBE = (
    REPO_ROOT
    / "tools"
    / "hollow-knight-patches"
    / "src"
    / "DirectDisplayProbe.cs"
)
ENTRYPOINTS = REPO_ROOT / "tools" / "hollow-knight-patches" / "entrypoints.json"


def read_probe() -> str:
    return PROBE.read_text(encoding="utf-8") if PROBE.is_file() else ""


def method_body(source: str, signature_pattern: str) -> str:
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


def strip_csharp_comments(source: str) -> str:
    source = re.sub(r"/\*.*?\*/", "", source, flags=re.DOTALL)
    return re.sub(r"//[^\r\n]*", "", source)


class HollowKnightDirectDisplayProbeContractTest(unittest.TestCase):
    def test_probe_remains_diagnostic_only_and_production_owns_the_entrypoint(self):
        self.assertTrue(PROBE.is_file(), f"missing probe source: {PROBE}")
        entrypoints = json.loads(ENTRYPOINTS.read_text(encoding="utf-8"))["entryPoints"]
        self.assertNotIn(
            {
                "nameSpace": "HollowKnightPatches",
                "className": "DirectDisplayProbe",
                "methodName": "Bootstrap",
                "loadTypes": 0,
            },
            entrypoints,
            "the H1 diagnostic must not compete with the H2 production owner",
        )
        self.assertIn(
            {
                "nameSpace": "HollowKnightPatches",
                "className": "HkDirectDisplayAdapter",
                "methodName": "Bootstrap",
                "loadTypes": 0,
            },
            entrypoints,
        )
        self.assertIn(
            {
                "nameSpace": "HollowKnightPatches",
                "className": "InjectionProbe",
                "methodName": "Start",
                "loadTypes": 2,
            },
            entrypoints,
            "the existing injection probe must remain registered unchanged",
        )

    def test_bootstrap_fails_closed_before_creating_a_host_object(self):
        source = read_probe()
        self.assertIn("namespace HollowKnightPatches", source)
        self.assertRegex(
            source,
            r"\[RuntimeInitializeOnLoadMethod\(RuntimeInitializeLoadType\.AfterSceneLoad\)\]",
        )
        bootstrap = method_body(source, r"(?:public\s+)?static\s+void\s+Bootstrap\s*\(\s*\)")
        self.assertTrue(bootstrap, "missing static Bootstrap method")
        self.assertIn("HkDirectDisplayAdapter.IsProductionEnabled()", bootstrap)
        self.assertIn("if (!ShouldRun()) return;", bootstrap)
        self.assertLess(
            bootstrap.index("HkDirectDisplayAdapter.IsProductionEnabled()"),
            bootstrap.index("ShouldRun()"),
        )
        self.assertLess(bootstrap.index("ShouldRun()"), bootstrap.index("new GameObject("))

        should_run = method_body(source, r"(?:public\s+)?static\s+bool\s+ShouldRun\s*\(\s*\)")
        self.assertTrue(should_run, "missing opt-in reader")
        self.assertIn("Application.persistentDataPath", should_run)
        self.assertIn('"hollow_knight_direct_display_probe"', source)
        self.assertIn("File.Exists", should_run)
        self.assertRegex(should_run, r"if\s*\(\s*!File\.Exists\([^)]*\)\s*\)\s*return false\s*;")
        self.assertIn("return false;", should_run)
        executable = strip_csharp_comments(should_run)
        self.assertRegex(executable, r'enabled\s*!=\s*"1"')
        self.assertRegex(
            executable,
            r'string\.Equals\(\s*enabled\s*,\s*"true"\s*,'
            r"\s*StringComparison\.OrdinalIgnoreCase\s*\)",
        )
        self.assertNotRegex(
            strip_csharp_comments("// enabled=1 or enabled=true"),
            r"enabled\s*=\s*(?:1|true)",
            "comment-only opt-in markers must not satisfy this contract",
        )

    def test_probe_uses_only_shared_transport_with_pinned_hollow_knight_values(self):
        source = read_probe()
        for required in (
            "DirectDisplayHost",
            "DirectDisplayPresentation",
            "DirectDisplayTouch",
            "IDirectDisplayContent",
            "DISPLAY_INDEX = 1",
            "CONTENT_LAYER = 6",
            "OVERLAY_LAYER = 7",
            "FALLBACK_WIDTH = 1240",
            "FALLBACK_HEIGHT = 1080",
        ):
            with self.subTest(required=required):
                self.assertIn(required, source)
        self.assertRegex(
            source,
            r"new\s+DirectDisplayPresentation\s*\(\s*transform\s*,\s*"
            r"DISPLAY_INDEX\s*,\s*CONTENT_LAYER\s*,\s*OVERLAY_LAYER\s*,\s*"
            r"FALLBACK_WIDTH\s*,\s*FALLBACK_HEIGHT\s*,\s*ReadPositiveConfigInt\s*\)",
        )
        self.assertIn("DirectDisplayTouch.ConfigureTargetDisplay(DISPLAY_INDEX)", source)

    def test_content_is_transport_only_and_constructed_only_after_readiness(self):
        source = read_probe()
        bringup = method_body(source, r"IEnumerator\s+Bringup\s*\(\s*\)")
        self.assertTrue(bringup, "missing activation coroutine")
        settled = bringup.index("yield return presentation.Bringup();")
        guard = bringup.index("presentation.Ready", settled)
        construction = bringup.index("TryAttachDiagnosticContent(presentation, host)", guard)
        publish = bringup.index("host.SetPresentationReady(true", construction)
        self.assertLess(settled, guard)
        self.assertLess(guard, construction)
        self.assertLess(construction, publish)

        self.assertRegex(
            source,
            r"sealed\s+class\s+DiagnosticContent\s*:\s*IDirectDisplayContent",
        )
        for required in (
            "HOLLOW KNIGHT DIRECT DISPLAY",
            "TRANSPORT ONLY",
            "AddComponent<Image>()",
            "Resources.GetBuiltinResource<Font>",
            "void SetTransportActive(bool active)",
            "void OnPanelGeometry(float width, float height)",
            "void Dispose()",
        ):
            with self.subTest(required=required):
                self.assertIn(required, source)
        set_active = method_body(
            source, r"public\s+void\s+SetTransportActive\s*\(\s*bool\s+active\s*\)"
        )
        self.assertRegex(set_active, r"_root\.gameObject\.SetActive\(active\)")
        geometry = method_body(
            source,
            r"public\s+void\s+OnPanelGeometry\s*\(\s*float\s+width\s*,\s*float\s+height\s*\)",
        )
        self.assertIn("width", geometry)
        self.assertIn("height", geometry)
        dispose = method_body(source, r"public\s+void\s+Dispose\s*\(\s*\)")
        self.assertRegex(dispose, r"Object\.Destroy\(_ownedRoot\)")

    def test_lifecycle_handles_hotplug_pause_resume_and_late_coroutines(self):
        source = read_probe()
        for required in (
            "Display.onDisplaysUpdated += OnDisplaysUpdated",
            "Display.onDisplaysUpdated -= OnDisplaysUpdated",
            "void OnDisplaysUpdated()",
            "void OnApplicationPause(bool paused)",
            "SetDisplayPresent(false)",
            "MarkUnavailable()",
            "SetPaused(paused)",
            "SweepCameras()",
            "DirectDisplayTouch.InstallFence(gameObject)",
            "DirectDisplayTouch.RemoveFence()",
            "OnApplicationQuit()",
            "OnDestroy()",
            "void Shutdown()",
        ):
            with self.subTest(required=required):
                self.assertIn(required, source)

        bringup = method_body(source, r"IEnumerator\s+Bringup\s*\(\s*\)")
        post_yield = bringup[bringup.index("yield return presentation.Bringup();") :]
        self.assertIn("host.IsDisposed", post_yield)
        self.assertIn("ReferenceEquals(_host, host)", post_yield)
        self.assertIn("ReferenceEquals(_presentation, presentation)", post_yield)

        shutdown = method_body(source, r"void\s+Shutdown\s*\(\s*\)")
        self.assertRegex(shutdown, r"if\s*\(\s*_shutdown\s*\)\s*return\s*;")
        self.assertIn("_shutdown = true", shutdown)
        self.assertLess(
            shutdown.index("Display.onDisplaysUpdated -= OnDisplaysUpdated"),
            shutdown.index("_host.Dispose()"),
        )

    def test_display_loss_steps_are_ordered_independent_and_best_effort(self):
        source = read_probe()
        body = method_body(source, r"void\s+OnDisplaysUpdated\s*\(\s*\)")
        self.assertTrue(body)
        self.assertGreaterEqual(
            len(re.findall(r"\bTryStep\s*\(", body)),
            2,
            "display loss must isolate presence and presentation invalidation",
        )
        presence = body.index("SetDisplayPresent(false)")
        unavailable = body.index("MarkUnavailable()", presence)
        logged = body.index("LogFailures(", unavailable)
        self.assertLess(presence, unavailable)
        self.assertLess(unavailable, logged)

    def test_pause_display_loss_steps_and_pause_publication_are_best_effort(self):
        source = read_probe()
        body = method_body(source, r"void\s+OnApplicationPause\s*\(\s*bool\s+paused\s*\)")
        self.assertTrue(body)
        self.assertGreaterEqual(
            len(re.findall(r"\bTryStep\s*\(", body)),
            3,
            "presence, invalidation, and pause publication must be independent",
        )
        presence = body.index("SetDisplayPresent(false)")
        unavailable = body.index("MarkUnavailable()", presence)
        paused = body.index("SetPaused(paused)", unavailable)
        logged = body.index("LogFailures(", paused)
        self.assertLess(presence, unavailable)
        self.assertLess(unavailable, paused)
        self.assertLess(paused, logged)

    def test_best_effort_helper_accumulates_failures_before_logging(self):
        source = read_probe()
        try_step = method_body(
            source,
            r"static\s+void\s+TryStep\s*\(\s*Action\s+step\s*,\s*"
            r"List\s*<\s*Exception\s*>\s+failures\s*\)",
        )
        self.assertIn("try", try_step)
        self.assertIn("catch (Exception e)", try_step)
        self.assertIn("failures.Add(e)", try_step)

        log_failures = method_body(
            source,
            r"static\s+void\s+LogFailures\s*\(\s*string\s+context\s*,\s*"
            r"List\s*<\s*Exception\s*>\s+failures\s*\)",
        )
        self.assertIn("failures.Count", log_failures)
        self.assertIn("new AggregateException(context, failures)", log_failures)
        self.assertIn("Debug.LogError", log_failures)

    def test_diagnostic_attachment_transfers_ownership_transactionally(self):
        source = read_probe()
        bringup = method_body(source, r"IEnumerator\s+Bringup\s*\(\s*\)")
        self.assertIn("TryAttachDiagnosticContent(presentation, host)", bringup)
        self.assertNotIn("_content = new DiagnosticContent", bringup)

        attach = method_body(
            source,
            r"bool\s+TryAttachDiagnosticContent\s*\(\s*"
            r"DirectDisplayPresentation\s+presentation\s*,\s*"
            r"DirectDisplayHost\s+host\s*\)",
        )
        self.assertIn("DiagnosticContent candidate = null", attach)
        constructed = attach.index("candidate = new DiagnosticContent(")
        attached = attach.index("host.AttachContent(candidate)", constructed)
        published = attach.index("_content = candidate", attached)
        self.assertLess(constructed, attached)
        self.assertLess(attached, published)
        failure = attach.index("catch (Exception", published)
        disposed = attach.index("candidate.Dispose()", failure)
        rearmed = attach.index("host.SetPresentationReady(false)", failure)
        logged = attach.index("LogFailures(", max(disposed, rearmed))
        self.assertLess(failure, disposed)
        self.assertLess(failure, rearmed)
        self.assertLess(max(disposed, rearmed), logged)

        constructor = method_body(
            source,
            r"public\s+DiagnosticContent\s*\(\s*RectTransform\s+parent\s*,\s*"
            r"int\s+layer\s*,\s*float\s+width\s*,\s*float\s+height\s*\)",
        )
        self.assertIn("try", constructor)
        constructor_failure = constructor.index("catch (Exception constructionFailure)")
        self.assertIn("TryStep(Dispose, failures)", constructor[constructor_failure:])

    def test_shutdown_final_cleanup_is_independent_and_does_not_use_finally(self):
        source = read_probe()
        shutdown = method_body(source, r"void\s+Shutdown\s*\(\s*\)")
        self.assertNotIn("finally", shutdown)
        remove = shutdown.index(
            "TryStep(() => DirectDisplayTouch.RemoveFence(), failures)"
        )
        release = shutdown.index("TryStep(ReleasePresentation, failures)", remove)
        logged = shutdown.index("LogFailures(", release)
        self.assertLess(remove, release)
        self.assertLess(release, logged)

    def test_probe_has_no_native_bridge_gameplay_or_silksong_dependencies(self):
        source = read_probe()
        forbidden = (
            "AndroidJavaObject",
            "EGL",
            "RenderTexture",
            "AsyncGPUReadback",
            "Silksong",
            "DsPresentation",
            "DsTouch",
            "DsShell",
            "HeroController",
            "GameManager",
            "GameCameras",
            "PlayerData",
        )
        for token in forbidden:
            with self.subTest(token=token):
                self.assertNotIn(token, source)


if __name__ == "__main__":
    unittest.main()
