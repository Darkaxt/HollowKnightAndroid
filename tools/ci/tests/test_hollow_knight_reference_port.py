import json
import pathlib
import re
import unittest


REPO_ROOT = pathlib.Path(__file__).resolve().parents[3]
PATCH_ROOT = REPO_ROOT / "tools" / "hollow-knight-patches"
SOURCE_ROOT = PATCH_ROOT / "src"
REFERENCE_ROOT = SOURCE_ROOT / "dualsouls"
ADAPTER = REFERENCE_ROOT / "HkDirectDisplayAdapter.cs"
ENTRYPOINTS = PATCH_ROOT / "entrypoints.json"
PROJECT = PATCH_ROOT / "HollowKnightPatches.csproj"
PROVENANCE = REPO_ROOT / "docs" / "verification" / "hollow-knight-direct-display.md"

PINNED_REPOSITORY = "igawa6/dualsouls"
PINNED_COMMIT = "5c22451435b772acde0c7e6456f9019bc1baef73"
PINNED_MODULE_HASHES = {
    "HKDualScreen.cs":
        "7c9f11b59d768d7dde9506f0b546dee03f9704dbc4329ad20cfb8bd3f15b8d3d",
    "HKDualScreen.Util.cs":
        "20a021e2970ac0ff8852188506accba3277ce475e8f0a314a3a85e69fbc8f65a",
    "HKDualScreen.Bottom.Layering.cs":
        "a7c4a720c9756026de363a1c3744ca29e2a96455286cc779bcbe20ae5c1692ea",
    "HKDualScreen.Bottom.Frame.cs":
        "26f4f4650a76787d8e8286d6f375abebdd469c4ac1366cd19de88276d7cc5a7a",
    "HKDualScreen.Bottom.Hud.cs":
        "f8aeaadf76ee6e53380b9aeefa24af14ed4e6def9754e795ac5842d5145b33d7",
    "HKDualScreen.Bottom.Inventory.cs":
        "32f0b52f21fcbd8c89c10acd554853fe1dde41b01586f36ddc93a7b42d6ce7ef",
    "HKDualScreen.Bottom.Charms.cs":
        "66bb323f25130af869021d11ef0eb9b229fbbc8a09bc9fb9e400a371a9f2c67c",
    "HKDualScreen.Bottom.Map.cs":
        "5099609d8af787e6f421a685334d06285cc8210638498d3e6fb055befd3fe2bb",
    "HKDualScreen.Bottom.Select.cs":
        "9394e124f96072b7d73ea2f8b5eaf2cbd3ecb51452d2f364ce918d21cb17f968",
}

MODULE_CONTRACTS = {
    "HKDualScreen.cs": (
        "partial class HKDualScreen",
        "void Tick()",
        "void RelayerHud(",
        "void MainGameHooks(",
    ),
    "HKDualScreen.Util.cs": (
        "partial class HKDualScreen",
        "void RouteToLayer(",
        "void RestoreRoutedLayers()",
    ),
    "HKDualScreen.Bottom.Layering.cs": (
        "partial class HKDualScreen",
        "bool ApplyDualScreenToggle()",
        "void SyncBottomFade()",
    ),
    "HKDualScreen.Bottom.Frame.cs": (
        "void BuildFrame()",
        "void UpdateCompanion(",
        "void BuildCompanionTab(",
        "void TeardownCompanion()",
    ),
    "HKDualScreen.Bottom.Hud.cs": (
        "void FrameHudCams(",
        "void CenterTutorial()",
        "void CenterDialogue()",
        "void RestoreDialogueShape()",
    ),
    "HKDualScreen.Bottom.Inventory.cs": (
        "void FinalizeInvPane(",
        "void PopulateInvDetail(",
        "void RefreshInvCounters(",
    ),
    "HKDualScreen.Bottom.Charms.cs": (
        "void CharmsTick()",
        "void PopulateCharmDetail(",
        "LayoutCharmsRedesign(",
    ),
    "HKDualScreen.Bottom.Map.cs": (
        "void MapTick()",
        "void MapFrameTick(",
        "void BuildMapClone(",
    ),
    "HKDualScreen.Bottom.Select.cs": (
        "void PollTouch()",
        "void PositionSelection(",
        "void ReassertControlPrompt()",
    ),
}


