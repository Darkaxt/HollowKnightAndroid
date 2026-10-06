"""Complete conditional skin callers; only namespaces/import/guard envelopes change.

Unity objects, hierarchy ingress and deferred destruction are typed host models.
Discovery, mapping, admission, decoder ownership and session decisions are production.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path


def generate(root, output):
    sha = lambda data: hashlib.sha256(data).hexdigest()
    parts, identities = [], []
    for profile, name, original, isolated in (
        ("hollow-knight", "HollowKnight", "DualSouls.Skins.HollowKnight.Runtime", "SkinUnityHk"),
        ("silksong", "Silksong", "DualSouls.Skins.Silksong.Runtime", "SkinUnitySs"),
    ):
        path = root / f"tools/{profile}-patches/src/skins/runtime/{name}SkinRuntime.cs"
        source = path.read_text(encoding="utf-8")
        # No method selection or product-body substitutions. Keep both SS partials
        # and HK's actual gate/scheduler/readiness helpers with their native callers.
        text = re.sub(r"^using .*;\n", "", source, flags=re.M)
        text = text.replace("#if UNITY_ANDROID && !UNITY_EDITOR", "").replace("#endif", "")
        text = text.replace("namespace " + original, "namespace " + isolated)
        aliases = "\n".join("    using " + name + " = SkinUnityNativeModel." + name + ";"
                            for name in ("PlayMakerFSM", "HeroController", "GameManager"))
        aliases += "\n    using UnityEngine = SkinUnityNativeModel;"
        text = text.replace("namespace " + isolated + "\n{", "namespace " + isolated + "\n{\n" + aliases)
        original_bodies = re.findall(r"namespace " + re.escape(original) + r"\s*\{(.*?)\n\}", source, re.S)
        assert original_bodies and all(body in text for body in original_bodies)
        parts.append(text)
        identities.append({"source": str(path.relative_to(root)), "source_sha256": sha(path.read_bytes()),
                           "namespace_bodies_sha256": [sha(body.encode()) for body in original_bodies],
                           "body_identical": True})
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text("\n".join((
        "using System;", "using System.Collections.Generic;", "using System.Linq;",
        "using System.Reflection;", "using System.Threading;", "using DualSouls.Skins.Runtime;",
        "using DualSouls.Skins.HollowKnight.Runtime;", "using DualSouls.Skins.Silksong.Runtime;",
        "using SkinUnityNativeModel;", "using SkinUnityNativeModel.SceneManagement;",
        "using UObject = SkinUnityNativeModel.Object;",
        "using HutongGames = SkinUnityNativeModel.HutongGames;",
        *parts,
    )), encoding="utf-8")
    model = Path(__file__).with_name("SkinUnityNativeModel.cs")
    output.with_suffix(".manifest.json").write_text(json.dumps({
        "output_sha256": sha(output.read_bytes()), "bodies": identities,
        "generator_sha256": sha(Path(__file__).read_bytes()), "model_sha256": sha(model.read_bytes()),
        "envelope_changes": ["remove conditional guards and outer imports", "remap namespaces", "alias typed engine model"],
        "modeled": ["Unity component/hierarchy/resource ingress", "texture decode and GPU readback", "deferred Destroy", "scene events"],
        "not_proven": ["Unity scheduling/native lifecycle", "JNI", "GPU/default optical appearance", "Android allocations/FPS", "live/parity"],
    }, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--source-root", type=Path, default=Path(__file__).resolve().parents[2])
    args = parser.parse_args()
    generate(args.source_root, args.output)
