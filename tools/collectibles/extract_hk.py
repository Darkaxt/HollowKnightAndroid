"""Extract Hollow Knight collectible locations from a local game copy (#37).

Usage:  python -I extract_hk.py <hollow_knight_Data dir> <out.json>

Each entry: scene, kind, save key, position in the room (world units), plus
the room's tilemap size so the runtime can place the pin on the game's map the
same way GameMap.PositionCompass places the Knight.
"""
import json
import sys

import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

sys.path.insert(0, __import__("os").path.dirname(__import__("os").path.abspath(__file__)))
from unity_scan import build_scenes, components, fsm_var, world_pos

SKIP_PREFIX = ("GG_", "Menu_", "Quit_", "Cinematic_", "Opening_", "End_", "PermaDeath", "BetaEnd", "Knight_Pickup", "Pre_Menu")


def classify(go, comps):
    name = go.m_Name
    has_pbi = any(t == "PersistentBoolItem" for t, _ in comps)
    if name.startswith("Grub Bottle") and has_pbi:
        return "grub", name
    if name == "Heart Piece" and has_pbi:
        return "mask", name
    if name.startswith("Vessel Fragment") and has_pbi:
        return "vessel", name
    if name == "Dream Plant":
        return "root", "pdlist:scenesEncounteredDreamPlantC"
    if name.startswith("Shiny Item"):
        for t, r in comps:
            if t != "PlayMakerFSM":
                continue
            raw = r.get_raw_data()
            if b"Shiny Control" not in raw[:256]:
                continue
            if fsm_var(raw, "Charm", "bool"):
                cid = fsm_var(raw, "Charm ID", "int")
                if cid and 0 < cid <= 40:
                    return "charm", f"pd:gotCharm_{cid}"
    return None


def main(data_dir, out):
    scenes = build_scenes(data_dir)
    gen = None
    entries, sizes = [], {}
    for i, scene in enumerate(scenes):
        if scene.startswith(SKIP_PREFIX):
            continue
        env = UnityPy.load(f"{data_dir}/level{i}")
        found = []
        tilemap = None
        for o in env.objects:
            if o.type.name == "MonoBehaviour" and tilemap is None:
                mb = o.read(check_read=False)
                try:
                    cls = mb.m_Script.read().m_ClassName
                except Exception:
                    continue
                if cls == "tk2dTileMap":
                    tilemap = o
                continue
            if o.type.name != "GameObject":
                continue
            go = o.read()
            comps = list(components(go))
            hit = classify(go, comps)
            if not hit:
                continue
            tr = next(r.read() for t, r in comps if t == "Transform")
            x, y = world_pos(tr)
            found.append({"scene": scene, "kind": hit[0], "key": hit[1], "x": x, "y": y})
        if not found:
            continue
        if tilemap is not None:
            if gen is None:
                gen = TypeTreeGenerator(env.objects[0].assets_file.unity_version)
                gen.load_local_dll_folder(f"{data_dir}/Managed")
            env.typetree_generator = gen
            tree = tilemap.read_typetree()
            sizes[scene] = [tree["width"], tree["height"]]
        entries.extend(found)
        print(scene, len(found), sizes.get(scene), file=sys.stderr)
    with open(out, "w", encoding="utf-8") as f:
        json.dump({"entries": entries, "sizes": sizes}, f, indent=0)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
