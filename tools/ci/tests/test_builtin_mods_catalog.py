import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
CSHARP = {
    "hollowKnight": ROOT / "tools/hollow-knight-patches/src/mods/HollowKnightTweakAdapter.cs",
    "silksong": ROOT / "tools/silksong-patches/src/mods/SilksongTweakAdapter.cs",
}
KOTLIN = ROOT / "src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/builtinmods/BuiltInModCatalog.kt"

ROW_RE = re.compile(
    r'(?P<factory>Available|Unavailable|available|unavailable)\('
    r'"(?P<id>[^"]+)",\s*"(?P<contract>[^"]+)",\s*'
    r'(?:(?:TweakControlKind|BuiltInModControlKind)\.(?P<kind>\w+),\s*)?'
    r'"(?P<group>[^"]+)",\s*"(?P<title>[^"]+)",\s*"(?P<description>[^"]*)",\s*'
    r'"(?P<default>[^"]+)",\s*(?:new\[\]\s*\{|listOf\()(?P<values>[^}\)]*)[}\)](?:,\s*[A-Z_]+)?\)',
)
DIRECT_ROW_RE = re.compile(
    r'Row\("(?P<id>[^"]+)",\s*"(?P<contract>[^"]+)",\s*'
    r'TweakControlKind\.(?P<kind>\w+),\s*"(?P<group>[^"]+)",\s*'
    r'"(?P<title>[^"]+)",\s*"(?P<description>[^"]*)",\s*'
    r'"(?P<default>[^"]+)"(?P<values>(?:,\s*"[^"]+")+?)\)',
)


def parse_rows(text: str):
    rows = []
    matches = [(match.start(), match, False) for match in ROW_RE.finditer(text)]
    matches.extend((match.start(), match, True) for match in DIRECT_ROW_RE.finditer(text))
    for _, match, direct in sorted(matches, key=lambda item: item[0]):
        fields = match.groupdict()
        unavailable = not direct and fields["factory"].lower() == "unavailable"
        rows.append({
            "id": fields["id"],
            "contract": fields["contract"],
            "kind": fields["kind"] or "Choice",
            "group": fields["group"],
            "title": fields["title"],
            "description": fields["description"],
            "default": fields["default"],
            "values": tuple(re.findall(r'"([^"]+)"', fields["values"])),
            "available": not unavailable,
        })
    return rows


def kotlin_rows(name: str):
    text = KOTLIN.read_text(encoding="utf-8")
    block = text.split(f"val {name} = listOf(", 1)[1].split("\n    )", 1)[0]
    return parse_rows(block)


