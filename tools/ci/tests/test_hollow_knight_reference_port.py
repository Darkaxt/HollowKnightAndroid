import json
import pathlib
import re
import unittest


REPO_ROOT = pathlib.Path(__file__).resolve().parents[3]
PATCH_ROOT = REPO_ROOT / "tools" / "hollow-knight-patches"
SOURCE_ROOT = PATCH_ROOT / "src"
REFERENCE_ROOT = SOURCE_ROOT / "dualsouls"
MODS_ROOT = SOURCE_ROOT / "mods"
ADAPTER = REFERENCE_ROOT / "HkDirectDisplayAdapter.cs"
MODS_PRESENTER = REFERENCE_ROOT / "HollowKnightModsPresenter.cs"
MODS_RUNTIME = MODS_ROOT / "HollowKnightModsRuntime.cs"
FLASH_POLICY = MODS_ROOT / "HollowKnightLifebloodFlashPolicy.cs"
STAGE_HOOKS = REFERENCE_ROOT / "HkStageHooks.cs"
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
        "void ScanTutorials(",
        "void RouteHealParticles(",
        "void RouteDreamLore(",
        "void RouteLoreDialogue(",
        "void SyncBgCapture(",
        "void Tick()",
        "void RelayerHud(",
        "void MainGameHooks(",
        "void CenterAttribution(",
        "void RestoreNameCard()",
    ),
    "HKDualScreen.Util.cs": (
        "partial class HKDualScreen",
        "void RouteToLayer(",
        "void RestoreRoutedLayers()",
    ),
    "HKDualScreen.Bottom.Layering.cs": (
        "partial class HKDualScreen",
        "void SetupBottomCameras()",
        "void StripPrivateLayers()",
        "bool ApplyDualScreenToggle()",
        "bool CompanionVisible(",
        "bool CreditShowing()",
        "void SyncBottomFade()",
        "void PushToBottom()",
    ),
    "HKDualScreen.Bottom.Frame.cs": (
        "void BuildFrame()",
        "void BuildTabRow(",
        "void PositionFrame()",
        "void TabSlideTick()",
        "void PrewarmTick()",
        "void UpdateCompanion(",
        "void BuildCompanionTab(",
        "GameObject BuildPaneClone(",
        "void TeardownCompanion()",
    ),
    "HKDualScreen.Bottom.Hud.cs": (
        "void BuildAreaName(",
        "void BuildStats(",
        "void BuildEquipCharmRow()",
        "void UpdateEquipCharmRow(",
        "void UpdateNotchRow(",
        "void FrameHudCams(",
        "void CenterTutorial()",
        "void EnsureNameClone()",
        "void CenterDialogue()",
        "void RestoreDialogueShape()",
    ),
    "HKDualScreen.Bottom.Inventory.cs": (
        "void PopulateSpellDetail(",
        "void PopulateEquipDetail(",
        "void PopulateGodfinderDetail(",
        "void PopulateGeoDetail(",
        "void PaneSettleTick(",
        "void FinalizeInvPane(",
        "void PopulateInvDetail(",
        "void RefreshInvCounters(",
        "void ReassertEquipment(",
    ),
    "HKDualScreen.Bottom.Charms.cs": (
        "void CharmsTick()",
        "void CharmsPaneInit(",
        "void PopulateCharmDetail(",
        "LayoutCharmsRedesign(",
    ),
    "HKDualScreen.Bottom.Map.cs": (
        "void SetupQuickMap(",
        "void GateMarkers(",
        "void MapTick()",
        "void MapFrameTick(",
        "void MirrorRoomState(",
        "void BuildMapClone(",
    ),
    "HKDualScreen.Bottom.Select.cs": (
        "void MapPinchTick()",
        "void PollTouch()",
        "void PollItemTap(",
        "void RefreshSelectedDetail(",
        "void PositionSelection(",
        "void ReassertControlPrompt()",
        "void PopulateControlPrompt(",
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

    def test_h3_mods_presenter_owns_the_h2_view_boundary(self):
        presenter = strip_csharp_comments(read(MODS_PRESENTER))
        hooks = strip_csharp_comments(read(REFERENCE_ROOT / "HkStageHooks.cs"))
        self.assertTrue(presenter, "missing HollowKnightModsPresenter.cs")
        self.assertRegex(presenter, r"public\s+partial\s+class\s+HKDualScreen\b")

        required_fields = (
            "tweaksOpen", "tweaksRoot", "tweakRows", "gearT", "gearSR",
            "gearTex", "hudGearAnchor", "hudGearH", "hudGearOk", "hudFpsB",
        )
        for field in required_fields:
            with self.subTest(presenter_field=field):
                self.assertRegex(presenter, rf"\b{field}\b")
                self.assertNotRegex(hooks, rf"\b{field}\b")

        required_methods = (
            "TweaksPaneTick", "PositionGear", "GearTapN", "ToggleTweaksPane",
            "CloseTweaksPane", "TeardownModsPresenter",
        )
        for method in required_methods:
            with self.subTest(presenter_method=method):
                self.assertRegex(presenter, rf"\b{method}\s*\(")
                self.assertNotRegex(hooks, rf"\b{method}\s*\(")

        self.assertNotRegex(hooks, r"\bpartial\s+class\s+HKDualScreen\b")

    def test_h3_mods_presenter_borrows_the_process_owned_session_and_menu(self):
        presenter = strip_csharp_comments(read(MODS_PRESENTER))
        self.assertIn("HollowKnightModsRuntime.Current", presenter)
        self.assertRegex(presenter, r"\.Session\b")
        self.assertRegex(presenter, r"\.Menu\b")
        self.assertIn("TweakMenuModel", presenter)
        for forbidden in (
            "new TweakController", "new TweakMenuModel", "new HollowKnightModsSession",
            "new HollowKnightModsRuntime", "new PlayerPrefsTweakStore",
            "new HollowKnightTweakAdapter", "HollowKnightModsRuntime.EnsureStarted",
        ):
            with self.subTest(second_authority=forbidden):
                self.assertNotIn(forbidden, presenter)
        self.assertNotRegex(presenter, r"\bITweakStore\b")

    def test_h3_mods_presenter_preserves_the_three_accepted_tabs(self):
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        presenter = strip_csharp_comments(read(MODS_PRESENTER))
        tab_row = method_body(frame, r"void\s+BuildTabRow\s*\([^)]*\)")
        self.assertRegex(frame, r"TAB_TO_COL\s*=\s*\{\s*1\s*,\s*0\s*,\s*2\s*\}")
        self.assertRegex(
            frame,
            r"const\s+int\s+COMP_MAP\s*=\s*0\s*,\s*COMP_INV\s*=\s*1\s*,\s*COMP_CHARM\s*=\s*2",
        )
        self.assertEqual(3, len(re.findall(r"LocalizedLabel\(", tab_row)))
        for label in ("Inventory", "Map", "Charms"):
            self.assertIn(f'LocalizedLabel("{label}"', tab_row)
        self.assertNotIn('LocalizedLabel("Mods"', tab_row)
        self.assertNotIn("TAB_TO_COL", presenter)
        self.assertNotRegex(presenter, r"\b(?:tab\.tap|cfg\.compTab)\b")

    def test_h3_mods_modal_renders_model_rows_and_deferred_truthfully(self):
        presenter = strip_csharp_comments(read(MODS_PRESENTER))
        repaint = method_body(
            presenter,
            r"void\s+RepaintModsModal\s*\([^)]*\)",
        )
        value = method_body(
            presenter,
            r"string\s+FriendlyModsValue\s*\([^)]*\)",
        )
        set_text = method_body(
            presenter,
            r"void\s+SetModsText\s*\([^)]*\)",
        )
        self.assertTrue(repaint, "missing bounded Mods repaint")
        for required in (
            "VisibleRows", "CurrentRows", "WindowStart", "SelectedRowIndex",
            "Controller.Value", "IsAvailable", '"DEFERRED"', "Description",
            "TrackingId", "UnavailableReason", "Message", "MessageIsError",
            '"MODS"',
        ):
            with self.subTest(repaint=required):
                self.assertIn(required, repaint)
        self.assertIn("ToUpperInvariant()", value)
        self.assertNotIn("catch", set_text)
        self.assertIn("InvalidOperationException", set_text)
        self.assertNotIn("new GameObject", repaint)
        self.assertNotIn("Instantiate(", repaint)

    def test_h3_mods_view_teardown_detaches_without_stopping_active_mods(self):
        presenter = strip_csharp_comments(read(MODS_PRESENTER))
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        teardown = method_body(
            presenter,
            r"void\s+TeardownModsPresenter\s*\(\s*\)",
        )
        companion_teardown = method_body(
            frame,
            r"void\s+TeardownCompanion\s*\(\s*\)",
        )
        frame_teardown = method_body(
            frame,
            r"void\s+TeardownFrame\s*\(\s*\)",
        )
        clear_frame = method_body(
            presenter,
            r"void\s+ClearModsFrameReferences\s*\(\s*\)",
        )
        self.assertTrue(teardown, "missing deterministic Mods view teardown")
        self.assertRegex(teardown, r"\bClose\s*\(")
        self.assertIn("tweaksOpen = false", teardown)
        self.assertIn("SetPresenterAttached(false)", teardown)
        self.assertIn("Destroy(tweaksRoot)", teardown)
        self.assertIn("tweakRows.Clear()", teardown)
        for cleared in ("tweaksRoot = null", "gearT = null", "gearSR = null", "gearTex = null"):
            self.assertIn(cleared, teardown)
        self.assertIn("frameAssets.Remove(gearSprite)", teardown)
        self.assertIn("frameAssets.Remove(gearTex)", teardown)
        for forbidden in (
            ".Dispose(", "Controller.Dispose", "SetMaster(", "ToggleMaster(",
            "RestoreBaseline", "MasterEnabled =",
        ):
            with self.subTest(teardown_mutation=forbidden):
                self.assertNotIn(forbidden, teardown)
        self.assertIn("TeardownModsPresenter()", companion_teardown)
        self.assertNotIn("Destroy(tweaksRoot)", companion_teardown)
        self.assertNotIn("tweakRows.Clear()", companion_teardown)
        self.assertNotIn("tweaksOpen = false", companion_teardown)
        self.assertIn("ClearModsFrameReferences()", frame_teardown)
        for cleared in ("gearT = null", "gearSR = null", "gearTex = null", "hudGearOk = false"):
            self.assertIn(cleared, clear_frame)
        self.assertNotIn("Destroy(", clear_frame)

        direct = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.DirectDisplay.cs")
        )
        display_active = method_body(
            direct,
            r"internal\s+void\s+SetDirectDisplayActive\s*\([^)]*\)",
        )
        self.assertIn("TeardownModsPresenter()", display_active)
        self.assertLess(
            display_active.index("TeardownModsPresenter()"),
            display_active.index("SetRoleCamerasEnabled(false)"),
        )

    def test_h3_mods_presenter_uses_frame_geometry_and_the_clean_touch_stream(self):
        presenter = strip_csharp_comments(read(MODS_PRESENTER))
        select = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Select.cs")
        )
        build_gear = method_body(presenter, r"void\s+BuildModsGear\s*\(\s*\)")
        position_gear = method_body(
            presenter,
            r"void\s+PositionGear\s*\([^)]*\)",
        )
        gear_tap = method_body(presenter, r"bool\s+GearTapN\s*\([^)]*\)")
        build_modal = method_body(
            presenter,
            r"void\s+BuildModsModal\s*\([^)]*\)",
        )
        set_text = method_body(
            presenter,
            r"void\s+SetModsText\s*\([^)]*\)",
        )
        stow = method_body(
            presenter,
            r"void\s+StowModsCoveredContent\s*\(\s*\)",
        )
        tick = method_body(presenter, r"void\s+TweaksPaneTick\s*\([^)]*\)")
        tap = method_body(presenter, r"void\s+HandleModsCleanTap\s*\([^)]*\)")
        poll = method_body(select, r"void\s+PollTouch\s*\(\s*\)")

        for required in ("frameRoot", "Texture2D", "Sprite.Create", "Own(", "ATTR_LAYER", "sortingOrder"):
            self.assertIn(required, build_gear)
        self.assertIn("frameRoot.GetComponentsInChildren<Renderer>(true)", build_gear)
        self.assertIn("modsSortingOrder = highestChromeOrder + 10", build_gear)
        self.assertIn("sortingOrder = modsSortingOrder", build_gear)
        self.assertIn("sortingOrder = modsSortingOrder + 10", set_text)
        for required in ("hudGearAnchor", "hudGearH", "hudFpsB", "attrCam", "orthographicSize", "aspect", "tabY"):
            self.assertIn(required, position_gear)
        for required in ("attrCam.rect", "Contains", "ViewportToWorldPoint", "gearSR.bounds", "hudFpsB", "Expand"):
            self.assertIn(required, gear_tap)
        self.assertIn("frameRoot", build_modal)
        self.assertIn("compRoot", build_modal)
        self.assertIn("SanitizeDetachedTmpClone", build_modal)
        self.assertIn("ForceMeshUpdate", presenter)
        self.assertIn("NeutralizeDetachedTmpClip", presenter)
        self.assertNotRegex(presenter, r"\bCanvas\b")

        self.assertIn("transport.CleanTapSequence", tick)
        self.assertIn("StowModsCoveredContent()", tick)
        for covered in ("mapClone", "invCloneCache", "charmCloneCache"):
            self.assertIn(covered, stow)
        self.assertGreaterEqual(stow.count("SetActive(false)"), 3)
        self.assertNotRegex(tick, r"\bInput\.")
        for action in ("MoveGroup(", "MoveRow(", "ToggleMaster(", "CycleSelected(", "Reset("):
            self.assertIn(action, tap)
        self.assertIn("modsClosePending = true", tap)
        self.assertIn("CloseTweaksPane()", tick)
        self.assertLess(
            tick.index("if (modsClosePending)"),
            tick.index("transport.CleanTapSequence"),
        )
        self.assertIn("if (tweaksOpen) return", poll)
        self.assertRegex(
            poll,
            r"if\s*\(hitTab\s*>=\s*0\)\s*\{\s*CloseTweaksPane\(\);\s*tab\.tap\s*=\s*hitTab",
        )

    def test_h3_mods_presenter_bounds_idle_paint_and_uses_shared_behavior_helpers(self):
        presenter = strip_csharp_comments(read(MODS_PRESENTER))
        build_label = method_body(
            presenter,
            r"ModsLabel\s+BuildModsLabel\s*\([^)]*\)",
        )
        set_text = method_body(
            presenter,
            r"void\s+SetModsText\s*\([^)]*\)",
        )
        tick = method_body(
            presenter,
            r"void\s+TweaksPaneTick\s*\([^)]*\)",
        )
        model_stamp = method_body(
            presenter,
            r"long\s+ComputeModsModelPaintStamp\s*\([^)]*\)",
        )
        geometry_stamp = method_body(
            presenter,
            r"long\s+ComputeModsGeometryPaintStamp\s*\([^)]*\)",
        )
        resolved_geometry = method_body(
            presenter,
            r"bool\s+TryGetModsGeometry\s*\([^)]*\)",
        )
        rebind = method_body(
            presenter,
            r"void\s+RebindModsPresenter\s*\([^)]*\)",
        )
        close = method_body(
            presenter,
            r"void\s+CloseTweaksPane\s*\(\s*\)",
        )
        stow = method_body(
            presenter,
            r"void\s+StowModsCoveredContent\s*\(\s*\)",
        )

        for cached in (
            'GetProperty("text")', 'GetProperty("color")',
            "GetComponent<Renderer>()", "new ModsLabel(",
        ):
            self.assertIn(cached, build_label)
        self.assertRegex(
            build_label,
            r'GetMethod\s*\(\s*"ForceMeshUpdate"',
        )
        for forbidden in ("GetProperty(", "GetMethod(", "TmpProp("):
            self.assertNotIn(forbidden, set_text)
        self.assertIn("label.LastText", set_text)
        self.assertIn("label.LastColor", set_text)
        self.assertEqual(1, set_text.count("NeutralizeDetachedTmpClip("))
        self.assertLess(
            set_text.index("label.LastText"),
            set_text.index("NeutralizeDetachedTmpClip("),
        )

        for helper in (
            "TweakPresenterInteraction", "TweakPresenterLifecycle",
            "TweakPresenterPaintInvalidation", "TryMapNormalizedTopLeft",
            "ResolveAction(", "TryAcceptCleanTap(", "ShouldPaint(",
            "Acknowledge(", "HasCurrentGeometry(",
        ):
            self.assertIn(helper, presenter)
        self.assertEqual(1, tick.count("RepaintModsModal("))
        self.assertNotIn("new ", tick)
        self.assertNotIn("StringBuilder", tick)
        self.assertLess(
            tick.index("TryAcceptCleanTap("),
            tick.index("ShouldPaint("),
        )
        self.assertLess(
            tick.index("ShouldPaint("),
            tick.index("RepaintModsModal("),
        )
        self.assertIn("ComputeModsModelPaintStamp", tick)
        self.assertIn("ComputeModsGeometryPaintStamp", tick)

        for state in (
            "SelectedGroupIndex", "SelectedRowIndex", "WindowStart",
            "VisibleRows", "Message", "MessageIsError", "IsOpen",
            "MasterEnabled", "descriptor.Id", "descriptor.IsAvailable",
            "Controller.Value", "RuntimeHelpers.GetHashCode(session)",
            "RuntimeHelpers.GetHashCode(menu)",
        ):
            self.assertIn(state, model_stamp)
        self.assertNotIn("new ", model_stamp)
        for fallback_geometry in (
            "float.IsNaN(frameInnerTopFrac)", "cfg.compSepTopY",
            "float.IsNaN(frameInnerBotFrac)", "cfg.compTabY + 0.4f",
        ):
            self.assertIn(fallback_geometry, resolved_geometry)
        for geometry in (
            "TryGetModsGeometry(",
            "TweakPresenterGeometryPaintStamp.Compute(",
            "attrCam.rect", "attrCam.transform.position",
            "attrCam.orthographicSize", "attrCam.aspect",
            "left", "right", "bottom", "top", "scale",
        ):
            self.assertIn(geometry, geometry_stamp)
        for duplicated_layout_input in (
            "frameInnerTopFrac", "frameInnerBotFrac",
            "cfg.compSepTopY", "cfg.compTabY", "float.IsNaN",
        ):
            self.assertNotIn(duplicated_layout_input, geometry_stamp)
        self.assertNotIn("new ", geometry_stamp)
        self.assertIn("modsLifecycle.Rebind(", rebind)
        self.assertIn("modsPaint.Invalidate()", close)
        self.assertIn("RequestCoveredContentStow()", stow)

    def test_h3_mods_presenter_has_no_copied_h3_or_native_bridge_surface(self):
        compiled = "\n".join(
            strip_csharp_comments(read(path)) for path in SOURCE_ROOT.rglob("*.cs")
        )
        for copied in ("HKTweaks", "HKModsMenu", "HKDualScreen.Bottom.Tweaks"):
            self.assertNotIn(copied, compiled)
        presenter = strip_csharp_comments(read(MODS_PRESENTER))
        for forbidden in (
            "DllImport", "AndroidJava", "IssuePluginEvent", "PlayerPrefs.Set",
            "persistentDataPath", "GetJoystickNames", "GetButton", "GetAxis",
        ):
            with self.subTest(prohibited_surface=forbidden):
                self.assertNotIn(forbidden, presenter)

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

    def test_backdrop_has_one_stage_hook_decision_in_the_capture_policy(self):
        main = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.cs"))
        sync = method_body(main, r"void\s+SyncBgCapture\s*\([^)]*\)")
        self.assertTrue(sync, "missing SyncBgCapture")
        self.assertEqual(1, sync.count("HkStageHooks.BlackBackground"))
        self.assertRegex(
            " ".join(sync.split()),
            r"bgDimmer\.Brightness\s*=\s*HkStageHooks\.BlackBackground\s*"
            r"\?\s*0f\s*:\s*cfg\.dim\s*;",
        )

    def test_lifeblood_flash_tracker_samples_live_state_without_self_contamination(self):
        self.assertTrue(FLASH_POLICY.is_file(), "missing process-owned flash policy")
        policy = strip_csharp_comments(read(FLASH_POLICY))
        tick = method_body(
            policy,
            r"void\s+Tick\s*\(\s*HollowKnightFlashMode\?\s+desiredMode\s*,\s*float\s+softAlpha\s*\)",
        )
        reconcile = method_body(
            policy,
            r"(?:static\s+)?void\s+ReconcileLifebloodFlashState\s*\([^)]*\)",
        )
        self.assertTrue(tick, "missing process flash Tick")
        self.assertTrue(reconcile, "missing shared flash-state reconciliation")
        self.assertRegex(
            policy,
            r"struct\s+FlashBaseline\s*\{[^}]*bool\s+GameEnabled\s*;[^}]*"
            r"Color\s+GameColor\s*;[^}]*bool\s+PolicyActive\s*;[^}]*"
            r"bool\s+LastPolicyEnabled\s*;[^}]*Color\s+LastPolicyColor\s*;[^}]*\}",
        )
        self.assertRegex(
            policy,
            r"readonly\s+Dictionary<SpriteRenderer,\s*FlashBaseline>\s+"
            r"_baselines\s*=\s*new\s+Dictionary<SpriteRenderer,\s*FlashBaseline>\s*\(\s*\)",
        )
        self.assertIn('GameObject.Find("_GameCameras/CameraParent/tk2dCamera")', tick)
        self.assertIn('child.name.StartsWith("Screen Flash")', tick)
        self.assertIn("child.GetComponent<SpriteRenderer>()", tick)

        lookup = tick.index("_baselines.TryGetValue(renderer, out baseline)")
        reconcile_call = tick.index(
            "ReconcileLifebloodFlashState(renderer, ref baseline)", lookup
        )
        policy_dispatch = tick.index("switch (desiredMode.Value)", reconcile_call)
        renderer_writes = [
            match.start()
            for match in re.finditer(r"renderer\.(?:enabled|color)\s*=", tick)
        ]
        self.assertTrue(renderer_writes)
        self.assertLess(lookup, reconcile_call)
        self.assertLess(reconcile_call, policy_dispatch)
        self.assertLess(policy_dispatch, min(renderer_writes))
        self.assertEqual(1, tick.count("ReconcileLifebloodFlashState("))
        self.assertIn("_baselines[renderer] = baseline", tick[policy_dispatch:])

        normalized_reconcile = " ".join(reconcile.split())
        self.assertRegex(
            normalized_reconcile,
            r"bool\s+liveEnabled\s*=\s*renderer\.enabled\s*;\s*"
            r"Color\s+liveColor\s*=\s*renderer\.color\s*;",
        )
        self.assertRegex(
            normalized_reconcile,
            r"if\s*\(\s*!baseline\.PolicyActive\s*\)\s*\{\s*"
            r"baseline\.GameEnabled\s*=\s*liveEnabled\s*;\s*"
            r"baseline\.GameColor\s*=\s*liveColor\s*;\s*\}",
        )
        self.assertRegex(
            normalized_reconcile,
            r"else\s*\{\s*"
            r"if\s*\(\s*liveEnabled\s*!=\s*baseline\.LastPolicyEnabled\s*\)\s*"
            r"baseline\.GameEnabled\s*=\s*liveEnabled\s*;\s*"
            r"if\s*\(\s*liveColor\s*!=\s*baseline\.LastPolicyColor\s*\)\s*"
            r"baseline\.GameColor\s*=\s*liveColor\s*;\s*\}",
        )

    def test_lifeblood_flash_modes_preserve_animation_and_restore_only_on_exit(self):
        self.assertTrue(FLASH_POLICY.is_file(), "missing process-owned flash policy")
        policy = strip_csharp_comments(read(FLASH_POLICY))
        tick = method_body(
            policy,
            r"void\s+Tick\s*\(\s*HollowKnightFlashMode\?\s+desiredMode\s*,\s*float\s+softAlpha\s*\)",
        )
        self.assertTrue(tick, "missing process flash Tick")
        for mode in ("Soft", "Vanilla", "Off"):
            self.assertIn(f"case HollowKnightFlashMode.{mode}:", tick)

        soft_start = tick.index("case HollowKnightFlashMode.Soft:")
        off_start = tick.index("case HollowKnightFlashMode.Off:")
        vanilla_start = tick.index("case HollowKnightFlashMode.Vanilla:")
        default_start = tick.index("default:", vanilla_start)
        self.assertLess(soft_start, off_start)
        self.assertLess(off_start, vanilla_start)
        self.assertLess(vanilla_start, default_start)
        writeback = tick.index("_baselines[renderer] = baseline", default_start)
        pre_dispatch = tick[
            tick.index("_baselines.TryGetValue(renderer, out baseline)") : soft_start
        ]

        soft_policy = tick[soft_start:off_start]
        self.assertIn("Color softened = baseline.GameColor", soft_policy)
        self.assertIn("Color livePolicyColor = renderer.color", pre_dispatch)
        self.assertRegex(
            " ".join(soft_policy.split()),
            r"if\s*\(\s*baseline\.PolicyActive\s*&&\s*"
            r"livePolicyColor\.a\s*<\s*softened\.a\s*\)\s*"
            r"softened\.a\s*=\s*livePolicyColor\.a\s*;",
        )
        self.assertIn("renderer.enabled = baseline.GameEnabled", soft_policy)
        self.assertIn("softAlpha <= 0f", soft_policy)
        self.assertIn("softened.a = 0f", soft_policy)
        self.assertIn("softened.a > softAlpha", soft_policy)
        self.assertIn("softened.a = softAlpha", soft_policy)
        self.assertIn("renderer.color = softened", soft_policy)
        self.assertIn("baseline.LastPolicyEnabled = renderer.enabled", soft_policy)
        self.assertIn("baseline.LastPolicyColor = renderer.color", soft_policy)
        self.assertIn("baseline.PolicyActive = true", soft_policy)

        off_policy = tick[off_start:vanilla_start]
        self.assertIn("renderer.enabled = false", off_policy)
        self.assertNotIn("renderer.color =", off_policy)
        self.assertIn("baseline.LastPolicyEnabled = renderer.enabled", off_policy)
        self.assertIn("baseline.LastPolicyColor = renderer.color", off_policy)
        self.assertIn("baseline.PolicyActive = true", off_policy)

        vanilla_policy = tick[vanilla_start:writeback]
        self.assertRegex(
            " ".join(vanilla_policy.split()),
            r"case\s+HollowKnightFlashMode\.Vanilla\s*:\s*default\s*:\s*"
            r"if\s*\(\s*baseline\.PolicyActive\s*\)\s*\{\s*"
            r"renderer\.enabled\s*=\s*baseline\.GameEnabled\s*;\s*"
            r"renderer\.color\s*=\s*baseline\.GameColor\s*;\s*"
            r"baseline\.PolicyActive\s*=\s*false\s*;\s*\}\s*break\s*;",
        )
        self.assertNotRegex(pre_dispatch, r"renderer\.(?:enabled|color)\s*=")

    def test_lifeblood_flash_prunes_destroyed_keys_and_restores_survivors(self):
        self.assertTrue(FLASH_POLICY.is_file(), "missing process-owned flash policy")
        policy = strip_csharp_comments(read(FLASH_POLICY))
        tick = method_body(
            policy,
            r"void\s+Tick\s*\(\s*HollowKnightFlashMode\?\s+desiredMode\s*,\s*float\s+softAlpha\s*\)",
        )
        restore = method_body(policy, r"void\s+Restore\s*\(\s*\)")
        dispose = method_body(policy, r"void\s+Dispose\s*\(\s*\)")
        self.assertTrue(tick, "missing process flash Tick")
        self.assertTrue(restore, "missing process flash Restore")
        self.assertTrue(dispose, "missing process flash Dispose")

        prune_clear = tick.index("_dead.Clear()")
        enumerate_keys = tick.index("foreach (var renderer in _baselines.Keys)")
        collect_dead = tick.index("_dead.Add(renderer)", enumerate_keys)
        remove_dead = tick.index("_baselines.Remove(_dead[i])", collect_dead)
        self.assertLess(prune_clear, enumerate_keys)
        self.assertLess(enumerate_keys, collect_dead)
        self.assertLess(collect_dead, remove_dead)
        self.assertIn("if (renderer == null)", tick[enumerate_keys:remove_dead])

        loop = restore.index("foreach (var pair in _baselines)")
        renderer = restore.index("var renderer = pair.Key", loop)
        baseline = restore.index("var baseline = pair.Value", renderer)
        surviving = restore.index("if (renderer == null) continue", baseline)
        reconcile = restore.index(
            "ReconcileLifebloodFlashState(renderer, ref baseline)", surviving
        )
        enabled = restore.index("renderer.enabled = baseline.GameEnabled", reconcile)
        color = restore.index("renderer.color = baseline.GameColor", enabled)
        clear_baselines = restore.index("_baselines.Clear()", color)
        clear_scratch = restore.index("_dead.Clear()", clear_baselines)
        self.assertLess(loop, renderer)
        self.assertLess(renderer, baseline)
        self.assertLess(baseline, surviving)
        self.assertLess(surviving, reconcile)
        self.assertLess(reconcile, enabled)
        self.assertLess(enabled, color)
        self.assertLess(color, clear_baselines)
        self.assertLess(clear_baselines, clear_scratch)
        self.assertIn("if (_disposed) return", dispose)
        self.assertIn("Restore()", dispose)

    def test_lifeblood_flash_policy_is_process_owned_and_runtime_disposed(self):
        self.assertTrue(FLASH_POLICY.is_file(), "missing process-owned flash policy")
        policy = strip_csharp_comments(read(FLASH_POLICY))
        runtime = strip_csharp_comments(read(MODS_RUNTIME))
        self.assertIn("#if UNITY_ANDROID && !UNITY_EDITOR", read(FLASH_POLICY))
        self.assertIn("sealed class HollowKnightLifebloodFlashPolicy", policy)
        self.assertNotIn(": MonoBehaviour", policy)
        self.assertIn(
            "HollowKnightLifebloodFlashPolicy _lifebloodFlashPolicy", runtime
        )
        self.assertEqual(
            1, runtime.count("new HollowKnightLifebloodFlashPolicy()")
        )

        update = method_body(runtime, r"void\s+Update\s*\(\s*\)")
        session_tick = update.index("session.Tick()")
        resolve = update.index("HollowKnightFlashModeResolver.Resolve(", session_tick)
        policy_tick = update.index("policy.Tick(", resolve)
        self.assertLess(session_tick, resolve)
        self.assertLess(resolve, policy_tick)
        self.assertIn('session.Controller.Value("lifeblood_flash")', update)
        self.assertIn("global::HkStageHooks.LegacyFlashMode", update)

        destroy = method_body(runtime, r"void\s+OnDestroy\s*\(\s*\)")
        session_dispose = destroy.index("session.Dispose()")
        policy_dispose = destroy.index("policy.Dispose()", session_dispose)
        self.assertLess(session_dispose, policy_dispose)

    def test_legacy_flash_signal_is_explicit_and_separate_from_mods_override(self):
        hooks = strip_csharp_comments(read(STAGE_HOOKS))
        self.assertIn("static HollowKnightFlashMode? _legacyFlashMode", hooks)
        self.assertIn(
            "internal static HollowKnightFlashMode? LegacyFlashMode => _legacyFlashMode",
            hooks,
        )
        set_mods = method_body(
            hooks,
            r"void\s+SetFlashOverride\s*\(\s*HollowKnightFlashMode\s+mode\s*\)",
        )
        self.assertIn("_flashOverride = mode", set_mods)
        self.assertNotIn("_flashOverride = null", set_mods)

        set_legacy = method_body(
            hooks,
            r"void\s+SetLegacyFlashMode\s*\(\s*HollowKnightFlashMode\s+mode\s*,\s*float\s+softAlpha\s*\)",
        )
        clear_legacy = method_body(
            hooks, r"void\s+ClearLegacyFlashMode\s*\(\s*\)"
        )
        clear_mods = method_body(
            hooks, r"void\s+ClearPresentationOverrides\s*\(\s*\)"
        )
        self.assertIn("_legacyFlashMode = mode", set_legacy)
        self.assertIn("_legacyFlashMode = null", clear_legacy)
        self.assertNotIn("_legacyFlashMode", clear_mods)

    def test_reference_only_publishes_and_clears_legacy_flash_mode(self):
        main = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.cs"))
        publish = method_body(
            main, r"void\s+PublishLegacyLifebloodFlashMode\s*\(\s*\)"
        )
        self.assertTrue(publish, "missing legacy flash publisher")
        self.assertIn("HkStageHooks.SetLegacyFlashMode(", publish)
        self.assertIn("cfg.killBlueFlash == 1", publish)
        self.assertIn("cfg.flashAlpha", publish)
        self.assertNotRegex(publish, r"(?:SpriteRenderer|\.enabled\s*=|\.color\s*=)")
        for token in (
            "FlashBaseline",
            "flashBaselines",
            "flashDead",
            "ReconcileLifebloodFlashState",
            "RestoreLifebloodFlashBaselines",
        ):
            self.assertNotIn(token, main)

        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        teardown = method_body(frame, r"void\s+TeardownCompanion\s*\(\s*\)")
        self.assertNotIn("LifebloodFlash", teardown)
        self.assertNotIn("ClearLegacyFlashMode", teardown)

        direct = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.DirectDisplay.cs")
        )
        restore_reference = method_body(
            direct,
            r"void\s+RestoreReferenceRouting\s*\(\s*\)",
        )
        self.assertIn("HkStageHooks.ClearLegacyFlashMode()", restore_reference)
        self.assertNotIn("RestoreLifebloodFlashBaselines", restore_reference)
        self.assertNotIn("HollowKnightLifebloodFlashPolicy", direct)
        self.assertNotRegex(restore_reference, r"policy\.(?:Restore|Dispose)\s*\(")

    def test_frame_tab_clones_reenable_the_retained_tmp_visual(self):
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        tabs = method_body(frame, r"void\s+BuildTabRow\s*\([^)]*\)")
        position = method_body(frame, r"void\s+PositionFrame\s*\(\s*\)")
        self.assertIn("Instantiate(src.gameObject, frameRoot.transform)", tabs)
        self.assertNotIn("HKTabCloneStaging", tabs)
        first_activation = tabs.index("go.SetActive(true)")
        text_assignment = tabs.index('GetProperty("text")')
        mesh_update = tabs.index("ForceMeshUpdate")
        self.assertLess(first_activation, text_assignment)
        self.assertLess(text_assignment, mesh_update)
        self.assertIn("glyphRenderer.enabled = true", position)
        self.assertLess(
            position.index("glyphRenderer.enabled = true"),
            position.index("var glyphBounds = glyphRenderer.bounds"),
        )

    def test_frame_tab_sanitization_keeps_only_the_actual_tmp_graphic(self):
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        tabs = method_body(frame, r"void\s+BuildTabRow\s*\([^)]*\)")
        self.assertIn("SanitizeDetachedTmpClone(go)", tabs)
        self.assertLess(
            tabs.index("SanitizeDetachedTmpClone(go)"),
            tabs.index("go.SetActive(true)"),
        )
        self.assertNotIn('Name.Contains("TextMeshPro")', tabs)
        self.assertNotRegex(tabs, r"Destroy(?:Immediate)?\s*\(\s*mb\s*\)")

    def test_detached_tmp_clone_sanitizer_disables_clip_driver_and_neutralizes_clip_bounds(self):
        util = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Util.cs")
        )
        identify = method_body(
            util,
            r"bool\s+IsTextMeshProGraphic\s*\([^)]*\)",
        )
        sanitize = method_body(
            util,
            r"void\s+SanitizeDetachedTmpClone\s*\([^)]*\)",
        )
        neutralize = method_body(
            util,
            r"void\s+NeutralizeDetachedTmpClip\s*\([^)]*\)",
        )
        self.assertIn('type.Name == "TextMeshPro"', identify)
        self.assertIn('type.Name == "TextMeshProUGUI"', identify)
        self.assertIn('type.Namespace == "TMProOld"', identify)
        self.assertIn('type.Namespace == "TMPro"', identify)
        self.assertIn("!IsTextMeshProGraphic(mb)", sanitize)
        self.assertIn("mb.enabled = false", sanitize)
        self.assertIn("NeutralizeDetachedTmpClip(clone)", sanitize)
        self.assertIn('Shader.PropertyToID("_ClipRect")', util)
        self.assertIn(
            "clone.GetComponentsInChildren<Renderer>(true)", neutralize
        )
        self.assertIn("renderer.GetPropertyBlock(block)", neutralize)
        self.assertIn("block.SetVector(TMP_CLIP_RECT", neutralize)
        self.assertIn("renderer.SetPropertyBlock(block)", neutralize)
        self.assertRegex(
            neutralize,
            r"new\s+Vector4\s*\(\s*-32767f\s*,\s*-32767f\s*,\s*32767f\s*,\s*32767f\s*\)",
        )

    def test_all_detached_frame_text_clones_use_the_clip_safe_sanitizer(self):
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        hud = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Hud.cs")
        )
        for source, signature, minimum_calls in (
            (frame, r"void\s+BuildFrame\s*\(\s*\)", 1),
            (frame, r"void\s+BuildTabRow\s*\([^)]*\)", 1),
            (hud, r"void\s+BuildAreaName\s*\([^)]*\)", 1),
            (hud, r"void\s+BuildStats\s*\([^)]*\)", 2),
            (hud, r"void\s+BuildNoMapLabel\s*\([^)]*\)", 1),
            (hud, r"void\s+EnsureNameClone\s*\(\s*\)", 1),
        ):
            body = method_body(source, signature)
            self.assertGreaterEqual(
                body.count("SanitizeDetachedTmpClone("), minimum_calls
            )
            self.assertNotIn('Name.Contains("TextMeshPro")', body)

        select = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Select.cs")
        )
        control = method_body(
            select,
            r"void\s+EnsureCtrlOverlay\s*\([^)]*\)",
        )
        self.assertIn("SanitizeDetachedTmpClone(v)", control)

        finalize = method_body(
            frame,
            r"void\s+FinalizeFrameTabLabels\s*\(\s*\)",
        )
        position_hud = method_body(
            hud,
            r"void\s+PositionHudStrip\s*\([^)]*\)",
        )
        set_name = method_body(hud, r"void\s+SetNameClone\s*\([^)]*\)")
        populate_control = method_body(
            select,
            r"void\s+PopulateControlPrompt\s*\([^)]*\)",
        )
        build_frame = method_body(frame, r"void\s+BuildFrame\s*\(\s*\)")
        reset_start = build_frame.index('go.name = "F_MapReset"')
        reset_end = build_frame.index('Dbg($"HKDS frame built', reset_start)
        map_reset = build_frame[reset_start:reset_end]
        self.assertLess(
            map_reset.index("ForceMeshUpdate"),
            map_reset.index("NeutralizeDetachedTmpClip(go)"),
        )
        self.assertLess(
            finalize.index("ForceMeshUpdate"),
            finalize.index("NeutralizeDetachedTmpClip(t.gameObject)"),
        )
        for clone in (
            "areaNameT.gameObject",
            "statsT.gameObject",
            "battLevelT.gameObject",
            "noMapT.gameObject",
        ):
            call = f"NeutralizeDetachedTmpClip({clone})"
            starts = [m.start() for m in re.finditer(re.escape(call), position_hud)]
            self.assertTrue(starts, clone)
            previous_neutral = -1
            for start in starts:
                force = position_hud.rfind("ForceMeshUpdate", 0, start)
                self.assertGreater(force, previous_neutral, clone)
                previous_neutral = start
        self.assertIn("ForceMeshUpdate", set_name)
        self.assertIn("NeutralizeDetachedTmpClip(dlgNameClone.gameObject)", set_name)
        self.assertLess(
            set_name.index("ForceMeshUpdate"),
            set_name.index("NeutralizeDetachedTmpClip(dlgNameClone.gameObject)"),
        )
        self.assertLess(
            populate_control.index("ForceMeshUpdate"),
            populate_control.index("NeutralizeDetachedTmpClip(ctrlMyVerbT.gameObject)"),
        )

    def test_tick_waits_for_scene_managers_without_calling_logging_singleton_getters(self):
        source = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.cs"))
        tick = method_body(source, r"void\s+Tick\s*\(\s*\)")
        resolver = method_body(
            source,
            r"bool\s+TryResolveSceneManagers\s*\([^)]*\)",
        )
        self.assertIn("TryResolveSceneManagers(out gc, out gm)", tick)
        self.assertLess(
            tick.index("TryResolveSceneManagers(out gc, out gm)"),
            tick.index("HkStageHooks.Tick"),
        )
        pre_resolution = tick[: tick.index("HkStageHooks.Tick")]
        self.assertNotIn("GameCameras.instance", pre_resolution)
        self.assertNotIn("GameManager.instance", pre_resolution)
        self.assertIn("FindFirstObjectByType<GameCameras>()", resolver)
        self.assertIn("FindFirstObjectByType<GameManager>()", resolver)

    def test_pre_fixture_helpers_never_call_the_logging_game_cameras_getter(self):
        layering = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Layering.cs")
        )
        direct = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.DirectDisplay.cs")
        )
        for body in (
            method_body(layering, r"void\s+SyncBottomFade\s*\(\s*\)"),
            method_body(layering, r"void\s+FadeSyncDiag\s*\([^)]*\)"),
            method_body(direct, r"void\s+RestoreReferenceRouting\s*\(\s*\)"),
        ):
            self.assertIn("resolvedGameCameras", body)
            self.assertNotIn("GameCameras.instance", body)

    def test_lower_hud_fixture_is_default_off_menu_bound_and_preempts_gameplay_hooks(self):
        layout = strip_csharp_comments(read(REFERENCE_ROOT / "HKLayout.cs"))
        main = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.cs"))
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        tick = method_body(main, r"void\s+Tick\s*\(\s*\)")
        fixture = method_body(
            frame,
            r"bool\s+TryRunLowerHudFixture\s*\([^)]*\)",
        )
        self.assertRegex(layout, r"compLowerHudFixture\s*=\s*0")
        self.assertIn("TryRunLowerHudFixture(gc, gm)", tick)
        self.assertLess(
            tick.index("TryRunLowerHudFixture(gc, gm)"),
            tick.index("HkStageHooks.Tick"),
        )
        self.assertIn("cfg.debug == 1", fixture)
        self.assertIn("cfg.compLowerHudFixture == 1", fixture)
        self.assertIn("directDisplayActive", fixture)
        self.assertIn("cfg.dualScreen != 0", fixture)
        self.assertIn("GlobalEnums.GameState.MAIN_MENU", fixture)
        for forbidden in (
            "PollTouch(",
            "BuildCompanionTab(",
            "RelayerHud(",
            "SendEvent(",
            "PlayerData",
        ):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, fixture)

    def test_lower_hud_fixture_uses_real_frame_path_and_tears_down_cleanly(self):
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        fixture = method_body(
            frame,
            r"bool\s+TryRunLowerHudFixture\s*\([^)]*\)",
        )
        for required in (
            "ApplyDualScreenToggle()",
            "EnsureCompRoot()",
            "ApplyCompanionCamera(compRoot.position)",
            "BuildFrame()",
            "PositionFrame()",
            "PushToBottom()",
        ):
            with self.subTest(required=required):
                self.assertIn(required, fixture)
        self.assertIn("TeardownCompanion()", fixture)
        self.assertIn("lowerHudFixtureActive = false", fixture)
        self.assertRegex(
            fixture,
            r"if\s*\(\s*!lowerHudFixtureActive\s*\)\s*\{\s*"
            r"TeardownCompanion\(\);\s*"
            r"if\s*\(\s*!TryAcquireLowerHudFixtureInputLock\(\)\s*\)",
        )
        self.assertLess(
            fixture.index("TryAcquireLowerHudFixtureInputLock()"),
            fixture.index("BuildFrame()"),
        )

    def test_lower_hud_fixture_owns_and_exactly_restores_native_menu_input_lock(self):
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        acquire = method_body(
            frame,
            r"bool\s+TryAcquireLowerHudFixtureInputLock\s*\(\s*\)",
        )
        release = method_body(
            frame,
            r"void\s+ReleaseLowerHudFixtureInputLock\s*\(\s*\)",
        )
        for required in (
            "InputHandler.Instance",
            "EventSystem.current",
            "allowMouseInput",
            "acceptingInput = false",
            "sendNavigationEvents = false",
            "allowMouseInput = false",
        ):
            with self.subTest(acquire=required):
                self.assertIn(required, acquire)
        for required in (
            "lowerHudFixtureInputWasAccepting",
            "lowerHudFixtureNavigationWasEnabled",
            "lowerHudFixtureMouseWasEnabled",
        ):
            with self.subTest(release=required):
                self.assertIn(required, release)
        teardown = method_body(frame, r"void\s+TeardownCompanion\s*\(\s*\)")
        self.assertIn("ReleaseLowerHudFixtureInputLock()", teardown)
        self.assertRegex(
            release,
            r"if\s*\(\s*failures\.Count\s*==\s*0\s*\)\s*\{[^}]*"
            r"lowerHudFixtureInputLockHeld\s*=\s*false",
        )
        self.assertNotRegex(
            release,
            r"failures\.Count\s*>\s*0[^}]*lowerHudFixtureInputLockHeld\s*=\s*false",
        )
        self.assertIn("restoreFailures.Count > 0", acquire)
        self.assertIn("lowerHudFixtureInputLockHeld = true", acquire)

        direct = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.DirectDisplay.cs")
        )
        routing = method_body(
            direct,
            r"void\s+RestoreReferenceRouting\s*\(\s*\)",
        )
        self.assertIn(
            "TryDirectStep(ReleaseLowerHudFixtureInputLockOrThrow, failures)",
            routing,
        )

    def test_frame_tab_row_uses_pinned_reference_scale_and_centres_real_glyph_bounds(self):
        layout = strip_csharp_comments(read(REFERENCE_ROOT / "HKLayout.cs"))
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        position = method_body(frame, r"void\s+PositionFrame\s*\(\s*\)")
        self.assertRegex(layout, r"compTabScale\s*=\s*2\.7f")
        self.assertRegex(layout, r"compTabY\s*=\s*-0\.76f")
        self.assertIn("desiredGlyphCenter", position)
        self.assertIn("desiredGlyphCenter.x - glyphBounds.center.x", position)
        self.assertIn("desiredGlyphCenter.y - glyphBounds.center.y", position)

    def test_frame_tab_labels_sort_above_chrome_and_fleurs_are_slot_bounded(self):
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        tabs = method_body(frame, r"void\s+BuildTabRow\s*\([^)]*\)")
        position = method_body(frame, r"void\s+PositionFrame\s*\(\s*\)")
        self.assertIn('r.sortingLayerName = "Inventory"', tabs)
        self.assertRegex(tabs, r"r\.sortingOrder\s*=\s*10080\s*\+\s*i")
        self.assertIn("float fleurMaxW", position)
        self.assertIn("Mathf.Min(charmsW", position)
        self.assertIn("float textBot = actB.min.y", position)

    def test_frame_tab_labels_finalize_after_frame_construction(self):
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        tabs = method_body(frame, r"void\s+BuildTabRow\s*\([^)]*\)")
        finalize = method_body(
            frame,
            r"void\s+FinalizeFrameTabLabels\s*\(\s*\)",
        )
        position = method_body(frame, r"void\s+PositionFrame\s*\(\s*\)")
        self.assertIn('SetValue(c, "", null)', tabs)
        self.assertNotIn("SetValue(c, labels[i]", tabs)
        self.assertIn("frameTabLabels.Add(labels[i])", tabs)
        self.assertLess(
            tabs.index("frameTabLabelsPending = true"),
            tabs.index("for (int i = 0; i < labels.Length; i++)"),
        )
        self.assertIn("frameTabBuildFailed = true", tabs)
        self.assertIn(
            "if (frameTabBuildFailed) { TeardownFrame(); return; }",
            position,
        )
        self.assertIn("FinalizeFrameTabLabels()", position)
        self.assertLess(
            position.index("FinalizeFrameTabLabels()"),
            position.index("foreach (var kv in frameEdge)"),
        )
        self.assertIn("frameTabLabels[i]", finalize)
        self.assertIn("ForceMeshUpdate", finalize)
        self.assertIn("glyphRenderer.enabled = true", finalize)
        self.assertIn("bool textSet = false", finalize)
        self.assertIn("bool meshUpdated = false", finalize)
        self.assertIn(
            "if (!textSet || !meshUpdated) { complete = false; continue; }",
            finalize,
        )
        self.assertIn("bool complete = true", finalize)
        self.assertIn("if (!hv) { complete = false; continue; }", finalize)
        self.assertIn("if (!frameBase.ContainsKey(t))", finalize)
        self.assertIn("frameBase[t] = t.localScale", finalize)
        self.assertIn("frameTabLabelsPending = !complete", finalize)
        teardown = method_body(frame, r"void\s+TeardownFrame\s*\(\s*\)")
        self.assertIn("frameTabBuildFailed = false", teardown)

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

    def test_reference_restoration_uses_failure_propagating_helpers(self):
        direct = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.DirectDisplay.cs")
        )
        restore = method_body(
            direct,
            r"void\s+RestoreReferenceRouting\s*\(\s*\)",
        )
        self.assertIn("TryDirectStep(RestoreNameCardOrThrow", restore)
        self.assertIn("TryDirectStep(RestoreDialogueShapeOrThrow", restore)

        main = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.cs"))
        strict_name = method_body(
            main,
            r"void\s+RestoreNameCardOrThrow\s*\(\s*\)",
        )
        self.assertNotIn("catch", strict_name)
        self.assertIn("dlgNameRouted = false", strict_name)

        hud = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Hud.cs")
        )
        strict_dialogue = method_body(
            hud,
            r"void\s+RestoreDialogueShapeOrThrow\s*\(\s*\)",
        )
        self.assertNotIn("catch", strict_dialogue)
        self.assertIn("dlgShaped = false", strict_dialogue)

    def test_bottom_fade_clears_without_a_source_and_reframes_on_aspect(self):
        layering = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Layering.cs")
        )
        sync = method_body(layering, r"void\s+SyncBottomFade\s*\(\s*\)")
        self.assertRegex(
            sync,
            r"fadeFsm\s*==\s*null[^{}]*\{[^{}]*fadeQuadMR\.enabled\s*=\s*false",
        )
        self.assertIn("asp != fadeQuadAspect", sync)
        self.assertIn("fadeQuadAspect = asp", sync)

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

    def test_tutorial_scan_covers_the_persistent_hud_root_and_relayered_nodes(self):
        main = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.cs"))
        scan = method_body(main, r"void\s+ScanTutorials\s*\([^)]*\)")
        node = method_body(main, r"void\s+ScanNode\s*\([^)]*\)")
        self.assertIn("resolvedGameCameras", scan)
        self.assertNotIn("GameCameras.instance", scan)
        self.assertIn("ScanNode(persistentRoot, layer)", scan)
        self.assertIn("go.layer == hudLayer", node)

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
