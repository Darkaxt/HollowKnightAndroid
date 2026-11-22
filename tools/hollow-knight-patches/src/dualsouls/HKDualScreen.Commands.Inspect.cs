using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

// [Q] QA inspection commands, matching Silksong's: the inventory FSM, any loaded PlayMaker FSM,
// a loaded scene's gates and the PlayerData conditions its objects read. See RunCommand.
public partial class HKDualScreen
{
    // inv: the "Inventory Control" FSM (GameManager.inventoryFSM) state and transitions, so an open
    // event can be read off the device; `inv send <event>` sends one. HK has no next-pane selector,
    // so Silksong's `inv next` and `inv tools` have no counterpart here.
    static string CommandInventory(string[] a, GameManager gm)
    {
        if (gm == null) return "error no GameManager";
        PlayMakerFSM fsm = gm.inventoryFSM;
        if (fsm == null) return "error no inventory FSM";
        string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "fsm";
        if (sub == "send" && a.Length >= 3)
        {
            string name = string.Join(" ", a, 2, a.Length - 2), before = fsm.ActiveStateName;
            fsm.SendEvent(name);
            return "ok sent=" + name + " state=" + before + "->" + fsm.ActiveStateName;
        }
        if (sub != "fsm") return "error inv [fsm|send <event>]";
        var sb = new StringBuilder();
        sb.Append("state=").Append(fsm.ActiveStateName);
        AppendTransitions(sb, fsm, "");
        return sb.ToString();
    }

    static void AppendTransitions(StringBuilder sb, PlayMakerFSM fsm, string filter)
    {
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
    }

    // fsm: inspect and drive any loaded PlayMaker FSM without injected input. Arguments after the
    // subcommand are '|'-separated because object, FSM and state names contain spaces:
    // `fsm show <object>|<fsm>[|<state filter>]`, `fsm send <object>|<fsm>|<event>`,
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
            var seen = new Dictionary<string, int>();
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
        AppendTransitions(sb, fsm, filter);
        return sb.ToString();
    }

    static string DescribeActionValue(object value)
    {
        if (value == null) return null;
        var named = value as HutongGames.PlayMaker.NamedVariable;
        if (named != null)
        {
            if (named.UseVariable) return "{" + named.Name + "}";
            // HK's PlayMaker build: read each wrapper's own Value property rather than assume RawValue.
            PropertyInfo prop = named.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            object raw = prop != null && prop.GetIndexParameters().Length == 0 ? prop.GetValue(named, null) : null;
            if (raw == null) return null;
            string s = raw is UnityEngine.Object ? ((UnityEngine.Object)raw).name : raw.ToString();
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }
        var e = value as HutongGames.PlayMaker.FsmEvent;
        if (e != null) return "!" + e.Name;
        if (value is string || value is int || value is bool || value is float || value is Enum) return value.ToString();
        return null;
    }

    // gates: the transition gates of a loaded scene, so `travel` can name a real gate. HK has no
    // teleport map of unloaded scenes (Silksong's source), so only loaded scenes answer.
    static string CommandGates(string[] a)
    {
        if (a.Length != 2) return "error gates <scene>";
        if (TransitionPoint.TransitionPoints == null) return "error no TransitionPoint list";
        var names = new List<string>();
        var sb = new StringBuilder();
        foreach (TransitionPoint gate in TransitionPoint.TransitionPoints)
        {
            if (gate == null || !string.Equals(gate.gameObject.scene.name, a[1], StringComparison.OrdinalIgnoreCase)) continue;
            names.Add(gate.name);
            sb.Append('\n').Append(gate.name).Append(gate.gameObject.activeInHierarchy ? "" : " inactive")
              .Append(' ').Append(DsQaReport.Fields(gate, "targetScene", "entryPoint"));
        }
        if (names.Count == 0) return "error no gates in loaded scene " + a[1];
        return "gates=" + string.Join(",", names.ToArray()) + sb;
    }

    // pdtests: every component in a loaded scene (name contains <scene>) with a string field naming a
    // PlayerData field -- HK's activators and deactivators read their condition that way -- with
    // the current PlayerData value and the GameObject fields it switches. Optional text filters the
    // object name. FSM-held PlayerData tests are actions; read those with `fsm show`.
    static string CommandPlayerDataTests(string[] a)
    {
        if (a.Length < 2) return "error pdtests <scene> [text]";
        string text = a.Length > 2 ? string.Join(" ", a, 2, a.Length - 2) : null;
        var pd = PlayerData.instance;
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
                string name = field.FieldType == typeof(string) ? field.GetValue(component) as string : null;
                FieldInfo pdField = !string.IsNullOrEmpty(name)
                    ? typeof(PlayerData).GetField(name, BindingFlags.Public | BindingFlags.Instance) : null;
                if (pdField == null) continue;
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
                sb.Append("\n  ").Append(field.Name).Append('=').Append(name)
                  .Append(" now=").Append(pd != null ? Convert.ToString(pdField.GetValue(pd), System.Globalization.CultureInfo.InvariantCulture) : "?");
            }
            if (any) sb.Append('\n');
        }
        return shown == 0 ? "none" : sb.ToString().TrimEnd();
    }
}