def expected_required(game: str):
    silksong = game == "silksong"
    return [
        ("skins", "skins", "GENERAL", "SKINS", "Route", True, "open", ("open",)),
        ("black_background", "black_background" if silksong else "companion_backdrop", "GENERAL", "BLACK BACKGROUND", "Choice", True, "off" if silksong else "dimmed", ("off", "on") if silksong else ("dimmed", "black")),
        ("run_speed", "run_speed", "WORLD", "RUN SPEED", "Choice", True, "vanilla", ("vanilla", "plus_25", "plus_50")),
        ("fast_transitions", "fast_transitions", "WORLD", "FAST TRANSITIONS", "Choice", True, "off", ("off", "on")),
        ("auto_map", "auto_map", "WORLD", "AUTO MAP", "Choice", True, "off", ("off", "on")),
        ("innate_compass", "innate_compass", "WORLD", "INNATE COMPASS", "Choice", True, "off", ("off", "on")),
        ("bench_teleport", "bench_teleport", "WORLD", "BENCH TELEPORT", "Route", True, "open", ("open",)),
        ("secret_radar", "secret_radar", "WORLD", "SECRET RADAR", "Choice", True, "off", ("off", "on")),
        ("nail_damage", "nail_damage", "COMBAT", "NEEDLE DAMAGE" if silksong else "NAIL DAMAGE", "Choice", True, "x1", ("x1", "x2", "x3", "x5")),
        ("damage_taken", "damage_received", "COMBAT", "DAMAGE TAKEN", "Choice", True, "vanilla", ("vanilla", "prevent_death", "invincible") if silksong else ("vanilla", "no_mask_loss", "invincible")),
        ("damage_cap", "damage_cap", "COMBAT", "DAMAGE CAP", "Choice", True, "off", ("off", "on")),
        ("one_hit_kills", "one_hit_kills", "COMBAT", "ONE-HIT KILLS", "Choice", True, "off", ("off", "on")),
        ("unlimited_soul", "unlimited_silk" if silksong else "unlimited_soul", "COMBAT", "UNLIMITED SILK" if silksong else "UNLIMITED SOUL", "Choice", True, "off", ("off", "on")),
        ("enemy_health_bars", "health_bars", "ENCOUNTERS", "ENEMY HEALTH BARS", "Choice", True, "off", ("off", "on")),
        ("damage_numbers", "damage_numbers", "ENCOUNTERS", "DAMAGE NUMBERS", "Choice", True, "off", ("off", "on")),
        ("boss_retry", "boss_retry", "ENCOUNTERS", "BOSS RETRY", "Choice", True, "off", ("off", "on")),
        ("equip_anywhere", "equip_anywhere", "CRESTS & TOOLS" if silksong else "CHARMS", "EQUIP ANYWHERE", "Choice", True, "off", ("off", "on")),
        ("charm_costs", "charm_costs", "CRESTS & TOOLS" if silksong else "CHARMS", "TOOL COSTS" if silksong else "CHARM COSTS", "Choice", True, "vanilla", ("vanilla", "free")),
        ("unlimited_notches", "unlimited_notches", "CRESTS & TOOLS" if silksong else "CHARMS", "UNLIMITED TOOL SLOTS" if silksong else "UNLIMITED NOTCHES", "Choice", True, "off", ("off", "on")),
        ("state_slot", "state_slot", "SAVE STATES", "SLOT", "Choice", True, "1", ("1", "2", "3", "4", "5")),
        ("save_to_slot", "save_to_slot", "SAVE STATES", "SAVE TO SLOT", "Command", True, "run", ("run",)),
        ("load_from_slot", "load_from_slot", "SAVE STATES", "LOAD FROM SLOT", "Command", True, "run", ("run",)),
        ("delete_slot", "delete_slot", "SAVE STATES", "DELETE SLOT", "Command", True, "run", ("run",)),
        ("geo_magnet", "geo_magnet", "ECONOMY", "ROSARY MAGNET" if silksong else "GEO MAGNET", "Choice", True, "off", ("off", "on")),
        ("keep_geo_on_death", "keep_geo_on_death", "ECONOMY", "KEEP ROSARIES ON DEATH" if silksong else "KEEP GEO ON DEATH", "Choice", True, "off", ("off", "on")),
        ("journal_one_kill", "journal_one_kill", "ECONOMY", "JOURNAL IN ONE KILL", "Choice", True, "off", ("off", "on")),
        ("geo_multiplier", "geo_multiplier", "ECONOMY", "ROSARY MULTIPLIER" if silksong else "GEO MULTIPLIER", "Choice", True, "x1", ("x1", "x2", "x3", "x5")),
    ]


def compact(rows):
    return [
        (row["contract"], row["id"], row["group"], row["title"], row["kind"],
         row["available"], row["default"], row["values"])
        for row in rows
    ]


class BuiltInModsCatalogContractTest(unittest.TestCase):
    def test_exact_required_catalog_and_extras(self):
        expected_extras = {
            "hollowKnight": [("lifeblood_flash", "lifeblood_flash")],
            "silksong": [
                ("instant_dialogue", "instant_dialogue"),
                ("disable_world_rumble", "disable_world_rumble"),
                ("ignore_frost_slowdown", "ignore_frost_slowdown"),
            ],
        }
        for name, path in CSHARP.items():
            rows = parse_rows(path.read_text(encoding="utf-8"))
            self.assertEqual(expected_required("silksong" if name == "silksong" else "hollowKnight"), compact(rows[:27]), name)
            self.assertEqual(expected_extras[name], [(row["contract"], row["id"]) for row in rows[27:]], name)
            self.assertNotIn("state_slots", {row["id"] for row in rows})

    def test_android_catalog_matches_every_csharp_metadata_field(self):
        for name, path in CSHARP.items():
            expected = parse_rows(path.read_text(encoding="utf-8"))
            actual = kotlin_rows(name)
            self.assertEqual(expected, actual, name)

    def test_unavailable_rows_are_explicit_and_use_current_missing_language(self):
        for path in CSHARP.values():
            source = path.read_text(encoding="utf-8")
            rows = parse_rows(source)
            self.assertTrue(all(row["description"] for row in rows))
            if any(not row["available"] for row in rows):
                self.assertIn("currently unavailable", source)
            self.assertNotIn("HKMOD-", source)
            self.assertNotIn(" is deferred", source)

    def test_shared_controller_exposes_validated_set_without_persisting_operations(self):
        controller = (ROOT / "tools/shared-patches/src/Mods/TweakController.cs").read_text(encoding="utf-8")
        menu = (ROOT / "tools/shared-patches/src/Mods/TweakMenuModel.cs").read_text(encoding="utf-8")
        self.assertIn("TweakActionResult Set(string id, string value)", controller)
        self.assertIn("descriptor.ControlKind != TweakControlKind.Choice", controller)
        self.assertIn("TweakActionResult SetSelected(string value)", menu)
        self.assertNotIn("if (!descriptor.IsAvailable) continue", menu)


if __name__ == "__main__":
    unittest.main()
