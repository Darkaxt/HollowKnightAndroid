// [Q] QA COMMANDS for Silksong — the same app-owned channel as Hollow Knight's
// (see DsCommandFile): adb drops dualsouls-cmd.txt, replies land in
// dualsouls-cmd.out. `report` returns everything the per-candidate device
// checklist reads in one call. There is deliberately no `save` here: a
// reflection-chosen SaveGame overload could take a slot number.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Reflection;
using System.Text;
using DualSouls.Mods.Silksong;
using DualSouls.Skins.Silksong.Runtime;
using UnityEngine;

public partial class DualScreenV2
{
    const string CommandHelp =
        "help | report | state | pause | mods | skins | skin <list|row N|left N|right N|close> | tab <id> | mods-list | mod <id> <value> | pd <field>... | pdset <bool|int|enum field> <value> | mapsetup | die, hit <damage> [hp] [near] | hurt <n>, dash, perform on|off, travel <scene> <gate> | delivery <list|give <name>|hit|break|state> | crest [tools <type>|equip <slot> <tool>|unlock <tool>] | inv [fsm|next <pane>|send <event>|tools] | fsm find <text> | fsm show|send|set|goto <object>[#n]|<fsm>|<arg>[|<value>] | gates <scene> | pdtests <scene> [text] | sprites | assists | collect [checklist|why <scene prefix>]";

    static readonly string[] ReportPlayerFields =
        { "health", "maxHealth", "healthBlue", "silk", "silkMax", "geo", "ShellShards" };

    // QA: one hero nail hit on the nearest live enemy through HealthManager.Hit, as a slash
    // would land. The optional hp first sets the target's health, e.g. above the boss threshold.
    string CommandHit(string[] a, GameManager gm)
    {
        int damage, hp = 0;
        bool near = a.Length > 2 && a[a.Length - 1] == "near";
        int numbers = near ? a.Length - 1 : a.Length;
        if (numbers < 2 || !int.TryParse(a[1], out damage) || damage <= 0 || (numbers > 2 && !int.TryParse(a[2], out hp)))
            return "error hit <damage> [hp] [near]";
        if (gm == null) return "error no GameManager";
        var hero = HeroController.SilentInstance;
        if (hero == null) return "error no HeroController";
        HealthManager target = null;
        float best = float.MaxValue;
        foreach (HealthManager enemy in UnityEngine.Object.FindObjectsOfType<HealthManager>())
        {
            if (enemy.isDead || enemy.hp <= 0 || !enemy.gameObject.activeInHierarchy) continue;
            float distance = (enemy.transform.position - hero.transform.position).sqrMagnitude;
            if (distance < best) { best = distance; target = enemy; }
        }
        if (target == null) return "error no live enemy in scene";
        if (hp > 0) target.hp = hp;
        // near: bring the target beside the hero so its bar and damage number are on screen.
        if (near) target.transform.position = hero.transform.position + new Vector3(3f, 1f, 0f);
        int before = target.hp, hooks = DualSouls.Mods.CombatOverlay.HookCalls, heroHits = DualSouls.Mods.CombatOverlay.HeroHits;
        target.Hit(new HitInstance
        {
            Source = hero.gameObject, AttackType = AttackTypes.Nail, DamageDealt = damage,
            Multiplier = 1f, MagnitudeMultiplier = 1f, IgnoreInvulnerable = true, IsHeroDamage = true,
        });
        return "ok target=" + target.gameObject.name + " hp=" + before + "->" + target.hp + " distance=" + Mathf.Sqrt(best).ToString("0.0")
            + " hooks=+" + (DualSouls.Mods.CombatOverlay.HookCalls - hooks) + " heroHits=+" + (DualSouls.Mods.CombatOverlay.HeroHits - heroHits);
    }

