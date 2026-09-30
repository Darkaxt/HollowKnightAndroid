"""Compile unchanged production method bodies against bounded rendering stand-ins."""
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1] / "hollow-knight-patches/src/dualsouls"
methods = {
    "HKDualScreen.cs": ("Tick", "RelayerHud", "LogoTick", "SyncBgCapture"),
    "HKDualScreen.Bottom.Layering.cs": (
        "CompanionVisible", "ApplyDualScreenToggle", "ApplyLowerPauseGate"),
    "HKDualScreen.Bottom.Hud.cs": ("FrameHudCams",),
    "HKDualScreen.Bottom.Frame.cs": ("ApplyCompanionCamera",),
}
parts = []
for filename, names in methods.items():
    source = (root / filename).read_text(encoding="utf-8")
    for name in names:
        match = re.search(r"\b(?:void|bool)\s+" + name + r"\s*\([^)]*\)\s*\{", source)
        if match is None:
            # Before the fix this gate does not exist; retain an executable RED.
            if name == "ApplyLowerPauseGate":
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
        parts.append(source[match.start():end])
output = Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text("// Generated from production; do not edit.\nnamespace HkPauseContracts;\n"
                  "internal partial class HKDualScreen {\n" + "\n".join(parts) + "\n}\n" +
                  (root / "HKLayout.cs").read_text(encoding="utf-8").replace("using System;", "").replace("using UnityEngine;", ""),
                  encoding="utf-8")
