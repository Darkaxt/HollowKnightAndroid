"""Shared helpers for the collectible extractors (#37).

Reads a user's own game copy with UnityPy. Nothing from the game is copied
into the repo except the generated table of room names, positions and save
keys that the extractors print.
"""
import math
import struct

import UnityPy


def components(go):
    """Yield (type_or_script_class, object_reader) for a GameObject."""
    for c in go.m_Components:
        ptr = c.component if hasattr(c, "component") else c
        reader = ptr.deref()
        t = reader.type.name
        if t == "MonoBehaviour":
            try:
                t = reader.read(check_read=False).m_Script.read().m_ClassName
            except Exception:
                t = "MonoBehaviour?"
        yield t, reader


def world_pos(tr):
    """2D world position of a Transform, honouring parent scale and Z rotation."""
    chain = []
    t = tr
    while t is not None:
        chain.append(t)
        t = t.m_Father.read() if t.m_Father and t.m_Father.path_id else None
    x = y = 0.0
    # Walk root -> leaf: pos = parent.pos + R(parent) * (S(parent) * local)
    px = py = 0.0
    rot = 0.0
    sx = sy = 1.0
    for t in reversed(chain):
        lp = t.m_LocalPosition
        lx, ly = lp.x * sx, lp.y * sy
        c, s = math.cos(rot), math.sin(rot)
        px, py = px + c * lx - s * ly, py + s * lx + c * ly
        q = t.m_LocalRotation
        rot += 2 * math.atan2(q.z, q.w)
        ls = t.m_LocalScale
        sx, sy = sx * ls.x, sy * ls.y
    return round(px, 2), round(py, 2)


def build_scenes(data_dir):
    gg = UnityPy.load(f"{data_dir}/globalgamemanagers")
    for o in gg.objects:
        if o.type.name == "BuildSettings":
            return [p.split("/")[-1].rsplit(".", 1)[0] for p in o.read().scenes]
    raise RuntimeError("BuildSettings not found")


def _str_at(raw, i):
    n = struct.unpack_from("<i", raw, i)[0]
    if n < 0 or n > 4096 or i + 4 + n > len(raw):
        raise ValueError("bad string")
    s = raw[i + 4:i + 4 + n].decode("utf-8", "replace")
    return s, i + 4 + ((n + 3) & ~3)


def fsm_var(raw, name, kind):
    """Read a PlayMaker variable value by name from a serialized PlayMakerFSM.

    Variables sit at the end of the FSM blob as
    useVariable(b4) name(str) tooltip(str) showInInspector(b4) networkSync(b4) value.
    The last length-prefixed occurrence of the name is the variable entry.
    Returns None when absent.
    """
    key = struct.pack("<i", len(name)) + name.encode()
    i = raw.rfind(key)
    while i != -1:
        try:
            j = i + 4 + ((len(name) + 3) & ~3)
            _, j = _str_at(raw, j)          # tooltip
            j += 8                           # showInInspector, networkSync
            if kind == "int":
                return struct.unpack_from("<i", raw, j)[0]
            if kind == "bool":
                return raw[j] != 0
            if kind == "string":
                return _str_at(raw, j)[0]
            raise ValueError(kind)
        except (ValueError, struct.error):
            i = raw.rfind(key, 0, i)
    return None


def fsm_name(raw):
    """FSM name: the first serialized string after the MonoBehaviour header."""
    # m_GameObject(12) m_Enabled(4) m_Script(12) m_Name(str) then fsm.dataVersion... ;
    # cheaper and stable enough: search the leading strings.
    j = 28
    _, j = _str_at(raw, j)  # m_Name
    for _ in range(6):
        try:
            s, k = _str_at(raw, j)
            if s:
                return s
            j = k
        except ValueError:
            j += 4
    return None