    // QA (#43): drive the game's own inventory without injected input. `inv` reports the
    // "Inventory Control" FSM (GameManager.inventoryFSM) state and its transitions, so the open
    // event can be read off the device; `inv next <pane>` is InventoryPaneList.SetNextOpen;
    // `inv send <event>` sends an FSM event; `inv tools` lists the Tools pane's crest slots as drawn.
    string CommandInventory(string[] a, GameManager gm)
    {
        if (gm == null) return "error no GameManager";
        PlayMakerFSM fsm = gm.inventoryFSM;
        if (fsm == null) return "error no inventory FSM";
        string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "fsm";
        if (sub == "next" && a.Length == 3) { InventoryPaneList.SetNextOpen(a[2]); return "ok next=" + InventoryPaneList.NextPaneOpen; }
        if (sub == "send" && a.Length >= 3)
        {
            string name = string.Join(" ", a, 2, a.Length - 2), before = fsm.ActiveStateName;
            fsm.SendEvent(name);
            return "ok sent=" + name + " state=" + before + "->" + fsm.ActiveStateName;
        }
        if (sub == "tools") return InventoryToolsReport();
        if (sub != "fsm") return "error inv [fsm|next <pane>|send <event>|tools]";
        var sb = new StringBuilder();
        sb.Append("state=").Append(fsm.ActiveStateName).Append(" open=")
          .Append(PlayerData.HasInstance && PlayerData.instance.isInventoryOpen).Append(" next=").Append(InventoryPaneList.NextPaneOpen);
        sb.Append("\nglobal:");
        foreach (var t in fsm.FsmGlobalTransitions) sb.Append(' ').Append(t.EventName).Append("->").Append(t.ToState);
        foreach (var state in fsm.FsmStates)
        {
            sb.Append('\n').Append(state.Name).Append(':');
            foreach (var t in state.Transitions) sb.Append(' ').Append(t.EventName).Append("->").Append(t.ToState);
        }
        return sb.ToString();
    }

    static string InventoryToolsReport()
    {
        var managers = Resources.FindObjectsOfTypeAll<InventoryItemToolManager>();
        var field = typeof(InventoryItemToolManager).GetField("crestList", BindingFlags.Instance | BindingFlags.NonPublic);
        var sb = new StringBuilder();
        foreach (var manager in managers)
        {
            if (manager == null || !manager.gameObject.scene.IsValid()) continue;
            var list = field != null ? field.GetValue(manager) as InventoryToolCrestList : null;
            sb.Append("manager active=").Append(manager.gameObject.activeInHierarchy);
            if (list == null) { sb.Append(" crestList=null\n"); continue; }
            var crest = list.CurrentCrest;
            sb.Append(" crest=").Append(crest != null && crest.CrestData != null ? crest.CrestData.name : "-");
            int i = 0;
            foreach (InventoryToolCrestSlot slot in list.GetSlots())
            {
                if (slot == null) continue;
                Vector3 p = slot.transform.localPosition;
                sb.Append("\n  ").Append(i++).Append(' ').Append(slot.Type).Append(slot.IsLocked ? " locked" : "")
                  .Append(slot.gameObject.activeInHierarchy ? "" : " inactive")
                  .Append(" at=").Append(p.x.ToString("0.00")).Append(',').Append(p.y.ToString("0.00"))
                  .Append(" tool=").Append(slot.EquippedItem != null ? slot.EquippedItem.name : "-");
            }
            sb.Append('\n');
        }
        return sb.Length == 0 ? "error no InventoryItemToolManager" : sb.ToString().TrimEnd();
    }

    // QA: a scene's transition gates and respawn points from the game's own teleport map, so
    // `travel` can name a real gate.
    static string CommandGates(string[] a)
    {
        if (a.Length != 2) return "error gates <scene>";
        var map = SceneTeleportMap.GetTeleportMap();
        SceneTeleportMap.SceneInfo info;
        if (map == null || !map.TryGetValue(a[1], out info) || info == null) return "error no teleport-map entry for " + a[1];
        return "gates=" + string.Join(",", info.TransitionGates.ToArray()) + " respawns=" + string.Join(",", info.RespawnPoints.ToArray());
    }

