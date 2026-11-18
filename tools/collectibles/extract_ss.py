"""Extract Silksong collectible locations from a local game copy (#37).

Usage:  python -I extract_ss.py <Hollow Knight Silksong_Data dir> <out.json> [--all]

Each entry: scene, kind, save key, position in the room (world units), plus the
room's tilemap size so the runtime places the pin with the same formula as
GameMap.GetMapPosition. --all also lists pickups whose item is not a tracked
collectible (for reviewing the classification).
"""
import glob
import json
import os
import sys
import warnings

import UnityPy
from UnityPy.classes import PPtr
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_scan import components, world_pos  # noqa: E402

warnings.filterwarnings("ignore")
UNITY = "6000.0.50f1"
UnityPy.config.FALLBACK_UNITY_VERSION = UNITY


def classify(item_name, item_class):
    """Map a pickup's item asset to a tracked kind, or None."""
    if item_class == "ToolItemBasic" or item_class.startswith("ToolItem"):
        return "tool"
    if item_class == "ToolCrest":
        return "crest"
    if item_class == "CollectableRelic":
        return "relic"
    n = item_name.lower()
    if "heart piece" in n or n == "mask shard":
        return "mask"
    if "spool" in n:
        return "spool"
    if "crest socket" in n or "locket" in n:
        return "locket"
    return None


def scene_name(env):
    for b in env.files.values():
        for sf in getattr(b, "files", {}).values():
            for o in getattr(sf, "objects", {}).values():
                if o.type.name == "AssetBundle":
                    for path, _ in o.read().m_Container:
                        if path.endswith(".unity"):
                            return path.rsplit("/", 1)[-1][:-6]
    return None


def deref(obj, ref):
    p = PPtr[object](m_FileID=ref["m_FileID"], m_PathID=ref["m_PathID"])
    p.assetsfile = obj.assets_file
    return p.deref()


PBI_KINDS = {"Heart Piece": "mask", "Silk Spool": "spool"}


def pbi_id(obj, ref, fallback):
    try:
        data_id = deref(obj, ref).read_typetree().get("itemData", {}).get("ID") or ""
        return data_id or fallback
    except Exception:
        return fallback


def scan_scene(env, scene, gen, pd_fields, other, resolve_items=False):
    """(tilemap size, spots) for one scene, or None when pickups need the data bundles."""
    pickups, tilemap, fleas, named = [], None, [], []
    own = [sf for b in list(env.files.values())[:1] for sf in b.files.values() if hasattr(sf, "objects")]
    for o in (o for sf in own for o in sf.objects.values()):
        if o.type.name == "MonoBehaviour":
            try:
                cls = o.read(check_read=False).m_Script.read().m_ClassName
            except Exception:
                continue
            if cls == "tk2dTileMap" and tilemap is None:
                tilemap = o
            elif cls == "CollectableItemPickup":
                pickups.append(o)
        elif o.type.name == "GameObject":
            go = o.read()
            if go.m_Name in PBI_KINDS:
                named.append(go)
            elif go.m_Name.startswith("Flea") and f"SavedFlea_{scene}" in pd_fields:
                fleas.append(go)
    if pickups and not resolve_items:
        return None
    found = []
    if pickups or named or fleas:
        env.typetree_generator = gen
    for o in pickups:
        t = o.read_typetree()
        go = o.read(check_read=False).m_GameObject.read()
        try:
            it = deref(o, t["item"]).read(check_read=False)
            iname, icls = it.m_Name, it.m_Script.read().m_ClassName
        except Exception:
            iname, icls = "?", "?"
        kind = classify(iname, icls)
        tr = next(r.read() for k, r in components(go) if k == "Transform")
        x, y = world_pos(tr)
        key = go.m_Name
        if t.get("playerDataBool"):
            key = "pd:" + t["playerDataBool"]
        elif t["persistent"]["m_PathID"]:
            key = pbi_id(o, t["persistent"], go.m_Name)
        row = {"scene": scene, "kind": kind, "key": key, "item": iname, "cls": icls, "x": x, "y": y}
        (found if kind else other).append(row)
    for go in named:
        comps = list(components(go))
        pbi = next((r for k, r in comps if k == "PersistentBoolItem"), None)
        if pbi is None:
            continue
        key = pbi.read_typetree().get("itemData", {}).get("ID") or go.m_Name
        x, y = world_pos(next(r.read() for k, r in comps if k == "Transform"))
        found.append({"scene": scene, "kind": PBI_KINDS[go.m_Name], "key": key, "x": x, "y": y})
    if fleas:
        fleas.sort(key=lambda g: (not g.m_IsActive, g.m_Name))
        x, y = world_pos(next(r.read() for k, r in components(fleas[0]) if k == "Transform"))
        found.append({"scene": scene, "kind": "flea", "key": f"pd:SavedFlea_{scene}", "x": x, "y": y})
    size = None
    if found and tilemap is not None:
        tt = tilemap.read_typetree()
        size = [tt["width"], tt["height"]]
    return size, found


def main(data_dir, out, show_all):
    aa = f"{data_dir}/StreamingAssets/aa/StandaloneLinux64"
    monos = glob.glob(f"{aa}/*_monoscripts.bundle")
    data = glob.glob(f"{aa}/dataassets_assets_assets/**/*.bundle", recursive=True)
    data += glob.glob(f"{aa}/dataassets*.bundle")
    gen = TypeTreeGenerator(UNITY)
    gen.load_local_dll_folder(f"{data_dir}/Managed")
    pd_fields = {c.m_Name for c in gen.get_nodes_up("Assembly-CSharp", "PlayerData").m_Children}

    entries, sizes, other = [], {}, []
    for path in sorted(glob.glob(f"{aa}/scenes_scenes_scenes/*.bundle")):
        env = UnityPy.load(path, *monos)
        scene = scene_name(env)
        if not scene:
            continue
        found = scan_scene(env, scene, gen, pd_fields, other)
        if found is None:
            # Pickups name their item in the data bundles: load those only where needed.
            env = UnityPy.load(path, *monos, *data)
            found = scan_scene(env, scene, gen, pd_fields, other, resolve_items=True)
        if found and found[0] is not None:
            sizes[scene] = found[0]
        entries.extend(found[1] if found else [])
        if found and found[1]:
            print(scene, len(found[1]), sizes.get(scene), file=sys.stderr)
    flea_fields = sorted(f for f in pd_fields if f.startswith("SavedFlea_"))
    placed = {e["key"] for e in entries if e["kind"] == "flea"}
    for f in flea_fields:
        if "pd:" + f not in placed:
            print("flea not placed:", f, file=sys.stderr)
    result = {"entries": entries, "sizes": sizes}
    if show_all:
        result["other"] = other
    with open(out, "w", encoding="utf-8") as f:
        json.dump(result, f, indent=0)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], "--all" in sys.argv)
