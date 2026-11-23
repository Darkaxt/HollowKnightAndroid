// HkCharmPresets -- #56 loadout presets: the list of equipped charm numbers,
// stored per save profile next to the game's own saves, plus which preset is
// active. #69 moved the controls into the game's own Charms pane
// (HkNativeCharmPresets); `preset save|load N` stays as a QA command.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HollowKnightPatches.Lower
{
    public static class HkCharmPresets
    {
        public const int Count = 3;
        const int CharmCount = 40;

        static string PathFor(int profile, int preset) =>
            Path.Combine(Application.persistentDataPath, "dualsouls-loadout-presets", "hollow-knight",
                         "profile-" + profile, "preset-" + preset + ".txt");

        // The slot the player picked: a new game's PlayerData.profileID stays 0 until
        // its first save, so the GameManager's slot keys the presets.
        static int Slot(PlayerData pd)
        {
            var gm = GameManager.UnsafeInstance;
            return gm != null ? gm.profileID : pd.profileID;
        }

        /// <summary>Why presets cannot be loaded right now, or null when they can.</summary>
        public static string BlockReason(PlayerData pd)
        {
            var gm = GameManager.UnsafeInstance;
            if (gm == null || pd == null || HeroController.UnsafeInstance == null) return "Unavailable";
            if (gm.gameState != GlobalEnums.GameState.PLAYING || gm.IsInSceneTransition) return "Unavailable";
            if (gm.inventoryFSM != null && gm.inventoryFSM.ActiveStateName != "Closed") return "Close the inventory";
            if (BossSequenceController.BoundCharms) return "Charms are bound";
            if (!pd.atBench && !DualSouls.Mods.HollowKnight.HollowKnightGameplayFeatures.EquipAnywhereActive) return "Rest at a bench";
            return null;
        }

        static int _activeSlot = -1, _active = 1;

        /// <summary>The preset the selector shows, per save slot (1 when never set).</summary>
        public static int Active
        {
            get
            {
                var pd = PlayerData.instance;
                if (pd == null) return 1;
                int slot = Slot(pd);
                if (slot != _activeSlot)
                {
                    _activeSlot = slot;
                    _active = 1;
                    try
                    {
                        int n;
                        string path = ActivePath(slot);
                        if (File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out n) && n >= 1 && n <= Count) _active = n;
                    }
                    catch { }
                }
                return _active;
            }
        }

        static string ActivePath(int profile) =>
            Path.Combine(Application.persistentDataPath, "dualsouls-loadout-presets", "hollow-knight",
                         "profile-" + profile, "active.txt");

        /// <summary>
        /// #69, from the Charms pane, after the pane's own bench and binding checks:
        /// keep the current charms in the active preset, then equip <paramref name="to"/>.
        /// A preset never used before starts as a copy of the current charms.
        /// </summary>
        public static string SwitchTo(int to)
        {
            var pd = PlayerData.instance;
            if (!DsFrame.InGame || pd == null || pd.equippedCharms == null) return "Load a save first";
            if (to < 1 || to > Count) return "No such preset";
            Save(Active);
            int slot = Slot(pd);
            _activeSlot = slot;
            _active = to;
            try
            {
                string path = ActivePath(slot);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, to.ToString());
            }
            catch (Exception e) { Debug.LogWarning("[HkCharms] active preset not saved: " + e.Message); }
            if (!Exists(to)) return Save(to);
            return Load(to, true);
        }

        public static bool Exists(int preset)
        {
            var pd = PlayerData.instance;
            return pd != null && preset >= 1 && preset <= Count && File.Exists(PathFor(Slot(pd), preset));
        }

        public static string Save(int preset)
        {
            var pd = PlayerData.instance;
            if (!DsFrame.InGame || pd == null || pd.equippedCharms == null) return "Load a save first";
            if (preset < 1 || preset > Count) return "No such preset";
            string path = PathFor(Slot(pd), preset);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, string.Join(",", pd.equippedCharms.ConvertAll(n => n.ToString()).ToArray()));
            return "Preset " + preset + " saved (" + pd.equippedCharms.Count + " charms)";
        }

        public static string Load(int preset) { return Load(preset, false); }

        static string Load(int preset, bool inPane)
        {
            var pd = PlayerData.instance;
            var gm = GameManager.UnsafeInstance;
            var hero = HeroController.UnsafeInstance;
            if (!DsFrame.InGame || pd == null || gm == null || hero == null || pd.equippedCharms == null) return "Load a save first";
            if (!ReferenceEquals(gm.playerData, pd)) return "Unavailable";
            string reason = inPane ? null : BlockReason(pd);
            if (reason != null) return reason;
            if (preset < 1 || preset > Count) return "No such preset";
            string path = PathFor(Slot(pd), preset);
            if (!File.Exists(path)) return "Preset " + preset + " is empty";

            var wanted = new List<int>();
            foreach (string part in File.ReadAllText(path).Split(','))
            {
                int n;
                if (int.TryParse(part.Trim(), out n) && n >= 1 && n <= CharmCount && !wanted.Contains(n)) wanted.Add(n);
            }

            int skipped = 0;
            bool writes = false;
            try
            {
                // Off first, so the notches the preset needs are free. Void Heart
                // cannot be removed, as on the pane itself.
                foreach (int n in pd.equippedCharms.ToArray())
                {
                    if (wanted.Contains(n)) continue;
                    if (n == 36 && pd.royalCharmState == 4) continue;
                    Apply(pd, n, false);
                    writes = true;
                }
                foreach (int n in wanted)
                {
                    if (pd.equippedCharms.Contains(n)) continue;
                    if (!CanEquip(pd, n)) { skipped++; continue; }
                    Apply(pd, n, true);
                    writes = true;
                }
                gm.RefreshOvercharm();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HkCharms] preset " + (writes ? "partially applied: " : "rejected: ") + e.Message);
            }
            if (writes)
            {
                try { hero.CharmUpdate(); } catch (Exception e) { Debug.LogWarning("[HkCharms] CharmUpdate: " + e.Message); }
                try { PlayMakerFSM.BroadcastEvent("CHARM INDICATOR CHECK"); } catch { }
                try { PlayMakerFSM.BroadcastEvent("UPDATE BLUE HEALTH"); } catch { }
                if (!pd.atBench) try { PlayMakerFSM.BroadcastEvent("CHARM EQUIP CHECK"); } catch { }
            }
            return "Preset " + preset + " loaded" + (skipped > 0 ? ", " + skipped + " skipped" : "");
        }

        // The pane's own per-charm rules: owned, not broken, Kingsoul complete, and
        // a free notch (an overcharm is allowed only once the game has unlocked it).
        static bool CanEquip(PlayerData pd, int n)
        {
            if (!pd.GetBool("gotCharm_" + n)) return false;
            if ((n == 23 && pd.brokenCharm_23) || (n == 24 && pd.brokenCharm_24) || (n == 25 && pd.brokenCharm_25)) return false;
            if (n == 36 && (pd.royalCharmState == 1 || pd.royalCharmState == 2)) return false;
            if (pd.charmSlotsFilled >= pd.charmSlots) return false;
            int cost = pd.GetInt("charmCost_" + n);
            return pd.charmSlotsFilled + cost <= pd.charmSlots || pd.canOvercharm;
        }

        static void Apply(PlayerData pd, int n, bool equip)
        {
            pd.SetBool("equippedCharm_" + n, equip);
            if (equip) pd.EquipCharm(n); else pd.UnequipCharm(n);
            pd.CalculateNotchesUsed();
        }
    }
}
#endif