    // QA: every component in a loaded scene (name contains <scene>) that carries a PlayerDataTest
    // field -- activators, additive-load conditions, deactivators -- with its conditions, whether
    // they pass now, and the GameObject fields it switches. Optional text filters the object name.
    static string CommandPlayerDataTests(string[] a)
    {
        if (a.Length < 2) return "error pdtests <scene> [text]";
        string text = a.Length > 2 ? string.Join(" ", a, 2, a.Length - 2) : null;
        var sb = new StringBuilder();
        int shown = 0;
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var component in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (component == null || !component.gameObject.scene.IsValid()
                || component.gameObject.scene.name.IndexOf(a[1], StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (text != null && component.gameObject.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var fields = component.GetType().GetFields(Fields);
            bool any = false;
            foreach (var field in fields)
            {
                var test = field.FieldType == typeof(PlayerDataTest) ? field.GetValue(component) as PlayerDataTest : null;
                if (test == null || !test.IsDefined) continue;
                if (!any)
                {
                    if (++shown > 50) { sb.Append("...\n"); return sb.ToString().TrimEnd(); }
                    Transform parent = component.transform.parent;
                    sb.Append(parent != null ? parent.name + "/" : "").Append(component.gameObject.name)
                      .Append(" [").Append(component.GetType().Name).Append("] active=").Append(component.gameObject.activeInHierarchy);
                    foreach (var target in fields)
                    {
                        var go = target.FieldType == typeof(GameObject) ? target.GetValue(component) as GameObject : null;
                        if (go != null) sb.Append(' ').Append(target.Name).Append('=').Append(go.name).Append(go.activeInHierarchy ? "(on)" : "(off)");
                    }
                    any = true;
                }
                sb.Append("\n  ").Append(field.Name).Append(" pass=").Append(test.IsFulfilled).Append(':');
                for (int g = 0; g < test.TestGroups.Length; g++)
                {
                    if (g > 0) sb.Append(" OR");
                    foreach (var t in test.TestGroups[g].Tests)
                    {
                        sb.Append(' ').Append(t.FieldName).Append(' ');
                        switch (t.Type)
                        {
                            case PlayerDataTest.TestType.Bool: sb.Append("==").Append(t.BoolValue); break;
                            case PlayerDataTest.TestType.Int:
                            case PlayerDataTest.TestType.Enum: sb.Append(t.NumType).Append(' ').Append(t.IntValue); break;
                            case PlayerDataTest.TestType.Float: sb.Append(t.NumType).Append(' ').Append(t.FloatValue); break;
                            default: sb.Append(t.Type).Append(' ').Append(t.StringType).Append(" \"").Append(t.StringValue).Append('"'); break;
                        }
                        sb.Append(PlayerData.HasInstance && t.IsFulfilled(PlayerData.instance) ? "(ok)" : "(no)");
                    }
                }
            }
            if (any) sb.Append('\n');
        }
        return shown == 0 ? "none" : sb.ToString().TrimEnd();
    }

    // QA: inspect and drive any loaded PlayMaker FSM (festival games, NPC flows) without injected
    // input. Arguments after the subcommand are '|'-separated because object, FSM and state names
    // contain spaces: `fsm show <object>|<fsm>[|<state filter>]`, `fsm send <object>|<fsm>|<event>`,
    // `fsm set <object>|<fsm>|<variable>|<value>`, `fsm goto <object>|<fsm>|<state>`.
    static string CommandFsm(string line)
    {
        string[] words = line.Split((char[])null, 3, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3) return "error fsm find <text> | fsm show|send|set|goto <object>[#n]|<fsm>|...";
        string sub = words[1].ToLowerInvariant();
        var all = Resources.FindObjectsOfTypeAll<PlayMakerFSM>();
        if (sub == "find")
        {
            string text = words[2].Trim();
            var sb = new StringBuilder();
            int shown = 0;
            var seen = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var f in all)
            {
                if (f == null || !f.gameObject.scene.IsValid()) continue;
                if (f.gameObject.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0
                    && f.FsmName.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++shown > 60) { sb.Append("...\n"); break; }
                // #n is the 1-based position that `show|send|set|goto <object>#n|<fsm>` selects.
                string key = f.gameObject.name + "|" + f.FsmName;
                int n;
                seen[key] = n = (seen.TryGetValue(key, out n) ? n : 0) + 1;
                sb.Append(f.gameObject.name).Append('#').Append(n).Append('|').Append(f.FsmName).Append(" state=").Append(f.ActiveStateName)
                  .Append(f.gameObject.activeInHierarchy ? "" : " inactive").Append(" scene=").Append(f.gameObject.scene.name).Append('\n');
            }
            return shown == 0 ? "none" : sb.ToString().TrimEnd();
        }
        string[] p = words[2].Split('|');
        if (p.Length < 2) return "error expected <object>|<fsm>";
        PlayMakerFSM fsm = null;
        int matches = 0, pick;
        string objectName = p[0].Trim();
        int hash = objectName.LastIndexOf('#');
        if (hash > 0 && int.TryParse(objectName.Substring(hash + 1), out pick) && pick > 0) objectName = objectName.Substring(0, hash);
        else pick = 0;
        foreach (var f in all)
        {
            if (f == null || !f.gameObject.scene.IsValid() || f.gameObject.name != objectName || f.FsmName != p[1].Trim()) continue;
            matches++;
            if (pick > 0) { if (matches == pick) fsm = f; }
            else if (fsm == null || (!fsm.gameObject.activeInHierarchy && f.gameObject.activeInHierarchy)) fsm = f;
        }
        if (fsm == null) return "error no FSM " + p[0] + "|" + p[1];
        string arg = p.Length > 2 ? p[2].Trim() : "";
        string before = fsm.ActiveStateName;
        switch (sub)
        {
            case "send":
                fsm.SendEvent(arg);
                return "ok sent=" + arg + " state=" + before + "->" + fsm.ActiveStateName + " matches=" + matches;
            case "goto":
                fsm.SetState(arg);
                return "ok state=" + before + "->" + fsm.ActiveStateName + " matches=" + matches;
            case "set":
                return p.Length == 4 ? SetFsmVariable(fsm, arg, p[3].Trim()) : "error fsm set <object>|<fsm>|<variable>|<value>";
            case "show":
                return DescribeFsm(fsm, arg, matches);
            default: return "error unknown fsm subcommand " + sub;
        }
    }

    static string SetFsmVariable(PlayMakerFSM fsm, string name, string value)
    {
        var v = fsm.FsmVariables;
        var i = v.FindFsmInt(name); if (i != null) { int x; if (!int.TryParse(value, out x)) return "error not an int"; i.Value = x; return "ok " + name + "=" + i.Value; }
        var b = v.FindFsmBool(name); if (b != null) { bool x; if (!bool.TryParse(value, out x)) return "error not a bool"; b.Value = x; return "ok " + name + "=" + b.Value; }
        var fl = v.FindFsmFloat(name); if (fl != null) { float x; if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)) return "error not a float"; fl.Value = x; return "ok " + name + "=" + fl.Value; }
        var s = v.FindFsmString(name); if (s != null) { s.Value = value; return "ok " + name + "=" + s.Value; }
        return "error no int/bool/float/string variable " + name;
    }

    static string DescribeFsm(PlayMakerFSM fsm, string filter, int matches)
    {
        var sb = new StringBuilder();
        sb.Append("state=").Append(fsm.ActiveStateName).Append(" active=").Append(fsm.gameObject.activeInHierarchy).Append(" matches=").Append(matches);
        var v = fsm.FsmVariables;
        sb.Append("\nvars:");
        foreach (var x in v.IntVariables) sb.Append(' ').Append(x.Name).Append('=').Append(x.Value);
        foreach (var x in v.BoolVariables) sb.Append(' ').Append(x.Name).Append('=').Append(x.Value);
        foreach (var x in v.FloatVariables) sb.Append(' ').Append(x.Name).Append('=').Append(x.Value.ToString("0.##"));
        foreach (var x in v.StringVariables) sb.Append(' ').Append(x.Name).Append("=\"").Append(x.Value).Append('"');
        sb.Append("\nglobal:");
        foreach (var t in fsm.FsmGlobalTransitions) sb.Append(' ').Append(t.EventName).Append("->").Append(t.ToState);
        foreach (var state in fsm.FsmStates)
        {
            if (filter.Length != 0 && state.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            sb.Append('\n').Append(state.Name).Append(':');
            foreach (var t in state.Transitions) sb.Append(' ').Append(t.EventName).Append("->").Append(t.ToState);
            if (filter.Length == 0) continue;
            // With a filter, also the actions: type plus their simple and FSM-variable fields.
            foreach (var action in state.Actions)
            {
                if (action == null) continue;
                sb.Append("\n  ").Append(action.GetType().Name);
                foreach (var field in action.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    string shown = DescribeActionValue(field.GetValue(action));
                    if (shown != null) sb.Append(' ').Append(field.Name).Append('=').Append(shown);
                }
            }
        }
        return sb.ToString();
    }

    static string DescribeActionValue(object value)
    {
        if (value == null) return null;
        var named = value as HutongGames.PlayMaker.NamedVariable;
        if (named != null)
        {
            if (named.UseVariable) return "{" + named.Name + "}";
            object raw = named.RawValue;
            if (raw == null) return null;
            string s = raw is UnityEngine.Object ? ((UnityEngine.Object)raw).name : raw.ToString();
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }
        var e = value as HutongGames.PlayMaker.FsmEvent;
        if (e != null) return "!" + e.Name;
        if (value is string || value is int || value is bool || value is float || value is Enum) return value.ToString();
        return null;
    }

    void PollCommands() { DsCommandFile.Poll(RunCommand); }

    string RunCommand(string line)
    {
        string[] a = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        GameManager gm = _gameManager;
        switch (a[0].ToLowerInvariant())
        {
            case "help": return CommandHelp;
            case "report": return CommandReport(gm);
            case "collect":
                // #37: what the checklist shows and how many pins are on the map; "checklist" toggles the panel.
                if (a.Length > 1 && a[1] == "checklist") DsMapScreen.RequestChecklistToggle = true;
                // #47: "collect why <scene prefix>" says why each spot there is or is not pinned.
                if (a.Length > 2 && a[1] == "why") return SsCollectibles.WhyReport(a[2]);
                return "collect " + DsMapScreen.ChecklistReport() + " pins=" + SsCollectibles.PinsShown;
            case "state": return CommandState(gm);
            case "pause":
                if (gm == null) return "error no GameManager";
                bool was = NativeGamePaused();
                return DsQaReport.Invoke(gm, gm, "PauseGameToggle", null) + " was-paused=" + was;
            case "mods": return SilksongNativeModsMenu.OpenForCommand(0);
            case "skins": return SilksongNativeModsMenu.OpenForCommand(1);
            case "skin":
                if (a.Length < 2 || (a[1] != "list" && a[1] != "close" &&
                    (a.Length < 3 || (a[1] != "row" && a[1] != "left" && a[1] != "right"))))
                    return "error skin <list|row N|left N|right N|close>";
                return SilksongNativeModsMenu.SkinCommand(a[1], a.Length > 2 ? a[2] : "");
            case "tab":
                if (a.Length < 2) return "error tab <id>";
                if (_shell == null) return "error no shell";
                return _shell.ShowId(a[1]) ? "ok active=" + _shell.ActiveId : "error unknown tab";
            case "mods-list": return CommandModsList();
            case "preset":
            {
                int n;
                if (a.Length < 3 || !int.TryParse(a[2], out n)) return "error preset <save|load> N";
                if (a[1] == "save") return "ok " + DsLoadoutPresets.Save(n);
                if (a[1] == "load") return "ok " + DsLoadoutPresets.Load(n);
                return "error preset <save|load> N";
            }
            case "assists": return SilksongGameplayFeatures.DeathReport() + "\n" + SilksongCheats.AssistReport();
            case "mod":
            {
                if (a.Length < 3) return "error mod <id> <value>";
                var c = ModsController();
                if (c == null) return "error Mods session not ready";
                var r = c.Set(a[1], a[2]);
                return (r.Success ? "ok" : "error " + r.Error) + (r.Pending ? " pending" : "") + " value=" + c.Value(a[1])
                    + (r.Readback.Kind == DualSouls.Mods.TweakReadbackKind.Text ? " result=" + r.Readback.Value : "");
            }
            case "pd":
                if (a.Length < 2) return "error pd <field>...";
                var names = new string[a.Length - 1];
                Array.Copy(a, 1, names, 0, names.Length);
                return DsQaReport.Fields(PlayerData.instance, names);
            case "pdset":
                if (a.Length != 3) return "error pdset <bool|int|enum field> <value>";
                return DsQaReport.SetBool(PlayerData.instance, a[1], a[2]);
            case "mapsetup":
                // The game's own room-state pass (what opening its map runs): mapped/visited rooms
                // pick up scenesMapped, mapAllRooms and the quill. QA after pdset on a disposable save.
                if (gm == null || gm.gameMap == null) return "error no GameMap";
                gm.gameMap.SetupMap(false);
                return "ok SetupMap " + DsQaReport.Fields(PlayerData.instance, "mapAllRooms", "hasQuill");
            case "die": return CommandDie(gm);
            case "hazard":
            {
                // QA (#52): one spike hit through the game's own damage path.
                var hero = HeroController.instance;
                var pd = PlayerData.instance;
                if (hero == null || pd == null) return "error no HeroController";
                if (gm == null || gm.GameState != GlobalEnums.GameState.PLAYING) return "error not playing";
                string before = DsQaReport.Fields(pd, "health");
                return DsQaReport.Invoke(hero, hero, "TakeDamage", new object[] { hero.gameObject }, "CollisionSide.bottom", "HazardType.SPIKES")
                       + " " + before + " -> " + DsQaReport.Fields(pd, "health");
            }
            case "fly":
            {
                // QA (#49): hold the flight's movement input (x y in -1..1; 0 0 releases).
                float fx, fy;
                if (a.Length != 3 || !float.TryParse(a[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fx) ||
                    !float.TryParse(a[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fy))
                    return "error fly x y";
                SilksongExtraCheats.QaFlightInput = new Vector2(Mathf.Clamp(fx, -1f, 1f), Mathf.Clamp(fy, -1f, 1f));
                return "ok fly=" + SilksongExtraCheats.QaFlightInput.x + "," + SilksongExtraCheats.QaFlightInput.y;
            }
            case "perform":
                // QA (#54): the game's own Needolin performing flag, which a held input sets.
                if (a.Length != 2 || (a[1] != "on" && a[1] != "off")) return "error perform on|off";
                HeroPerformanceRegion.IsPerforming = a[1] == "on";
                return "ok performing=" + HeroPerformanceRegion.IsPerforming;
            case "hit": return CommandHit(a, gm);
            case "hurt": return CommandHurt(a, gm);
            case "dash": return CommandDash(gm);
            case "travel": return CommandTravel(a, gm);
            case "delivery": return CommandDelivery(a, gm);
            case "crest":
            {
                // #43: the current crest's slots, unlocked tools of a type, or equip one into a slot.
                int slotIndex;
                if (a.Length == 1) return SilksongGameplayFeatures.CrestReport();
                if (a[1] == "tools" && a.Length == 3) return SilksongGameplayFeatures.CrestTools(a[2]);
                if (a[1] == "equip" && a.Length >= 4 && int.TryParse(a[2], out slotIndex))
                    return SilksongGameplayFeatures.CrestEquip(slotIndex, string.Join(" ", a, 3, a.Length - 3));
                if (a[1] == "unlock" && a.Length >= 3) return SilksongGameplayFeatures.CrestUnlockTool(string.Join(" ", a, 2, a.Length - 2));
                return "error crest [tools <type>|equip <slot> <tool name>|unlock <tool name>]";
            }
            case "inv": return CommandInventory(a, gm);
            case "fsm": return CommandFsm(line);
            case "gates": return CommandGates(a);
            case "pdtests": return CommandPlayerDataTests(a);
            case "sprites":
                var skinRuntime = SilksongSkinRuntime.Current;
                return skinRuntime != null ? skinRuntime.DescribeCollections() : "error no skin runtime";
            default: return "error unknown command; " + CommandHelp;
        }
    }

    string CommandState(GameManager gm)
    {
        var sb = new StringBuilder();
        sb.Append("scene=").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        if (gm != null)
        {
            sb.Append(" game=").Append(gm.GameState).Append(" paused=").Append(NativeGamePaused())
              .Append(' ').Append(DsQaReport.Fields(gm, "profileID"));
            if (gm.ui != null) sb.Append(" ui=").Append(gm.ui.uiState);
        }
        else sb.Append(" game=no GameManager");
        sb.Append("\nshell=").Append(_shell != null ? _shell.ActiveId : "-")
          .Append(" shellPaused=").Append(_shell != null && _shell.Paused)
          .Append(" card=").Append(_card != null);
        return sb.ToString();
    }

    // One call for device QA: everything the per-candidate checklist reads, so no UI navigation is needed.
    string CommandReport(GameManager gm)
    {
        var sb = new StringBuilder();
        sb.Append("report: game=Silksong version=").Append(Application.version)
          .Append(" time=").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        sb.Append(CommandState(gm)).Append('\n');
        sb.Append(_shell != null ? _shell.Report() : "panes: no shell").Append('\n');
        sb.Append("player: ").Append(DsQaReport.Fields(PlayerData.instance, ReportPlayerFields)).Append('\n');
        sb.Append("mods: ").Append(ModsController() != null ? "\n" + CommandModsList() : "not ready").Append('\n');
        var skins = SilksongSkinRuntime.Current;
        sb.Append("skins: ").Append(skins != null
            ? DsQaReport.Fields(skins.LastResult, "Status", "Detail") + " lastError=" + skins.LastError
              + " pack: " + DsQaReport.Fields(skins.CurrentPack, "Id", "Mode", "SpriteScope")
            : "no runtime").Append('\n');
        var modsRuntime = SilksongModsRuntime.Current;
        var library = modsRuntime != null ? modsRuntime.SkinLibrary : null;
        sb.Append("rotation: ").Append(library != null ? library.Describe() : "no library").Append('\n');
        sb.Append(DualSouls.Mods.HeroTint.Diagnostics).Append('\n');
        sb.Append(DualSouls.Mods.CombatOverlay.Describe()).Append('\n');
        sb.Append(DsQaReport.Frames()).Append('\n');
        sb.Append(DsQaReport.Errors());
        return sb.ToString();
    }

    // #39 QA on a disposable save: start a courier delivery, hurt Hornet through the game's own
    // damage path, and make the static DeliveryQuestItem.BreakAll call that the Bellway and
    // Ventrica arrival FSMs make. Each reply ends with the active delivery items.
    string CommandDelivery(string[] a, GameManager gm)
    {
        if (a.Length < 2) return "error delivery <list|give <name>|hit|break|state>";
        if (gm == null) return "error no GameManager";
        var hero = HeroController.SilentInstance;
        string result;
        switch (a[1])
        {
            case "list":
            {
                var sb = new StringBuilder("ok");
                foreach (BasicQuestBase basic in QuestManager.GetAllQuests())
                {
                    var quest = basic as FullQuestBase;
                    if (quest == null) continue;
                    foreach (FullQuestBase.QuestTarget target in quest.Targets)
                        if (target.Counter is DeliveryQuestItem)
                            sb.Append(' ').Append(quest.name).Append(':').Append(target.Counter.name).Append('x').Append(target.Count)
                              .Append(quest.IsAccepted ? (quest.IsCompleted ? "(done)" : "(active)") : "");
                }
                foreach (DeliveryQuestItemStandalone item in Resources.FindObjectsOfTypeAll<DeliveryQuestItemStandalone>())
                    sb.Append(" standalone:").Append(item.name).Append('x').Append(item.TargetCount);
                return sb.ToString();
            }
            case "give":
            {
                if (a.Length < 3) return "error delivery give <quest or standalone item name>";
                string wanted = string.Join(" ", a, 2, a.Length - 2);
                result = null;
                foreach (BasicQuestBase basic in QuestManager.GetAllQuests())
                {
                    var quest = basic as FullQuestBase;
                    if (quest == null || quest.name != wanted) continue;
                    foreach (FullQuestBase.QuestTarget target in quest.Targets)
                    {
                        var item = target.Counter as DeliveryQuestItem;
                        if (item == null) continue;
                        quest.BeginQuest(null, false);
                        item.Collect(target.Count, false);
                        result = "ok began " + quest.name + " with " + item.name + "x" + target.Count;
                        break;
                    }
                    break;
                }
                if (result == null)
                    foreach (DeliveryQuestItemStandalone item in Resources.FindObjectsOfTypeAll<DeliveryQuestItemStandalone>())
                    {
                        if (item.name != wanted) continue;
                        item.Collect(item.TargetCount, false);
                        result = "ok collected " + item.name + "x" + item.TargetCount;
                        break;
                    }
                if (result == null) return "error no delivery quest or standalone item " + wanted;
                break;
            }
            case "hit":
            {
                if (hero == null) return "error no HeroController";
                var pd = PlayerData.instance;
                int before = pd.health;
                hero.TakeDamage(hero.gameObject, GlobalEnums.CollisionSide.left, 1, GlobalEnums.HazardType.ENEMY);
                result = "ok health=" + before + "->" + pd.health;
                break;
            }
            case "break":
                DeliveryQuestItem.BreakAll();
                result = "ok BreakAll";
                break;
            case "state":
                result = "ok";
                break;
            default: return "error delivery <list|give <name>|hit|break|state>";
        }
        var items = new StringBuilder();
        foreach (DeliveryQuestItem.ActiveItem active in DeliveryQuestItem.GetActiveItems())
            items.Append(' ').Append(active.Item.name).Append('=').Append(active.CurrentCount).Append('/').Append(active.MaxCount);
        return result + " | active:" + (items.Length == 0 ? " none" : items.ToString()) + " | " + SilksongGameplayHooks.DeliveryReport();
    }

    // QA: one lethal hit through the game's own damage path, so death rotation runs for real.
    string CommandDie(GameManager gm)
    {
        var hero = HeroController.instance;
        var pd = PlayerData.instance;
        if (hero == null || pd == null) return "error no HeroController";
        if (gm == null || gm.GameState != GlobalEnums.GameState.PLAYING) return "error not playing";
        FieldInfo health = typeof(PlayerData).GetField("health", BindingFlags.Public | BindingFlags.Instance);
        FieldInfo blue = typeof(PlayerData).GetField("healthBlue", BindingFlags.Public | BindingFlags.Instance);
        if (health == null) return "error no PlayerData.health";
        if (blue != null) blue.SetValue(pd, 0);
        health.SetValue(pd, 1);
        return DsQaReport.Invoke(hero, hero, "TakeDamage", new object[] { hero.gameObject }, "CollisionSide.top", "HazardType.NON_HAZARD")
               + " " + DsQaReport.Fields(pd, "health");
    }

    // QA hooks for what injected input cannot reach (InControl ignores adb key events).
    // hurt: non-lethal damage through the game's own TakeHealth, so the HUD follows.
    string CommandHurt(string[] a, GameManager gm)
    {
        int amount;
        if (a.Length < 2 || !int.TryParse(a[1], out amount) || amount <= 0) return "error hurt <amount>";
        if (gm == null) return "error no GameManager";
        var pd = PlayerData.instance;
        if (pd == null) return "error no PlayerData";
        pd.TakeHealth(Mathf.Min(amount, pd.health - 1), false, false);
        return "ok " + DsQaReport.Fields(pd, "health", "maxHealth");
    }

    // dash: the hero's own dash start, as the dash button would trigger it.
    string CommandDash(GameManager gm)
    {
        if (gm == null) return "error no GameManager";
        var hero = HeroController.instance;
        if (hero == null) return "error no HeroController";
        if (!hero.CanDash()) return "error hero cannot dash now";
        MethodInfo dash = typeof(HeroController).GetMethod("HeroDash", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(bool) }, null);
        if (dash == null) return "error no HeroController.HeroDash(bool)";
        dash.Invoke(hero, new object[] { false });
        return "ok " + DsQaReport.Fields(hero.cState, "dashing");
    }

    // travel: a normal scene transition through a named entrance.
    string CommandTravel(string[] a, GameManager gm)
    {
        if (a.Length < 3) return "error travel <scene> <gate>";
        if (gm == null) return "error no GameManager";
        if (gm.GameState != GlobalEnums.GameState.PLAYING || gm.IsInSceneTransition) return "error not playing";
        gm.BeginSceneTransition(new GameManager.SceneLoadInfo
        {
            SceneName = a[1],
            EntryGateName = a[2],
            PreventCameraFadeOut = false,
            WaitForSceneTransitionCameraFade = true,
            Visualization = GameManager.SceneLoadVisualizations.Default,
        });
        return "ok travelling to " + a[1] + " via " + a[2];
    }

    static DualSouls.Mods.TweakController ModsController()
    {
        var runtime = SilksongModsRuntime.Current;
        var session = runtime != null ? runtime.Session : null;
        return session != null && session.IsReady ? session.Controller : null;
    }

    static string CommandModsList()
    {
        var c = ModsController();
        if (c == null) return "error Mods session not ready";
        var sb = new StringBuilder();
        foreach (var d in c.Descriptors)
        {
            sb.Append(d.Group).Append('/').Append(d.Id).Append('=').Append(c.Value(d.Id));
            if (!d.IsAvailable) sb.Append(" (unavailable: ").Append(d.UnavailableReason).Append(')');
            sb.Append(" [").Append(string.Join(",", d.Values)).Append("]\n");
        }
        return sb.ToString().TrimEnd('\n');
    }
}
#endif
