import hashlib
import inspect
import json
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
HUD_EVIDENCE = REPO_ROOT / "docs" / "verification" / "task99-hud-29980-evidence.json"
TASK112_EVIDENCE = REPO_ROOT / "docs" / "verification" / "evidence" / "task112-spec-fix"
SOURCE_AUDIT = REPO_ROOT / "docs" / "verification" / "dualscreen-source-audit.md"
DUALSCREEN_SOURCES = (
    REPO_ROOT / "tools" / "silksong-patches" / "src" / "dualscreen"
)
SHARED_DUALSCREEN_SOURCES = (
    REPO_ROOT / "tools" / "shared-patches" / "src" / "DualScreen"
)
DUAL_SCREEN = DUALSCREEN_SOURCES / "DualScreenV2.cs"
PRESENTATION = SHARED_DUALSCREEN_SOURCES / "DirectDisplayPresentation.cs"
PRESENTATION_SHIM = DUALSCREEN_SOURCES / "DsPresentation.cs"
DISPLAY_HOST = SHARED_DUALSCREEN_SOURCES / "DirectDisplayHost.cs"
PORT_RUNTIME = DUALSCREEN_SOURCES / "DsPortRuntime.cs"
PORT_LAYERS = DUALSCREEN_SOURCES / "DsPortLayers.cs"
PORT_UTIL = DUALSCREEN_SOURCES / "DsPortUtil.cs"
RESIDENT_UI = DUALSCREEN_SOURCES / "DsResidentUi.cs"
PORT_FRAME = DUALSCREEN_SOURCES / "DsPortFrame.cs"
PORT_FRAME_STATE = DUALSCREEN_SOURCES / "DsPortFrameState.cs"
PORT_HUD = DUALSCREEN_SOURCES / "DsPortHud.cs"
PORT_HUD_STATE = DUALSCREEN_SOURCES / "DsPortHudState.cs"
UI_MSG_DISMISS_REWRITE = REPO_ROOT / "tools" / "bundle-surgery" / "RedirectUIMsgDismiss.cs"
SILKSONG_DEATH_REWRITE = REPO_ROOT / "tools" / "bundle-surgery" / "BridgeSilksongNormalDeath.cs"
SILKSONG_DEATH_ADAPTER = REPO_ROOT / "tools" / "silksong-patches" / "src" / "skins" / "runtime" / "SilksongSkinDeathAdapter.cs"
SILKSONG_SKIN_LIBRARY = REPO_ROOT / "tools" / "silksong-patches" / "src" / "skins" / "runtime" / "SilksongSkinLibrary.cs"
BUNDLE_SURGERY_PROGRAM = REPO_ROOT / "tools" / "bundle-surgery" / "Program.cs"
IL2CPP_CONVERTER = REPO_ROOT / "src" / "SilksongLauncher.Launcher" / "app" / "src" / "main" / "kotlin" / "dev" / "silksong" / "launcher" / "Il2cppConverter.kt"
SKIN_RUNTIME_BRIDGE = REPO_ROOT / "src" / "SilksongLauncher.Launcher" / "app" / "src" / "main" / "kotlin" / "dev" / "silksong" / "launcher" / "runtime" / "SkinLibraryRuntimeBridge.kt"
SKIN_LIBRARY_STORE = REPO_ROOT / "src" / "SilksongLauncher.Launcher" / "app" / "src" / "main" / "kotlin" / "dev" / "silksong" / "launcher" / "skins" / "library" / "SkinLibraryStore.kt"


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


