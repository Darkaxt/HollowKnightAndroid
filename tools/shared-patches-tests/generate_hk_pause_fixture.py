"""Compile unchanged production method bodies against bounded rendering stand-ins."""
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1] / "hollow-knight-patches/src/dualsouls"
methods = {
    "HKDualScreen.cs": ("Tick", "LoadConfig", "RelayerHud", "LogoTick", "SyncBgCapture"),
    "HKDualScreen.Bottom.Layering.cs": (
        "CompanionVisible", "ApplyDualScreenToggle", "ApplyLowerPauseGate"),
    "HKDualScreen.Bottom.Hud.cs": ("FrameHudCams", "BuildEquipCharmRow", "PositionHudStrip"),
    "HKDualScreen.Util.cs": ("SetTmpFont", "NeutralizeDetachedTmpClip", "ItemBounds", "SanitizeDetachedTmpClone"),
    "HKDualScreen.Bottom.Inventory.cs": ("Refs", "FitOccupiedNative", "BuildNativePaneGraphics", "PositionNativePaneGraphics", "EnsureNativeInventory", "LayoutNativeInventory", "ScrollNativePane", "LayoutNativeDetail", "PopulateInvDetail", "PopulateSpellDetail", "PopulateEquipDetail", "PopulateGeoDetail", "PopulateGodfinderDetail", "ClearInvDetail", "ClearInvDetailLocal"),
    "HKDualScreen.Bottom.Charms.cs": ("TryTmpGlyphBoundsWorld", "CharmNumOf", "EnsureCharmRefs", "LayoutCharmsRedesign", "PopulateCharmDetail"),
    "HKDualScreen.Bottom.Frame.cs": ("BuildFrame", "BuildTabRow", "ResolveTabDonors", "ShellPoint", "OnConfigReloaded", "FitSprite", "UpdateCompanion", "ApplyCompanionCamera", "InvalidateCompanionClones", "RetireCompanionCaches", "TeardownFrame", "TeardownCompanion", "TabSlideTick", "StowSlideClone"),
    "HKDualScreen.DirectDisplay.cs": ("SetDirectDisplayActive", "SetRoleCamerasEnabled", "RestoreReferenceRouting", "TryDirectStep"),
    "HKDualScreen.Bottom.MapControls.cs": ("BuildMapControls", "PositionMapControls", "HandleMapControlTap", "ValidButton"),
    "HKDualScreen.Bottom.Select.cs": ("PollTouch", "RefreshSelectedDetail", "SetDetailFont", "PositionSelection", "AnimateSelectionBounds", "ResetSelectionAnimation", "PollItemTap", "VisibleItemRenderer", "TrySelectionBounds"),
    "HKDualScreen.Bottom.JournalGuide.cs": ("CopyPaneLabel", "ResetJournalDetailScroll", "CloneForTab", "ReadyForTab", "SyncSupplementarySource", "RetireSupplementaryPanes", "DiscardSupplementaryPane", "BuildSupplementaryPane", "GuideRowCondition", "BindJournalLabels", "SetPaneLabel", "SetPaneLabelVisible", "ApplyPaneLabelClip", "PanePixel", "BuildPaneGraphics", "CopyPaneMask", "PositionPaneGraphics", "PositionPaneRule", "PositionPaneMask", "SetPaneSelection", "PaneCursorTick", "LayoutJournal", "LayoutGuide", "JournalTap", "GuideTap", "ScrollSupplementary", "SupplementaryTick", "RefreshJournal", "RefreshGuide"),
}
parts = []
config_method = None
for filename, names in methods.items():
    source = (root / filename).read_text(encoding="utf-8")
    for name in names:
        match = re.search(r"\b(?:static\s+)?(?:void|bool|GameObject|string|Vector3|Renderer|NativePaneLabel|PaneRefs|PaneGraphics|FitResult|Bounds|int)\s+" + name + r"\s*\([^)]*\)\s*\{", source)
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
        if name in ("BuildMapControls", "PositionMapControls"):
            body = source[match.start():end]
            boundary = "        if (mapZoomTrack == null)" if name == "BuildMapControls" else "        EnsureSelectedMarkerType();"
            parts.append(body[:body.index(boundary)].replace(name + "(",name + "Fixture(",1) + "    }")
        elif name == "PositionHudStrip":
            # Extract the actual complete header branch; unrelated notch/toast
            # rendering is outside this bounded TMP regression fixture.
            body = source[match.start():end]
            cut = body.index("        UpdateNotchRow(")
            parts.append(body[:cut].replace("PositionHudStrip(", "PositionHeaderFixture(", 1) + "    }")
        elif name == "BuildFrame":
            parts.append(source[match.start():end].replace("BuildFrame(", "BuildFrameBody(", 1))
        elif name == "LoadConfig":
            config_method = source[match.start():end]
        elif name in ("LayoutNativeInventory", "LayoutCharmsRedesign", "TeardownFrame", "TeardownCompanion", "PollItemTap"):
            parts.append(source[match.start():end].replace(name + "(", name + "Body(", 1))
        elif name in ("LayoutJournal", "LayoutGuide", "JournalTap", "GuideTap", "ScrollSupplementary"):
            # Only the signature is redirected so the fixture can count calls;
            # the executable production body remains byte-for-byte unchanged.
            parts.append(source[match.start():end].replace(name + "(", name + "Body(", 1))
        else:
            parts.append(source[match.start():end])
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
    if filename == "HKDualScreen.Bottom.JournalGuide.cs":
        for name in ("GuideRows", "GuideConditions", "GuideStates", "GuideVariables"):
            declaration = re.search(r"static readonly string\[\] " + name + r"\s*=\s*\{[^;]+;",source)
            if declaration is None:
                raise RuntimeError(f"Missing production declaration {name}")
            parts.append(declaration.group())
output = Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text("// Generated from production; do not edit.\nusing System.IO;\nnamespace HkPauseContracts;\n"
                  "internal partial class HKDualScreen {\n" + "\n".join(parts) + "\n}\n" +
                  "internal partial class HKConfigFixture {\n" + config_method + "\n}\n" +
                  (root / "HKLayout.cs").read_text(encoding="utf-8").replace("using System;", "").replace("using UnityEngine;", ""),
                  encoding="utf-8")
