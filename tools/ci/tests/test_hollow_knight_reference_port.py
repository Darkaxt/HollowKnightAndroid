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
MAP_CONTROLS = REFERENCE_ROOT / "HKDualScreen.Bottom.MapControls.cs"
MODS_RUNTIME = MODS_ROOT / "HollowKnightModsRuntime.cs"
FLASH_POLICY = MODS_ROOT / "HollowKnightLifebloodFlashPolicy.cs"
FLASH_CORE = MODS_ROOT / "HollowKnightFlashPolicyCore.cs"
STAGE_HOOKS = REFERENCE_ROOT / "HkStageHooks.cs"
ENTRYPOINTS = PATCH_ROOT / "entrypoints.json"
PROJECT = PATCH_ROOT / "HollowKnightPatches.csproj"

PINNED_MODULE_HASHES = {
    "HKDualScreen.cs":
        "e8b2edaac6d2970e7426a8bdc5654328ab2b50f54f7278211adcf03b512c819e",
    "HKDualScreen.Util.cs":
        "20a021e2970ac0ff8852188506accba3277ce475e8f0a314a3a85e69fbc8f65a",
    "HKDualScreen.Bottom.Layering.cs":
        "a7c4a720c9756026de363a1c3744ca29e2a96455286cc779bcbe20ae5c1692ea",
    "HKDualScreen.Bottom.Frame.cs":
        "d44198e724ad300940c9e1eaf5e20870a0d3b5ac468aab5378ef4cd2efc3807e",
    "HKDualScreen.Bottom.Hud.cs":
        "d4a713c3373ecb91f5ecf73509ea5a65443b56c86a541b81d67d0be4071f42be",
    "HKDualScreen.Bottom.Inventory.cs":
        "32f0b52f21fcbd8c89c10acd554853fe1dde41b01586f36ddc93a7b42d6ce7ef",
    "HKDualScreen.Bottom.Charms.cs":
        "66bb323f25130af869021d11ef0eb9b229fbbc8a09bc9fb9e400a371a9f2c67c",
    "HKDualScreen.Bottom.Map.cs":
        "5099609d8af787e6f421a685334d06285cc8210638498d3e6fb055befd3fe2bb",
    "HKDualScreen.Bottom.Select.cs":
        "dee351d52dd642f741fa4f1b6ae907eda88aac76df7cf7e19f990eda8cba2588",
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


class HollowKnightFiveRoutesContractTest(unittest.TestCase):
    def test_five_camera_dispatches_and_retirements_are_real(self):
        frame = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs"))
        for token in ("COMP_JOURNAL = 3", "COMP_GUIDE = 4", "BuildSupplementaryPane(tab)",
                      "HKLowerLayout.SlideDirection(prevTab, tab.cur)", "CloneForTab(tab.cur)",
                      "RetireSupplementaryPanes()", "SyncSupplementarySource()"):
            self.assertIn(token, frame)

    def test_native_donor_authorities_are_not_relic_or_stale_flower(self):
        frame = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs"))
        for token in ("InvNailSprite", "nativeNail.level1", "charms.spriteList[0]",
                      '"pins_combined"', '"bestiary_hunter_mark_f"',
                      '"inv_item_map_quill_combined"', "iconRetry.Due(Time.frameCount)"):
            self.assertIn(token, frame)
        self.assertNotIn("wanderers-journal", frame)
        self.assertNotIn("White_Flower_Full", frame)

    def test_journal_and_guide_never_clone_or_drive_native_managers(self):
        panes = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.Bottom.JournalGuide.cs"))
        for token in ("list.list", "JournalEntryStats", "stats.playerDataName", "stats.convoName",
                      "stats.sprite", "HKLowerLayout.NotesUnlocked", "hasJournal", "GuideVisible",
                      "hasPinBench", "hasPinBlackEgg", "JournalTap", "GuideTap"):
            self.assertIn(token, panes)
        for forbidden in ("BuildEnemyList(", "UpdateEnemyList(", "SetBool(", "SetInt(",
                          "newData", "Instantiate(template", "MapKeymanager", "InventoryPane"):
            self.assertNotIn(forbidden, panes)

    def test_guide_conditions_come_from_native_root_control_not_guessed_row_flags(self):
        source = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.Bottom.JournalGuide.cs"))
        resolve = method_body(source, r"string\s+GuideRowCondition\s*\([^)]*\)")
        build = method_body(source, r"GameObject\s+BuildGuidePane\s*\(\s*\)")
        refresh = method_body(source, r"void\s+RefreshGuide\s*\([^)]*\)")
        for token in ('root.GetComponents<PlayMakerFSM>()', 'fsm.FsmName != "Control"',
                      'binding.Value != row.gameObject', 'state.Name == "Draw Pins"',
                      'test.boolName.Value == "hasPin"', 'test.isFalse.Name == "NO PIN"',
                      'test.isFalse.Name == "FINISHED"', 'return null'):
            self.assertIn(token, resolve)
        self.assertIn("GuideRowCondition(root,row,i)", build)
        self.assertIn('pd.GetBool("hasPin")', refresh)
        self.assertNotIn("pd.hasMap", refresh)
        self.assertNotIn("SendEvent(", resolve)
        self.assertNotIn("SetActive(", resolve)

    def test_partial_supplementary_build_cleanup_preserves_donor_deadlines(self):
        source = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.Bottom.JournalGuide.cs"))
        build = method_body(source, r"GameObject\s+BuildSupplementaryPane\s*\([^)]*\)")
        discard = method_body(source, r"void\s+DiscardSupplementaryPane\s*\([^)]*\)")
        self.assertIn("catch(Exception e)", build)
        self.assertIn("DiscardSupplementaryPane(id)", build)
        self.assertIn('WarnOnce("read-only pane build",e)', build)
        self.assertNotIn("Retry.Reset", discard)
        for pane in ("Journal", "Guide"):
            body = method_body(source, rf"GameObject\s+Build{pane}Pane\s*\(\s*\)")
            self.assertLess(body.index(f"Refresh{pane}(true)"), body.index(f"{pane.lower()}Retry.Resolved()"))
            self.assertIn("BuildPaneGraphics(go,COMP_" + pane.upper() + ")", body)

    def test_new_pane_graphics_and_local_clips_are_wired_into_actual_layout_and_retirement(self):
        source = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.Bottom.JournalGuide.cs"))
        graphics = method_body(source, r"void\s+PositionPaneGraphics\s*\([^)]*\)")
        clip = method_body(source, r"void\s+ApplyPaneLabelClip\s*\([^)]*\)")
        cursor = method_body(source, r"void\s+PaneCursorTick\s*\([^)]*\)")
        label = method_body(source, r"void\s+SetPaneLabel\s*\([^)]*\)")
        for token in ("415f", "820f", "875*sx", "16f : 20f", "v.Top", "v.Bottom", "v.Left", "v.Right"):
            self.assertIn(token, graphics)
        for pane in ("Journal", "Guide"):
            body = method_body(source, rf"void\s+Layout{pane}\s*\(\s*\)")
            self.assertIn(f"PositionPaneGraphics({pane.lower()}Graphics,COMP_{pane.upper()})", body)
            self.assertIn(f"SetPaneSelection({pane.lower()}Graphics", body)
            self.assertIn(f"PaneCursorTick({pane.lower()}Graphics)", body)
        self.assertIn("label.Root.InverseTransformPoint", clip)
        self.assertIn("foreach(var r in label.ClipRenderers)", clip)
        self.assertIn("SetVector(TMP_CLIP_RECT,bounds)", clip)
        self.assertIn("r.SetPropertyBlock(label.ClipBlock)", clip)
        self.assertIn("ApplyPaneLabelClip(label,rect)", label)
        for token in ("v.CursorFrame != Time.frameCount", "SelectionMoveSeconds", "22,22", "110,110"):
            self.assertIn(token, cursor)
        for forbidden in ("new ", "Resources.", "FindDeep", "GetComponents", "File."):
            self.assertNotIn(forbidden, method_body(source, r"void\s+SupplementaryTick\s*\(\s*\)"))
        discard = method_body(source, r"void\s+DiscardSupplementaryPane\s*\([^)]*\)")
        self.assertIn("journalGraphics=null", discard)
        self.assertIn("guideGraphics=null", discard)

    def test_journal_labels_use_verified_native_keys_and_read_only_fill_journal(self):
        source = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.Bottom.JournalGuide.cs"))
        binding = method_body(source, r"void\s+BindJournalLabels\s*\([^)]*\)")
        refresh = method_body(source, r"void\s+RefreshJournal\s*\([^)]*\)")
        for token in ('NativeText("INV_NAME_JOURNAL","UI")', 'NativeText("KILL_COUNT_1","Journal")',
                      'NativeText("KILL_COUNT_2","Journal")', "record.NameKey", "record.DescKey", "record.NotesKey"):
            self.assertIn(token, binding)
        self.assertIn('pd.GetBool("fillJournal")', refresh)
        self.assertIn("killed || fill", refresh)
        self.assertNotIn("record.Remaining.ToString()", binding)
        self.assertNotIn("LocalizedLabel", binding)

    def test_touch_uses_shared_geometry_and_down_to_clean_tap(self):
        source = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.Bottom.Select.cs"))
        body = method_body(source, r"void\s+PollTouch\s*\(\s*\)")
        for token in ("HitColumn", "lowerTabGesture.Down", "lowerTabGesture.Tap",
                      "CleanTapSequence", "TouchCount", "lowerTabGesture.Cancel",
                      "slideT < 1f", "mapMarkerMode", "JournalTap", "GuideTap"):
            self.assertIn(token, body)
        self.assertNotIn("compTabBandY", body)
        self.assertNotIn("frameTabs", body)


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

    def test_reference_modules_retain_their_concrete_responsibilities(self):
        for filename, required_tokens in MODULE_CONTRACTS.items():
            source = strip_csharp_comments(read(REFERENCE_ROOT / filename))
            for token in required_tokens:
                with self.subTest(module=filename, token=token):
                    self.assertIn(token, source)

    def test_page_and_selection_cursors_travel_instead_of_teleporting(self):
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        select = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Select.cs")
        )
        update = method_body(frame, r"void\s+UpdateCompanion\s*\([^)]*\)")
        animate = method_body(
            select,
            r"Bounds\s+AnimateSelectionBounds\s*\([^)]*\)",
        )
        animate_tab = method_body(
            frame,
            r"float\s+AnimateTabFleurX\s*\([^)]*\)",
        )

        self.assertIn("slideStartCamPos = attrCam.transform.position", update)
        self.assertIn("slideCamValid = true", update)
        self.assertIn("SelectionMoveSeconds = 0.15f", select)
        self.assertIn("TabCaretMoveSeconds = 0.15f", frame)
        self.assertIn("Time.unscaledDeltaTime", animate)
        self.assertIn("Vector3.Lerp", animate)
        self.assertIn("Time.unscaledDeltaTime", animate_tab)
        self.assertIn("Mathf.Lerp", animate_tab)
        self.assertIn("AnimateSelectionBounds(selBB, sel.item)", select)
        self.assertIn("AnimateTabFleurX(activeCol,(activeCol+.5f)*g.CellWidth)", frame)

    def test_map_controls_use_native_marker_state_and_visible_zoom(self):
        source = strip_csharp_comments(read(MAP_CONTROLS))
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        select = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Select.cs")
        )
        build_frame = method_body(frame, r"void\s+BuildFrame\s*\(\s*\)")
        position_frame = method_body(frame, r"void\s+PositionFrame\s*\(\s*\)")
        update = method_body(frame, r"void\s+UpdateCompanion\s*\([^)]*\)")
        pinch = method_body(select, r"void\s+MapPinchTick\s*\(\s*\)")
        handle_tap = method_body(
            source,
            r"bool\s+HandleMapControlTap\s*\([^)]*\)",
        )

        self.assertIn("sealed class MapActionButton", source)
        self.assertIn("LineRenderer mapZoomTrack", source)
        self.assertIn("mapContentVisible && !mapNeedsSetup", source)
        self.assertIn("void PositionMapControls(", source)
        self.assertIn("bool MapControlTouchTick(", source)
        self.assertIn("bool HandleMapControlTap(", source)
        for field in (
            "placedMarkers_b", "placedMarkers_r",
            "placedMarkers_y", "placedMarkers_w",
            "spareMarkers_b", "spareMarkers_r",
            "spareMarkers_y", "spareMarkers_w",
        ):
            with self.subTest(native_marker_field=field):
                self.assertIn(field, source)
        self.assertIn("mapGm.SetupMapMarkers()", source)
        self.assertIn("mapClone.transform.InverseTransformPoint(world)", source)
        self.assertIn("Mathf.Exp(Mathf.Log(maxZoom) * position)", source)
        self.assertIn("BuildMapControls(root)", build_frame)
        self.assertIn("PositionMapControls(attrCam.orthographicSize,attrCam.aspect,frameInnerTopFrac,frameInnerBotFrac,tab.cur==COMP_MAP)", position_frame)
        self.assertIn("SetMapMarkerMode(false)", update)
        self.assertLess(
            pinch.index("MapControlTouchTick(tc)"),
            pinch.index("if (tc >= 2)"),
        )
        self.assertLess(
            pinch.index("HandleMapControlTap(world)"),
            pinch.index("mapResetR != null"),
        )
        self.assertIn("!mapMarkerMode", pinch)
        self.assertLess(
            handle_tap.index("mapResetR.bounds"),
            handle_tap.index("PlaceOrRemoveMarker(world)"),
        )
        self.assertNotIn("PlayerPrefs", source)

    def test_map_controls_toggle_native_world_map_and_restore_area_view(self):
        controls = strip_csharp_comments(read(MAP_CONTROLS))
        map_source = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Map.cs")
        )
        hud = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Hud.cs")
        )
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        build = method_body(
            controls,
            r"void\s+BuildMapControls\s*\([^)]*\)",
        )
        position = method_body(
            controls,
            r"void\s+PositionMapControls\s*\([^)]*\)",
        )
        handle_tap = method_body(
            controls,
            r"bool\s+HandleMapControlTap\s*\([^)]*\)",
        )
        toggle = method_body(
            controls,
            r"void\s+SetWorldMapMode\s*\([^)]*\)",
        )
        marker_mode = method_body(
            controls,
            r"void\s+SetMapMarkerMode\s*\([^)]*\)",
        )
        setup = method_body(
            map_source,
            r"void\s+SetupQuickMap\s*\([^)]*\)",
        )
        has_any = method_body(
            map_source,
            r"bool\s+HasAnyMap\s*\([^)]*\)",
        )
        disable_areas = method_body(
            map_source,
            r"void\s+DisableAllMapAreas\s*\([^)]*\)",
        )
        map_tick = method_body(
            map_source,
            r"void\s+MapTick\s*\([^)]*\)",
        )
        frame_tick = method_body(
            map_source,
            r"void\s+MapFrameTick\s*\([^)]*\)",
        )
        world_bounds = method_body(
            map_source,
            r"bool\s+TryWorldBounds\s*\([^)]*\)",
        )
        clone = method_body(
            map_source,
            r"void\s+BuildMapClone\s*\([^)]*\)",
        )
        hud_strip = method_body(
            hud,
            r"void\s+PositionHudStrip\s*\([^)]*\)",
        )
        update = method_body(
            frame,
            r"void\s+UpdateCompanion\s*\([^)]*\)",
        )

        self.assertIn("bool mapWorldMode", map_source)
        self.assertIn("bool mapAnyAvailable", map_source)
        self.assertIn("mapViewAction", controls)
        self.assertIn('"FULL MAP"', build)
        self.assertIn('mapWorldMode ? "AREA MAP" : "FULL MAP"', position)
        self.assertIn("showViewSwitch = onMap && !mapMarkerMode", position)
        self.assertIn("SetMapAction(mapViewAction, showViewSwitch", position)
        self.assertLess(
            handle_tap.index("mapViewAction.Hit.Contains(world)"),
            handle_tap.index("if (!mapAvailable"),
        )
        self.assertLess(
            handle_tap.index("mapViewAction.Hit.Contains(world)"),
            handle_tap.index("PlaceOrRemoveMarker(world)"),
        )
        self.assertIn("SetWorldMapMode(!mapWorldMode)", handle_tap)
        self.assertIn("if (mapMarkerMode && !mapWorldMode) SetWorldMapMode(true)", marker_mode)
        self.assertIn("pd != null && pd.hasMap", has_any)
        self.assertIn("mapAnyAvailable = (tab.cur == COMP_MAP) && HasAnyMap()", map_tick)
        self.assertIn("mapWorldMode ? mapAnyAvailable : HasMapForCurrentZone()", map_tick)
        self.assertIn("if (mapAnyAvailable) MapPinchTick()", update)
        self.assertIn("effectiveTab == COMP_MAP && !mapWorldMode", hud_strip)
        self.assertIn("GameManager.instance.GetCurrentMapZone()", hud_strip)
        self.assertIn('title=!string.IsNullOrEmpty(raw) ? ZoneName(raw) : LocalizedLabel("Map","PANE_MAP")', hud_strip)
        self.assertIn("mapNeedsSetup = true", toggle)
        self.assertIn("ResetMapView()", toggle)
        self.assertIn("m.WorldMap()", setup)
        self.assertLess(setup.index("DisableAllMapAreas(m)"), setup.index("m.WorldMap()"))
        self.assertIn("m.areaDirtmouth", disable_areas)
        self.assertIn("area.SetActive(false)", disable_areas)
        self.assertIn("mapWorldMode ? TryWorldBounds(m, out b)", frame_tick)
        self.assertIn("area.activeSelf", world_bounds)
        self.assertIn("mapWorldMode = false", clone)

    def test_lower_display_has_no_hollow_knight_mods_presenter_or_input_owner(self):
        self.assertFalse(MODS_PRESENTER.exists())
        frame = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs")
        )
        hud = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Hud.cs")
        )
        select = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Select.cs")
        )
        direct = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.DirectDisplay.cs")
        )
        lower = "\n".join((frame, hud, select, direct))
        for token in (
            "TweaksPaneTick", "PositionGear", "GearTapN", "ToggleTweaksPane",
            "CloseTweaksPane", "TeardownModsPresenter", "tweaksOpen",
            "modsLifecycle", "modsInteraction", "modsGearHit",
            "ClearModsFrameReferences", "CacheModsTabHits",
        ):
            with self.subTest(lower_mods_owner=token):
                self.assertNotIn(token, lower)

        main = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.cs"))
        route = method_body(
            main,
            r"public\s+static\s+void\s+OpenBenchTeleportRoute\s*\([^)]*\)",
        )
        self.assertIn("tab.tap = COMP_MAP", route)

    def test_h3_runtime_retains_failed_teardown_and_blocks_replacement(self):
        runtime = strip_csharp_comments(read(MODS_RUNTIME))
        self.assertIn("HollowKnightModsRestorePump.BlocksReplacement", runtime)
        self.assertIn("HollowKnightModsRestorePump.Create(session)", runtime)
        self.assertIn("new PendingTweakTeardown()", runtime)
        self.assertIn("_pending.TryRetain(session)", runtime)
        self.assertIn("_pending.Tick()", runtime)
        self.assertLess(
            runtime.index("session.Dispose()"),
            runtime.index("HollowKnightModsRestorePump.Create(session)"),
        )

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

        direct = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.DirectDisplay.cs")
        )
        restore = method_body(
            direct,
            r"void\s+RestoreReferenceRouting\s*\(\s*\)",
        )
        self.assertIn("RelayerHud(cameras, true)", restore)

    def test_active_direct_display_suppresses_pause_but_preserves_inventory(self):
        source = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.cs"))
        tick = method_body(source, r"void\s+Tick\s*\(\s*\)")
        self.assertRegex(
            " ".join(tick.split()),
            r"presentationOverlay\s*=\s*paused\s*\|\|\s*invOpen",
        )
        self.assertIn("ApplyLowerPauseGate(gc, paused)", tick)
        self.assertLess(
            tick.index("if (!dsOn) return;"),
            tick.index("ApplyLowerPauseGate(gc, paused)"),
        )
        self.assertLess(
            tick.index("ApplyLowerPauseGate(gc, paused)"),
            tick.index("MainGameHooks(gc)"),
        )
        self.assertIn("if (!paused && gc != null && gc.hudCamera != null)", tick)
        self.assertEqual(1, tick.count("ApplyLowerPauseGate(gc, paused)"))
        self.assertIn(
            "CompanionVisible(companionOn, paused, invOpen, hudFadedInGameplay, popupAny)",
            tick,
        )
        self.assertNotIn("RelayerHud(gc, presentationOverlay", tick)

        relayer = method_body(source, r"void\s+RelayerHud\s*\([^)]*\)")
        self.assertIn(
            "restoreToUpperDisplay ? UI_LAYER : suppressForPause ? ATTR_LAYER : hudLayer",
            relayer,
        )

        layering = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Layering.cs")
        )
        companion_visible = method_body(
            layering,
            r"bool\s+CompanionVisible\s*\([^)]*\)",
        )
        self.assertIn(
            "bool gameplayHudFaded = hudFaded && !paused && !inventoryOpen",
            companion_visible,
        )
        self.assertIn(
            "return companionOn && !paused && !gameplayHudFaded && !popupAny",
            companion_visible,
        )
        self.assertNotIn("return companionOn && !inventoryOpen", companion_visible)
        gate = method_body(layering, r"void\s+ApplyLowerPauseGate\s*\([^)]*\)")
        self.assertIn("RelayerHud(gc, false, paused)", gate)
        self.assertIn("hudCam2.cullingMask", gate)
        self.assertIn("attrCam.cullingMask = 0", gate)
        self.assertIn("promptCam.cullingMask", gate)
        self.assertNotRegex(gate, r"Destroy|Teardown|SetActive|new\s+")
        logo = method_body(source, r"void\s+LogoTick\s*\(\s*\)")
        self.assertIn("logoGo.SetActive(!bgShow)", logo)

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

    def test_canonical_black_shell_suppresses_capture_without_changing_logo_authority(self):
        main = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.cs"))
        adapter = strip_csharp_comments(read(ADAPTER))
        sync = method_body(main, r"void\s+SyncBgCapture\s*\([^)]*\)")
        tick = method_body(main, r"void\s+Tick\s*\(\s*\)")
        self.assertIn("bgCaptureCam.enabled = false", sync)
        self.assertIn("clearCam.clearFlags = CameraClearFlags.SolidColor", sync)
        self.assertNotIn("bgCaptureCam.CopyFrom", sync)
        self.assertNotIn("SetupBgCapture(gc)", tick)
        self.assertNotIn("bgShow =", sync)
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

    def test_black_shell_does_not_remove_the_upper_mods_background_setting(self):
        main = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.cs"))
        hooks = strip_csharp_comments(read(STAGE_HOOKS))
        sync = method_body(main, r"void\s+SyncBgCapture\s*\([^)]*\)")
        self.assertTrue(sync, "missing SyncBgCapture")
        self.assertNotIn("HkStageHooks.BlackBackground", sync)
        self.assertIn("BlackBackground", hooks)

    def test_lower_telemetry_objects_config_and_polling_are_retired(self):
        lower = "\n".join(strip_csharp_comments(read(REFERENCE_ROOT / name)) for name in (
            "HKDualScreen.Bottom.Frame.cs", "HKDualScreen.Bottom.Hud.cs", "HKLayout.cs"))
        for token in ("BuildStats", "DrawBatteryTex", "F_Stats", "F_BattIcon", "F_BattLevel",
                      "SystemInfo.batteryLevel", "fpsAccum", "fpsFrames", "fpsShown", "fpsStrFor",
                      "battPollT", "battShown", "battIconTex", "battIconLvl", "statsTmp",
                      "battLevelTmp", "compStats", "compBatt", "compFps", "tabMidR"):
            with self.subTest(retired=token):
                self.assertNotIn(token, lower)
        hud = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.Bottom.Hud.cs"))
        self.assertIn("HeaderActionRightMarginPixels = 40f", hud)
        self.assertEqual(2, hud.count("s * asp - HeaderActionRightMarginPixels"))

    def test_lifeblood_flash_policy_delegates_transitions_to_host_tested_core(self):
        self.assertTrue(FLASH_CORE.is_file(), "missing Unity-free flash policy core")
        core = strip_csharp_comments(read(FLASH_CORE))
        policy = strip_csharp_comments(read(FLASH_POLICY))
        project = read(REPO_ROOT / "tools/shared-patches-tests/SharedPatches.Tests.csproj")

        self.assertNotIn("UnityEngine", core)
        self.assertNotIn("#if UNITY_ANDROID", core)
        for required in (
            "readonly struct HollowKnightFlashRgba",
            "readonly struct HollowKnightFlashSample",
            "readonly struct HollowKnightFlashDecision",
            "readonly struct HollowKnightFlashTransition",
            "sealed class HollowKnightFlashStateTracker",
        ):
            self.assertIn(required, core)
        self.assertIn("HollowKnightFlashPolicyCore.cs", project)

        tick = method_body(
            policy,
            r"void\s+Tick\s*\(\s*HollowKnightFlashDecision\s+decision\s*\)",
        )
        self.assertTrue(tick, "missing process flash decision Tick")
        self.assertRegex(
            policy,
            r"Dictionary<SpriteRenderer,\s*HollowKnightFlashStateTracker>",
        )
        self.assertIn("Sample(renderer)", tick)
        self.assertIn("tracker.Apply(live, decision)", tick)
        self.assertIn("ApplyTransition(renderer, transition)", tick)
        self.assertNotIn("FlashBaseline", policy)
        self.assertNotIn("ReconcileLifebloodFlashState", policy)
        self.assertNotRegex(policy, r"switch\s*\(\s*decision\.Mode\s*\)")

    def test_lifeblood_flash_release_and_writes_stay_in_process_policy(self):
        policy = strip_csharp_comments(read(FLASH_POLICY))
        release = method_body(policy, r"void\s+ReleaseTrackedRenderers\s*\(\s*\)")
        apply_transition = method_body(
            policy,
            r"void\s+ApplyTransition\s*\([^)]*\)",
        )
        self.assertTrue(release, "missing tracked-renderer release")
        self.assertTrue(apply_transition, "missing Unity transition writer")
        self.assertIn("tracker.Release(live)", release)
        self.assertIn("ApplyTransition(renderer, transition)", release)
        self.assertIn("transition.WriteEnabled", apply_transition)
        self.assertIn("transition.WriteColor", apply_transition)
        self.assertIn("renderer.enabled = transition.Sample.Enabled", apply_transition)
        self.assertIn("renderer.color = ToUnityColor(transition.Sample.Color)", apply_transition)

    def test_lifeblood_flash_bindings_prune_rebind_release_and_dispose(self):
        policy = strip_csharp_comments(read(FLASH_POLICY))
        prune = method_body(policy, r"void\s+PruneDestroyedRenderers\s*\(\s*\)")
        rebind = method_body(
            policy,
            r"void\s+RebindCamera\s*\(\s*Transform\s+camera\s*\)",
        )
        release = method_body(policy, r"void\s+Release\s*\(\s*\)")
        dispose = method_body(policy, r"void\s+Dispose\s*\(\s*\)")
        self.assertTrue(prune, "missing destroyed-renderer pruning")
        self.assertTrue(rebind, "missing camera rebinding")
        self.assertTrue(release, "missing process ownership release")
        self.assertTrue(dispose, "missing process flash Dispose")
        self.assertIn("if (renderer == null)", prune)
        self.assertIn("_trackers.Remove(_dead[i])", prune)
        self.assertIn("if (ReferenceEquals(camera, _tk2dCamera)) return", rebind)
        self.assertIn("ReleaseTrackedRenderers()", rebind)
        self.assertIn("_tk2dCamera = camera", rebind)
        self.assertIn("ReleaseTrackedRenderers()", release)
        self.assertIn("_tk2dCamera = null", release)
        self.assertIn("if (_disposed) return", dispose)
        self.assertIn("Release()", dispose)

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
        self.assertIn("HollowKnightFlashDecisionResolver.Resolve(", update)
        resolve = update.index("HollowKnightFlashDecisionResolver.Resolve(", session_tick)
        self.assertIn("policy.Tick(decision)", update)
        policy_tick = update.index("policy.Tick(decision)", resolve)
        self.assertLess(session_tick, resolve)
        self.assertLess(resolve, policy_tick)
        self.assertIn('session.Controller.Value("lifeblood_flash")', update)
        self.assertIn("global::HkStageHooks.LegacyFlashMode", update)
        self.assertIn("global::HkStageHooks.LegacyFlashAlpha", update)

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

    def test_frame_native_icons_retain_five_slots_without_text_fallback(self):
        frame = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs"))
        tabs = method_body(frame, r"void\s+BuildTabRow\s*\([^)]*\)")
        resolve = method_body(frame, r"void\s+ResolveTabDonors\s*\([^)]*\)")
        self.assertIn("col < 5", tabs)
        self.assertIn('ShellSprite("F_Tab" + col', tabs)
        self.assertNotIn("Instantiate(", tabs)
        self.assertIn("frameTabs[col].enabled = tabIcons[col] != null", resolve)

    def test_read_only_native_text_is_sanitized_below_inactive_staging(self):
        source = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.Bottom.JournalGuide.cs"))
        copy = method_body(source, r"NativePaneLabel\s+CopyPaneLabel\s*\([^)]*\)")
        self.assertLess(copy.index("staging.SetActive(false)"), copy.index("Instantiate(donor.gameObject,staging.transform)"))
        self.assertLess(copy.index("SanitizeDetachedTmpClone(go)"), copy.index("go.SetActive(true)"))
        self.assertIn("!IsTextMeshProGraphic(driver)", copy)
        self.assertIn("DestroyImmediate(driver)", copy)
        self.assertIn("shellCapsFont.material", copy)

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

        position_hud = method_body(hud, r"void\s+PositionHudStrip\s*\([^)]*\)")
        set_name = method_body(hud, r"void\s+SetNameClone\s*\([^)]*\)")
        populate_control = method_body(select, r"void\s+PopulateControlPrompt\s*\([^)]*\)")
        header = method_body(hud, r"void\s+BuildAreaName\s*\([^)]*\)")
        self.assertIn('CopyPaneLabel(donor,frameRoot.transform,"F_AreaName",52,true)', header)
        panes = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.Bottom.JournalGuide.cs"))
        copy = method_body(panes, r"NativePaneLabel\s+CopyPaneLabel\s*\([^)]*\)")
        label = method_body(panes, r"void\s+SetPaneLabel\s*\([^)]*\)")
        self.assertIn("NeutralizeDetachedTmpClip(go)", copy)
        self.assertLess(label.index("ForceMeshUpdate"), label.index("ApplyPaneLabelClip(label,rect)"))
        clip = method_body(panes, r"void\s+ApplyPaneLabelClip\s*\([^)]*\)")
        self.assertIn("label.Root.InverseTransformPoint", clip)
        self.assertIn("foreach(var r in label.ClipRenderers)", clip)
        self.assertIn("SetVector(TMP_CLIP_RECT,bounds)", clip)
        self.assertIn("r.SetPropertyBlock(label.ClipBlock)", clip)
        for clone in (
            "areaNameT.gameObject",
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

    def test_frame_native_art_uses_measured_cells_and_trim_corrected_fit(self):
        frame = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs"))
        position = method_body(frame, r"void\s+PositionFrame\s*\(\s*\)")
        fit = method_body(frame, r"static\s+void\s+FitSprite\s*\([^)]*\)")
        self.assertIn("(col+.5f)*g.CellWidth", position)
        self.assertIn("g.IconMax*unit,g.IconMax*unit", position)
        self.assertIn("Mathf.Min(maxWidth", fit)
        self.assertIn("var b=sr.bounds", fit)
        self.assertIn("center-sr.bounds.center", fit)
        self.assertLess(fit.index("var b=sr.bounds"), fit.index("Mathf.Min(maxWidth"))
        self.assertLess(fit.index("local.x*scale"), fit.index("center-sr.bounds.center"))
        self.assertNotIn("b.center.x * scale", fit)
        self.assertNotIn("b.center.y * scale", fit)

    def test_tmp_glyph_bounds_world_encapsulates_every_rotated_corner(self):
        charms = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.Bottom.Charms.cs")
        )
        bounds = method_body(
            charms,
            r"static\s+bool\s+TryTmpGlyphBoundsWorld\s*\([^)]*\)",
        )

        self.assertEqual(4, bounds.count("TransformPoint("))
        self.assertIn("new Vector3(tb.min.x, tb.max.y, tb.min.z)", bounds)
        self.assertIn("new Vector3(tb.max.x, tb.min.y, tb.min.z)", bounds)
        self.assertIn("Vector3.Min", bounds)
        self.assertIn("Vector3.Max", bounds)

    def test_frame_native_cursors_and_glow_sort_above_clipped_body(self):
        frame = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs"))
        resolve = method_body(frame, r"void\s+ResolveTabDonors\s*\([^)]*\)")
        position = method_body(frame, r"void\s+PositionFrame\s*\(\s*\)")
        for name in ('"F_TabTL"', '"F_TabBR"', '"F_TabGlow"'):
            self.assertIn(name, resolve)
        self.assertIn("selected.bounds", position)
        self.assertIn("22*unit,22*unit", position)
        self.assertIn("110*unit,110*unit", position)
        self.assertIn("mapMaskTopR.enabled=true", position)
        self.assertIn("mapMaskBotR.enabled=true", position)

    def test_native_icon_retry_is_bounded_and_failed_attempt_keeps_five_slots(self):
        frame = strip_csharp_comments(read(REFERENCE_ROOT / "HKDualScreen.Bottom.Frame.cs"))
        resolve = method_body(frame, r"void\s+ResolveTabDonors\s*\([^)]*\)")
        self.assertIn("iconRetry.Due(Time.frameCount)", resolve)
        self.assertIn("if (complete) iconRetry.Resolved()", resolve)
        self.assertNotIn("TeardownFrame()", resolve)
        self.assertNotIn("iconRetry.Reset()", resolve)
        teardown = method_body(frame, r"void\s+TeardownFrame\s*\(\s*\)")
        self.assertIn("iconRetry.Reset()", teardown)
        self.assertIn("i<5", teardown)

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

    def test_final_teardown_restoration_failure_retains_owner_for_retry(self):
        direct = strip_csharp_comments(
            read(REFERENCE_ROOT / "HKDualScreen.DirectDisplay.cs")
        )
        complete = method_body(
            direct,
            r"void\s+CompleteDirectDisplayTeardown\s*\(\s*\)",
        )
        complete_flow = " ".join(complete.split())

        self.assertNotIn("TryDirectStep(TeardownCompanion", complete)
        self.assertRegex(
            complete_flow,
            r"try\s*\{\s*TeardownCompanion\(\)\s*;\s*\}\s*"
            r"catch\s*\([^)]*\)\s*\{(?:(?!\}).)*"
            r"directDisplayRestorePending\s*=\s*true\s*;(?:(?!\}).)*"
            r"directDisplayFinalTeardownPending\s*=\s*true\s*;(?:(?!\}).)*"
            r"throw\s*;\s*\}",
        )
        restored = complete.index("TeardownCompanion()")
        for terminal_action in (
            "directDisplayShuttingDown = true",
            "directDisplayFinalTeardownPending = false",
            "OnReferenceTeardownComplete(this)",
            "transport = null",
            "activeInstance = null",
            "started = false",
            "Destroy(gameObject)",
        ):
            self.assertGreater(complete.index(terminal_action), restored)

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
            "ApplyLowerPauseGate(",
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

    def test_skin_hot_path_uses_quiet_owner_reads_and_skips_incomplete_targets(self):
        runtime = strip_csharp_comments(
            read(SOURCE_ROOT / "skins" / "runtime" / "HollowKnightSkinRuntime.cs")
        )
        tick = method_body(runtime, r"public\s+void\s+Tick\s*\(\s*\)")
        self.assertIn("GameManager.UnsafeInstance", tick)
        self.assertIn("HeroController.UnsafeInstance", tick)
        self.assertNotIn("HeroController.instance", tick)

        death = strip_csharp_comments(
            read(SOURCE_ROOT / "skins" / "runtime" / "HollowKnightSkinDeathAdapter.cs")
        )
        bind = method_body(death, r"void\s+BindManaged\s*\(\s*\)")
        capture = method_body(
            death,
            r"static\s+void\s+CaptureManaged\s*\([^)]*\)",
        )
        self.assertIn("HeroController.UnsafeInstance", bind)
        self.assertIn("GameManager.UnsafeInstance", bind)
        self.assertIn("HeroController.UnsafeInstance", capture)
        self.assertIn("GameManager.UnsafeInstance", capture)
        self.assertNotIn("new SkinDeathFrame", capture)
        self.assertIn("DetailedSampleRequired", death)
        self.assertLess(
            capture.index("if (!detailed || h == null || m == null"),
            capture.index("m.GetCurrentMapZone()"),
        )

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