def read(path: pathlib.Path) -> str:
    return path.read_text(encoding="utf-8") if path.is_file() else ""


def strip_csharp_comments(source: str) -> str:
    source = re.sub(r"/\*.*?\*/", "", source, flags=re.DOTALL)
    return re.sub(r"//[^\r\n]*", "", source)


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


class HollowKnightReferencePortContractTest(unittest.TestCase):
    def test_every_pinned_dual_souls_reference_module_is_present(self):
        self.assertEqual(
            set(PINNED_MODULE_HASHES),
            set(MODULE_CONTRACTS),
            "the module contract must cover the complete pinned plan list",
        )
        for filename in PINNED_MODULE_HASHES:
            path = REFERENCE_ROOT / filename
            with self.subTest(module=filename):
                self.assertTrue(path.is_file(), f"missing pinned module: {filename}")

    def test_reference_provenance_records_exact_commit_and_source_hashes(self):
        provenance = read(PROVENANCE)
        self.assertIn(PINNED_REPOSITORY, provenance)
        self.assertIn(PINNED_COMMIT, provenance)
        for filename, sha256 in PINNED_MODULE_HASHES.items():
            with self.subTest(module=filename):
                self.assertRegex(
                    provenance,
                    rf"(?is){re.escape(filename)}.{{0,160}}{sha256}",
                    f"missing pinned SHA-256 provenance for {filename}",
                )

    def test_reference_modules_retain_their_concrete_responsibilities(self):
        for filename, required_tokens in MODULE_CONTRACTS.items():
            source = strip_csharp_comments(read(REFERENCE_ROOT / filename))
            for token in required_tokens:
                with self.subTest(module=filename, token=token):
                    self.assertIn(token, source)

    def test_adapter_is_concrete_shared_transport_content_not_authored_ui(self):
        source = strip_csharp_comments(read(ADAPTER))
        self.assertTrue(source, "missing HkDirectDisplayAdapter.cs")
        self.assertRegex(source, r"\bclass\s+HkDirectDisplayAdapter\b")
        self.assertNotRegex(source, r"\babstract\s+class\s+HkDirectDisplayAdapter\b")
        for required in (
            "DirectDisplayHost",
            "DirectDisplayPresentation",
            "DirectDisplayTouch",
            "IDirectDisplayContent",
            "HKDualScreen",
            "SetTransportActive(bool active)",
            "OnPanelGeometry(float width, float height)",
        ):
            with self.subTest(required=required):
                self.assertIn(required, source)

        for substitute in (
            "DiagnosticContent",
            "HOLLOW KNIGHT DIRECT DISPLAY",
            "AddComponent<Image>",
            "AddComponent<Text>",
            "Sprite.Create(",
            "new Texture2D(",
        ):
            with self.subTest(substitute=substitute):
                self.assertNotIn(
                    substitute,
                    source,
                    "the adapter must wire the reference rather than author replacement UI",
                )

    def test_compiled_hollow_knight_sources_exclude_the_old_display_bridge(self):
        project = read(PROJECT)
        self.assertIn('<Compile Include="src/**/*.cs" />', project)
        compiled_sources = sorted(SOURCE_ROOT.rglob("*.cs"))
        compiled_names = {path.name for path in compiled_sources}
        self.assertTrue(set(PINNED_MODULE_HASHES).issubset(compiled_names))
        self.assertIn("HkDirectDisplayAdapter.cs", compiled_names)
        self.assertEqual([], list(SOURCE_ROOT.rglob("*.java")))

        forbidden = {
            "Android HKAux class": r"\bAndroidJavaClass\s+aux\b",
            "HKAux package": r"com\.radit\.hkaux\.HKAux",
            "legacy bridge bootstrap": r"\bvoid\s+StartAux\s*\(",
            "native blitter import": r"DllImport\s*\(\s*\"hkgpu\"",
            "native render event": r"\bhkGetRenderEventFunc\b",
            "native texture push": r"\bhkSet(?:Bg)?Texture\b",
            "EGL render event": r"\bGL\.IssuePluginEvent\b",
            "Java touch polling": r"\baux\.CallStatic(?:<[^>]+>)?\s*\(\s*\"get(?:Touch|Tap|CleanTap|T[01])",
            "Android presentation visibility": r"\baux\.CallStatic\s*\(\s*\"setShown\"",
        }
        violations = []
        for path in compiled_sources:
            executable = strip_csharp_comments(read(path))
            for label, pattern in forbidden.items():
                if re.search(pattern, executable):
                    violations.append(f"{path.relative_to(REPO_ROOT)}: {label}")
        self.assertEqual([], violations)

    def test_live_hud_is_routed_in_place_reasserted_and_restored(self):
        source = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.cs"))
        relayer = method_body(source, r"void\s+RelayerHud\s*\([^)]*\)")
        self.assertTrue(relayer, "missing RelayerHud")
        self.assertIn("gc.hudCanvas.transform", relayer)
        self.assertRegex(relayer, r"hudRoot\s*=\s*hudRoot\.parent\.parent")
        self.assertIn("SetLayerRecursive(hudRoot, wantLayer)", relayer)
        self.assertRegex(relayer, r"Time\.frameCount\s*%\s*10")
        self.assertNotRegex(relayer, r"\b(?:Instantiate|Clone)\s*\(")
        self.assertNotIn("new GameObject", relayer)

        tick = method_body(source, r"void\s+Tick\s*\(\s*\)")
        self.assertRegex(
            " ".join(tick.split()),
            r"overlay\s*=\s*paused\s*\|\|\s*invOpen\s*\|\|\s*!dsOn",
        )
        self.assertIn("RelayerHud(gc, overlay || dsOff)", tick)
        self.assertIn("RestoreRoutedLayers()", tick)
        self.assertIn("RestoreNameCard()", tick)

    def test_inactive_transport_cannot_run_bottom_screen_routing_hooks(self):
        source = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.cs"))
        tick = method_body(source, r"void\s+Tick\s*\(\s*\)")
        apply_toggle = tick.index("bool dsOn = ApplyDualScreenToggle()")
        inactive_gate = tick.index("if (!dsOn) return;", apply_toggle)
        main_hooks = tick.index("MainGameHooks(", inactive_gate)
        self.assertLess(apply_toggle, inactive_gate)
        self.assertLess(inactive_gate, main_hooks)

    def test_h2_keeps_slot_aware_reference_controller_buttons(self):
        hooks = strip_csharp_comments(read(REFERENCE_ROOT / "HkStageHooks.cs"))
        self.assertIn("Input.GetJoystickNames()", hooks)
        self.assertRegex(hooks, r"\(slot\s*-\s*1\)\s*\*\s*20")
        self.assertRegex(hooks, r"JoyBtn\s*\(\s*int\s+index\s*\)")
        self.assertNotRegex(
            method_body(hooks, r"internal\s+static\s+KeyCode\s+JoyBtn\s*\([^)]*\)"),
            r"Joystick1Button0\s*\+\s*index",
        )

    def test_h2_only_queries_broken_flags_that_exist_in_hollow_knight_1_5_12620(self):
        util = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Util.cs")
        )
        broken = method_body(
            util,
            r"bool\s+CharmBroken\s*\(\s*PlayerData\s+pd\s*,\s*int\s+id\s*\)",
        )
        self.assertTrue(broken, "missing CharmBroken compatibility guard")
        self.assertRegex(broken, r"id\s*>=\s*23")
        self.assertRegex(broken, r"id\s*<=\s*25")
        self.assertIn("pd.GetBool(K_BROKEN[id])", broken)

    def test_partial_layout_json_overlays_defaults_instead_of_zeroing_them(self):
        main = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.cs")
        )
        load = method_body(main, r"void\s+LoadConfig\s*\(\s*bool\s+force\s*\)")
        self.assertTrue(load, "missing LoadConfig")
        self.assertRegex(load, r"parsed\s*=\s*new\s+HKLayout\s*\(\s*\)")
        self.assertIn("JsonUtility.FromJsonOverwrite(txt, parsed)", load)
        self.assertNotIn("JsonUtility.FromJson<HKLayout>(txt)", load)

    def test_backdrop_preserves_reference_blur_and_measured_aspect(self):
        main = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.cs"))
        adapter = strip_csharp_comments(read(ADAPTER))
        sync = method_body(main, r"void\s+SyncBgCapture\s*\([^)]*\)")
        self.assertIn("bgCaptureCam.CopyFrom(m)", sync)
        self.assertIn("bgCaptureCam.aspect = (float)BOTTOM_W / BOTTOM_H", sync)
        self.assertLess(
            sync.index("bgCaptureCam.CopyFrom(m)"),
            sync.index("bgCaptureCam.aspect = (float)BOTTOM_W / BOTTOM_H"),
        )
        self.assertIn("bgDimmer.BlurFactor = cfg.bgBlur", sync)
        self.assertIn("RenderTexture.GetTemporary", adapter)
        self.assertIn("RenderTexture.ReleaseTemporary", adapter)

    def test_backdrop_uses_a_tint_capable_shader_for_reference_dimming(self):
        adapter = strip_csharp_comments(read(ADAPTER))
        render = method_body(
            adapter,
            r"void\s+OnRenderImage\s*\(\s*RenderTexture\s+source\s*,\s*RenderTexture\s+destination\s*\)",
        )
        self.assertIn('Shader.Find("Sprites/Default")', render)
        self.assertIn('HasProperty("_Color")', render)
        self.assertNotIn('Shader.Find("Unlit/Texture")', render)

    def test_frame_tab_clones_reenable_the_retained_tmp_visual(self):
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        tabs = method_body(frame, r"void\s+BuildTabRow\s*\([^)]*\)")
        self.assertIn("tmpBehaviour.enabled = true", tabs)
        first_activation = tabs.index("go.SetActive(true)")
        text_assignment = tabs.index('GetProperty("text")')
        mesh_update = tabs.index("ForceMeshUpdate")
        self.assertLess(
            tabs.index("tmpBehaviour.enabled = true"),
            first_activation,
        )
        self.assertLess(first_activation, text_assignment)
        self.assertLess(text_assignment, mesh_update)

    def test_pane_clones_are_sanitized_while_inactive_before_activation(self):
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        pane = method_body(frame, r"GameObject\s+BuildPaneClone\s*\([^)]*\)")
        staging_off = pane.index("staging.SetActive(false)")
        instantiate = pane.index("Instantiate(srcT.gameObject, staging.transform)")
        strip_tween = pane.index("DestroyImmediate(tween)")
        reparent = pane.index("pane.transform.SetParent(compRoot, false)")
        activate = pane.index("pane.SetActive(true)")
        self.assertLess(staging_off, instantiate)
        self.assertLess(instantiate, strip_tween)
        self.assertLess(strip_tween, reparent)
        self.assertLess(reparent, activate)

    def test_final_teardown_retries_restore_before_discarding_owner(self):
        direct = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.DirectDisplay.cs")
        )
        shutdown = method_body(
            direct,
            r"internal\s+void\s+ShutdownDirectDisplayAndRestore\s*\(\s*\)",
        )
        self.assertIn("directDisplayFinalTeardownPending = true", shutdown)
        self.assertIn("TryDirectStep(RestoreReferenceRouting", shutdown)
        self.assertIn("CompleteDirectDisplayTeardown()", shutdown)
        self.assertLess(
            shutdown.index("TryDirectStep(RestoreReferenceRouting"),
            shutdown.index("CompleteDirectDisplayTeardown()"),
        )
        retry = method_body(
            direct,
            r"void\s+RetryPendingDirectDisplayRestore\s*\(\s*\)",
        )
        self.assertIn("RestoreReferenceRouting()", retry)
        self.assertIn("CompleteDirectDisplayTeardown()", retry)
        self.assertIn("transport.OnReferenceRestoreCompleted()", retry)
        main = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.cs"))
        self.assertIn("if (directDisplayRestorePending)", main)
        self.assertIn("RetryPendingDirectDisplayRestore()", main)
        adapter = strip_csharp_comments(read(ADAPTER))
        recovered = method_body(
            adapter,
            r"internal\s+void\s+OnReferenceRestoreCompleted\s*\(\s*\)",
        )
        self.assertIn("AcknowledgeContentInactiveAndReconcile()", recovered)

    def test_orchestrator_wires_frame_pages_selection_overlays_fade_and_lifecycle(self):
        main = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.cs"))
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        tick = method_body(main, r"void\s+Tick\s*\(\s*\)")
        update = method_body(frame, r"void\s+UpdateCompanion\s*\([^)]*\)")
        for call in (
            "RelayerHud(",
            "MainGameHooks(",
            "FrameHudCams(",
            "SyncBottomFade()",
            "UpdateCompanion(",
            "PollTouch()",
            "TeardownCompanion()",
        ):
            with self.subTest(orchestrator_call=call):
                self.assertIn(call, tick)
        for call in (
            "BuildCompanionTab(",
            "MapTick()",
            "MapFrameTick(",
            "MapPinchTick()",
            "PaneSettleTick(",
            "CharmsTick()",
            "RefreshInvCounters(",
            "BuildFrame()",
            "PositionFrame()",
            "ReassertControlPrompt()",
        ):
            with self.subTest(companion_call=call):
                self.assertIn(call, update)

    def test_adapter_routes_geometry_visibility_touch_and_teardown_to_reference(self):
        adapter = strip_csharp_comments(read(ADAPTER))
        active = method_body(
            adapter,
            r"public\s+void\s+SetTransportActive\s*\(\s*bool\s+active\s*\)",
        )
        geometry = method_body(
            adapter,
            r"public\s+void\s+OnPanelGeometry\s*\(\s*float\s+width\s*,\s*float\s+height\s*\)",
        )
        dispose = method_body(adapter, r"public\s+void\s+Dispose\s*\(\s*\)")
        touch_fence = method_body(
            adapter,
            r"void\s+SetTouchFenceActive\s*\(\s*bool\s+active\s*\)",
        )
        self.assertRegex(active, r"HKDualScreen|_reference|_dualSouls")
        self.assertIn("active", active)
        self.assertIn("width", geometry)
        self.assertIn("height", geometry)
        self.assertRegex(adapter, r"DirectDisplayTouch\.(?:CollectTargetDisplay|IsTargetDisplay)")
        self.assertIn("_gestures.Cancel()", touch_fence)
        self.assertLess(
            touch_fence.index("_gestures.Cancel()"),
            touch_fence.index("DirectDisplayTouch.RemoveFence()"),
        )
        self.assertRegex(dispose, r"HKDualScreen|_reference|_dualSouls")
        self.assertRegex(dispose, r"Dispose|Shutdown|Teardown|Restore")

    def test_h2_adapter_bootstrap_is_registered_as_an_entrypoint(self):
        entrypoints = json.loads(read(ENTRYPOINTS))["entryPoints"]
        matches = [
            entry
            for entry in entrypoints
            if entry.get("className") == "HkDirectDisplayAdapter"
            and entry.get("methodName") == "Bootstrap"
            and entry.get("loadTypes") == 0
        ]
        self.assertEqual(1, len(matches), "missing unique H2 adapter Bootstrap entrypoint")


if __name__ == "__main__":
    unittest.main()
