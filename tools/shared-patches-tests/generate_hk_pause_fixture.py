"""Compile unchanged production method bodies against bounded rendering stand-ins."""
from pathlib import Path
import re
import sys
import hashlib
import json

# Explicit immutable-original replay; never swap repository source files.
root = Path(sys.argv[3]) if len(sys.argv) > 3 else Path(__file__).resolve().parents[1] / "hollow-knight-patches/src/dualsouls"
fixture_namespace = sys.argv[4] if len(sys.argv) > 4 else "HkPauseContracts"
methods = {
    "HKDualScreen.cs": ("Tick", "LoadConfig", "RelayerHud", "LogoTick", "SyncBgCapture"),
    "HKDualScreen.Bottom.Layering.cs": (
        "CompanionVisible", "ApplyDualScreenToggle", "ApplyLowerPauseGate", "HudFadedInGameplay"),
    "HKDualScreen.Bottom.Hud.cs": ("FrameHudCams", "BuildEquipCharmRow", "UpdateEquipCharmRow", "BuildAreaName", "RefreshHeaderRenderers", "BuildNoMapLabel", "PositionHudStrip"),
    "HKDualScreen.Util.cs": ("SetTmpFont", "NeutralizeDetachedTmpClip", "ItemBounds", "SanitizeDetachedTmpClone", "BuildCharmKeys", "Charms", "CharmGot", "CharmBroken"),
    "HKDualScreen.Bottom.Inventory.cs": ("Refs", "FitOccupiedNative", "BuildNativePaneGraphics", "PositionNativePaneGraphics", "EnsureNativeInventory", "LayoutNativeInventory", "ScrollNativePane", "LayoutNativeDetail", "PopulateInvDetail", "PopulateSpellDetail", "PopulateEquipDetail", "PopulateGeoDetail", "PopulateGodfinderDetail", "ClearInvDetail", "ClearInvDetailLocal"),
    "HKDualScreen.Bottom.Charms.cs": ("TryTmpGlyphBoundsWorld", "CharmNumOf", "EnsureCharmRefs", "LayoutCharmsRedesign", "PopulateCharmDetail", "CharmsTick"),
    "HKDualScreen.Bottom.Frame.cs": ("BuildFrame", "BuildMapMask", "CreateShellRule", "ShellSprite", "MakePillSprite", "DiscardPartialFrame", "BuildTabRow", "ResolveTabDonors", "CopyShellSpriteOrientation", "ShellPoint", "AnimateTabFleurX", "PositionFrame", "OnConfigReloaded", "FitSprite", "UpdateCompanion", "ApplyCompanionCamera", "InvalidateCompanionClones", "RetireCompanionCaches", "TeardownFrame", "TeardownCompanion", "TabSlideTick", "StowSlideClone"),
    "HKDualScreen.DirectDisplay.cs": ("SetDirectDisplayActive", "SetRoleCamerasEnabled", "RestoreReferenceRouting", "TryDirectStep"),
    "HKDualScreen.Bottom.Map.cs": ("MapFrameTick", "ResolveMapArea", "MapTick"),
    "HKDualScreen.Bottom.MapControls.cs": (), # all complete methods; no partial Map playback
    "HKDualScreen.Bottom.MapRenderRoles.cs": (), # complete role partition/event/lifetime bodies
    "HKDualScreen.Bottom.Select.cs": ("ResetMapView", "ResetMapViewAnimated", "MapPinchTick", "TouchToWorld", "PollTouch", "RefreshSelectedDetail", "SetDetailFont", "PositionSelection", "AnimateSelectionBounds", "ResetSelectionAnimation", "PollItemTap", "VisibleItemRenderer", "TrySelectionBounds"),
    "HKDualScreen.Bottom.JournalGuide.cs": ("BuildJournalPane", "BuildGuidePane", "CopyPaneLabel", "ResetJournalDetailScroll", "CloneForTab", "ReadyForTab", "SyncSupplementarySource", "RetireSupplementaryPanes", "DiscardSupplementaryPane", "BuildSupplementaryPane", "GuideRowCondition", "BindJournalLabels", "SetPaneLabel", "SetPaneLabelVisible", "ApplyPaneLabelClip", "PanePixel", "BuildPaneGraphics", "CopyPaneMask", "PositionPaneGraphics", "PositionPaneRule", "PositionPaneMask", "SetPaneSelection", "PaneCursorTick", "LayoutJournal", "LayoutGuide", "JournalTap", "GuideTap", "ScrollSupplementary", "SupplementaryTick", "RefreshJournal", "RefreshGuide"),
}
parts = []
identity = []
config_method = None
for filename, names in methods.items():
    source = (root / filename).read_text(encoding="utf-8")
    if filename == "HKDualScreen.Bottom.Hud.cs" and "void RestoreHudCameraState(" in source:
        names += ("RestoreHudCameraState", "ValidateHudCameraState", "CaptureHudCameraState")
        start=source.index("    Camera hudCameraStateOwner")
        end=source.index("    void RestoreHudCameraState",start)
        declaration=source[start:end]
        parts.append(declaration)
        identity.append({"source":str(root / filename),"declaration":"complete owned gameplay camera state",
                         "source_file_sha256":hashlib.sha256((root / filename).read_bytes()).hexdigest(),
                         "declaration_utf8_lf_sha256":hashlib.sha256(declaration.encode()).hexdigest(),
                         "declaration_identical":True})
    if filename == "HKDualScreen.Bottom.Charms.cs":
        names += tuple(re.findall(r"^    (?:static )?(?:void|bool|CharmActionInputs|CharmActionResult|CharmFeedbackResult)\s+(CharmAction\w+)\s*\(", source, re.M))
        start = source.find("    // Collection action state")
        if start >= 0:
            end = source.index("    // World-space bounds", start)
            text = source[start:end]
            parts.append(text)
            identity.append({"source": str(root / filename), "declaration": "owning collection action state",
                             "source_file_sha256": hashlib.sha256((root / filename).read_bytes()).hexdigest(),
                             "declaration_utf8_lf_sha256": hashlib.sha256(text.encode("utf-8")).hexdigest(),
                             "declaration_identical": True})
    if filename == "HKDualScreen.Util.cs":
        text = source[source.index("    struct CharmState"):source.index("    static void BuildCharmKeys")]
        parts.append(text)
        identity.append({"source": str(root / filename), "declaration": "actual frame-keyed charm cache",
                         "source_file_sha256": hashlib.sha256((root / filename).read_bytes()).hexdigest(),
                         "declaration_utf8_lf_sha256": hashlib.sha256(text.encode("utf-8")).hexdigest(),
                         "declaration_identical": True})
        if "void InvalidateCharmsFrame(" in source:
            names += ("InvalidateCharmsFrame",)
    if filename in ("HKDualScreen.Bottom.MapControls.cs", "HKDualScreen.Bottom.MapRenderRoles.cs"):
        names = re.findall(r"^    (?:static )?(?:void|bool|float|Rect|Sprite|Bounds|MapActionButton|GameObject\[\]|List<Vector3>|int)\s+(\w+)\s*\(", source, re.M)
    if filename == "HKDualScreen.Bottom.Frame.cs" and "struct ShellSpriteFit" in source:
        names += ("SamePoint", "SameBounds", "FitShellSprite", "PositionShellCursor")
        declaration = source[source.index("    struct ShellSpriteFit"):source.index("    static bool SamePoint")]
        parts.append(declaration.replace("struct ShellSpriteFit", "internal struct ShellSpriteFit", 1))
        identity.append({"source": str(root / filename), "declaration": "actual bounded shell fit slots",
                         "source_file_sha256": hashlib.sha256((root / filename).read_bytes()).hexdigest(),
                         "declaration_utf8_lf_sha256": hashlib.sha256(declaration.encode()).hexdigest(),
                         "access_only_redirect": True})
    if filename == "HKDualScreen.Bottom.Select.cs" and "struct SelectionMetric" in source:
        names += ("BindSelectionMetric", "MeasureSelectionMetric")
        declaration = source[source.index("    struct SelectionMetric"):source.index("    static SelectionMetric BindSelectionMetric")]
        parts.append(declaration.replace("struct SelectionMetric", "internal struct SelectionMetric", 1))
        identity.append({"source": str(root / filename), "declaration": "actual selected renderer measurement slots",
                         "source_file_sha256": hashlib.sha256((root / filename).read_bytes()).hexdigest(),
                         "declaration_utf8_lf_sha256": hashlib.sha256(declaration.encode()).hexdigest(),
                         "access_only_redirect": True})
    for name in names:
        match = re.search(r"\b(?:static\s+)?(?:void|bool|float|Rect|Sprite|SelectionMetric|CharmState|CharmActionInputs|CharmActionResult|CharmFeedbackResult|MapActionButton|GameObject\[\]|List<Vector3>|GameObject|string|Vector3|Transform|Renderer|SpriteRenderer|NativePaneLabel|PaneRefs|PaneGraphics|FitResult|Bounds|int)\s+" + name + r"\s*\([^)]*\)\s*\{", source)
        if match is None:
            # Before the fix this gate does not exist; retain an executable RED.
            if name in ("ApplyLowerPauseGate", "ResetJournalDetailScroll"):
                continue
            raise RuntimeError(f"Missing production method {filename}:{name}")
        depth = 1
        end = match.end()
        while depth:
            if source[end] == "{":
                depth += 1
            elif source[end] == "}":
                depth -= 1
            end += 1
        if name == "PositionHudStrip":
            parts.append(source[match.start():end].replace("PositionHudStrip(", "PositionHeaderFixture(", 1))
        elif name == "BuildFrame":
            parts.append(source[match.start():end].replace("BuildFrame(", "BuildFrameBody(", 1))
        elif name == "LoadConfig":
            config_method = source[match.start():end]
        elif name in ("ResetMapViewAnimated", "LayoutNativeInventory", "LayoutCharmsRedesign", "TeardownFrame", "TeardownCompanion", "PollItemTap", "BuildNoMapLabel", "PositionFrame", "BuildMapMask", "CreateShellRule", "MapTick"):
            parts.append(source[match.start():end].replace(name + "(", name + "Body(", 1))
        elif name in ("BuildJournalPane", "BuildGuidePane", "LayoutJournal", "LayoutGuide", "JournalTap", "GuideTap", "ScrollSupplementary"):
            # Only the signature is redirected so the fixture can count calls;
            # the executable production body remains byte-for-byte unchanged.
            parts.append(source[match.start():end].replace(name + "(", name + "Body(", 1))
        else:
            parts.append(source[match.start():end])
        original = source[match.start():end]
        extracted = config_method if name == "LoadConfig" else parts[-1]
        body = original[original.index("{"):]
        assert body == extracted[extracted.index("{"):], f"Changed executable body {filename}:{name}"
        identity.append({"source": str(root / filename), "method": name,
                         "source_file_sha256": hashlib.sha256((root / filename).read_bytes()).hexdigest(),
                         "body_utf8_lf_sha256": hashlib.sha256(body.encode("utf-8")).hexdigest(),
                         "body_identical": True, "signature_only_redirect": original != extracted})
    if filename == "HKDualScreen.Bottom.Hud.cs":
        # Optional on the immutable original; candidate retry/cache declarations
        # come from their owning source, not fixture update orchestration.
        for name in ("equipRowDonor", "equipRowRetry", "equipRowReady", "noMapFit", "shellTitleInkReady", "shellTitleInkRetry"):
            declaration = re.search(r"^    (?:readonly )?[^\n;]+\b" + name + r"\b[^\n]*;\s*$", source, re.M)
            if declaration is not None:
                text = declaration.group()
                parts.append(text)
                identity.append({"source": str(root / filename), "declaration": name,
                                 "source_file_sha256": hashlib.sha256((root / filename).read_bytes()).hexdigest(),
                                 "declaration_utf8_lf_sha256": hashlib.sha256(text.encode("utf-8")).hexdigest(),
                                 "declaration_identical": True})
    if filename == "HKDualScreen.Bottom.MapControls.cs":
        declaration = source.index("    sealed class MapActionButton")
        end = source.index("    void ", declaration)
        parts.append(source[declaration:end].replace("sealed class MapActionButton", "internal sealed class MapActionButton", 1).replace("    MapActionButton mapViewAction", "    internal MapActionButton mapViewAction").replace("    bool mapMarkerMode,", "    internal bool mapMarkerMode,"))
    if filename == "HKDualScreen.Bottom.MapRenderRoles.cs":
        declaration = source.index("    const int MapArtFadeOrder")
        end = source.index("    bool NativeMapRoomArt", declaration)
        parts.append(source[declaration:end])
        identity.append({"source": str(root / filename), "declaration": "MapRenderOrder/comparer/constants/cache/event fields",
                         "source_file_sha256": hashlib.sha256((root / filename).read_bytes()).hexdigest(),
                         "declaration_utf8_lf_sha256": hashlib.sha256(source[declaration:end].encode("utf-8")).hexdigest(),
                         "declaration_identical": True})
    if filename in ("HKDualScreen.Bottom.Inventory.cs", "HKDualScreen.Bottom.Charms.cs"):
        classes = ("PaneRefs", "NativeInventorySlot") if filename.endswith("Inventory.cs") else ("CharmBoard",)
        for name in classes:
            declaration = re.search(r"(?:sealed\s+)?class " + name + r"\s*\{", source)
            depth, end = 1, declaration.end()
            while depth:
                depth += (source[end] == "{") - (source[end] == "}")
                end += 1
            # Access only; declarations and all executable statements are unchanged.
            parts.append("internal " + source[declaration.start():end])
        if filename.endswith("Inventory.cs"):
            parts.append(re.search(r"static readonly string\[\] EQUIP_ORDER\s*=\s*\{[^;]+;", source).group())
    if filename == "HKDualScreen.Bottom.Select.cs":
        declaration=re.search(r"struct Selection\s*\{",source)
        depth,end=1,declaration.end()
        while depth:
            depth+=(source[end]=="{")-(source[end]=="}")
            end+=1
        parts.append(source[declaration.start():end])
    if filename == "HKDualScreen.Bottom.Frame.cs":
        declaration = re.search(r"float ShellPixel \{[^\n]+\}",source)
        if declaration is None:
            raise RuntimeError("Missing production ShellPixel property")
        parts.append(declaration.group())
        parts.append(re.search(r'const string ShellRulePng = "[^"]+";', source).group())
    if filename == "HKDualScreen.Bottom.JournalGuide.cs":
        declaration = source[source.index("    sealed class PaneGraphics"):source.index("    sealed class NativePaneLabel")]
        parts.append(declaration.replace("sealed class PaneGraphics", "internal sealed class PaneGraphics", 1))
        identity.append({"source": str(root / filename), "declaration": "actual PaneGraphics including owner fit slots",
                         "source_file_sha256": hashlib.sha256((root / filename).read_bytes()).hexdigest(),
                         "declaration_utf8_lf_sha256": hashlib.sha256(declaration.encode()).hexdigest(),
                         "access_only_redirect": True})
        for name in ("GuideRows", "GuideConditions", "GuideStates", "GuideVariables"):
            declaration = re.search(r"static readonly string\[\] " + name + r"\s*=\s*\{[^;]+;",source)
            if declaration is None:
                raise RuntimeError(f"Missing production declaration {name}")
            parts.append(declaration.group())
output = Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text("// Generated from production; do not edit.\nusing System.IO;\nnamespace " + fixture_namespace + ";\n"
                  "internal partial class HKDualScreen {\n" + "\n".join(parts) + "\n}\n" +
                  "internal partial class HKConfigFixture {\n" + config_method + "\n}\n" +
                  (root / "HKLayout.cs").read_text(encoding="utf-8").replace("using System;", "").replace("using UnityEngine;", ""),
                  encoding="utf-8")
if len(sys.argv) > 2:
    Path(sys.argv[2]).write_text(json.dumps({"line_endings": "UTF-8 LF text (Path.read_text normalizes CRLF); physical source file hashes retained",
                                          "output": str(output), "output_sha256": hashlib.sha256(output.read_bytes()).hexdigest(),
                                          "methods": identity}, indent=2), encoding="utf-8")