def authored_port_visual_violations(name: str, source: str):
    # A native fader sprite/tint consumer is not authored frame chrome. Admit
    # only its one Image allocation, never the entire overlay file/class.
    if name == "DsPortOverlays.cs":
        fade = csharp_method_body(
            source, r"sealed\s+class\s+NativeFade\s*:\s*IDsPortFade\s*,\s*System\.IDisposable"
        )
        required = (
            'typeof(ScreenFaderState).GetField("instance"',
            'typeof(ScreenFaderState).GetField("spriteRenderer"',
            "alpha = ScreenFaderState.Alpha;", "_image.sprite = _source.sprite;",
            "var color = _source.color;", "color.a = alpha;", "_image.color = color;",
            "_image.material = _source.sharedMaterial;", "_source.sharedMaterial == null",
            "!_source.gameObject.activeInHierarchy", "!_source.enabled", "_source.sprite == null",
            "if (_image != null) _image.enabled = false;",
        )
        allocation = "_image = go.AddComponent<UnityEngine.UI.Image>();"
        if fade and all(token in fade for token in required) and allocation in fade:
            source = source.replace(fade, fade.replace(allocation, "", 1), 1)
    rejected = (
        "DsWidgets.Fleur", "DsTheme.White", "DsTheme.Disc",
        "AddComponent<Image>", "AddComponent<UnityEngine.UI.Image>",
        "DsShell", "DsScreens", "Inventory/Border/Inv_Border_", "Sprite.Create(",
    )
    return [f"{name}: {token}" for token in rejected if token in source]


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
    def test_exact_silksong_normal_death_bridge_is_in_the_production_conversion_path(self):
        self.assertTrue(SILKSONG_DEATH_REWRITE.is_file(), "Task101 Cecil death bridge is missing")
        rewrite = read(SILKSONG_DEATH_REWRITE)
        program = read(BUNDLE_SURGERY_PROGRAM)
        converter = read(IL2CPP_CONVERTER)
        adapter = read(SILKSONG_DEATH_ADAPTER)
        library = read(SILKSONG_SKIN_LIBRARY)
        runtime_bridge = read(SKIN_RUNTIME_BRIDGE)
        store = read(SKIN_LIBRARY_STORE)
        for token in (
            '"HeroController"', '"<Die>d__1101"', '"MoveNext"', '"PlayerDead"',
            '"__dsNormalDeathOccurrence"', '"__dsNormalDeathHeroes"',
            '"__dsNormalDeathManagers"', '"__dsNormalDeathTokens"',
            '"DsRecordNormalDeath"', "PINNED_ASSEMBLY_SHA256",
            "PINNED_REWRITTEN_ASSEMBLY_SHA256", "AlreadyRewritten",
            "RequireCanonicalOutput", "RequireExactDieShape", "OccurrenceCapacity = 32",
            "PermadeathModes", "DemoHelper", "MaxDeathCount", "HasFinishedEnteringScene",
            "IsInSceneTransition", "IsLoadingSceneTransition",
        ):
            self.assertIn(token, rewrite)
        self.assertIn('"bridge-silksong-normal-death"', program)
        self.assertIn("BridgeSilksongNormalDeath.Run(args[1], args[2])", program)
        self.assertIn('"verify-silksong-normal-death"', program)
        self.assertIn("BridgeSilksongNormalDeath.Verify(args[1])", program)
        self.assertLess(rewrite.index("assembly.Write(staging)"),
                        rewrite.index("RequireCanonicalOutput(staging)"))
        self.assertLess(rewrite.index("RequireCanonicalOutput(staging)"),
                        rewrite.index("File.Move(staging, outputPath, false)"))
        self.assertIn("bridgeSilksongNormalDeath(context, root)", converter)
        self.assertIn('"bridge-silksong-normal-death"', converter)
        self.assertIn('listOf("verify-silksong-normal-death", output.absolutePath)', converter)
        self.assertIn("SILKSONG_DEATH_REWRITTEN_SHA256", converter)
        self.assertLess(converter.index("runVerify(output)"), converter.index("replace(output, assembly)"))
        self.assertLess(converter.index("bridgeSilksongNormalDeath(context, root)"),
                        converter.index("bridgeUiMessageDismissal(context, root)"))
        for token in ("HeroesFieldName", "ManagersFieldName", "TokensFieldName",
                      "tokens[index] != occurrence", "MaxPendingOccurrences = 32",
                      "PendingCancellations", "AcknowledgeCancellation"):
            self.assertIn(token, adapter)
        self.assertIn('Bridge.CallStatic<bool>("cancelDeath", run, occurrence)', library)
        self.assertIn("fun cancelDeath(run: String, occurrence: Long)", runtime_bridge)
        self.assertIn("store.cancelDeath(run, occurrence)", runtime_bridge)
        self.assertIn("internal fun cancelDeath(", store)

    def test_registered_native_message_companion_dismissal_rewrites_only_armed_wait(self):
        self.assertTrue(UI_MSG_DISMISS_REWRITE.is_file(), "Task105 Cecil rewrite is missing")
        rewrite = read(UI_MSG_DISMISS_REWRITE)
        program = read(BUNDLE_SURGERY_PROGRAM)
        overlay = read(DUALSCREEN_SOURCES / "DsPortOverlays.cs")

        self.assertIn('"bridge-ui-message-dismiss"', program)
        self.assertIn("RedirectUIMsgDismiss.Run(args[1], args[2])", program)
        for token in (
            '"UIMsgProxy"', '"UIMsgBase`1"', '"<DoMsg>d__10"',
            '"<wasPressed>5__2"', '"<>4__this"',
            '"get_WasSkipButtonPressed"', '"SetIsInMsg"',
            '"DsPortCompanionDismissBegin"', '"DsPortCompanionDismissArm"',
            '"DsPortCompanionDismissRequest"', '"DsPortCompanionDismissObserve"',
            '"DsPortCompanionDismissConsume"',
            '"DsPortCompanionDismissEnd"',
        ):
            self.assertIn(token, rewrite)
        for token in (
            "RequireDistinctPaths", "RequireOriginalWaitShape", "RequireExactNativeContinuation",
            "InjectBridgeState", "RewriteMoveNext", "RewriteDispose", "AlreadyRewritten",
        ):
            self.assertIn(token, rewrite)
        self.assertIn("BeginName, ArmName, ObserveName, RequestName, ConsumeName, EndName", rewrite)
        self.assertNotIn("SilksongPatches", rewrite)
        self.assertIn('GetMethod("DsPortCompanionDismissRequest"', overlay)
        gesture = csharp_method_body(overlay, r"public\s+bool\s+OnGesture\s*\([^)]*\)")
        self.assertIn("_tutorial.ConsumeGesture(gesture, visible)", gesture)
        self.assertIn("_powerUp.ConsumeGesture(gesture, visible)", gesture)
        route = csharp_method_body(overlay, r"sealed\s+class\s+NativeRegisteredMessage")
        consume = csharp_method_body(route, r"public\s+bool\s+ConsumeGesture\s*\(DsGesture\s+gesture,\s*bool\s+visible\)")
        self.assertIn("gesture.Type == DsGestureType.Tap", consume)
        self.assertIn("Current()", consume)
        self.assertIn("CompanionDismissObserve.Invoke(_owner", consume)
        self.assertIn("CompanionDismiss.Invoke(_owner, new object[] { generation })", consume)

    def test_converter_transactionally_bridges_staged_message_wait_and_invalidates_cache(self):
        converter = read(IL2CPP_CONVERTER)
        self.assertIn("bridgeUiMessageDismissal(context, root)", converter,
                      "production conversion does not invoke the staged bridge")
        stage = converter.index("var assemblies = stageAssemblies")
        bridge = converter.index("bridgeUiMessageDismissal(context, root)")
        weave = converter.index("val modInput")
        il2cpp = converter.index("prepareTool(deploy)")
        self.assertLess(stage, bridge)
        self.assertLess(bridge, weave)
        self.assertLess(bridge, il2cpp)
        for token in (
            '"bridge-ui-message-dismiss"', '"Assembly-CSharp.dll"', '".uimsg-bridge.part"',
            "PlayerImage.stageSurgery", "PlayerImage.run", "atomicReplaceStaged",
            "if (!output.isFile", "output.length()", "output.delete()", "throw",
            "UI_MESSAGE_DISMISS_ALGORITHM", "toolSha256", "assemblySha256",
            "uiMessageDismissMarker", "hasUiMessageDismissProvenance",
            "PlayerImage.surgeryAssetSha256(assets)",
        ):
            self.assertIn(token, converter)
        self.assertIn("uiMessageDismissMarker(root)", converter[converter.index("fun isComplete"):converter.index("fun isPresent")])
        self.assertIn("hasUiMessageDismissProvenance", converter[converter.index("fun isStale"):converter.index("// ── inputs")])

        rewrite = read(UI_MSG_DISMISS_REWRITE)
        for token in (
            "PINNED_GAME_VERSION", "PINNED_NATIVE_TAIL_SHA256", "NormalizeInstruction",
            "NativeTailFingerprint", "RequireExactNativeContinuation", "SHA256.HashData",
        ):
            self.assertIn(token, rewrite)
        self.assertNotIn("var tail = new HashSet<Instruction>()", rewrite)

    def test_native_map_failed_partial_build_blocks_replacement_until_exact_retry(self):
        source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        self.assertIn("DsPortMapPartialGraph<Source>", source)
        build = csharp_method_body(source, r"Source\s+BuildGraph\s*\(\s*\)")
        self.assertIn("_partial.Hold(source)", build)
        self.assertIn("_partial.Retire(_retirePartialGraph)", build)
        self.assertLess(build.index("_partial.Hold(source)"),
                        build.index("_partial.Retire(_retirePartialGraph)"))
        tick = csharp_method_body(source, r"public\s+void\s+Tick\s*\(bool\s+eligible\)")
        self.assertIn("_partial.Retire(_retirePartialGraph)", tick)
        self.assertLess(tick.index("_partial.Retire(_retirePartialGraph)"),
                        tick.index("BuildGraph()"))
        invalidate = csharp_method_body(source, r"public\s+void\s+Invalidate\s*\(\s*\)")
        self.assertIn("_partial.Retire(_retirePartialGraph)", invalidate)

    def test_native_map_preserves_renderer_and_only_present_indexed_property_blocks(self):
        source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        binding = csharp_method_body(source, r"sealed\s+class\s+PropertyBlockBinding")
        self.assertIn("MaterialPropertyBlock Renderer", binding)
        self.assertIn("MaterialPropertyBlock[] Materials", binding)
        self.assertIn("DsPortMapPropertyBlockPlan Plan", binding)
        refresh = csharp_method_body(source, r"static\s+void\s+RefreshPropertyBlocks\s*\([^)]*\)")
        for required in (
            "native.GetPropertyBlock(binding.Renderer)",
            "binding.Plan.ObserveRenderer(!binding.Renderer.isEmpty)",
            "binding.Plan.Renderer ? binding.Renderer : null",
            "native.GetPropertyBlock(block, index)",
            "binding.Plan.ObserveIndexed(index, !block.isEmpty)",
            "binding.Plan.UsesIndexed(index) ? block : null",
            "for (int index = 0; index < binding.Materials.Length; index++)",
        ):
            self.assertIn(required, refresh)
        self.assertNotIn("donor.SetPropertyBlock(block, index)", refresh)
        copy = csharp_method_body(source, r"static\s+void\s+CopyPropertyBlocks\s*\([^)]*\)")
        self.assertIn("new PropertyBlockBinding(native.sharedMaterials.Length)", copy)
        self.assertIn("RefreshPropertyBlocks(native, donor, binding)", copy)
        mutable = csharp_method_body(source, r"void\s+RefreshMutableDonors\s*\([^)]*\)")
        self.assertIn("RefreshPropertyBlocks(native, donor, binding)", mutable)

    def test_native_map_outgoing_ticks_poll_freshness_refresh_donors_and_retire_stale_graph(self):
        source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        tick = csharp_method_body(source, r"public\s+void\s+Tick\s*\(bool\s+eligible\)")
        self.assertIn("if (_outgoing && RefreshOutgoing()) return", tick)
        outgoing = csharp_method_body(source, r"bool\s+RefreshOutgoing\s*\(\s*\)")
        for required in (
            "_retained.TryReuse(", "Current(existing, presentationOnly: true)",
            "existing.SnapshotReader", "RefreshForDraw(source, presentationOnly: true)",
            "KeepOutgoing()", "_retained.Retire(_retireGraph)",
        ):
            self.assertIn(required, outgoing)
        self.assertLess(outgoing.index("_retained.TryReuse("),
                        outgoing.index("RefreshForDraw(source, presentationOnly: true)"))
        refresh = csharp_method_body(source, r"bool\s+RefreshForDraw\s*\(Source\s+source[^)]*\)")
        self.assertIn("Current(source, presentationOnly)", refresh)
        self.assertIn("RefreshMutableDonors(source)", refresh)

    def test_native_map_composes_cloned_renderer_hierarchy_directly_on_display_one(self):
        source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        for required in (
            'new GameObject("Native Map Renderer Hierarchy")',
            "source.Donors.transform.SetParent(source.Host, false)",
            "node.gameObject.layer = DsPresentation.CONTENT_LAYER",
            "group.sortingLayerID = nativeGroup.sortingLayerID",
            "group.sortingOrder = nativeGroup.sortingOrder",
            "group.sortAtRoot = nativeGroup.sortAtRoot",
            "donor.sharedMaterials = native.sharedMaterials",
        ):
            self.assertIn(required, source)
        for forbidden in (
            "RawImage", "RenderTexture", "CommandBuffer", "Graphics.ExecuteCommandBuffer",
            "DrawRenderer(", "SetRenderTarget(", "Native Map Output",
            "mixed native queues need concrete pass authority",
        ):
            self.assertNotIn(forbidden, source)

    def test_native_map_retains_owned_graph_between_bounded_snapshot_polls(self):
        source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        for token in (
            "DsPortMapRetainedGraph<Source>", "SnapshotIntervalSeconds = .125",
            ".TryReuse(", "RecordDynamicRefresh", "DynamicRefreshCount", "RetireGraph",
            "source.Donors", "source.SortingGroups", "source.Donors.SetActive(true)",
        ):
            self.assertIn(token, source)
        tick = csharp_method_body(source, r"public\s+void\s+Tick\s*\(bool\s+eligible\)")
        self.assertNotIn("CaptureSource()", tick)
        self.assertIn("BuildGraph()", tick)
        current = csharp_method_body(source, r"bool\s+Current\s*\(Source\s+source")
        self.assertNotIn("Freshness.Current", current)
        refresh = csharp_method_body(source, r"void\s+RefreshMutableDonors\s*\([^)]*\)")
        for token in (
            "source.DonorTransforms", "source.RendererSources", "source.SortingGroups",
            "source.PropertyBlocks", "RefreshPropertyBlocks(native, donor, binding)",
        ):
            self.assertIn(token, refresh)
        for allocation in (
            "new GameObject", "AddComponent<", "source.Queue.Own",
            "GetComponentsInChildren", ".sharedMaterials",
        ):
            self.assertNotIn(allocation, refresh)
        draw = csharp_method_body(source, r"void\s+Draw\s*\(Source\s+source\)")
        self.assertNotIn("RefreshMutableDonors", draw)
        self.assertIn("PrepareView(source)", draw)
        self.assertIn("source.Donors.SetActive(true)", draw)
        self.assertIn("_retained.Retire(_retireGraph)", draw)
        self.assertLess(draw.index("PrepareView(source)"), draw.index("source.Donors.SetActive(true)"))
        refresh_for_draw = csharp_method_body(source, r"bool\s+RefreshForDraw\s*\(Source\s+source[^)]*\)")
        self.assertIn("source.Authority.Same(CurrentAuthority(source, presentationOnly))", refresh_for_draw)
        self.assertIn("RefreshMutableDonors(source)", refresh_for_draw)
        self.assertIn("_retained.RecordDynamicRefresh(source)", refresh_for_draw)
        self.assertIn("_retained.Retire(_retireGraph)", refresh_for_draw)
        self.assertIn("RefreshForDraw(source)", tick)

    def test_native_map_direct_hot_path_reuses_retained_hierarchy_without_managed_construction(self):
        source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        tick = csharp_method_body(source, r"public\s+void\s+Tick\s*\(bool\s+eligible\)")
        self.assertIn("_retireGraph = RetireGraph", source)
        self.assertIn("_retireGraph, out source", tick)
        draw = csharp_method_body(source, r"void\s+Draw\s*\(Source\s+source\)")
        prepare = csharp_method_body(source, r"void\s+PrepareView\s*\([^)]*\)")
        for hot_path in (draw, prepare):
            for forbidden in (
                "new List<", "new Dictionary<", "GetComponent<", ".sharedMaterials",
                "DonorTransform(", "Get(source.", "RequireField(",
                "new DsPortMapRestoreQueue", "RenderTexture", "CommandBuffer",
            ):
                self.assertNotIn(forbidden, hot_path)
        for token in ("source.Host.rect", "pixelsPerUnit", "root.localScale", "root.localPosition"):
            self.assertIn(token, prepare)
        current = csharp_method_body(source, r"bool\s+Current\s*\(Source\s+source")
        self.assertNotIn("Get(source.", current)
        owner_current = csharp_method_body(source, r"public\s+bool\s+Current\s*\(Camera\s+target")
        self.assertNotIn("ActiveSource.GetValue", owner_current)
        self.assertIn("source.RoomsAccess.ReadActiveSource()", source)
        project = csharp_method_body(source, r"public\s+static\s+void\s+ProjectToViewport\s*\([^)]*\)")
        self.assertRegex(source, r"ProjectToViewport\s*\([^)]*out\s+float\s+projectedX[^)]*out\s+float\s+projectedY")
        self.assertNotIn("new[]", project)
        refresh_for_draw = csharp_method_body(source, r"bool\s+RefreshForDraw\s*\(Source\s+source[^)]*\)")
        self.assertIn("RecordDynamicRefresh(source)", refresh_for_draw)

    def test_inventory_committed_extra_text_uses_its_traversing_page_owner(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Inventory.cs")
        extra = csharp_method_body(source, r"void\s+ShowConsumeExtra\s*\([^)]*\)")
        self.assertIn("page.Text.Prepare(extra, page.Token.Content)", extra)
        self.assertNotIn("visual.Text", source)
        self.assertNotIn("DsPortOwnedText", csharp_method_body(source, r"sealed\s+class\s+ConsumeVisual"))
        self.assertLess(extra.index("visual.Extras.Add(extra)"), extra.index("page.Text.Prepare(extra"))
        self.assertLess(extra.index("extra.SetActive(false)"), extra.index("page.Text.Prepare(extra"))
        self.assertLess(extra.index("page.Text.Prepare(extra"), extra.index("extra.transform.SetParent(consume.Entry.transform.parent"))
        self.assertLess(extra.index("page.Text.Prepare(extra"), extra.index("extra.SetActive(true)"))
        self.assertIn("if (!ConsumeCurrent(page, consume)) throw", extra)
        force = csharp_method_body(source, r"static\s+void\s+Force\s*\([^)]*\)")
        self.assertIn("page.Text.Validate(page.Root, page.DetailRoot)", force)
        self.assertIn("page.DetailText.Validate(page.DetailRoot)", force)
        self.assertLess(force.index("page.Text.Validate"), force.index("ForceMeshUpdate"))
        present = csharp_method_body(source, r"public\s+void\s+Present\s*\([^)]*\)")
        self.assertIn("Current(token)", present)
        self.assertIn("Force(page)", present)
        cancel = csharp_method_body(source, r"void\s+CancelConsume\s*\([^)]*\)")
        release = csharp_method_body(source, r"void\s+ReleaseConsumeVisual\s*\([^)]*\)")
        self.assertNotIn("Text.Clear", cancel + release)
        self.assertIn("Object.DestroyImmediate(extra)", release)
        self.assertLess(release.index("Object.DestroyImmediate(extra)"), release.index("page.ConsumeVisuals.Remove"))
        destroy = csharp_method_body(source, r"public\s+void\s+DestroyOwned\s*\([^)]*\)")
        self.assertLess(destroy.index("RetireConsumeVisuals(page)"), destroy.index("Object.DestroyImmediate(page.Staging)"))
        self.assertLess(destroy.index("Object.DestroyImmediate(page.Staging)"), destroy.index("page.Text.Clear()"))
        self.assertLess(destroy.index("page.Text.Clear()"), destroy.index("page.Destroyed = true"))
        common = read(DUALSCREEN_SOURCES / "DsPortProgress.cs")
        prepare = csharp_method_body(common, r"public\s+void\s+Prepare\s*\([^)]*\)")
        self.assertIn("catch { _failed = true; throw; }", prepare)
        validate = csharp_method_body(common, r"public\s+void\s+Validate\s*\([^)]*\)")
        self.assertLess(validate.index("if (_failed) throw"), validate.index("GetComponentsInChildren<PaneText>"))
        self.assertIn("ValidateText(text)", validate)
        font = csharp_method_body(common, r"void\s+ValidateText\s*\([^)]*\)")
        self.assertIn('if (!_fonts.Contains(font)) throw Missing("native text font identity escaped owned graph")', font)
        self.assertIn("DsPortTextPreflight.Resolve(chosen, character, value => _fonts.Contains", font)

    def test_loadout_tool_entry_animator_keeps_native_controller_variants(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        self.assertIn("PrepareToolAnimators(page, root)", source)
        prepare = csharp_method_body(source, r"static\s+void\s+PrepareToolAnimators\s*\([^)]*\)")
        for token in ("activeInHierarchy", "runtimeAnimatorController", "finally", "InspectEffectComponent", "DsPortToolAnimatorAuthority"):
            self.assertIn(token, prepare)
        for field in ("slotAnimatorControllers", "attackAnimatorControllers", "skillAnimatorControllers"):
            self.assertIn('"' + field + '"', source)
        validate = csharp_method_body(source, r"static\s+void\s+AssertToolAnimators\s*\([^)]*\)")
        for token in ("page.Template", "Within(entry.transform, page.Root.transform)", 'Get(entry, "slotAnimator")',
                      "authority.Current", "animator.enabled", 'Get(entry, "manager")', "page.Manager"):
            self.assertIn(token, validate)
        guard = csharp_method_body(source, r"static\s+void\s+AssertInputBlocked\s*\([^)]*\)")
        self.assertIn("AssertToolAnimators(page)", guard)
        self.assertIn("page.Manager.enabled", guard)
        core = csharp_method_body(source, r"bool\s+SubmitCore\s*\([^)]*\)")
        self.assertLess(core.index("AssertInputBlocked(page)"), core.index("slot.SetEquipped(pending"))
        self.assertIn("Present(page.Token, page)", core)
        refresh = csharp_method_body(source, r"bool\s+RefreshActionPage\s*\([^)]*\)")
        self.assertIn("AssertInputBlocked(page)", refresh)
        self.assertNotIn("runtimeAnimatorController =", refresh)

    def test_inventory_release_preserves_committed_page_owned_effects(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Inventory.cs")
        cancel = csharp_method_body(source, r"void\s+CancelConsume\s*\([^)]*\)")
        self.assertNotIn("consume.Signal.gameObject.SetActive(false)", cancel)
        self.assertIn("RetireUncommittedVisual(page, consume)", cancel)
        for token in ("StopCoroutine", "EventFired -=", "consume.Audio.Stop()", "FinishConsumeClose(consume, true)"):
            self.assertIn(token, cancel)
        prepare = csharp_method_body(source, r"void\s+PrepareConsumeVisual\s*\([^)]*\)")
        for token in ("page.ConsumeVisuals.TryGetValue", "4096", "ReferenceEquals", "page.ConsumeVisuals.Add"):
            self.assertIn(token, prepare)
        routine = csharp_method_body(source, r"IEnumerator\s+ConsumeRoutine\s*\([^)]*\)")
        self.assertLess(routine.index("consume.Commit.Commit("), routine.index("consume.Visual.Lifetime.Commit()"))
        self.assertLess(routine.index("consume.Visual.Lifetime.Commit()"), routine.index("consume.Signal.EventFired +="))
        self.assertIn('Set(entry, "consumeFadeUpDelay", .3f - fade)', routine)
        self.assertLess(routine.index("CheckConsumeVisualBudget(page, consume.Visual)"), routine.index("consume.Commit.Commit("))
        self.assertLess(routine.index("while (!consume.HitSignal)"), routine.index("ShowConsumeExtra(page, consume)"))
        extra = csharp_method_body(source, r"void\s+ShowConsumeExtra\s*\([^)]*\)")
        for token in ("Object.Instantiate(visual.ExtraSource", "visual.Extras.Add(extra)", "page.ConsumeExtraNodes +=", "extra.SetActive(true)"):
            self.assertIn(token, extra)
        self.assertNotIn("Object.Destroy", extra)
        self.assertNotIn("visual.Extras.Clear", extra)
        self.assertNotIn("ScaleTo", cancel)
        self.assertNotIn("AlphaSelf =", cancel)
        release = csharp_method_body(source, r"void\s+RetireUncommittedVisual\s*\([^)]*\)")
        self.assertIn("Lifetime.ReleaseHold", release)
        destroy = csharp_method_body(source, r"public\s+void\s+DestroyOwned\s*\([^)]*\)")
        self.assertIn("RetireConsumeVisuals(page)", destroy)
        retirement = csharp_method_body(source, r"void\s+RetireConsumeVisuals\s*\([^)]*\)")
        self.assertIn("Lifetime.Retire", retirement)
        self.assertNotIn("ConsumeItemResponse", retirement)

    def test_inventory_explicit_consume_uses_guarded_native_semantics_not_shared_routine(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Inventory.cs")
        gesture = csharp_method_body(source, r"public\s+bool\s+OnGesture\s*\([^)]*\)")
        self.assertIn("DsGestureType.Down", gesture)
        self.assertIn("BeginConsume(page, entry)", gesture)
        self.assertNotIn("bool IDsPortSelection.Submit(object owner, object item) => false", source)
        for token in ("DsPortActionBoundary", "DsPortActionHold", "ConsumeItemResponse()", "Take(1, showCounter: false)",
                      "ConsumeClosesInventory", "CanConsumeRightNow()", "DoUseBenchItem", "EventRegisterEvents.InventoryCancel",
                      "WaitForSecondsRealtime", "1.5f : .5f", "PreventUseChaining", "IsConsumeAtMax()", "EventFired +=",
                      "EventFired -=", "_actions.DeferRetirement()", "RefreshAfterAction", "OnConsumeComplete()", "GetItemOrFallback", "GridSectionIndex", "GridItemIndex",
                      "OnConsumeBlocked()", "ShowMemoryUseMsg(page)", '"failedAnimator"', '"failedAudioTable"'):
            self.assertIn(token, source)
        self.assertNotIn('GetMethod("ConsumeRoutine"', source)
        self.assertNotIn(".Spawn(", source)
        self.assertNotIn(".SpawnAndPlayOneShot(", source)
        self.assertNotIn("ManagerSingleton<HeroChargeEffects>.Instance", source)
        self.assertNotIn("_spawnedCustomDisplays", source)
        snapshot = csharp_method_body(source, r"static\s+IEnumerable<object>\s+ReadContent\s*\([^)]*\)")
        for token in ("CanConsumeRightNow()", "ConsumeClosesInventory", "TakeItemOnConsume", "ReadConsumeAudioInputs", "HeroChargeEffects"):
            self.assertIn(token, snapshot)
        routine = csharp_method_body(source, r"IEnumerator\s+ConsumeRoutine\s*\([^)]*\)")
        denied = routine[routine.index("if (consume.Blocked)"):routine.index("while (TouchHeld")]
        self.assertIn("OnConsumeBlocked()", denied)
        self.assertIn("!ConsumeCurrent(page, consume)", denied)
        self.assertNotIn("Commit(", denied)
        show = csharp_method_body(source, r"static\s+void\s+ShowMemoryUseMsg\s*\([^)]*\)")
        self.assertIn("Within(group.transform, page.Root.transform)", show)
        self.assertIn("page.MemoryMessage = true", show)
        self.assertNotIn("paneList.InSubMenu =", source)
        drive = csharp_method_body(source, r"IEnumerator\s+DriveConsume\s*\([^)]*\)")
        for token in ("!ConsumeCurrent(page, consume)", "_actions.Pending", "finally", "CancelConsume(page)", "if (retire) Invalidate()"):
            self.assertIn(token, drive)
        cancel = csharp_method_body(source, r"void\s+CancelConsume\s*\([^)]*\)")
        for token in ("_actions.DeferRetirement()", "FinishConsumeClose(consume, true)", "EventFired -=", "StopCoroutine", "consume.Audio.Stop()",
                      "consume.FailedAnimator.enabled = consume.FailedAnimatorEnabled", "Object.DestroyImmediate(consume.Staging)", "consume.FailedSounds.Clear()"):
            self.assertIn(token, cancel)
        for name in ("ClearSelection", "DestroyOwned"):
            release = csharp_method_body(source, r"public\s+void\s+" + name + r"\s*\([^)]*\)")
            self.assertIn("_actions.DeferRetirement()", release)
            self.assertIn("CancelConsume(page)", release)

    def test_page_slides_retain_owned_content_but_not_outgoing_interaction(self):
        frame = read(DUALSCREEN_SOURCES / "DsPortFrame.cs")
        self.assertIn("public bool IsOutgoingPresentation(DsPageRole role)", frame)
        for file in ("DsPortProgress.cs", "DsPortMap.cs"):
            source = read(DUALSCREEN_SOURCES / file)
            self.assertIn("_frame.SelectionChanged += SelectionChanged", source)
            self.assertIn("_frame.SelectionChanged -= SelectionChanged", source)
            self.assertNotIn("_frame.SelectionChanged += Invalidate", source)
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.cs")
        keep = csharp_method_body(source, r"static\s+bool\s+KeepOutgoing\s*\([^)]*\)")
        for token in ("IsOutgoingPresentation(role)", "ReferenceEquals", "token.Data", "token.Pane", "token.List", "token.Records"):
            self.assertIn(token, keep)
        for role in ("Inventory", "Loadout", "Tasks"):
            source = read(DUALSCREEN_SOURCES / ("DsPortProgress." + role + ".cs"))
            change = csharp_method_body(source, r"public\s+void\s+SelectionChanged\s*\([^)]*\)")
            self.assertIn("_selection.Clear()", change)
            self.assertIn("KeepOutgoing(_frame, DsPageRole." + role + ", page.Token)", change)
            self.assertNotIn("ClearSelection(page)", change)
            self.assertIn("_state.RetainOutgoingPresentation()", change)
            tick = csharp_method_body(source, r"public\s+void\s+Tick\s*\([^)]*\)")
            self.assertIn("KeepOutgoing(_frame, DsPageRole." + role, tick)
        source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        tick = csharp_method_body(source, r"public\s+void\s+Tick\s*\([^)]*\)")
        self.assertLess(tick.index("RefreshOutgoing()"), tick.index("_presented.Donors.SetActive(false)"))
        change = csharp_method_body(source, r"void\s+SelectionChanged\s*\([^)]*\)")
        self.assertIn("_ready = false", change)
        self.assertIn("_outgoing = KeepOutgoing()", change)

    def test_loadout_slot_animators_keep_exact_owned_visual_consequences(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        bind = csharp_method_body(source, r"public\s+void\s+BindAndVerify\s*\([^)]*\)")
        self.assertNotIn("foreach (var animator in root.GetComponentsInChildren<Animator>(true)) animator.enabled = false", bind)
        self.assertIn("PrepareSlotAnimators(root)", bind)
        prepare = csharp_method_body(source, r"static\s+void\s+PrepareSlotAnimators\s*\([^)]*\)")
        self.assertIn("ValidateSlotAnimator(animator)", prepare)
        validate = csharp_method_body(source, r"static\s+bool\s+ValidateSlotAnimator\s*\([^)]*\)")
        for token in ('"slotAnimator"', '"slotFilledAnimator"', "Within(animator.transform, slot.transform)",
                      "animator.applyRootMotion", "InspectEffectComponent(animator", "GetComponentsInChildren<Component>(true)",
                      "throw new InvalidOperationException", "ReferenceEquals"):
            self.assertIn(token, validate)
        present = csharp_method_body(source, r"static\s+void\s+AssertInputBlocked\s*\([^)]*\)")
        self.assertIn("ValidateSlotAnimator(animator)", present)
        core = csharp_method_body(source, r"bool\s+SubmitCore\s*\([^)]*\)")
        self.assertIn("RefreshActionPage(page)", core)
        self.assertNotIn("Invalidate()", core)

    def test_loadout_every_submit_retains_native_targets_until_callback_unwind(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        submit = csharp_method_body(source, r"bool\s+IDsPortSelection\.Submit\s*\([^)]*\)")
        self.assertIn("_actions.Run(() => result = SubmitCore(page, item))", submit)
        core = csharp_method_body(source, r"bool\s+SubmitCore\s*\([^)]*\)")
        self.assertIn("var pending = page.PendingTool", core)
        self.assertIn("var equipped = slot.EquippedItem", core)
        self.assertIn("ReferenceEquals(page.TargetSlot, slot)", core)
        self.assertIn("ReferenceEquals(page.Selected, item)", core)
        self.assertIn("slot.SetEquipped(pending, isManual: true, refreshTools: true)", core)
        removal = core.index("ToolItemManager.UnequipTool(equipped)")
        commit = core.index("slot.SetEquipped(pending")
        self.assertIn("RefreshActionPage(page)", core[removal:commit])
        self.assertIn("Legal(page)", core[removal:commit])
        self.assertIn("_actions.Pending", core[removal:commit])
        self.assertIn("RefreshActionPage(page)", core[commit:])
        self.assertIn("Present(page.Token, page)", core[commit:])
        self.assertNotIn("Invalidate(); return true", core)

    def test_overlay_native_fade_bridges_validate_exact_local_targets_and_parent(self):
        source = read(DUALSCREEN_SOURCES / "DsPortOverlays.cs")
        bridges = csharp_method_body(source, r"static\s+bool\s+ValidateFadeBridge\s*\([^)]*\)")
        self.assertTrue(bridges, "native-populated fade bridge admission missing")
        for token in ("typeof(NestedFadeGroupSpriteRenderer)", "typeof(NestedFadeGroupTextMeshPro)",
                      '"spriteRenderer"', '"textMesh"', '"meshRenderer"', "GetComponent<SpriteRenderer>()",
                      "GetComponent<TMProOld.TextMeshPro>()", "GetComponent<MeshRenderer>()",
                      "DsPortOverlayTargets.BridgeReference", "ParentOverride", "ParentGroup", "OverlayWithin"):
            self.assertIn(token, bridges)
        for family in ("NativePlaneCarrier", "NativeOpeningCredits"):
            body = csharp_method_body(source, rf"sealed\s+class\s+{family}")
            self.assertIn("ValidateFadeBridge(component, root)", body)
        lore = csharp_method_body(source, r"sealed\s+class\s+NativeLoreBox")
        self.assertIn("NativePlaneCarrier.ValidateComponents", lore)
        for forbidden in (".enabled =", "AddComponent", "SetParent(", "UpdateParent(", "GetMissingReferences("):
            self.assertNotIn(forbidden, bridges)

    def test_dialogue_and_credits_retry_owned_parent_and_sibling_independently(self):
        source = read(DUALSCREEN_SOURCES / "DsPortOverlays.cs")
        for name in ("NativeDialogue", "NativeOpeningCredits"):
            body = csharp_method_body(source, rf"sealed\s+class\s+{name}")
            self.assertIn("DsPortOverlayParentRestore ParentRestore", body, name)
            self.assertIn("retained.ParentRestore.Return(", body, name)
            self.assertIn("retained.ParentRestore.Sibling(", body, name)
            self.assertNotIn("retained.NativeRebound = true", body, name)
        dialogue = csharp_method_body(source, r"sealed\s+class\s+NativeDialogue")
        self.assertLess(dialogue.index("retained.ParentRestore.Sibling("), dialogue.index("retained.ParentRestored = true"))

    def test_area_title_routes_exact_initialised_visual_and_validates_live_fsm_targets(self):
        source = read(DUALSCREEN_SOURCES / "DsPortOverlays.cs")
        area = csharp_method_body(source, r"sealed\s+class\s+NativeAreaTitle")
        self.assertTrue(area, "missing reachable AreaTitle visual adapter")
        for token in ("new NativeAreaTitle(layers.Overlays)", "_areaTitle.Tick(visible)",
                      "_areaTitle.Restore", "ManagerSingleton<AreaTitle>.UnsafeInstance",
                      "_owner.Initialised", '"Area Title Control"', "ValidateTitleActions",
                      "NativePlaneCarrier", "DsPortDialogueLease"):
            self.assertIn(token, source)
        for forbidden in ("NpcDialogueTitle", "SetActive(", "SendEvent(", "StartCoroutine(",
                          "localPosition =", "localRotation =", "localScale ="):
            self.assertNotIn(forbidden, area)
        admission = csharp_method_body(source, r"static\s+void\s+ValidateTitleActions\s*\([^)]*\)")
        self.assertIn("fsm.FsmStates", admission)
        self.assertIn("state.Actions", admission)
        self.assertIn("DsPortOverlayTargets.Local", admission)
        self.assertIn("position.space == Space.Self", admission)
        self.assertIn("ResolveTarget(fsm, position.gameObject)", admission)
        self.assertIn("throw new InvalidOperationException", admission)
        self.assertIn("DsPortOverlayTargets.LocalTween", admission)
        self.assertIn("move.space == Space.Self", admission)
        self.assertIn("move.transforms.Length != 0", admission)
        self.assertIn('OverlayGet(action, "itweenEvents")', area)
        self.assertIn('args["target"] as GameObject != tween.gameObject', area)
        carrier = csharp_method_body(source, r"sealed\s+class\s+NativePlaneCarrier")
        self.assertIn("_parentRestore.Return(_carrier", carrier)
        self.assertIn("_parentRestore.Sibling", carrier)
        self.assertLess(carrier.index("_parentRestore.Return(_carrier"),
                        carrier.index("if (_root.parent != _carrier) _root.SetParent(_carrier, false)"))
        self.assertIn("!_parentRestore.Returned || !saved.RelativeReturned", carrier)
        for forbidden in ("_root.localPosition =", "_root.localRotation =", "_root.localScale =", "SetActive("):
            self.assertNotIn(forbidden, carrier)

    def test_native_tutorial_routes_only_running_message_and_never_invents_dismissal(self):
        source = read(DUALSCREEN_SOURCES / "DsPortOverlays.cs")
        tutorial = csharp_method_body(source, r"sealed\s+class\s+NativeTutorial")
        self.assertTrue(tutorial, "missing concrete tutorial routing")
        route = csharp_method_body(source, r"sealed\s+class\s+NativeRegisteredMessage")
        self.assertTrue(route, "missing actual shared native tutorial route")
        for token in ("new NativeTutorial(layers.Overlays)", "_tutorial.Tick(visible)",
                      "_tutorial.ConsumeGesture(gesture, visible)", "_tutorial.Restore",
                      "typeof(ToolTutorialMsg)", '"rumblePreventers"',
                      "DsPortOverlayTargets.Message", "NativePlaneCarrier.Acquire",
                      '"rootFader"', '"animator"', '"defaultUIAudioSourcePrefab"',
                      "spatialBlend", '"_instance"'):
            self.assertIn(token, source)
        for forbidden in ("WasSkipButtonPressed", "SetIsInMsg(", "StartCoroutine(",
                          "SetActive(", ".Spawn(", ".Setup(", "SendEvent(", ".Play(",
                          "StopCoroutine(", "SetBool(", "SetFloat(", "FindObjectsByType"):
            self.assertNotIn(forbidden, tutorial + route)
        self.assertIn("Pending || _faulted || current", route)

    def test_skill_get_routes_exact_registered_instance_without_progression_replay(self):
        source = read(DUALSCREEN_SOURCES / "DsPortOverlays.cs")
        family = csharp_method_body(source, r"sealed\s+class\s+NativeSkillGet")
        self.assertTrue(family, "missing exact SkillGetMsg route")
        for token in (
            "new NativeSkillGet(layers.Overlays)", "_skillGet.Tick(visible)",
            "_skillGet.ConsumeGesture(gesture, visible)", "_skillGet.Restore",
            "typeof(SkillGetMsg)", '"crestGroup"', '"crestSprite"',
            '"crestGlowSprite"', '"skillSprite"', '"skillGlowSprite"',
            '"skillSilhouetteSprite"', '"skillIconSprite"', '"prefixText"',
            '"nameText"', '"descText"',
        ):
            self.assertIn(token, source)
        route = csharp_method_body(source, r"sealed\s+class\s+NativeRegisteredMessage")
        for token in (
            "_owner.GetType() == _type", "ReferenceEquals(Message(), _owner)",
            "CompanionDismissObserve.Invoke(_owner", "CompanionDismiss.Invoke(_owner",
            "_lease?.Restore()",
        ):
            self.assertIn(token, route)
        restore = csharp_method_body(source, r"public\s+void\s+RestoreNative\s*\(\s*\)")
        dispose = csharp_method_body(source, r"public\s+void\s+Dispose\s*\(\s*\)")
        self.assertIn("_skillGet.Restore", restore)
        self.assertIn("RestoreNative();", dispose)
        for forbidden in (
            ".Spawn(", ".Setup(", "ToolPaneHasNew", "AddInputBlocker(",
            "RemoveInputBlocker(", "HUDOut(", "HUDIn(", "SendEvent(",
            "StartCoroutine(", "wasEquipped =", "SetLocalPosition2D(",
        ):
            self.assertNotIn(forbidden, family + route)

    def test_powerup_evaheal_uses_exact_registered_native_sequence_and_local_prompts(self):
        source = read(DUALSCREEN_SOURCES / "DsPortOverlays.cs")
        family = csharp_method_body(source, r"sealed\s+class\s+NativePowerUp")
        self.assertTrue(family, "missing concrete PowerUp/EvaHeal routing")
        for token in ("new NativePowerUp(layers.Overlays)", "_powerUp.Tick(visible)",
                      "_powerUp.ConsumeGesture(gesture, visible)", "_powerUp.Restore",
                      "typeof(PowerUpGetMsg)", '"promptGroup"', '"promptButtonSingle"',
                      '"promptButtonModifier"', '"upModifier"', '"downModifier"'):
            self.assertIn(token, source)
        route = csharp_method_body(source, r"sealed\s+class\s+NativeRegisteredMessage")
        self.assertTrue(route, "missing actual shared native-message route")
        for token in ("DsPortOverlayTargets.Message", "NativePlaneCarrier.Acquire",
                      '"rumblePreventers"', '"animator"', "spatialBlend", "Pending ||"):
            self.assertIn(token, route)
        for forbidden in ("FindObjectsByType", "SetIsInMsg(", "WasSkipButtonPressed",
                          "SetActive(", ".Spawn(", ".Setup(", "SendEvent(", ".Play(",
                          "InvPaneHasNew", "LastSelectionUpdate", "PlayerData", "StartCoroutine("):
            self.assertNotIn(forbidden, route + family)

    def test_item_popup_retains_native_stack_root_and_routes_contained_visual_islands(self):
        source = read(DUALSCREEN_SOURCES / "DsPortOverlays.cs")
        item = csharp_method_body(source, r"sealed\s+class\s+NativeItems")
        islands = csharp_method_body(source, r"sealed\s+class\s+NativePopupVisuals")
        self.assertTrue(item, "missing concrete item popup routing")
        self.assertTrue(islands, "missing contained popup island routing")
        for token in ("new NativeItems(layers.Overlays)", "_items.Tick(visible)", "_items.Restore",
                      "FindObjectsByType<CollectableUIMsg>", '"displayRoutine"', '"LastActiveMsgShared"',
                      "DsPortOverlayTargets.VisualIsland", "NativePlaneCarrier.Acquire", "HorizontalLayoutGroup"):
            self.assertIn(token, source)
        for forbidden in (".Spawn(", ".End(", ".Display(", ".Replace(", ".Recycle(", "DoQueuedSaveGame",
                          "sortingOrder =", "localPosition =", ".position =", "UpdatePosition(", "MinYPos ="):
            self.assertNotIn(forbidden, item + islands)
        self.assertNotIn("_owner.transform.SetParent", islands)
        self.assertIn("Cross-island popup clip reference", islands)
        self.assertIn("Popup renderer shares native stack or layout coordinate authority", islands)
        self.assertIn("anchors.Contains(layout.transform)", islands)
        self.assertIn('"m_RectChildren"', islands)
        self.assertIn("NativeChildren(layout.transform)", islands)
        self.assertIn("_restore.Change(carrier.Restore", islands)
        self.assertIn("_restore.Own(carrier.Release)", islands)
        carrier = csharp_method_body(source, r"sealed\s+class\s+NativePlaneCarrier")
        for token in ("typeof(RectTransform)", "rect.pivot = original.pivot", "rect.sizeDelta = Vector2.zero",
                      "rect.anchorMin = Vector2.zero", "rect.anchorMax = Vector2.one", "DsPortOverlayPlane.SameRect"):
            self.assertIn(token, carrier)
        present = csharp_method_body(carrier, r"public\s+void\s+Present\(\)")
        self.assertLess(present.index("DsPortOverlayPlane.SameRect"), present.index("_root.SetParent"))

    def test_lore_routes_exact_native_singletons_through_hide_without_continuation_writes(self):
        source = read(DUALSCREEN_SOURCES / "DsPortOverlays.cs")
        lore = csharp_method_body(source, r"sealed\s+class\s+NativeLore")
        route = csharp_method_body(source, r"sealed\s+class\s+NativeLoreBox")
        self.assertTrue(lore, "missing concrete memory/needolin lore routing")
        self.assertTrue(route, "missing native lore singleton carrier")
        for token in ("new NativeLore(layers.Overlays)", "_lore.Tick(visible)", "_lore.Restore",
                      "typeof(MemoryMsgBox)", "typeof(NeedolinMsgBox)", '"appearRoutine"', '"cycleTextsRoutine"',
                      '"hideRoutine"', '"textDisplays"', '"primaryText"', '"secondaryText"',
                      '"_instance"', "DsPortOverlayTargets.LoreVisible", "NativePlaneCarrier.Acquire",
                      '"DIALOGUE BOX APPEARING"', '"ReceivedEvent"', "GetInvocationList"):
            self.assertIn(token, source)
        for forbidden in ("FindObjectsByType", "AddText(", "RemoveText(", "ClearAllText(", "GetNewText(",
                          "HideNeedolinMsgBox(", "ReceiveEvent(", "SetActive(", ".Play(", "SetFloat(",
                          "StartCoroutine(", "StopCoroutine(", "SetIsInMsg(", "GetRegisterGuaranteed("):
            self.assertNotIn(forbidden, lore + route)
        self.assertIn("DsPortOverlayRestoration.All(_memory.Restore, _needolin.Restore)", lore)
        self.assertIn("AlphaSelf", route)
        self.assertNotIn('"isHidden"', route)

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
            r"_port\s*=\s*new\s+DsPortRuntime\s*\(\s*screen\s*\)\s*;",
        )
        presentation_assignment = source.index("_screen = new DsPresentation(_releasePump.transform);")
        host_construction = source.index("_host = new DirectDisplayHost(")
        presence_publication = source.index("_host.SetDisplayPresent(")
        bringup_yield = source.index("yield return screen.Bringup();")
        ready_guard = source.index("if (!present || !screen.Ready)")
        runtime_construction = source.index("_port = new DsPortRuntime(screen);")
        self.assertLess(presentation_assignment, host_construction)
        self.assertLess(host_construction, presence_publication)
        self.assertLess(presence_publication, bringup_yield)
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
        shim = read(PRESENTATION_SHIM)
        self.assertRegex(shim, r"public\s+const\s+int\s+DISPLAY\s*=\s*1\s*;")
        self.assertRegex(shim, r"public\s+const\s+int\s+CONTENT_LAYER\s*=\s*6\s*;")
        self.assertRegex(shim, r"public\s+const\s+int\s+OVERLAY_LAYER\s*=\s*3\s*;")
        self.assertRegex(shim, r"const\s+int\s+FALLBACK_W\s*=\s*1240\s*;")
        self.assertRegex(shim, r"const\s+int\s+FALLBACK_H\s*=\s*1080\s*;")
        self.assertRegex(
            source,
            r"_ownedLayerMask\s*=\s*\(1\s*<<\s*contentLayer\)\s*\|\s*"
            r"\(1\s*<<\s*overlayLayer\)\s*;",
        )
        self.assertEqual(2, len(re.findall(r"AddComponent<Camera>\s*\(\s*\)", source)))
        self.assertEqual(
            [("CONTENT_LAYER", "6"), ("OVERLAY_LAYER", "3")],
            re.findall(r"const\s+int\s+(\w*LAYER\w*)\s*=\s*(\d+)\s*;", shim),
        )
        self.assertRegex(
            shim,
            r":\s*base\s*\(\s*parent\s*,\s*DISPLAY\s*,\s*CONTENT_LAYER\s*,\s*"
            r"OVERLAY_LAYER\s*,\s*FALLBACK_W\s*,\s*FALLBACK_H\s*,\s*"
            r"DsConfig\.Int\s*,\s*DsTouch\.Begin\s*,\s*"
            r"\(\s*\)\s*=>\s*DsTouch\.Ready\s*,\s*"
            r"\(\s*\)\s*=>\s*DsTouch\.SurfaceSize\s*,\s*DsTouch\.Stop\s*\)",
        )

        for camera, layer in (
            ("Camera", "ContentLayer"),
            ("OverlayCamera", "OverlayLayer"),
        ):
            with self.subTest(camera=camera):
                block = re.search(
                    rf"(?ms)^\s*{camera}\s*=\s*.*?AddComponent<Camera>\s*\(\s*\)\s*;"
                    rf"(?P<body>.*?)(?=^\s*$)",
                    source,
                )
                self.assertIsNotNone(block, f"missing construction block for {camera}")
                body = block.group("body")
                self.assertRegex(body, rf"\b{camera}\.targetDisplay\s*=\s*DisplayIndex\s*;")
                self.assertRegex(body, rf"\b{camera}\.cullingMask\s*=\s*1\s*<<\s*{layer}\s*;")

        self.assertRegex(
            source,
            r"if\s*\(\s*camera\s*==\s*null\s*\|\|\s*IsOwnedCamera\s*\(\s*camera\s*\)\s*\)\s*continue\s*;",
        )
        self.assertRegex(source, r"camera\.cullingMask\s*&=\s*~_ownedLayerMask\s*;")
        owned_check = re.search(
            r"bool\s+IsOwnedCamera\s*\(\s*Camera\s+camera\s*\)\s*\{(?P<body>.*?)\}",
            source,
            re.DOTALL,
        )
        self.assertIsNotNone(owned_check)
        self.assertIn("camera == Camera", owned_check.group("body"))
        self.assertIn("camera == OverlayCamera", owned_check.group("body"))

    def test_shared_presentation_validates_config_and_owns_compatibility_statics(self):
        source = read(PRESENTATION)
        constructor = csharp_method_body(
            source,
            r"public\s+DirectDisplayPresentation\s*\([^)]*\)",
        )
        self.assertTrue(constructor)
        for guard in (
            r"displayIndex\s*<\s*0",
            r"contentLayer\s*<\s*0\s*\|\|\s*contentLayer\s*>\s*31",
            r"overlayLayer\s*<\s*0\s*\|\|\s*overlayLayer\s*>\s*31",
            r"contentLayer\s*==\s*overlayLayer",
            r"fallbackWidth\s*<=\s*0",
            r"fallbackHeight\s*<=\s*0",
        ):
            with self.subTest(guard=guard):
                self.assertRegex(constructor, guard)

        positive_config = csharp_method_body(
            source,
            r"int\s+PositiveConfigInt\s*\(\s*string\s+key\s*,\s*int\s+fallback\s*\)",
        )
        self.assertTrue(positive_config)
        self.assertIn(
            "int value = _readConfigInt != null ? _readConfigInt(key, fallback) : fallback;",
            re.sub(r"\s+", " ", positive_config).strip(),
        )
        self.assertRegex(
            positive_config,
            r"return\s+value\s*>\s*0\s*\?\s*value\s*:\s*fallback\s*;",
        )
        for key, fallback in (
            ("settle_ms", "1500"),
            ("sweep_ms", "500"),
            ("panel_w", "_fallbackWidth"),
            ("panel_h", "_fallbackHeight"),
        ):
            with self.subTest(key=key):
                self.assertRegex(
                    source,
                    rf'PositiveConfigInt\s*\(\s*"{key}"\s*,\s*{fallback}\s*\)',
                )

        self.assertRegex(
            source,
            r"static\s+DirectDisplayPresentation\s+_compatibilityOwner\s*;",
        )
        publish = csharp_method_body(
            source, r"void\s+PublishCompatibilityState\s*\(\s*\)"
        )
        self.assertTrue(publish)
        self.assertIn("_compatibilityOwner = this;", publish)
        self.assertIn("PanelW = Width;", publish)
        self.assertIn("PanelH = Height;", publish)
        self.assertIn("UiCamera = Camera;", publish)

        bringup = csharp_method_body(
            source, r"public\s+IEnumerator\s+Bringup\s*\(\s*\)"
        )
        self.assertLess(
            bringup.index("PublishCompatibilityState();"),
            bringup.index("Ready = true;"),
        )

        dispose = csharp_method_body(
            source, r"public\s+void\s+Dispose\s*\(\s*\)"
        )
        owner_clear = re.search(
            r"if\s*\(\s*ReferenceEquals\s*\(\s*_compatibilityOwner\s*,\s*this\s*\)\s*\)"
            r"\s*\{(?P<body>.*?)\}",
            dispose,
            re.DOTALL,
        )
        self.assertIsNotNone(owner_clear)
        for statement in (
            "_compatibilityOwner = null;",
            "PanelW = 0;",
            "PanelH = 0;",
            "UiCamera = null;",
        ):
            self.assertIn(statement, owner_clear.group("body"))
        self.assertEqual(1, dispose.count("UiCamera = null;"))

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
        self.assertIn("_hud.RestoreBefore(() => _layers.SetVisible(false))", source)
        self.assertIn("_layers.SetVisible(true);", source)
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
        activate = presentation.index("displays[DisplayIndex].Activate();")
        settle = presentation.index("while (Time.realtimeSinceStartup < until)", activate)
        refreshed = presentation.index("displays = Display.displays;", settle)
        presence_check = presentation.index(
            "if (displays.Length <= DisplayIndex)", refreshed
        )
        revision_check = presentation.index(
            "if (availabilityRevision != _availabilityRevision)",
            presence_check,
        )
        measure = presentation.index("MeasurePanel(displays[DisplayIndex]);", presence_check)
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
        retained = host.index("_screen = new DsPresentation(_releasePump.transform);")
        host_construction = host.index("_host = new DirectDisplayHost(")
        presence_publication = host.index("_host.SetDisplayPresent(")
        yielded = host.index("yield return screen.Bringup();")
        self.assertLess(retained, host_construction)
        self.assertLess(host_construction, presence_publication)
        self.assertLess(presence_publication, yielded)
        self.assertLess(retained, yielded)

        presentation = read(PRESENTATION)
        self.assertRegex(presentation, r"bool\s+_bringupInProgress\s*;")
        self.assertRegex(
            presentation,
            r"while\s*\(\s*_bringupInProgress\s*\)\s*yield\s+return\s+null\s*;",
        )
        self.assertNotRegex(presentation, r"bringup.{0,80}(?:timeout|deadline)")

    def test_inflight_activation_cannot_outlive_disposal(self):
        violations = []

        presentation = read(PRESENTATION)
        bringup = csharp_method_body(
            presentation, r"public\s+IEnumerator\s+Bringup\s*\(\s*\)"
        )
        settle = bringup.find("while (Time.realtimeSinceStartup < until)")
        refresh = bringup.find("displays = Display.displays;", settle + 1)
        disposed_guard = re.search(
            r"if\s*\(\s*_disposed\s*\)\s*yield\s+break\s*;",
            bringup[settle + 1:refresh] if settle >= 0 and refresh >= 0 else "",
        )
        if disposed_guard is None:
            violations.append("presentation missing post-settle disposed guard")

        dispose = csharp_method_body(
            presentation, r"public\s+void\s+Dispose\s*\(\s*\)"
        )
        if "_availabilityRevision++;" not in dispose:
            violations.append("presentation disposal does not invalidate generation")

        entry = read(DUAL_SCREEN)
        host_bringup = csharp_method_body(
            entry, r"IEnumerator\s+Bringup\s*\(\s*\)"
        )
        retained_screen = host_bringup.find("var screen = _screen;")
        retained_host = host_bringup.find("var host = _host;")
        yielded = host_bringup.find("yield return screen.Bringup();")
        presence_read = host_bringup.find(
            "bool present = Display.displays.Length > DsPresentation.DISPLAY;",
            yielded + 1,
        )
        owner_guard = re.search(
            r"if\s*\(\s*!_releaseState\.CanRoute\s*\|\|\s*host\.IsDisposed\s*\|\|\s*"
            r"!ReferenceEquals\s*\(\s*_host\s*,\s*host\s*\)\s*\|\|\s*"
            r"!ReferenceEquals\s*\(\s*_screen\s*,\s*screen\s*\)\s*\)\s*"
            r"yield\s+break\s*;",
            host_bringup[yielded + 1:presence_read]
            if yielded >= 0 and presence_read >= 0 else "",
        )
        if not (0 <= retained_screen < yielded and 0 <= retained_host < yielded):
            violations.append("entry bringup does not retain screen and host before yield")
        if owner_guard is None:
            violations.append("entry bringup missing post-yield disposed/identity guard")
        if re.search(
            r"(?m)^\s*host\.SetDisplayPresent\s*\(\s*present\s*\)\s*;",
            host_bringup[yielded + 1:],
        ) is None:
            violations.append("entry bringup does not use retained host after yield")

        self.assertEqual([], violations)

    def test_host_active_state_requires_unpaused_present_ready_display(self):
        source = read(DUAL_SCREEN)
        host = read(DISPLAY_HOST)
        self.assertRegex(
            host,
            r"bool\s+shouldBeActive\s*=\s*_enabled\s*&&\s*_displayPresent\s*&&\s*"
            r"_presentationReady\s*&&\s*!_paused\s*;",
        )
        self.assertRegex(source, r"bool\s+present\s*=\s*now\s*>\s*DsPresentation\.DISPLAY\s*;")
        self.assertIn("host.SetPresentationReady(true, screen.Width, screen.Height);", source)
        update = csharp_method_body(source, r"void\s+Update\s*\(\s*\)")
        self.assertRegex(update, r"if\s*\(\s*!DsTouch\.Ready\s*\)")
        self.assertIn("_host.SetPresentationReady(false);", update)
        self.assertIn("_screen.MarkUnavailable();", update)
        pause = csharp_method_body(
            source,
            r"void\s+OnApplicationPause\s*\(\s*bool\s+paused\s*\)",
        )
        self.assertTrue(pause)
        self.assertIn("_host.SetPaused(paused);", pause)
        self.assertNotIn("SetActive(!paused)", pause)

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

    def test_frame_discovery_retries_missing_sources_at_bounded_intervals(self):
        source = read(PORT_FRAME)
        tick = csharp_method_body(source, r"public\s+void\s+Tick\s*\(\s*float\s+dt\s*\)")
        self.assertTrue(tick)
        self.assertIn("DsGameData.InGame", tick)
        self.assertIn("inGame != _lastInGame", tick)
        self.assertIn("_buildAttempted", tick)
        self.assertIn("Time.unscaledTime >= _nextBuildAttempt", tick)
        self.assertIn("_nextBuildAttempt = Time.unscaledTime + 0.5f", tick)
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

    def test_live_hud_uses_actual_linked_state_and_no_native_visual_mutation(self):
        adapter = read(PORT_HUD)
        state = read(PORT_HUD_STATE)
        project = read(REPO_ROOT / "tools/shared-patches-tests/SharedPatches.Tests.csproj")
        self.assertIn("new DsPortHudState(new UnityNodes())", adapter)
        self.assertIn("silksong-patches/src/dualscreen/DsPortHudState.cs", project)
        self.assertIn("silksong-patches/src/dualscreen/DsPortFrameState.cs", project)
        self.assertIn("_layers.HUD, DsPresentation.CONTENT_LAYER", adapter)
        self.assertNotIn("UnityEngine", state)
        for forbidden in ("Instantiate(", "NormalizeRenderers(", ".SetActive(",
                          ".enabled =", ".color =", ".sortingOrder =", "AddComponent<"):
            self.assertNotIn(forbidden, adapter)
        self.assertIn("Node(node).gameObject.activeInHierarchy", adapter)
        self.assertIn("_nodes.ActiveInHierarchy(original.Parent)", state)

    def test_live_hud_samples_native_pause_inventory_visibility_and_transition(self):
        sample = csharp_method_body(read(PORT_HUD), r"public\s+DsHudEligibility\s+SampleEligibility\s*\([^)]*\)")
        for required in ("DsGameData.InGame", "GameState.PLAYING", "gm.isPaused",
                         "PlayerData.instance.isInventoryOpen", "gc.IsHudVisible", "HudCanvas.IsVisible",
                         "gc.IsInCinematic", "gm.IsInSceneTransition", "gm.IsLoadingSceneTransition",
                         "_frame.HudReady", "transitionBoundary"):
            self.assertIn(required, sample)
        self.assertNotRegex(sample, r"PlayerData\.[^;]*isInventoryOpen\s*=")

    def test_live_hud_discovery_is_typed_owner_aware_and_retryable(self):
        source = read(RESIDENT_UI)
        body = csharp_method_body(source, r"public\s+bool\s+TryGetHudSources\s*\([^)]*\)")
        for required in ("GameCameras.SilentInstance", "GetComponent<HUDCamera>()", "camera.GameplayChild",
                         "cameras.hudCanvasSlideOut", "cameras.silkSpool", "OwnsCurrentRig",
                         'gameplay.transform.Find(HudCanvasPath)', "DirectHudChild(hudCanvas",
                         'DirectHudChild(direct[2], "Spool")', 'DirectHudChild(spoolRoot, "Bind Orb")',
                         "HudComponents<PlayMakerFSM>", 'FsmName == "health_display"',
                         "HudComponents<CurrencyCounterStack>", "HudComponents<CurrencyCounter>",
                         "HudComponents<BindOrbHudFrame>", "HudComponents<ToolHudIcon>",
                         "new DsHud29980Inventory", "DsHud29980Topology.TryAdmit",
                         "Time.unscaledTime + 0.5f", "_nextHudProbe = 0f"):
            self.assertIn(required, body)
        for contextual_owner in (
            "PositionRelativeTo", "EventRegister", "DeliveryHudIcon",
            "DeactivateAfter2dtkAnimation", "CameraControlAnimationEvents",
            "DisableAfterTime", "ItemCurrencyCounter", "LiquidReserveCounter",
            "ParticleSystem", "Animator",
        ):
            self.assertIn(contextual_owner, body)
        state = read(PORT_HUD_STATE)
        for exact_rule in (
            "class DsHud29980Inventory", "class DsHud29980Topology",
            "TryAdmit(DsHud29980Inventory value", "HealthDisplayDrivers != 2",
            "ExtrasEventRegisters, 4, 8, 3", "ExtrasAnimators, 0, 1, 0",
            "SpoolChildren, Spool",
            "DeliveryChildren, Delivery", "DeliveryHudIcons != 1",
            "CrestDeactivators != 1", "BurstCameraControls != 1",
            "DripsRootParticleSystems != 1",
        ):
            self.assertIn(exact_rule, state)
        for exact_root in ("Health", "Extras", "Thread", "Tool Icons", "Crest Get Effects",
                           "Delivery Icon", "Counters", "Blue_Health_Overblue_HUD_burst",
                           "Blue_Health_Overblue_HUD_drips"):
            self.assertIn(f'"{exact_root}"', state)
        self.assertIn('const string HudCanvasPath = "Anchor TL/Hud Canvas Offset/Hud Canvas"', source)
        self.assertNotIn("HudVisualRoot", source)
        self.assertNotIn("HudComponents<BlueHealth>", body)
        self.assertNotIn("GetComponents<BlueHealth>", body)
        self.assertNotIn("GameObject.Find(", body)
        self.assertNotIn("Resources.FindObjectsOfTypeAll", body)

    def test_hud_evidence_records_exact_census_manifest_and_host_boundary(self):
        evidence = json.loads(read(HUD_EVIDENCE))
        self.assertEqual("Linux Silksong 1.0.29980", evidence["gameVersion"])
        self.assertEqual(9, len(evidence["directRoots"]))
        self.assertEqual(
            ["Bind Orb", "Thread Spool", "Spool Appear", "Bind Cancel Effects",
             "Curse Silk Cancel Effects"],
            evidence["exactInventory"]["Thread"]["spoolChildren"],
        )
        self.assertEqual(
            [0, 1, 0], evidence["exactInventory"]["Extras"]["AnimatorByChild"]
        )
        self.assertIn("do not prove Unity rendering", evidence["evidenceBoundary"])
        final_compile = evidence["finalCompile"]
        self.assertEqual("776c8fa30a6ca161e8ee2522f485cf8d339b9025", final_compile["head"])
        self.assertEqual(0, final_compile["exitCode"])
        self.assertEqual(7, final_compile["expectedWarnings"])
        self.assertEqual(0, final_compile["expectedErrors"])
        receipt = json.loads(read(REPO_ROOT / final_compile["receipt"]))
        for field in ("head", "exitCode"):
            self.assertEqual(final_compile[field], receipt[field])
        self.assertEqual(final_compile["expectedWarnings"], receipt["warnings"])
        self.assertEqual(final_compile["expectedErrors"], receipt["errors"])
        self.assertEqual(final_compile["expectedPatchCheckDllSha256"], receipt["outputSha256"])
        self.assertEqual(evidence["compileManifest"]["sourceCount"], receipt["compiledSourceCount"])
        self.assertEqual(
            evidence["compileManifest"]["sha256OfConcatenatedEntries"],
            receipt["compiledSourceManifestSha256"],
        )
        self.assertTrue((REPO_ROOT / final_compile["sourceManifest"]).is_file())
        self.assertTrue((REPO_ROOT / final_compile["log"]).is_file())
        self.assertEqual(3, evidence["staleCachedProjectFailure"]["errors"])

    def test_task112_evidence_covers_final_sources_tests_and_exact_legal_acknowledgement_bridge(self):
        receipt = json.loads(read(TASK112_EVIDENCE / "completion.json"))
        compile_receipt = receipt["managedCompile"]
        manifest_path = TASK112_EVIDENCE / "source-manifest.sha256"
        manifest = manifest_path.read_bytes()
        entries = manifest.decode("utf-8").splitlines()

        self.assertEqual("Linux Silksong 1.0.29980", receipt["gameVersion"])
        self.assertRegex(receipt["sourceCommit"], r"^[0-9a-f]{40}$")
        self.assertEqual(0, compile_receipt["exitCode"])
        self.assertEqual(7, compile_receipt["warnings"])
        self.assertEqual(0, compile_receipt["errors"])
        self.assertEqual(68, compile_receipt["compiledSourceCount"])
        self.assertEqual(68, len(entries))
        self.assertEqual(
            hashlib.sha256(manifest).hexdigest(),
            compile_receipt["compiledSourceManifestSha256"],
        )
        self.assertIn("--no-restore", compile_receipt["command"])
        self.assertIn("--no-incremental", compile_receipt["command"])
        self.assertIn("do not prove Unity display-1 pixels", receipt["evidenceBoundary"])

        project = read(REPO_ROOT / "tools" / "shared-patches-tests" / "obj" /
                       "task99-journal-continuation-20260908" / "ss-project" /
                       "PatchCheck.csproj")
        includes = re.findall(r'<Compile Include="([^"]+)"', project)
        ordered_paths = []
        for include in includes:
            normalized = include.replace("\\", "/")
            ordered_paths.append(normalized.split("/HollowKnightAndroid-h1/", 1)[1])
        manifest_paths = [entry.split("  ", 1)[1] for entry in entries]
        self.assertEqual(ordered_paths, manifest_paths)

        reviewed_path = TASK112_EVIDENCE / "reviewed-source-manifest.sha256"
        reviewed = reviewed_path.read_bytes()
        reviewed_entries = reviewed.decode("utf-8").splitlines()
        self.assertEqual(hashlib.sha256(reviewed).hexdigest(),
                         receipt["reviewedSourceManifestSha256"])
        reviewed_paths = []
        for entry in reviewed_entries:
            digest, relative_path = entry.split("  ", 1)
            reviewed_paths.append(relative_path)
            historical = subprocess.run(
                ["git", "-C", str(REPO_ROOT), "show",
                 f"{receipt['sourceCommit']}:{relative_path}"],
                check=True,
                capture_output=True,
            ).stdout
            self.assertEqual(hashlib.sha256(historical).hexdigest(), digest)
        for required in (
            "tools/silksong-patches/src/dualscreen/DsPortMap.cs",
            "tools/silksong-patches/src/dualscreen/DsPortOverlays.cs",
            "tools/bundle-surgery/RedirectUIMsgDismiss.cs",
            "tools/bundle-surgery/Program.cs",
            "tools/bundle-surgery/BundleSurgery.csproj",
            "tools/ci/tests/test_dual_souls_ui_port.py",
            "tools/shared-patches-tests/SilksongPortSelectionTests.cs",
            "tools/shared-patches-tests/SilksongPortOverlayTests.cs",
        ):
            self.assertIn(required, reviewed_paths)

        injector_manifest_path = TASK112_EVIDENCE / "injector-source-manifest.sha256"
        injector_manifest = injector_manifest_path.read_bytes()
        injector_entries = injector_manifest.decode("utf-8").splitlines()
        self.assertEqual(hashlib.sha256(injector_manifest).hexdigest(),
                         receipt["injectorSourceManifestSha256"])
        injector_paths = [entry.split("  ", 1)[1] for entry in injector_entries]
        for required in (
            "tools/bundle-surgery/RedirectUIMsgDismiss.cs",
            "tools/bundle-surgery/Program.cs",
            "tools/bundle-surgery/BundleSurgery.csproj",
            "src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/Il2cppConverter.kt",
        ):
            self.assertIn(required, injector_paths)

        bridge = receipt["legalAcknowledgementBridge"]
        self.assertEqual("1.0.29980", bridge["pinnedGameVersion"])
        self.assertEqual(
            "1886e0884a720b0b53412e04f912fb6d7c31d7c9da9cd365e9ac6b85fc4bc179",
            bridge["pinnedNativeTailSha256"],
        )
        self.assertEqual("tools/bundle-surgery/RedirectUIMsgDismiss.cs",
                         bridge["rewriteSource"])
        self.assertEqual(0, bridge["build"]["exitCode"])
        self.assertEqual(0, bridge["inject"]["exitCode"])
        self.assertEqual(0, bridge["verifyExisting"]["exitCode"])
        self.assertEqual(bridge["inject"]["outputSha256"],
                         bridge["verifyExisting"]["outputSha256"])
        self.assertIn("injected one armed UIMsgBase companion dismissal wait bridge",
                      read(REPO_ROOT / bridge["inject"]["log"]))
        self.assertIn("companion dismissal bridge already present; copied unchanged",
                      read(REPO_ROOT / bridge["verifyExisting"]["log"]))
        input_roles = {injector_input["role"] for injector_input in bridge["inputs"]}
        self.assertEqual(
            {"native-managed-assembly", "injector-binary", "mono-cecil",
             "assets-tools", "compile-response"},
            input_roles,
        )
        for injector_input in bridge["inputs"]:
            self.assertRegex(injector_input["sha256"], r"^[0-9a-f]{64}$")

        shared_build = receipt["sharedTestsBuild"]
        self.assertEqual(0, shared_build["exitCode"])
        self.assertIn("--no-restore", shared_build["command"])
        self.assertIn("--no-incremental", shared_build["command"])
        shared_manifest_path = REPO_ROOT / shared_build["sourceManifest"]
        shared_manifest = shared_manifest_path.read_bytes()
        shared_entries = shared_manifest.decode("utf-8").splitlines()
        self.assertEqual(shared_build["sourceCount"], len(shared_entries))
        self.assertEqual(hashlib.sha256(shared_manifest).hexdigest(),
                         shared_build["sourceManifestSha256"])
        for entry in shared_entries:
            digest, relative_path = entry.split("  ", 1)
            historical = subprocess.run(
                ["git", "-C", str(REPO_ROOT), "show",
                 f"{receipt['sourceCommit']}:{relative_path}"],
                check=True,
                capture_output=True,
            ).stdout
            self.assertEqual(hashlib.sha256(historical).hexdigest(), digest)
        self.assertRegex(shared_build["outputSha256"], r"^[0-9a-f]{64}$")
        self.assertIn("Build succeeded.", read(REPO_ROOT / shared_build["log"]))

        runs = {run["name"]: run for run in receipt["testRuns"]}
        for name in ("focused-python", "focused-shared", "full-python",
                     "full-shared", "bundle-surgery"):
            self.assertIn(name, runs)
            self.assertEqual(0, runs[name]["exitCode"])
            self.assertEqual(0, runs[name]["failed"])
            self.assertGreater(runs[name]["passed"], 0)
            self.assertTrue((REPO_ROOT / runs[name]["log"]).is_file())
        for name in ("focused-shared", "full-shared"):
            self.assertEqual(["dotnet", "vstest"], runs[name]["command"][:2])
            self.assertIn(shared_build["output"], runs[name]["command"])
            self.assertEqual(shared_build["outputSha256"], runs[name]["assemblySha256"])

        compiler_log = read(REPO_ROOT / compile_receipt["log"])
        self.assertIn("Build succeeded.", compiler_log)
        self.assertIn("7 Warning(s)", compiler_log)
        self.assertIn("0 Error(s)", compiler_log)

    def test_hud_restoration_precedes_every_composition_destruction(self):
        frame = read(PORT_FRAME)
        destroy = csharp_method_body(frame, r"void\s+DestroyComposition\s*\(\s*\)")
        notify = destroy.index("BeforeCompositionDestroyed?.Invoke();")
        self.assertLess(notify, destroy.index("_rendererMasks[i].Dispose();"))
        self.assertLess(notify, destroy.index("UnityEngine.Object.Destroy("))
        self.assertIn("_frame.BeforeCompositionDestroyed += Restore", read(PORT_HUD))
        dispose = csharp_method_body(read(PORT_RUNTIME), r"public\s+void\s+Dispose\s*\(\s*\)")
        self.assertLess(dispose.index("_hud.Dispose();"), dispose.index("_frame.Dispose();"))
        self.assertLess(dispose.index("_frame.Dispose();"), dispose.index("_layers.Dispose();"))
        tick = csharp_method_body(read(PORT_RUNTIME), r"public\s+void\s+Tick\s*\([^)]*\)")
        self.assertLess(tick.index("_hud.CheckEligibility("), tick.index("_frame.Tick("))
        self.assertLess(tick.index("RestoreHud();"), tick.index("_frame.InvalidateResidentSources();"))
        restore = csharp_method_body(read(PORT_RUNTIME), r"public\s+void\s+RestoreHud\s*\([^)]*\)")
        self.assertIn("try { _hud.Restore(); }", restore)
        self.assertIn("try { _map.Invalidate(); }", restore)
        self.assertIn("finally { _overlays.RestoreNative(); }", restore)
        self.assertGreaterEqual(restore.count("finally"), 2)

    def test_native_pre_unload_events_are_owner_rebound_and_completion_only_rearms(self):
        source = read(DUAL_SCREEN)
        bind = csharp_method_body(source, r"void\s+BindGameManager\s*\(\s*\)")
        unbind = csharp_method_body(source, r"void\s+UnbindGameManager\s*\(\s*\)")
        self.assertIn("ReferenceEquals(current, _gameManager)", bind)
        self.assertLess(bind.index("_port.RestoreHud();"), bind.index("UnbindGameManager();"))
        for event, handler in (("GameStateChange", "_stateHandler"),
                               ("GamePausedChange", "_pauseHandler"),
                               ("UnloadingLevel", "_unloadHandler"),
                               ("OnFinishedEnteringScene", "_finishedHandler")):
            self.assertIn(f"_gameManager.{event} += {handler}", bind)
            self.assertIn(f"_gameManager.{event} -= {handler}", unbind)
        self.assertIn("ReferenceEquals(_gameManager, null)", unbind)
        self.assertIn("_managerCallbacks.Unbind()", unbind)
        self.assertIn("_managerCallbacks.Bind(current,", bind)
        self.assertIn("_stateHandler = state => subscription.State(", bind)
        self.assertIn("_pauseHandler = paused => subscription.Pause(paused)", bind)
        self.assertIn("_unloadHandler = subscription.Unloading", bind)
        self.assertIn("_finishedHandler = () => subscription.Finished()", bind)
        self.assertIn("new DsHudManagerCallbacks(() => GameManager.SilentInstance", source)
        state = read(PORT_HUD_STATE)
        self.assertIn("ReferenceEquals(owner, _owner)", state)
        self.assertIn("ReferenceEquals(owner, _current())", state)
        self.assertIn("if (!Accept(owner) || !TransitionPending) return", state)
        self.assertNotRegex(source, r"sceneUnloaded\s*\+=")
        pre = csharp_method_body(source, r"void\s+BeforeNativeUnload\s*\(\s*\)")
        self.assertIn("_port.BeforeSceneTransition();", pre)
        finished = csharp_method_body(source, r"void\s+OnFinishedEnteringScene\s*\(\s*\)")
        self.assertIn("_port.FinishedEnteringScene();", finished)
        self.assertNotIn("Restore", finished)
        late = csharp_method_body(source, r"void\s+LateUpdate\s*\(\s*\)")
        self.assertIn("_port.LateTick();", late)
        self.assertIn("_port.RestoreHud();", late)
        self.assertIn("[DefaultExecutionOrder(10000)]", source)

    def test_native_template_layer_provenance_is_reconciled_before_tick_and_restore(self):
        adapter = read(PORT_HUD)
        state = read(PORT_HUD_STATE)
        self.assertIn('typeof(RadialHudIcon).GetField("templateNotch", flags)', adapter)
        self.assertIn('typeof(RadialHudIcon).GetField("notches", flags)', adapter)
        self.assertIn("GetComponentsInChildren<RadialHudIcon>(true)", adapter)
        self.assertIn("yield return new DsHudClone", adapter)
        self.assertNotIn("Resources.FindObjectsOfTypeAll", adapter)
        late = csharp_method_body(state, r"public\s+void\s+LateTick\s*\([^)]*\)")
        restore = csharp_method_body(state, r"public\s+void\s+Restore\s*\(\s*\)")
        self.assertLess(late.index("ReconcileLayers()"), late.index("Relayer(route.Node)"))
        self.assertLess(restore.index("ReconcileLayers()"), restore.index("_nodes.SetParent("))
        self.assertIn("MapClone(children[i], originals[i], sources)", state)
        self.assertIn("return OriginalLayer(template, sources)", state)
        self.assertIn("New private-layer HUD node has no native layer provenance", state)

    def test_faulted_enclosing_release_retains_independent_restoration_only_pump(self):
        source = read(DUAL_SCREEN)
        self.assertIn("new DsPresentation(_releasePump.transform)", source)
        self.assertIn("DsHudReleasePump.Create(_releaseState)", source)
        bootstrap = csharp_method_body(source, r"static\s+void\s+Bootstrap\s*\(\s*\)")
        self.assertIn("DsHudReleasePump.BlocksReplacement", bootstrap)
        self.assertIn("ReferenceEquals(Instance, null)", bootstrap)
        for method in ("Update", "LateUpdate", "RequestActivation", "OnDisplaysUpdated", "OnApplicationPause"):
            body = csharp_method_body(source, rf"void\s+{method}\s*\([^)]*\)")
            self.assertIn("!_releaseState.CanRoute", body)
        shutdown = csharp_method_body(source, r"void\s+Shutdown\s*\(\s*\)")
        self.assertIn("_releaseState.RequestShutdown(", shutdown)
        self.assertNotIn("Instance = null", shutdown)
        self.assertNotIn("_port = null", shutdown)
        self.assertIn("void OnDestroy() { Shutdown(); }", source)
        self.assertIn("void OnApplicationQuit() { Shutdown(); }", source)
        release = csharp_method_body(source, r"void\s+ReleasePresentation\s*\(\s*\)")
        self.assertIn("_releaseState.ReleasePresentation()", release)
        self.assertNotIn("_screen.Dispose()", release)
        pump = source.split("public sealed class DsHudReleasePump", 1)[1]
        self.assertIn("DontDestroyOnLoad(go)", pump)
        self.assertIn("_state.Retry()", pump)
        self.assertLess(pump.index("if (!_state.Completed) return"), pump.index("Destroy(gameObject)"))
        for forbidden in ("DirectDisplayHost", "Bringup(", "SetEnabled(", "DsInput", "_port.", "LateTick("):
            self.assertNotIn(forbidden, pump)
        state = read(PORT_HUD_STATE)
        release = csharp_method_body(state, r"public\s+void\s+ReleasePresentation\s*\(\s*\)")
        self.assertLess(release.index("_restore()"), release.index("_disposeContent()"))
        self.assertLess(release.index("_disposeContent()"), release.index("_release()"))
        self.assertLess(release.index("_release()"), release.index("_retire()"))
        self.assertLess(release.index("_retire()"), release.index("Completed = true"))

    def test_existing_tab_input_reaches_page_only_toggle_without_hud_restoration(self):
        frame = read(PORT_FRAME)
        runtime = read(PORT_RUNTIME)
        gesture = csharp_method_body(frame, r"public\s+void\s+OnGesture\s*\([^)]*\)")
        self.assertLess(gesture.index("TabPressed?.Invoke"), gesture.index("Select(_tabs[i].Role)"))
        self.assertIn("_frame.TabPressed += OnPageTabPressed", runtime)
        handler = csharp_method_body(runtime, r"void\s+OnPageTabPressed\s*\([^)]*\)")
        self.assertIn("DsPortFrameState.PagesVisibleAfterTab(_pagesVisible, role == _frame.SelectedRole)", handler)
        page = csharp_method_body(runtime, r"public\s+void\s+SetPagesVisible\s*\([^)]*\)")
        self.assertIn("_layers.Pages.gameObject.SetActive(IsVisible && visible)", page)
        self.assertNotIn("_hud.", page)
        self.assertNotIn("_layers.SetVisible", page)

    def test_tasks_adapter_has_its_own_native_initialization_and_input_boundary(self):
        path = DUALSCREEN_SOURCES / "DsPortProgress.Tasks.cs"
        self.assertTrue(path.is_file(), "native Tasks page is not implemented")
        source = read(path)
        for required in ("InventoryItemQuestManager", "InventoryItemMainQuest", "QuestItemDescription",
                         "mainQuestTemplateItem", "subQuestItemTemplate", '"isPaneVisible", false',
                         "IDsPortJournalNative", "IDsPortSelection", "SetDisplay((InventoryItemSelectable)",
                         "SetClampedPos(", "ClearSelection(", "DsJournalAdmission.ReleaseOwned("):
            self.assertIn(required, source)
        owner = read(DUALSCREEN_SOURCES / "DsPortProgress.cs")
        self.assertIn("_tasks = new Tasks(frame);", owner)
        self.assertIn("_tasks.Tick(eligible);", owner)
        self.assertIn("_tasks.OnGesture(gesture)", owner)
        for forbidden in ("GetCollectedItems(", "ReportPreviouslyCollected(", ".PaneStart(",
                          ".PaneEnd(", ".SetSelected(", ".GetStartSelectable("):
            self.assertNotIn(forbidden, source)

    def test_loadout_crest_factory_defers_optional_visual_without_cloning_asset_lifecycle(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        inspect = csharp_method_body(source, r"static\s+void\s+InspectAssets\s*\([^)]*\)")
        self.assertNotIn("crest.DisplayPrefab != null", inspect)
        factory = csharp_method_body(source, r"void\s+BuildOwnedCrests\s*\([^)]*\)")
        for token in ("ScriptableObject.CreateInstance<ToolCrest>()", "DsPortCrestSetup.Run(",
                      "(ToolCrest.SlotInfo[])source.Slots.Clone()", 'Set(crest, "<CrestData>k__BackingField", exact)',
                      'Set(page.Crests, "templateCrest", null)', "VerifyCrestAuthority(page, crest, source)"):
            self.assertIn(token, factory)
        for token in ("Object.Instantiate(source)", '"previousVersion"', '"upgradedVersion"', 'Set(source,'):
            self.assertNotIn(token, factory)
        visual = csharp_method_body(source, r"bool\s+TryCrestVisual\s*\([^)]*\)")
        for token in ("crest.CrestData.DisplayPrefab", "Inventory.AuxiliaryProblem(prefab)",
                      'Get(crest, "spawnedDisplayObjects")', 'Set(crest, "activeDisplayObject", visual)',
                      "Inventory.AuxiliaryProblem(visual)"):
            self.assertIn(token, visual)
        self.assertLess(visual.index("Inventory.AuxiliaryProblem(visual)"), visual.index("visual.SetActive(true)"))
        verify = csharp_method_body(source, r"static\s+void\s+VerifyCrestAuthority\s*\([^)]*\)")
        for token in ("ReferenceEquals(crest.CrestData, source)", "slot.Crest != crest", "slot.SlotIndex != i",
                      'Get(slot, "getSavedDataOverride")', 'Get(slot, "setSavedDataOverride")', "SaveCallback("):
            self.assertIn(token, verify)

    def test_loadout_floating_actions_use_exact_native_config_and_save_callback(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        for token in ("CaptureFloatingSlots(page)", "FloatingCurrent(page)", "AllSlots(page)",
                      'Get(page.Floating, "currentConfig")', 'Get(page.Token.List, "extraSlots")',
                      'Get(descriptor, "Id")', "ExtraToolEquips.GetData(", "typeof(InventoryFloatingToolSlots)",
                      "DsPortSlotRefresh.Try(", "slot.Crest == null", "slot.SlotIndex == -1"):
            self.assertIn(token, source)
        self.assertNotIn("ExtraToolEquips.SetData(", source)
        self.assertNotIn("ToolItemManager.SetExtraEquippedTool(", source)
        submit = csharp_method_body(source, r"bool\s+SubmitCore\s*\([^)]*\)")
        self.assertLess(submit.index("RefreshActionSlots(page)"), submit.index("slot.SetEquipped(pending, isManual: true"))

    def test_loadout_optional_tool_detail_is_selected_owned_and_failure_local(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        inspect = csharp_method_body(source, r"static\s+void\s+InspectAssets\s*\([^)]*\)")
        self.assertNotIn("CheckItem(item)", inspect)
        # This contract is the tool's optional description, not the distinct
        # socket-resource Item setter's static custom-icon cache boundary.
        self.assertNotIn("requires owned cache adaptation", csharp_method_body(source, r"bool\s+TryDisplayExtra\s*\([^)]*\)"))
        display = csharp_method_body(source, r"void\s+IDsPortSelection\.Display\s*\([^)]*\)")
        self.assertIn("DsPortDetailAttempt.Try(", display)
        self.assertIn("TryDisplayExtra(page, entry)", display)
        detail = csharp_method_body(source, r"bool\s+TryDisplayExtra\s*\([^)]*\)")
        for token in ("ExtraDescriptionSection", "AuxiliaryProblem(prefab, setupOwner)", "AuxiliaryProblem(detail, setupOwner)",
                      "detail.transform.SetParent(binding.Destination, false)", "SetupExtraDescription(detail)",
                      "Inventory.ExtraSetupOwner(item)"):
            self.assertIn(token, detail)
        self.assertLess(detail.index("AuxiliaryProblem(detail, setupOwner)"), detail.index("detail.SetActive(true)"))
        self.assertNotIn("_spawnedExtraDescriptions", source)

    def test_loadout_native_locked_description_and_combo_are_owned_prompt_graphs(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        allowed = source[source.index("static bool Allowed("):source.index("static void InspectScope(")]
        for name in ("CrestSocketUnlockInventoryDescription", "InventoryItemComboButtonPromptDisplay",
                     "InventoryItemSelectableButtonEvent", "InventoryItemCollectable", "SetTextMeshProGameText"):
            self.assertIn("typeof(" + name + ")", allowed)
        scope = csharp_method_body(source, r"static\s+void\s+InspectScope\s*\([^)]*\)")
        for field in ("slotIcon", "leftLock", "leftLockGlow", "rightLock", "rightLockGlow",
                      "parentWithoutModifier", "actionButtonWithoutModifier", "parentWithModifier",
                      "actionButtonWithModifier", "modifierUp", "modifierDown", "promptText"):
            self.assertIn('"' + field + '"', scope)
        self.assertIn("InspectPrompts(manager,", source)
        self.assertIn("InspectPrompts(page.Manager,", source)
        self.assertIn("combo.Hide()", csharp_method_body(source, r"static\s+void\s+ClearPrompts\s*\([^)]*\)"))
        for forbidden in (".CustomAction(", ".HasBeenSeen =", ".SaveData ="):
            self.assertNotIn(forbidden, source)

    def test_loadout_hold_observes_raw_release_and_guards_native_continuations(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        runtime = read(PORT_RUNTIME)
        gesture = csharp_method_body(runtime, r"public\s+void\s+OnGesture\s*\([^)]*\)")
        self.assertIn("_progress.ObserveGesture(gesture)", gesture)
        self.assertLess(gesture.index("_progress.ObserveGesture(gesture)"), gesture.index("DsPortGesturePrecedence.Consume("))
        for token in ("DsPortActionHold", "DsPortGuardedRoutine", "SetTouchState(",
                      "DsGestureType.Down", "DsGestureType.Up", "StartUnlockHold(", "CancelAction(page)",
                      '"UnlockHoldRoutine"', "yield return native.Current", "slot.SubmitReleased()",
                      "ExtraLegal(", '"DoExtraPress"', '"SetReloading"'):
            self.assertIn(token, source)
        cancel = csharp_method_body(source, r"void\s+CancelAction\s*\([^)]*\)")
        self.assertNotIn("ExtraReleased(", cancel)
        for method in (r"public\s+void\s+ClearSelection", r"public\s+void\s+DestroyOwned"):
            self.assertIn("CancelAction(page)", csharp_method_body(source, method + r"\s*\([^)]*\)"))
        for forbidden in (".SaveData =", ".Take(1", 'Set(slot, "IsUnlocked"', ".CustomAction(" ):
            self.assertNotIn(forbidden, source)

    def test_hold_liveness_uses_the_single_drained_input_snapshot(self):
        entry = read(DUAL_SCREEN)
        input_source = read(DUALSCREEN_SOURCES / "DsInput.cs")
        runtime = read(PORT_RUNTIME)
        progress = read(DUALSCREEN_SOURCES / "DsPortProgress.cs")
        inventory = read(DUALSCREEN_SOURCES / "DsPortProgress.Inventory.cs")
        loadout = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")

        self.assertIn("public bool SingleTouchActive => _active.Count == 1;", input_source)
        update = csharp_method_body(entry, r"void\s+Update\s*\(\s*\)")
        poll = update.index("_input.Poll();")
        forward = update.index("_port.SetTouchState(_input.SingleTouchActive);")
        gestures = update.index("var gestures = _input.Gestures;")
        self.assertLess(poll, forward)
        self.assertLess(forward, gestures)

        deactivate = csharp_method_body(entry, r"void\s+SetTouchFenceActive\s*\([^)]*\)")
        cancel = deactivate.index("_input.Cancel();")
        forward_cancel = deactivate.index("_port.SetTouchState(_input.SingleTouchActive);")
        dispatch = deactivate.index("var gestures = _input.Gestures;")
        self.assertLess(cancel, forward_cancel)
        self.assertLess(forward_cancel, dispatch)

        runtime_forward = csharp_method_body(runtime, r"public\s+void\s+SetTouchState\s*\([^)]*\)")
        self.assertIn("_progress.SetTouchState(singleTouchActive);", runtime_forward)
        progress_forward = csharp_method_body(progress, r"public\s+void\s+SetTouchState\s*\([^)]*\)")
        self.assertIn("_inventory.SetTouchState(singleTouchActive);", progress_forward)
        self.assertIn("_loadout.SetTouchState(singleTouchActive);", progress_forward)

        for source in (inventory, loadout):
            self.assertNotIn("DsTouch.CollectSecondScreen(", source)
            held = csharp_method_body(source, r"bool\s+TouchHeld\s*\([^)]*\)")
            self.assertIn("_singleTouchActive", held)

    def test_loadout_socket_custom_icon_uses_owned_factory_without_static_item_setter(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        self.assertIn("DisplayLockedSlot(page, locked)", source)
        display = csharp_method_body(source, r"void\s+DisplayLockedSlot\s*\([^)]*\)")
        self.assertIn("PrepareSocketIcon(page)", display)
        self.assertIn("SetDisplay(locked.gameObject)", display)
        icon = csharp_method_body(source, r"void\s+PrepareSocketIcon\s*\([^)]*\)")
        for token in ("Inventory.AuxiliaryProblem(", "Inventory.PrepareAuxiliary(", "page.Text.Prepare(",
                      "Object.Instantiate(", "display.Owner = entry", 'Set(entry, "currentCustomDisplay", display)',
                      'Set(entry, "item", resource)', "UpdateDisplay"):
            self.assertIn(token, icon)
        self.assertNotIn(".Item =", icon)
        self.assertNotIn("_spawnedCustomDisplays", source)
        self.assertNotIn("socket resource custom display requires owned cache adaptation", source)

    def test_loadout_locked_equipped_slot_has_reachable_explicit_remove_tap(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        gesture = csharp_method_body(source, r"public\s+bool\s+OnGesture\s*\([^)]*\)")
        self.assertIn("ReferenceEquals(page.Selected, slot) && slot.IsLocked && slot.EquippedItem != null", gesture)
        self.assertIn("DsPortSlotAction.CanPlaceOrRemove(slot.IsLocked", source)
        self.assertIn("if (!slot.IsLocked && !page.Manager.CanChangeEquips", source)

    def test_loadout_toggle_owns_native_effect_before_source_action_commit(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        dispatch = csharp_method_body(source, r"bool\s+Toggle\s*\([^)]*\)")
        self.assertIn("_actions.Run(() => result = ToggleCore(page))", dispatch)
        toggle = csharp_method_body(source, r"bool\s+ToggleCore\s*\([^)]*\)")
        self.assertIn("PrepareActionEffect(page, entry)", toggle)
        self.assertIn("item.DoToggle(out bool changedVisually)", toggle)
        self.assertLess(toggle.index("PrepareActionEffect(page, entry)"), toggle.index("item.DoToggle("))
        self.assertIn("ExtraLegal(page, entry, item, false)", toggle[toggle.index("PrepareActionEffect(page, entry)"):])
        self.assertIn("ShowActionEffect(page, effect, entry)", toggle)
        effect = csharp_method_body(source, r"GameObject\s+PrepareActionEffect\s*\([^)]*\)")
        for token in ("InspectEffect(prefab)", "Object.Instantiate(", "InspectEffect(effect)", "page.Text.Prepare("):
            self.assertIn(token, effect)
        self.assertNotIn(".Spawn(", effect)
        self.assertNotIn("requires owned native pool factory", source)

    def test_loadout_repeated_effect_does_not_reconstruct_runtime_callbacks_or_activate_parent(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        prepare = csharp_method_body(source, r"GameObject\s+PrepareActionEffect\s*\([^)]*\)")
        show = csharp_method_body(source, r"void\s+ShowActionEffect\s*\([^)]*\)")
        self.assertNotIn("ReconstructEvent(", show)
        self.assertNotIn("parent.gameObject.SetActive", show)
        self.assertIn("response.IsFullfilled = ReconstructEvent", prepare)
        self.assertIn("response.IsNotFulfilled = ReconstructEvent", prepare)
        self.assertIn('GetMethod("Evaluate"', show)

    def test_loadout_particle_updates_are_owned_without_singleton_creation(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        self.assertIn("PrepareParticleCallbacks(page, effect)", source)
        self.assertIn("PrepareParticleCallbacks(page, page.Root)", source)
        prepare = csharp_method_body(source, r"void\s+PrepareParticleCallbacks\s*\([^)]*\)")
        self.assertIn("particles.enabled = false", prepare)
        self.assertIn("page.ParticleCallbacks.Add", prepare)
        pump = csharp_method_body(source, r"void\s+TickParticles\s*\([^)]*\)")
        self.assertIn("Time.frameCount", pump)
        self.assertIn("gameObject.activeInHierarchy", pump)
        self.assertIn('GetMethod("OnUpdate"', pump)
        self.assertIn("TickParticles(actionPage)", source)
        self.assertNotIn("ComponentSingleton<PlayParticleEffectsCallbackHooks>.Instance", source)

    def test_loadout_slot_actions_use_selected_crest_native_save_authority(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        legal = csharp_method_body(source, r"bool\s+Legal\s*\([^)]*\)")
        self.assertNotIn("ReferenceEquals(slot.Crest.CrestData, current)", legal)
        self.assertIn("page.SourceCrests.TryGetValue(slot.Crest, out var selectedSource)", legal)
        self.assertIn("DsPortSlotAction.CanTargetCrest(page.Crests.CurrentCrest, slot.Crest", legal)
        self.assertIn("VerifyCrestAuthority(page, slot.Crest, selectedSource)", legal)
        self.assertIn("ToolItemManager.GetCrestByName(selectedSource.name)", legal)
        self.assertIn("selectedSource.Slots[slot.SlotIndex]", legal)
        self.assertIn("page.EquippedCrestId != ((PlayerData)page.Token.Data).CurrentCrestID", legal)
        submit = csharp_method_body(source, r"bool\s+SubmitCore\s*\([^)]*\)")
        self.assertIn("RefreshActionSlots(page)", submit)
        self.assertIn("slot.SetEquipped(pending, isManual: true", submit)

    def test_loadout_browse_is_separate_from_source_proven_equip_dispatch(self):
        path = DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs"
        self.assertTrue(path.is_file(), "native Loadout page is not implemented")
        source = read(path)
        for required in ("InventoryItemToolManager", "InventoryToolCrestList", "InventoryToolCrestSlot",
                         "page.Manager.enabled = false;", "SetDisplay((InventoryItemSelectable)",
                         "page.Manager.CanChangeEquips()", "page.Manager.IsHeroCursed", "slot.IsLocked",
                         "item.IsUnlockedNotHidden", "slot.SetEquipped(pending, isManual: true",
                         "ToolItemManager.SetEquippedCrest(", "_selection.TrySubmit()"):
            self.assertIn(required, source)
        for forbidden in (".PaneStart(", ".PaneEnd(", ".SetSelected(", ".GetStartSelectable(",
                          ".StopSwitchingCrests(", ".BeginSwitchingCrest(", ".HasBeenSeen =", ".SaveData ="):
            self.assertNotIn(forbidden, source)
        owner = read(DUALSCREEN_SOURCES / "DsPortProgress.cs")
        self.assertIn("_loadout = new Loadout(frame);", owner)
        self.assertIn("_loadout.OnGesture(gesture)", owner)

    def test_loadout_native_action_preserves_manual_callback_and_fresh_slots(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        for required in ("AssertInputBlocked(page);", 'Get(component, "configs")',
                         'NeedLocal(slot, component.transform, "SlotObject", "CursedSlot")',
                         "page.Manager.IsActionsBlocked", "page.Crests.IsBlocked", "page.Crests.IsSwitchingCrests",
                         "ToolItemManager.GetCrestByName(", "ToolItemManager.GetToolByName(",
                         "ToolItemManager.UnequipTool(equipped)",
                         "slot.SetEquipped(pending, isManual: true, refreshTools: true)",
                         'Get(slot, "OnSetEquipSaved")', 'callback.Method.Name != "SaveEquips"',
                         "slot.SetEquipped(fresh, isManual: false, refreshTools: false)",
                         "InventoryItemToolManager.CanChangeEquipsTypes.Regular", '"selectCrestPrompt"'):
            self.assertIn(required, source)
        self.assertNotIn("ToolItemManager.SetEquippedTools(", source)
        activation = csharp_method_body(source, r"public\s+void\s+ActivateForLayout\s*\([^)]*\)")
        self.assertGreaterEqual(activation.count("AssertInputBlocked(page);"), 2)
        self.assertLess(activation.index("AssertInputBlocked(page);"), activation.index("page.Staging.SetActive(true);"))
        for method in (r"public\s+void\s+Present", r"void\s+IDsPortSelection\.Display", r"public\s+void\s+ClearSelection"):
            body = csharp_method_body(source, method + r"\s*\([^)]*\)")
            for forbidden in ("isManual: true", "SetEquippedCrest(", "UnequipTool("):
                self.assertNotIn(forbidden, body)

    def test_inventory_uses_native_templates_with_typed_read_only_enumeration(self):
        path = DUALSCREEN_SOURCES / "DsPortProgress.Inventory.cs"
        self.assertTrue(path.is_file(), "native Inventory page is not implemented")
        source = read(path)
        hook = read(DUALSCREEN_SOURCES / "DsPortCollectableManager.cs")
        self.assertIn("DsPortCollectableManager : InventoryItemCollectableManager", hook)
        self.assertIn("protected override List<CollectableItem> GetItems()", hook)
        for forbidden in ("base.GetItems(", "GetCollectedItems(", "ReportPreviouslyCollected("):
            self.assertNotIn(forbidden, source + hook)
        for required in ("BindReadOnlyItems(", "InventoryItemCollectable", "SetDisplay((InventoryItemSelectable)",
                         "Object.DestroyImmediate(old)", "AddComponent<DsPortCollectableManager>()",
                         "CustomInventoryDisplay", "SetClampedPos(", "DsJournalAdmission.ReleaseOwned("):
            self.assertIn(required, source)
        owner = read(DUALSCREEN_SOURCES / "DsPortProgress.cs")
        self.assertIn("_inventory = new Inventory(frame);", owner)
        self.assertIn("_inventory.Tick(eligible);", owner)
        self.assertIn("_inventory.OnGesture(gesture)", owner)

    def test_inventory_custom_icons_are_owned_and_extra_details_do_not_reject_enumeration(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Inventory.cs")
        for required in ("BindReadOnlySections(", 'Set(entry, "item", item)',
                         "Object.Instantiate(item.CustomInventoryDisplay, entry.IconTransform)",
                         "display.Owner = entry;", "entry.gameObject.activeInHierarchy",
                         "typeof(CustomInventoryItemCollectableDisplay)", "ReadOnlySections(",
                         '"consumableHeader"', "HideHeaderIfNoneBefore = true"):
            self.assertIn(required, source)
        self.assertNotRegex(source, r"entry\.Item\s*=(?!=)")
        for forbidden in ("_spawnedCustomDisplays", "base.GetGridSections("):
            self.assertNotIn(forbidden, source)
        check = csharp_method_body(source, r"static\s+void\s+CheckItem\s*\([^)]*\)")
        self.assertNotIn("ExtraDescriptionSection", check)
        self.assertNotIn("requires owned cache adaptation", check)
        display = csharp_method_body(source, r"void\s+IDsPortSelection\.Display\s*\([^)]*\)")
        self.assertIn("TryDisplayExtra(page, entry)", display)

    def test_inventory_custom_icon_clone_is_revalidated_before_binding_or_activation(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Inventory.cs")
        sections = csharp_method_body(source, r"List<InventoryItemGrid.GridSection>\s+ReadOnlySections\s*\([^)]*\)")
        self.assertIn("AuxiliaryProblem(display.gameObject)", sections)
        self.assertLess(sections.index("Object.Instantiate(item.CustomInventoryDisplay"), sections.index("AuxiliaryProblem(display.gameObject)"))
        self.assertLess(sections.index("AuxiliaryProblem(display.gameObject)"), sections.index("display.Owner = entry;"))
        self.assertLess(sections.index("AuxiliaryProblem(display.gameObject)"), sections.index("entry.gameObject.SetActive(true)"))

    def test_inventory_extra_detail_keeps_direct_native_layout_parent(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Inventory.cs")
        detail = csharp_method_body(source, r"bool\s+TryDisplayExtra\s*\([^)]*\)")
        self.assertIn("detail.transform.SetParent(page.DetailParent, false);", detail)
        self.assertLess(detail.index("detail.SetActive(false);"), detail.index("detail.transform.SetParent(page.DetailParent, false);"))
        self.assertLess(detail.index("detail.transform.SetParent(page.DetailParent, false);"), detail.index("detail.SetActive(true);"))
        self.assertIn("page.DetailRoot = detail;", detail)
        self.assertIn("AuxiliaryProblem(detail, setupOwner)", detail)
        self.assertNotIn("page.DetailStaging.SetActive(true)", detail)
        clear = csharp_method_body(source, r"static\s+void\s+ClearExtra\s*\([^)]*\)")
        self.assertIn("page.DetailRoot.SetActive(false)", clear)
        self.assertIn("Object.DestroyImmediate(page.DetailRoot)", clear)
        self.assertLess(clear.index("Object.DestroyImmediate(page.DetailRoot)"), clear.index("page.DetailMaterials"))

    def test_map_same_observation_recipe_inputs_are_fresh_before_publication(self):
        source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        self.assertIn("new DsPortMapFreshness(source.SnapshotReader())", source)
        self.assertIn("Time.unscaledTime, existing.SnapshotReader", source)
        self.assertIn("source.Freshness.Same(ReadConsumedInputs(source))", source)
        inputs = csharp_method_body(source, r"static\s+IEnumerable<object>\s+ReadRecipeInputs\s*\([^)]*\)")
        for token in ("HeroController.instance", "Gameplay.CompassTool", "MazeController.NewestInstance", "HeroCorpseScene",
                      "HeroDeathScenePos", "HeroDeathSceneSize", "mapZoneInfo", "mapMarkerTemplates", "GetComponentsInChildren<Component>",
                      "ReadSerializedInputs", "localToWorldMatrix", "sharedMaterials", "renderQueue"):
            self.assertIn(token, inputs)
        self.assertNotIn("Serialize(source.Data", source)
        draw = csharp_method_body(source, r"void\s+Draw\s*\(Source\s+source\)")
        self.assertIn("if (!Live(source)) { _retained.Retire(_retireGraph); return; }", draw)
        self.assertLess(draw.index("if (!Live(source))"), draw.index("source.Donors.SetActive(true)"))
        self.assertGreater(draw.rindex("if (!Live(source))"), draw.index("source.Donors.SetActive(true)"))

    def test_map_uses_direct_hierarchy_without_native_camera_callbacks_or_buffers(self):
        source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        for required in ("source.Rooms", "source.Decor", "worldToCameraMatrix", "projectionMatrix",
                         "transparencySortMode", "transparencySortAxis", "source.Donors.SetActive(true)"):
            self.assertIn(required, source)
        for forbidden in (".Render()", ".AddCommandBuffer(", ".RemoveCommandBuffer(", "GetCommandBuffers(",
                          "Camera.onPreRender", "CommandBuffer", "RenderTexture", "RawImage"):
            self.assertNotIn(forbidden, source)
        for field in ("targetTexture", "cullingMask", "rect", "clearFlags", "backgroundColor", "projectionMatrix"):
            self.assertNotRegex(source, rf"(?:camera|Camera)\.{field}\s*=(?!=)")

    def test_map_material_and_group_sorting_preserves_native_authority_and_mixed_queues(self):
        source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        copy_group = csharp_method_body(source, r"static\s+void\s+CopySortingGroup\s*\([^)]*\)")
        copy_renderer = csharp_method_body(source, r"static\s+Renderer\s+CopyRenderer\s*\([^)]*\)")
        refresh = csharp_method_body(source, r"static\s+void\s+RefreshMutableDonors\s*\([^)]*\)")
        for token in ("native.GetComponent<SortingGroup>()", "group.enabled = nativeGroup.enabled",
                      "group.sortingLayerID = nativeGroup.sortingLayerID",
                      "group.sortingOrder = nativeGroup.sortingOrder",
                      "group.sortAtRoot = nativeGroup.sortAtRoot"):
            self.assertIn(token, copy_group)
        for token in ("donor.sharedMaterials = native.sharedMaterials", "donor.sortingLayerID = native.sortingLayerID",
                      "donor.sortingOrder = native.sortingOrder"):
            self.assertIn(token, copy_renderer)
        for token in ("source.SortingGroups", "group.sortingLayerID = nativeGroup.sortingLayerID",
                      "donor.sortingOrder = native.sortingOrder"):
            self.assertIn(token, refresh)
        self.assertNotIn("mixed native queues", source)
        self.assertNotIn("material.renderQueue", csharp_method_body(source, r"void\s+Draw\s*\([^)]*\)"))

    def test_map_adapter_is_reachable_without_native_saved_marker_setup_or_activation(self):
        source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        runtime = read(PORT_RUNTIME)
        self.assertIn("new DsPortMap(_frame)", runtime)
        self.assertIn("_map.Tick(", runtime)
        self.assertIn("_map.OnGesture(gesture)", runtime)
        self.assertIn("_map.Dispose();", runtime)
        for required in ("DsPortMapTransaction.CopyMarkers(", "GetOrCreatePageHost(DsPageRole.Map)",
                         "PrimaryShowing(", 'new GameObject("Native Map Renderer Hierarchy")',
                         "source.Donors.transform.SetParent(source.Host, false)", "activeSource",
                         "CameraRenderToMesh.ActiveSources.GameMap"):
            self.assertIn(required, source)
        donor = csharp_method_body(source, r"static\s+Transform\s+DonorTransform\s*\([^)]*\)")
        self.assertIn("source.Donors.SetActive(false);", donor)
        for forbidden in ("new DsMapView(", "TryOpenQuickMap(", ".WorldMap(", "SetupMapMarkers(",
                          "CalculateMapScrollBounds(", "EnsureArraySize(", "MapPin.ToggleQuickMapView(",
                          "Instantiate(", ".SetupMap("):
            self.assertNotIn(forbidden, source)
        self.assertNotIn("source.Map.gameObject.SetActive", source)
        self.assertNotRegex(source, r"placedMarkers\s*(?:\[[^]]+\])?\s*=(?!=)")

    def test_map_restoration_queue_owns_direct_hierarchy_retirement(self):
        source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        self.assertIn("new DsPortMapRestoreQueue()", source)
        self.assertNotIn("queue.Change(", source)
        donor = csharp_method_body(source, r"static\s+Transform\s+DonorTransform\s*\([^)]*\)")
        self.assertIn("source.Queue.Own(() =>", donor)
        self.assertIn("Object.DestroyImmediate(root)", donor)
        build = csharp_method_body(source, r"Source\s+BuildGraph\s*\([^)]*\)")
        self.assertIn("CaptureSource()", build)
        self.assertIn("_partial.Hold(source)", build)
        self.assertIn("_partial.Retire(_retirePartialGraph)", build)
        partial = csharp_method_body(source, r"void\s+RetirePartialGraph\s*\([^)]*\)")
        self.assertIn("source.Queue.Restore()", partial)
        retire = csharp_method_body(source, r"void\s+RetireGraph\s*\([^)]*\)")
        self.assertLess(retire.index("source.Donors.SetActive(false)"), retire.index("source.Queue.Restore()"))
        dispose = csharp_method_body(source, r"public\s+void\s+Dispose\s*\([^)]*\)")
        self.assertIn("Invalidate();", dispose)
        for field in ("aspect", "orthographicSize", "projectionMatrix"):
            self.assertNotRegex(source, rf"camera\.{field}\s*=(?!=)")

    def test_map_pending_restoration_uses_existing_outer_release_owner(self):
        runtime = read(PORT_RUNTIME)
        restore = csharp_method_body(runtime, r"public\s+void\s+RestoreHud\s*\([^)]*\)")
        self.assertIn("_map.Invalidate();", restore)
        self.assertIn("finally", restore)
        entry = read(DUAL_SCREEN)
        self.assertIn("() => { if (_port != null) _port.RestoreHud(); }", entry)
        self.assertIn("_releaseState.ReleasePresentation()", entry)
        self.assertIn("_nextRetry = Time.unscaledTime + 0.25f", entry)
        self.assertIn("_state.Retry()", entry)
        self.assertIn("DsHudReleasePump.BlocksReplacement", entry)
        content = entry.split("sealed class PortContent", 1)[1].split("public sealed class DsHudReleasePump", 1)[0]
        self.assertLess(content.index("_port.Dispose();"), content.index("_port = null;"))

    def test_map_projects_native_compass_corpse_and_world_pin_groups_read_only(self):
        source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        for required in ("ReadCompassAndCorpse(", "OwnedMapPosition(", "ReadSerializedRooms(",
                         'source.Data.HeroCorpseScene', "DsPortMapTransaction.CompassScene(",
                         "DsPortMapTransaction.PinCanBeActive(", '"mainQuestPins"', '"fleaPinParents"',
                         "CaravanTroupeHunter.PdBools[", "MapPin.CurrentState", "PinOverrides",
                         '"hideIfOtherActive"', '"activeCondition"', "_residency.Probe(",
                         "_residency.Wait();", "_residency.Ready();", "NEEDS_CONTEXT ungenerated native text"):
            self.assertIn(required, source)
        for forbidden in ("PositionCompassAndCorpse(", "UpdateCurrentScene(", ".OnStart(",
                          ".ApplyState(", ".CanBeActive(", ".IsActive =", ".SetPosition("):
            self.assertNotIn(forbidden, source)

    def test_map_cold_recipes_use_serialized_sources_and_owned_renderer_donors(self):
        source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        for required in ('"mapZoneInfo"', '"Parents"', '"Parent"', "ReadSerializedRooms(source)",
                         "DsPortMapTransaction.RoomRecipe(", "DsPortMapTransaction.OtherMapped(",
                         '"checkedSprite"', '"initialSprite"', '"initialColor"', '"mappedParent"',
                         "group.IsFulfilled(data)", '"playerDataOverride"', '"positionsOrdered"',
                         "DsPortMapTransaction.LayoutOffset(", "source.SceneData.PersistentBools.TryGetValue(",
                         "new HashSet<string>(source.Data.scenesMapped)", '"mapMarkerTemplates"',
                         "DsPortMapTransaction.CopyMarkers(templates.Length, 9", '"m_mesh"',
                         "DsPortMapTransaction.TextGeometry(", "OwnedRenderer(", "room.Selected.vertices",
                         '"excludeBounds"', "source.Game.tilemap", "ReferenceEquals(SceneData.instance, source.SceneData)"):
            self.assertIn(required, source)
        for forbidden in ('"lastMappedCount"', '"mapCaches"', '"spawnedMapMarkers"', 'native.BoundsSprite',
                          '.GetCorpsePosition()', '"GetMapPosition"', '"GetSceneInfo"', '.InitZoneMaps(',
                          '.Evaluate(', '.DoLayout(', '.ForceMeshUpdate(', '.LoadFontAsset(',
                          '.RefreshTilemapInfo(', 'Object.Instantiate('):
            self.assertNotIn(forbidden, source)

    def test_map_donors_preserve_native_marker_parent_and_camera_layers(self):
        source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        for required in ("CopyTemplateRenderers(", "template.transform.parent", "TemplateTransform(",
                         '"arrowPrefab"', "node.gameObject.layer = DsPresentation.CONTENT_LAYER",
                         "target.gameObject.layer = DsPresentation.CONTENT_LAYER",
                         "new CameraOwnerAccess(source.RoomsOwner)",
                         "new CameraOwnerAccess(source.DecorOwner)", "TargetCamera.GetValue(Owner)",
                         "MeshRenderer.GetValue(Owner)", "SourceCamera.GetValue(Owner)"):
            self.assertIn(required, source)
        for forbidden in ("renderer.transform.position - template.transform.position", "renderer.transform.lossyScale;"):
            self.assertNotIn(forbidden, source)

    def test_map_corpse_arrow_uses_owned_viewport_and_retained_donor_rotation(self):
        source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        for required in ("DsPortMapTransaction.ProjectToViewport(", "PrepareView(source);",
                         "source.Rotations", "SetRotation(source, source.CorpseArrowDonor", "donor.localRotation = desired",
                         "DirectionToAngle()", "AddNativeRoot(source, source.CorpseArrow", "source.Donors.transform.InverseTransformPoint",
                         "SetCorpseArrowVisible(source, false)", "SetCorpseArrowVisible(source, true)"):
            self.assertIn(required, source)
        self.assertNotIn(".ViewportEdge", source)
        self.assertNotIn("ViewPosUpdated", source)
        prepare = csharp_method_body(source, r"void\s+PrepareView\s*\(Source\s+source\)")
        self.assertIn("SetRotation(source, source.CorpseArrowDonor", prepare)
        self.assertNotIn("source.CorpseArrow.transform.localRotation =", prepare)
        draw = csharp_method_body(source, r"void\s+Draw\s*\(Source\s+source\)")
        self.assertLess(draw.index("PrepareView(source);"), draw.index("source.Donors.SetActive(true)"))

    def test_tasks_known_custom_detail_rejection_retains_native_list(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Tasks.cs")
        display = csharp_method_body(source, r"void\s+IDsPortSelection\.Display\s*\([^)]*\)")
        self.assertIn("DsPortDetailAttempt.Try(", display)
        self.assertIn("TryDisplayCounter(page, entry)", display)
        self.assertIn("() => ClearSelection(page)", display)
        self.assertIn("error => Report(", display)
        self.assertIn("return;", display)
        for forbidden in ("throw ", "_state.Clear", "_failed =", "DestroyOwned"):
            self.assertNotIn(forbidden, display)
        gesture = csharp_method_body(source, r"public\s+bool\s+OnGesture\s*\([^)]*\)")
        self.assertIn("_selection.Select(page, entry, page.Token.Data); return true;", gesture)

    def test_tasks_template_copy_scopes_and_input_block_are_rechecked(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Tasks.cs")
        for required in ("typeof(TextMeshProContainerFitter)", "typeof(TMProOld.TextContainer)",
                         'NeedLocal(component, component.transform, "spriteRenderer")',
                         "AssertInputBlocked(page);", "entry.Quest is FullQuestBase full",
                         "graphic.GetComponentInParent<InventoryItemQuest>()"):
            self.assertIn(required, source)
        activation = csharp_method_body(source, r"public\s+void\s+ActivateForLayout\s*\([^)]*\)")
        self.assertLess(activation.index("AssertInputBlocked(page);"), activation.index("page.Staging.SetActive(true);"))
        self.assertGreaterEqual(activation.count("AssertInputBlocked(page);"), 2)
        self.assertIn("page.Manager.enabled ||", source)
        self.assertIn('Get(page.Manager, "isPaneVisible")', source)
        self.assertIn("GetComponentsInChildren<InventoryPaneInput>(true).Length != 0", source)

    def test_journal_cursor_and_detail_geometry_use_native_world_bounds_and_current_clip(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.cs")
        self.assertIn("var world = InSpace(page.View, page.ScrollParent, null);", source)
        self.assertIn("if (to != null) p = to.InverseTransformPoint(p);", source)
        display = csharp_method_body(source, r"void\s+IDsPortSelection\.Display\s*\([^)]*\)")
        self.assertLess(display.index("page.Manager.SetDisplay("), display.index("UpdateFit(page);"))
        self.assertLess(display.index("page.Cursor.SetTarget("), display.index("ContainFixed(page);"))
        present = csharp_method_body(source, r"public\s+void\s+Present\s*\([^)]*\)")
        self.assertIn("ContainFixed(page);", present)
        self.assertIn("_frame.ContentMask", source)
        self.assertIn("node.gameObject.layer = DsPresentation.CONTENT_LAYER;", source)
        self.assertIn("renderer.sortingOrder = DsPortLayers.PAGE_RENDER_ORDER", source)
        self.assertNotRegex(source, r"renderer\.enabled\s*=")
        self.assertIn("NeedLocal(Get(page.Manager, \"completedText\")", source)
        self.assertIn("NeedLocal(Get(page.Manager, \"encounteredText\")", source)

    def test_journal_adapter_is_reachable_and_keeps_native_browse_read_only(self):
        path = DUALSCREEN_SOURCES / "DsPortProgress.cs"
        self.assertTrue(path.is_file(), "native Journal adapter is not implemented")
        source = read(path)
        for token in ("IDsPortJournalNative", "IDsPortSelection", "new DsPortJournalState(",
                      "new DsPortSelectState(", "staging.SetActive(false);",
                      "Object.Instantiate(source, staging.transform, false)",
                      "DsJournalAdmission.ReleaseOwned(", "forceRenderingOff",
                      "GetPropertyBlock(block)", "SetPropertyBlock(block)",
                      "ForceUpdateLayoutNoCanvas()", "SetClampedPos(", "SetTarget(",
                      '"JustStart"', '"m_Mode"', '"m_CallState"', '"m_BoolArgument"'):
            self.assertIn(token, source)
        no_submit = "bool IDsPortSelection.Submit(object owner, object item) => false;"
        self.assertIn(no_submit, source)
        browse = source.replace(no_submit, "", 1)
        for forbidden in (r"\.SetSelected\s*\(", r"\.PaneStart\s*\(", r"\.PaneEnd\s*\(",
                          r"\.InstantScroll\s*\(", r"\.GetStartSelectable\s*\(",
                          r"\.SetJournalSeen\s*\(", r"\.IsSeen\w*\s*=", r"\.Submit\s*\("):
            self.assertNotRegex(browse, forbidden)
        runtime = read(PORT_RUNTIME)
        self.assertIn("_progress = new DsPortProgress(_frame)", runtime)
        self.assertIn("_progress.OnGesture(gesture)", runtime)
        self.assertIn("_progress.Tick(", runtime)
        self.assertIn("_progress.Dispose();", runtime)
        select = csharp_method_body(read(PORT_FRAME), r"public\s+void\s+Select\s*\([^)]*\)")
        self.assertLess(select.index("_selected = role;"), select.index("SelectionChanged?.Invoke();"))

    def test_production_port_sources_do_not_draw_authored_frame_substitutes(self):
        violations = []
        sources = list(DUALSCREEN_SOURCES.glob("DsPort*.cs")) + [RESIDENT_UI]
        for path in sorted(set(sources)):
            violations.extend(authored_port_visual_violations(path.name, read(path)))
        self.assertEqual([], violations)

    def test_dialogue_carrier_is_reachable_and_never_restores_driver_pose(self):
        source = read(DUALSCREEN_SOURCES / "DsPortOverlays.cs")
        for required in ("sealed class NativeDialogue", "new DsPortDialogueLease(", 'typeof(DialogueBox).GetField("_instance"',
                         "_dialogue.Tick(", "DsPortOverlayRestoration.All(_dialogue.Restore, _credits.Restore,", "carrier.SetParent(retained.Parent, false)",
                         "retained.Root.SetParent(retained.Carrier, false)", "retained.Root.SetParent(retained.Parent != null ? retained.Parent : null, false)",
                         "retained.Root.SetSiblingIndex(retained.Sibling)", '"relativeTo"', '"min"', '"max"',
                         '"AdvanceConversation"', '"isPrintingText"', '"waitingToAdvance"', "RefreshClip(clip);",
                         "GetComponentsInChildren<TextMeshProClipRect>(true)"):
            self.assertIn(required, source)
        dialogue = csharp_method_body(source, r"sealed\s+class\s+NativeDialogue")
        for forbidden in (".ParentOverride =", ".AlphaSelf =", "initialPos", "OffsetY", "SetActive(",
                          "ForceMeshUpdate(", ".enabled = false", "SendEvent", "AdvanceConversation("):
            self.assertNotIn(forbidden, dialogue)
        for field in ("localPosition", "localRotation", "localScale"):
            self.assertNotRegex(dialogue, rf"(?:Root|root)\.{field}\s*=(?!=)")
        runtime = read(PORT_RUNTIME)
        restore = csharp_method_body(runtime, r"public\s+void\s+RestoreHud\s*\([^)]*\)")
        self.assertIn("_overlays.RestoreNative();", restore)
        self.assertGreaterEqual(restore.count("finally"), 2)
        visible = csharp_method_body(runtime, r"public\s+void\s+SetVisible\s*\([^)]*\)")
        self.assertLess(visible.index("_overlays.RestoreNative();"), visible.index("_layers.SetVisible(false)"))

    def test_opening_credits_route_existing_started_owner_without_replaying_native_progression(self):
        source = read(DUALSCREEN_SOURCES / "DsPortOverlays.cs")
        credits = csharp_method_body(source, r"sealed\s+class\s+NativeOpeningCredits")
        for token in ("OpeningGameplayCredits", 'GetField("pd"', "FindObjectsByType<OpeningGameplayCredits>",
                      "ReferenceEquals(_owner.animator, _animator)", "_animator.applyRootMotion",
                      "GetBehaviours<StateMachineBehaviour>()", "new DsPortDialogueLease(",
                      "retained.Root.SetParent(retained.Carrier, false)", "retained.Root.SetParent(retained.Parent != null ? retained.Parent : null, false)",
                      "retained.Root.SetSiblingIndex(retained.Sibling)", "retained.Presentation.Change(",
                      "_owner.gameObject.scene.isLoaded", "_nextProbe", "Pending"):
            self.assertIn(token, credits)
        for forbidden in (".Start(", ".SetActive(", ".enabled =", ".Play(", ".SetBool(",
                          "openingCreditsPlayed =", "Object.Instantiate(", ".ParentOverride =", ".AlphaSelf ="):
            self.assertNotIn(forbidden, credits)
        for field in ("localPosition", "localRotation", "localScale"):
            self.assertNotRegex(credits, rf"(?:Root|root)\.{field}\s*=(?!=)")
        self.assertIn("_credits = new NativeOpeningCredits(layers.Overlays)", source)
        self.assertIn("_credits.Tick(visible)", source)
        restore = csharp_method_body(source, r"public\s+void\s+RestoreNative\s*\([^)]*\)")
        self.assertIn("DsPortOverlayRestoration.All(_dialogue.Restore, _credits.Restore,", restore)

    def test_opening_credits_partial_parent_setup_already_owns_sibling_recovery(self):
        source = read(DUALSCREEN_SOURCES / "DsPortOverlays.cs")
        credits = csharp_method_body(source, r"sealed\s+class\s+NativeOpeningCredits")
        present = csharp_method_body(credits, r"void\s+Present\s*\([^)]*\)")
        self.assertLess(present.index("retained.Root.SetSiblingIndex(retained.Sibling)"),
                        present.index("() => retained.Root.SetParent(retained.Carrier, false)"))
        self.assertIn("DsPortOverlayPlane.TryMap(", present)
        self.assertIn("_data.openingCreditsPlayed", credits)

    def test_tasks_custom_counter_uses_exact_native_template_destination_without_shared_cache(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Tasks.cs")
        for token in ("DsPortOwnedDetail", "TryDisplayCounter(", "full.CustomDescPrefab", "binding.Condition.WillDisplay",
                      '"descSectionParent"', '"subQuestItemTemplate"', '"wasInCompletedSection"',
                      "DsPortDetailAttempt.Try(", "page.Counter.Show(", "page.Counter.Clear();",
                      "Object.Instantiate(prefab, detail.Staging.transform, false)",
                      "detail.Root.transform.SetParent(binding.Destination, false)",
                      "ReferenceEquals(entry.Quest, full)", "Inventory.PrepareAuxiliary(detail.Root)"):
            self.assertIn(token, source)
        for forbidden in ("_spawnedExtraDescriptions", ".ExtraDescPrefab =", ".Select(", ".SetSelected(",
                          ".OnSelected(", ".HasBeenSeen =", ".BeginQuest(", ".TryEndQuest("):
            self.assertNotIn(forbidden, source.replace("_selection.Select(", "adapterSelect("))
        self.assertNotIn("custom counter requires owned prefab-cache adaptation", source)
        display = csharp_method_body(source, r"void\s+IDsPortSelection\.Display\s*\([^)]*\)")
        self.assertIn("page.Manager.SetDisplay((InventoryItemSelectable)entry)", display)
        self.assertIn("TryDisplayCounter(page, entry)", display)

    def test_tasks_counter_native_detail_setup_precedes_activation_and_binds_selected_quest(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Tasks.cs")
        display = csharp_method_body(source, r"void\s+IDsPortSelection\.Display\s*\([^)]*\)")
        self.assertIn("ReferenceEquals(entry.Quest, quest)", display)
        self.assertLess(display.index("page.Manager.SetDisplay((InventoryItemSelectable)entry)"),
                        display.index("TryDisplayCounter(page, entry)"))
        release = csharp_method_body(source, r"static\s+void\s+ReleaseCounter\s*\([^)]*\)")
        self.assertLess(release.index("Object.DestroyImmediate(detail.Root)"), release.index("detail.Text.Clear()"))
        self.assertLess(release.index("detail.Text.Clear()"), release.index("while (detail.Materials.Count != 0)"))

    def test_native_page_retirement_retries_on_inactive_restore_and_blocks_progress_publication(self):
        runtime = read(PORT_RUNTIME)
        restore = csharp_method_body(runtime, r"public\s+void\s+RestoreHud\s*\([^)]*\)")
        self.assertIn("_progress.Invalidate();", restore)
        self.assertGreaterEqual(restore.count("finally"), 3)
        progress = read(DUALSCREEN_SOURCES / "DsPortProgress.cs")
        invalidate = csharp_method_body(progress, r"public\s+void\s+Invalidate\s*\([^)]*\)")
        self.assertLess(invalidate.index("_retiringPages = true"), invalidate.index("_journal.Clear();"))
        self.assertGreaterEqual(invalidate.count("finally"), 3)
        tick = csharp_method_body(progress, r"public\s+void\s+Tick\s*\([^)]*\)")
        self.assertLess(tick.index("if (_retiringPages) Invalidate();"), tick.index("_tasks.Tick(eligible)"))
        gesture = csharp_method_body(progress, r"public\s+bool\s+OnGesture\s*\([^)]*\)")
        self.assertIn("if (_retiringPages) return true;", gesture)
        v2 = read(DUALSCREEN_SOURCES / "DualScreenV2.cs")
        self.assertIn("{ _port.RestoreHud(); return; }", v2)
        self.assertIn("_portContent.Dispose(); else if (_port != null) _port.Dispose();", v2)
        self.assertIn("DsHudReleasePump.Create(_releaseState)", v2)

    def test_page_adapters_latch_destroyed_only_after_native_retirement_returns(self):
        for file in ("DsPortProgress.cs", "DsPortProgress.Tasks.cs", "DsPortProgress.Inventory.cs", "DsPortProgress.Loadout.cs"):
            source = read(DUALSCREEN_SOURCES / file)
            destroy = csharp_method_body(source, r"public\s+void\s+DestroyOwned\s*\([^)]*\)")
            with self.subTest(file=file):
                self.assertIn("if (page.Destroyed) return;", destroy)
                self.assertLess(destroy.index("page.Released = true"), destroy.index("DsJournalAdmission.ReleaseOwned("))
                self.assertGreater(destroy.index("page.Destroyed = true"), destroy.index("DsJournalAdmission.ReleaseOwned("))
                self.assertNotIn("if (page.Released) return;", destroy)
                self.assertIn("page.Stopped.Contains(driver)", destroy)
                self.assertLess(destroy.index("driver.StopAllCoroutines();"), destroy.index("page.Stopped.Add(driver);"))
                self.assertLess(destroy.index("Object.DestroyImmediate(page.Staging);"), destroy.index("page.Staging = null;"))
                if file.endswith("Tasks.cs"):
                    self.assertLess(destroy.index("page.Released = true"), destroy.index("Hide(page.Root, true)"))
                if file.endswith("Loadout.cs"):
                    self.assertNotIn("page.Metadata.Clear()", destroy)
                    self.assertLess(destroy.index("Object.Destroy(metadata);"), destroy.index("page.Metadata.RemoveAt("))

    def test_all_native_page_captures_use_explicit_content_and_real_layout_rebuild(self):
        for file in ("DsPortProgress.cs", "DsPortProgress.Tasks.cs", "DsPortProgress.Inventory.cs", "DsPortProgress.Loadout.cs"):
            source = read(DUALSCREEN_SOURCES / file)
            with self.subTest(file=file):
                capture = csharp_method_body(source, r"DsJournalToken\s+Capture\s*\([^)]*\)")
                self.assertIn("new DsPortPageSnapshot(ReadContent(", capture)
                content = csharp_method_body(source, r"IEnumerable<object>\s+ReadContent\s*\([^)]*\)")
                self.assertIn("yield return", content)
                self.assertNotIn("Json", content)
                self.assertNotIn("Serialize", content)
                activate = csharp_method_body(source, r"public\s+void\s+ActivateForLayout\s*\([^)]*\)")
                self.assertIn("lossyScale", activate)
                self.assertIn("Quaternion.identity", activate)
                self.assertIn("SetActive(true)", activate)
                destroy = csharp_method_body(source, r"public\s+void\s+DestroyOwned\s*\([^)]*\)")
                self.assertIn("StopAllCoroutines", destroy)
                clear = csharp_method_body(source, r"public\s+void\s+ClearSelection\s*\([^)]*\)")
                self.assertIn("_selection.Clear()", clear)
        journal = read(DUALSCREEN_SOURCES / "DsPortProgress.cs")
        tasks = read(DUALSCREEN_SOURCES / "DsPortProgress.Tasks.cs")
        inventory = read(DUALSCREEN_SOURCES / "DsPortProgress.Inventory.cs")
        loadout = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        for required in ("GetKillData(record.name)", "record.IsVisible", "record.Notes"):
            self.assertIn(required, journal)
        for required in ("QuestManager.Version", "quest.IsAccepted", "full.Counters", "main.SubQuests", "sub.GetCurrent()"):
            self.assertIn(required, tasks)
        for required in ("item.CollectedAmount", "item.CustomInventoryDisplay", "item.GetIcon("):
            self.assertIn(required, inventory)
        for required in ("ToolItemManager.Version", "tool.IsUnlockedNotHidden", "data.ToolEquips.GetData(", "data.ExtraToolEquips.GetData("):
            self.assertIn(required, loadout)

    def test_snapshot_polling_failures_and_gesture_rechecks_are_not_stale_publication(self):
        for file in ("DsPortProgress.cs", "DsPortProgress.Tasks.cs", "DsPortProgress.Inventory.cs", "DsPortProgress.Loadout.cs"):
            source = read(DUALSCREEN_SOURCES / file)
            with self.subTest(file=file):
                tick = csharp_method_body(source, r"public\s+void\s+Tick\s*\([^)]*\)")
                self.assertIn("Time.unscaledTime < _nextContentProbe", tick)
                self.assertIn("Time.unscaledTime + .125f", tick)
                self.assertIn("_current = null;", tick)
                self.assertIn("DsPortPageSnapshot.IsReadFailure(e) ? null : _current", tick)
                gesture = csharp_method_body(source, r"public\s+bool\s+OnGesture\s*\([^)]*\)")
                self.assertLess(gesture.index("try"), gesture.index("IsCurrent(page.Token)"))
                self.assertIn("DsPortPageSnapshot.IsReadFailure(e) ? null : page.Token", gesture)
                self.assertNotIn("_nextContentProbe", gesture)
        conditions = csharp_method_body(read(DUALSCREEN_SOURCES / "DsPortProgress.cs"), r"static\s+IEnumerable<object>\s+ReadPaneConditions\s*\([^)]*\)")
        self.assertIn("extra.WillDisplay", conditions)
        self.assertNotIn(".Evaluate(", conditions)

    def test_loadout_selected_detail_uses_exact_tool_crest_or_floating_slot_source(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        display = csharp_method_body(source, r"bool\s+TryDisplayExtra\s*\([^)]*\)")
        self.assertNotIn("selected slot custom detail requires exact slot template binding", display)
        self.assertIn("BindExtra(page, entry)", display)
        self.assertIn("binding.Condition.WillDisplay", display)
        self.assertIn("detail.transform.SetParent(binding.Destination, false)", display)
        self.assertGreaterEqual(display.count("RequireExtraBinding(page, entry, item, binding)"), 3)
        binding = csharp_method_body(source, r"ExtraBinding\s+BindExtra\s*\([^)]*\)")
        for required in ("entry is InventoryItemTool", "entry is InventoryToolCrestSlot", "FloatingCurrent(page)",
                         "page.FloatingSlots.TryGetValue", "retained.SourceSlot", "VerifyCrestAuthority",
                         '"templateCrest"', '"templateSlots"', "slots[(int)slot.Type]", '"descSectionParent"'):
            self.assertIn(required, binding)
        self.assertNotIn("page.DetailCondition", display)
        self.assertNotIn("page.DetailParent", display)
        self.assertIn("Inventory.ExtraSetupOwner(item)", display)
        self.assertNotIn("_spawnedExtraDescriptions", source)

    def test_liquid_tool_extra_uses_exact_native_override_and_owned_meter(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        inventory = read(DUALSCREEN_SOURCES / "DsPortProgress.Inventory.cs")
        detail = csharp_method_body(source, r"bool\s+TryDisplayExtra\s*\([^)]*\)")
        self.assertIn("Inventory.ExtraSetupOwner(item)", detail)
        self.assertIn("AuxiliaryProblem(prefab, setupOwner)", detail)
        self.assertIn("AuxiliaryProblem(detail, setupOwner)", detail)
        self.assertIn("Inventory.OwnExtraMaterials(detail, page.DetailMaterials)", detail)
        self.assertLess(detail.index("Inventory.OwnExtraMaterials("), detail.index("detail.SetActive(true)"))
        self.assertIn("item.SetupExtraDescription(detail)", detail)
        owner = csharp_method_body(inventory, r"public\s+static\s+Type\s+ExtraSetupOwner\s*\([^)]*\)")
        self.assertIn("setup.DeclaringType == typeof(ToolItemStatesLiquid)", owner)
        self.assertIn("setup.DeclaringType == typeof(SavedItem)", owner)
        self.assertIn("throw new InvalidOperationException", owner)
        self.assertIn("setupOwner == typeof(ToolItemStatesLiquid) && type == typeof(LiquidMeter)", inventory)
        for field in ('"liquidParent"', '"liquidSprite"', '"liquidOffset"', '"countText"', '"glandParent"'):
            self.assertIn(field, inventory)
        snapshot = csharp_method_body(source, r"static\s+IEnumerable<object>\s+ReadContent\s*\([^)]*\)")
        for value in ("liquid.HasInfiniteRefills", "liquid.RefillsMax", "liquid.LiquidColor", "liquid.LiquidSavedData", "RefillsLeft"):
            self.assertIn(value, snapshot)
        clear = csharp_method_body(source, r"static\s+void\s+ClearExtra\s*\([^)]*\)")
        self.assertLess(clear.index("Object.DestroyImmediate(page.DetailRoot)"), clear.index("page.DetailMaterials"))
        self.assertNotIn("_spawnedExtraDescriptions", source)
        for forbidden in ("SetExtraSeen(", "TakeLiquid(", "RefillRefills("):
            self.assertNotIn(forbidden, source)

    def test_quest_item_override_uses_bounded_inactive_native_counter_factories(self):
        source = read(DUALSCREEN_SOURCES / "DsPortProgress.Inventory.cs")
        owner = csharp_method_body(source, r"public\s+static\s+Type\s+ExtraSetupOwner\s*\([^)]*\)")
        self.assertIn("setup.DeclaringType == typeof(CollectableItemQuestDisplay)", owner)
        detail = csharp_method_body(source, r"bool\s+TryDisplayExtra\s*\([^)]*\)")
        for token in ("ExtraSetupOwner(item)", "AuxiliaryProblem(detail, setupOwner)", "PrepareQuestExtra(detail, item)",
                      "OwnExtraMaterials(detail, page.DetailMaterials)", "item.SetupExtraDescription(detail)", "RequireExtraBinding(page, entry, item, prefab)"):
            self.assertIn(token, detail)
        self.assertLess(detail.index("PrepareQuestExtra("), detail.index("detail.SetActive(true)"))
        self.assertLess(detail.index("OwnExtraMaterials("), detail.index("detail.SetActive(true)"))
        prepare = csharp_method_body(source, r"static\s+void\s+PrepareQuestExtra\s*\([^)]*\)")
        for token in ("DsPortDetailBudget", "budget.Reserve", "root.activeInHierarchy", "Object.Instantiate(template,", '"spawnedTextDisplays"'):
            self.assertIn(token, prepare)
        materials = csharp_method_body(source, r"public\s+static\s+void\s+OwnExtraMaterials\s*\([^)]*\)")
        for token in ('"counterMaterials"', '"activeState"', '"inactiveState"', '"Material"', "new Material(source)"):
            self.assertIn(token, materials)
        self.assertIn("ReadQuestExtraContent(item)", source)
        self.assertNotIn("_spawnedExtraDescriptions", source)
        self.assertNotIn("IncrementQuestCounter(", source)
        clear = csharp_method_body(source, r"static\s+void\s+ClearExtra\s*\([^)]*\)")
        self.assertLess(clear.index("Object.DestroyImmediate(page.DetailRoot)"), clear.index("page.DetailMaterials"))

    def test_partial_native_page_creation_is_adopted_before_throwing_or_retiring(self):
        for file in ("DsPortProgress.cs", "DsPortProgress.Tasks.cs", "DsPortProgress.Inventory.cs", "DsPortProgress.Loadout.cs"):
            source = read(DUALSCREEN_SOURCES / file)
            with self.subTest(file=file):
                clone = csharp_method_body(source, r"public\s+object\s+CloneInactive\s*\([^)]*\)")
                self.assertNotIn("DestroyOwned(page)", clone)
                self.assertIn("catch (Exception error) { page.CreationError = error; return page; }", clone)
                bind = csharp_method_body(source, r"public\s+void\s+BindAndVerify\s*\([^)]*\)")
                self.assertIn("page.CreationError != null", bind)
                self.assertIn("page.CreationError);", bind)
                self.assertLess(bind.index("page.CreationError != null"), bind.index("page.Manager"))

    def test_owned_native_text_is_wired_before_all_page_and_detail_activation(self):
        for file in ("DsPortProgress.cs", "DsPortProgress.Tasks.cs", "DsPortProgress.Inventory.cs", "DsPortProgress.Loadout.cs"):
            source = read(DUALSCREEN_SOURCES / file)
            with self.subTest(file=file):
                self.assertIn("readonly DsPortOwnedText Text = new DsPortOwnedText()", source)
                bind = csharp_method_body(source, r"public\s+void\s+BindAndVerify\s*\([^)]*\)")
                self.assertIn("page.Text.Prepare(root, token.Content)", bind)
                destroy = csharp_method_body(source, r"public\s+void\s+DestroyOwned\s*\([^)]*\)")
                self.assertLess(destroy.index("Object.DestroyImmediate(page.Staging)"), destroy.index("page.Text.Clear()"))
                self.assertLess(destroy.index("page.Text.Clear()"), destroy.index("page.Destroyed = true"))
        tasks = read(DUALSCREEN_SOURCES / "DsPortProgress.Tasks.cs")
        self.assertIn("detail.Text.Prepare(detail.Root, page.Token.Content)", tasks)
        for file in ("DsPortProgress.Inventory.cs", "DsPortProgress.Loadout.cs"):
            source = read(DUALSCREEN_SOURCES / file)
            detail = csharp_method_body(source, r"bool\s+TryDisplayExtra\s*\([^)]*\)")
            self.assertLess(detail.index("page.DetailText.Prepare(detail, page.Token.Content)"), detail.index("detail.SetActive(true)"))
            clear = csharp_method_body(source, r"static\s+void\s+ClearExtra\s*\([^)]*\)")
            self.assertLess(clear.index("Object.DestroyImmediate(page.DetailRoot)"), clear.index("page.DetailText.Clear()"))
        inventory = read(DUALSCREEN_SOURCES / "DsPortProgress.Inventory.cs")
        self.assertIn("page.Text.Prepare(display.gameObject, page.Token.Content)", inventory)
        loadout = read(DUALSCREEN_SOURCES / "DsPortProgress.Loadout.cs")
        self.assertIn("page.Text.Prepare(visual, page.Token.Content)", loadout)

    def test_owned_native_text_cold_map_uses_retained_mesh_before_owned_generation(self):
        source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        self.assertIn("readonly DsPortOwnedText Text = new DsPortOwnedText()", source)
        self.assertIn("source.Text.Prepare(", source)
        self.assertIn("source.Text.Clear", source)
        self.assertIn("source.Text.GenerateCold(", source)
        self.assertIn('Get(text, "m_mesh")', source)
        helper = read(DUALSCREEN_SOURCES / "DsPortProgress.cs").split("public sealed class DsPortOwnedText", 1)[1]
        self.assertIn("DsPortOwnedGraph", helper)
        self.assertIn('Get(font, "m_characterDictionary")', helper)
        self.assertNotIn("source.characterDictionary", re.sub(r"//[^\n]*", "", helper))
        for field in ("m_fontInfo", "m_glyphInfoList", "m_kerningInfo", "fallbackFontAssets", "fontWeights", "m_StyleDictionary", "s_Instance"):
            self.assertIn(field, helper)
        self.assertNotIn("Resources.Load", helper)

    def test_owned_native_text_coverage_uses_exact_native_text_weight_and_owned_backings(self):
        helper = read(DUALSCREEN_SOURCES / "DsPortProgress.cs").split("public sealed class DsPortOwnedText", 1)[1]
        validate = csharp_method_body(helper, r"void\s+ValidateText\s*\([^)]*\)")
        self.assertIn('Native(text, "ValidateHtmlTag", args)', validate)
        self.assertIn('Native(text, "GetFontAssetForWeight", Get(text, "m_fontWeightInternal"))', validate)
        self.assertIn("DsPortTextPreflight.Resolve(chosen, character", validate)
        self.assertNotIn("_coverageFonts", helper)
        self.assertNotIn("content.TextInputs", validate)
        self.assertIn("foreach (var input in content.TextInputs) Input(input)", helper)
        self.assertIn('_augmentedFallbacks.Add(font.fallbackFontAssets)', helper)
        self.assertIn('Set(text, "m_textInfo", null)', helper)
        self.assertIn('"m_fontMaterials", "m_fontSharedMaterials", "m_fontColorGradientPreset"', helper)
        self.assertIn('new[] { "m_material", "m_sharedMaterial" }', helper)
        cold = csharp_method_body(helper, r"public\s+PaneText\s+CopyCold\s*\([^)]*\)")
        self.assertIn('Set(owned, "m_renderer", renderer)', cold)
        self.assertIn('Set(owned, "m_subTextObjects", new TMProOld.TMP_SubMesh[16])', cold)
        self.assertIn('Data(Get(source, field))', cold)

    def test_map_cold_generation_stays_inactive_publishes_renderer_only_and_retires_exactly_once(self):
        helper = read(DUALSCREEN_SOURCES / "DsPortProgress.cs").split("public sealed class DsPortOwnedText", 1)[1]
        cold = csharp_method_body(helper, r"public\s+PaneText\s+CopyCold\s*\([^)]*\)")
        self.assertIn("staging.SetActive(false)", cold)
        self.assertIn("var coldRoot = new ColdRoot(staging)", cold)
        self.assertIn("_coldRoots.Add(coldRoot)", cold)
        self.assertIn("coldRoot.Text = owned", cold)
        self.assertLess(cold.index("staging.SetActive(false)"), cold.index("AddComponent<PaneText>()"))
        self.assertNotIn("SetParent(", cold)
        map_source = read(DUALSCREEN_SOURCES / "DsPortMap.cs")
        copy = csharp_method_body(map_source, r"static\s+Renderer\s+CopyRenderer\s*\([^)]*\)")
        for token in ("source.Watches.Add(source.Text.WatchCold(text))",
                      "source.Text.CopyCold(text)", "RefreshPropertyBlocks(native, coldRenderer",
                      "source.Text.GenerateCold(cold)", "TransferColdRenderer(",
                      "source, renderer, cold.transform, target, transforms"):
            self.assertIn(token, copy)
        self.assertLess(copy.index("source.Text.CopyCold(text)"),
                        copy.index("RefreshPropertyBlocks(native, coldRenderer"))
        self.assertLess(copy.index("RefreshPropertyBlocks(native, coldRenderer"),
                        copy.index("source.Text.GenerateCold(cold)"))
        transfer = csharp_method_body(map_source, r"static\s+Renderer\s+TransferColdRenderer\s*\([^)]*\)")
        for token in ("AddComponent<MeshFilter>()", "AddComponent<MeshRenderer>()",
                      "sharedMesh = mesh", "sharedMaterials = generated.sharedMaterials",
                      "CopyPropertyBlocks(source, generated, donor)"):
            self.assertIn(token, transfer)
        for forbidden in ("AddComponent<PaneText>", "AddComponent<TMProOld.TMP_SubMesh>",
                          "AddComponent<TMProOld.TextContainer>"):
            self.assertNotIn(forbidden, transfer)
        retire = csharp_method_body(helper, r"public\s+void\s+RetireCold\s*\(\s*\)")
        for token in ("GetComponentsInChildren<TMProOld.TMP_SubMesh>(true)",
                      "components.Add(text)",
                      "_coldRetirement.Retire(",
                      "coldRoot.Retirement, RetireColdComponent",
                      "Object.DestroyImmediate(root)"):
            self.assertIn(token, retire)
        self.assertLess(retire.index("GetComponentsInChildren<TMProOld.TMP_SubMesh>(true)"),
                        retire.index("components.Add(text)"))
        self.assertLess(retire.index("_coldRetirement.Retire("),
                        retire.index("Object.DestroyImmediate(root)"))
        callback = csharp_method_body(helper, r"static\s+void\s+RetireColdComponent\s*\([^)]*\)")
        self.assertIn('"OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic', callback)
        self.assertIn("method.Invoke(component, null)", callback)
        self.assertIn("DsPortExactRetirement<Component> _coldRetirement", helper)
        self.assertIn("HashSet<PaneText> _coldTexts", helper)
        self.assertIn("_coldTexts.Add(text)", helper)
        donor = csharp_method_body(map_source, r"static\s+Transform\s+DonorTransform\s*\([^)]*\)")
        self.assertLess(donor.index("source.Text.RetireCold()"), donor.index("Object.DestroyImmediate(root)"))
        self.assertLess(donor.index("Object.DestroyImmediate(root)"), donor.index("source.Text.Clear()"))

    def test_native_fade_exception_still_rejects_authored_image_replacements(self):
        source = read(DUALSCREEN_SOURCES / "DsPortOverlays.cs")
        allocation = "_image = go.AddComponent<UnityEngine.UI.Image>();"
        bad_sources = (
            source + "\n" + allocation,  # authored image outside the admitted consumer
            source.replace(allocation, allocation + "\n" + allocation),
            source.replace("_image.sprite = _source.sprite;", "_image.sprite = inventedSprite;"),
            source.replace("_image.material = _source.sharedMaterial;", "_image.material = inventedMaterial;"),
            source.replace("alpha = ScreenFaderState.Alpha;", "alpha = 1f;"),
            source.replace("!_source.enabled", "false"),
        )
        for index, bad in enumerate(bad_sources):
            with self.subTest(mutation=index):
                self.assertNotEqual(source, bad)
                self.assertTrue(authored_port_visual_violations("DsPortOverlays.cs", bad))
        for filename in ("DsPortFrame.cs", "DsPortHud.cs", "DsPortInventory.cs"):
            with self.subTest(filename=filename):
                self.assertTrue(authored_port_visual_violations(filename, source))


if __name__ == "__main__":
    unittest.main()
