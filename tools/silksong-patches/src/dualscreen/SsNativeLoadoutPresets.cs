// SsNativeLoadoutPresets -- #69 loadout preset selector inside the game's own
// Tools pane (Start menu), the Silksong counterpart of HkNativeCharmPresets.
//
// The pane's own "Change Crest" button is cloned beside it. Confirm on the clone
// cycles preset 1 -> 2 -> 3: the loadout being left is kept in its preset first
// (DsLoadoutPresets.SwitchTo). In Change Crest's place (one crest) Change Crest's own
// key presses it too, and the cursor reaches it wherever it reached Change Crest.
// The game's own CanChangeEquips check runs before
// anything changes, so away from a bench the pane shows its own "rest at a bench"
// message, and EQUIP ANYWHERE lifts it exactly as it does for crests.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Reflection;
using TMP_Text = TMProOld.TMP_Text;   // Silksong ships Team Cherry's TMProOld
using UnityEngine;

public static class SsNativeLoadoutPresets
{
    const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    const int Left = (int)InventoryItemManager.SelectionDirection.Left;
    const int Right = (int)InventoryItemManager.SelectionDirection.Right;

    static readonly FieldInfo FiChange = typeof(InventoryItemToolManager).GetField("changeCrestButton", Priv);
    static readonly FieldInfo FiCrestList = typeof(InventoryItemToolManager).GetField("crestList", Priv);
    static readonly FieldInfo FiPrompt = typeof(InventoryItemSelectableButtonEvent).GetField("interactionDisplayText", Priv);
    static readonly FieldInfo FiChangeText = typeof(InventoryItemToolManager).GetField("changeCrestButtonText", Priv);
    static readonly MethodInfo MiSetup = typeof(InventoryToolCrestList).GetMethod("Setup", Priv);

    static InventoryItemToolManager _mgr;
    static InventoryItemSelectableButtonEvent _orig, _btn;
    static Transform _src, _root;   // what was cloned (the button, or its prompt when the button has no visuals) and our copy
    static TMP_Text[] _labels = new TMP_Text[0];
    static float _next;
    static int _shown = -1;
    static bool _beside;            // the game's own Change Crest button is shown: sit beside it, else in its place
    static string _last = "";
    static Vector2 _offset = new Vector2(6.5f, 0f);   // from the Change Crest prompt, pane-local; QA "preset pos"
    static GameObject[] _glyphs = new GameObject[0];   // the cloned key glyph: Change Crest's own key (Super)
    static Vector3 _labelAt, _labelBeside;              // label beside the glyph, or in its place
    static InventoryPaneBase _pane;
    static InventoryPaneInput _paneInput;
    static bool _superHeld;
    static int _navRefs;
    static int _supers, _activations;

    public static bool Built { get { return _btn != null; } }

    public static void Tick()
    {
        if (_btn != null)
        {
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            if (!_btn.gameObject.activeSelf) _btn.gameObject.SetActive(true);
            bool beside = _orig != null && _orig.gameObject.activeInHierarchy;
            if (beside != _beside) { _beside = beside; Place(); }
            ShowActive(false);
            PollSuper();
            return;
        }
        if (Time.unscaledTime < _next || !DsGameData.InGame) return;
        _next = Time.unscaledTime + 1f;
        try { Build(); }
        catch (Exception e) { Debug.LogWarning("[DualScreen] preset button: " + e.Message); _next = Time.unscaledTime + 10f; }
    }

    static void Build()
    {
        if (FiChange == null) return;
        var managers = Resources.FindObjectsOfTypeAll<InventoryItemToolManager>();
        for (int i = 0; i < managers.Length; i++)
        {
            var m = managers[i];
            if (m == null || !m.gameObject.scene.IsValid()) continue;   // skip prefabs
            var orig = FiChange.GetValue(m) as InventoryItemSelectableButtonEvent;
            if (orig == null) continue;

            // The button itself is only a selectable; its icon and text sit on its parent prompt.
            Transform src = orig.transform;
            if (src.GetComponentsInChildren<Renderer>(true).Length == 0 && src.GetComponentsInChildren<TMP_Text>(true).Length == 0 && src.parent != null)
                src = src.parent;
            string labelPath = null;
            var changeText = FiChangeText != null ? FiChangeText.GetValue(m) as Component : null;
            if (changeText != null && changeText.transform.IsChildOf(src)) labelPath = PathFrom(src, changeText.transform);
            string btnPath = PathFrom(src, orig.transform);

            var go = UnityEngine.Object.Instantiate(src.gameObject, src.parent);
            go.name = "DualSouls Preset";
            var bt = btnPath.Length == 0 ? go.transform : go.transform.Find(btnPath);
            var btn = bt != null ? bt.GetComponent<InventoryItemSelectableButtonEvent>() : null;
            if (btn == null) { UnityEngine.Object.Destroy(go); return; }
            // The game's own localised text would overwrite ours: ours is plain.
            foreach (var t in go.GetComponentsInChildren<SetTextMeshProGameText>(true)) UnityEngine.Object.DestroyImmediate(t);
            if (FiPrompt != null) FiPrompt.SetValue(btn, null);
            btn.ButtonActivated = Activate;

            // Beside the original: left goes back to it, right to whatever it led to. When the
            // original is hidden (one crest), the game forwards moves onto it to its Right: us.
            if (btn.Selectables != null && orig.Selectables != null && orig.Selectables.Length > Right)
            {
                btn.Selectables[Left] = orig;
                btn.Selectables[Right] = orig.Selectables[Right];
                orig.Selectables[Right] = btn;
            }
            _src = src; _root = go.transform;
            var lt = labelPath != null ? go.transform.Find(labelPath) : null;
            var one = lt != null ? lt.GetComponent<TMP_Text>() : null;
            if (one == null)   // not the button prompt's glyph text: the one under "Text"
                foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
                    if (t.name == "Text" || (t.transform.parent != null && t.transform.parent.name == "Text")) { one = t; break; }
            _labels = one != null ? new[] { one } : new TMP_Text[0];
            // The clone keeps Change Crest's key glyph (the menu's Super action). In Change Crest's
            // place (one crest) that key presses the preset, so the glyph stays; beside the game's
            // own button the key is Change Crest's, so the glyph hides and the label takes its spot.
            var icons = go.GetComponentsInChildren<ActionButtonIconBase>(true);
            _glyphs = new GameObject[icons.Length];
            for (int k = 0; k < icons.Length; k++) _glyphs[k] = icons[k].gameObject;
            if (one != null)
            {
                _labelAt = _labelBeside = one.transform.localPosition;
                for (int k = 0; k < icons.Length; k++)
                    if (one.transform.parent == icons[k].transform.parent)
                        _labelBeside = new Vector3(icons[k].transform.localPosition.x, _labelAt.y, _labelAt.z);
            }
            // Wherever the cursor leads to Change Crest, it also leads to the preset when Change Crest is hidden.
            _navRefs = 0;
            foreach (var d in m.GetComponentsInChildren<InventoryItemSelectableDirectional>(true))
            {
                if (d == orig || d == btn || d.Selectables == null || d.FallbackSelectables == null) continue;
                for (int k = 0; k < d.Selectables.Length && k < d.FallbackSelectables.Length; k++)
                {
                    var fb = d.FallbackSelectables[k];
                    bool refs = d.Selectables[k] == orig || (fb != null && fb.Selectables != null && fb.Selectables.Contains(orig));
                    if (!refs) continue;
                    if (fb == null) d.FallbackSelectables[k] = fb = new InventoryItemSelectableDirectional.SelectableList();
                    if (fb.Selectables == null) fb.Selectables = new System.Collections.Generic.List<InventoryItemSelectable>();
                    if (!fb.Selectables.Contains(btn)) { fb.Selectables.Add(btn); _navRefs++; }
                }
            }
            _mgr = m; _orig = orig; _btn = btn;
            _beside = orig.gameObject.activeInHierarchy;
            Place();
            go.SetActive(true);
            ShowActive(true);
            return;
        }
    }

    static void Place()
    {
        if (_root == null || _src == null) return;
        var p = _src.localPosition;
        var o = _beside ? _offset : Vector2.zero;
        _root.localPosition = new Vector3(p.x + o.x, p.y + o.y, p.z);
        for (int k = 0; k < _glyphs.Length; k++) if (_glyphs[k] != null) _glyphs[k].SetActive(!_beside);
        if (_labels.Length > 0 && _labels[0] != null) _labels[0].transform.localPosition = _beside ? _labelBeside : _labelAt;
    }

    /// <summary>In Change Crest's place its key (Super) presses the preset, as it would have changed crest.</summary>
    static void PollSuper()
    {
        bool super = false;
        // Built while the inventory is closed: the pane is inactive then, so look it up (inactive included) here.
        if (_pane == null && _mgr != null) _pane = _mgr.GetComponentInParent<InventoryPaneBase>(true);
        if (_paneInput == null && _mgr != null) _paneInput = _mgr.GetComponentInParent<InventoryPaneInput>(true);
        if (!_beside && _mgr != null && _btn.gameObject.activeInHierarchy && !_mgr.IsActionsBlocked &&
            _pane != null && _pane.IsPaneActive && _paneInput != null && _paneInput.enabled)
        {
            var gm = GameManager.instance;
            super = gm != null && gm.inputHandler != null &&
                    Platform.Current.GetMenuAction(gm.inputHandler.inputActions) == Platform.MenuActions.Super;
        }
        if (super && !_superHeld) { _supers++; Activate(); }
        _superHeld = super;
    }

    static string PathFrom(Transform root, Transform t)
    {
        string path = "";
        for (var c = t; c != null && c != root; c = c.parent) path = path.Length == 0 ? c.name : c.name + "/" + path;
        return path;
    }

    static void Tree(System.Text.StringBuilder sb, Transform t, int depth)
    {
        sb.Append("\n").Append(' ', depth * 2).Append(t.name).Append(' ').Append(t.localPosition.ToString("F2"))
          .Append(t.gameObject.activeSelf ? "" : " off");
        var tmp = t.GetComponent<TMP_Text>();
        if (tmp != null) sb.Append(" text=\"").Append(tmp.text).Append("\" font=").Append(tmp.font != null ? tmp.font.name : "-");
        if (t.GetComponent<SpriteRenderer>() != null) sb.Append(" sprite");
        var abi = t.GetComponent<ActionButtonIconBase>();
        if (abi != null) sb.Append(" glyph=").Append(abi.GetType().Name).Append(abi.label != null ? " label=" + abi.label.name : "");
        if (t.GetComponent<InventoryItemSelectable>() != null) sb.Append(" selectable");
        if (depth < 4) foreach (Transform c in t) Tree(sb, c, depth + 1);
    }

    static void ShowActive(bool force)
    {
        int n = DsLoadoutPresets.Active;
        if (!force && n == _shown) return;
        _shown = n;
        for (int i = 0; i < _labels.Length; i++)
            if (_labels[i] != null) _labels[i].text = "Preset " + n;
    }

    static void Activate()
    {
        _activations++;
        var m = _mgr;
        if (m == null || m.EquipState != InventoryItemToolManager.EquipStates.None) return;
        // The pane's own rule and message: bench (or EQUIP ANYWHERE), not cursed.
        if (!m.CanChangeEquips(ToolItemType.Red, InventoryItemToolManager.CanChangeEquipsTypes.Regular)) return;
        Step(1);
    }

    static string Step(int d)
    {
        int c = DsLoadoutPresets.Count;
        int to = ((DsLoadoutPresets.Active - 1 + d) % c + c) % c + 1;
        _last = DsLoadoutPresets.SwitchTo(to);
        RefreshPane();
        ShowActive(true);
        return _last;
    }

    /// <summary>Re-read crest and tools from PlayerData, as the pane does when it is built.</summary>
    static void RefreshPane()
    {
        try
        {
            var list = _mgr != null && FiCrestList != null ? FiCrestList.GetValue(_mgr) as InventoryToolCrestList : null;
            if (list != null && MiSetup != null) MiSetup.Invoke(list, null);
            else if (_mgr != null) _mgr.RefreshTools();
        }
        catch (Exception e) { Debug.LogWarning("[DualScreen] preset pane refresh: " + e.Message); }
    }

    /// <summary>QA: "preset", "preset pos x y", "preset step N", "preset tree".</summary>
    public static string Command(string[] a)
    {
        if (a.Length >= 4 && a[1] == "pos")
        {
            float x, y;
            if (!float.TryParse(a[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) ||
                !float.TryParse(a[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y))
                return "error preset pos x y";
            _offset = new Vector2(x, y);
            Place();
        }
        else if (a.Length >= 2 && a[1] == "select" && _btn != null && _mgr != null)
        {
            _mgr.SetSelected(_btn, null);
        }
        else if (a.Length >= 3 && a[1] == "step")
        {
            int d;
            if (!int.TryParse(a[2], out d)) return "error preset step N";
            Step(d);
        }
        if (_btn == null) return "preset built=False active=" + DsLoadoutPresets.Active;
        if (a.Length >= 2 && a[1] == "tree")
        {
            var sb = new System.Text.StringBuilder("tree of " + (_src != null && _src.parent != null ? _src.parent.name : "-"));
            if (_src != null && _src.parent != null) Tree(sb, _src.parent, 0);
            return sb.ToString();
        }
        var lp = _root.localPosition;
        var op = _src != null ? _src.localPosition : Vector3.zero;
        return "preset built=True active=" + DsLoadoutPresets.Active + " last=\"" + _last + "\"" +
               " offset=" + _offset.x.ToString("0.00") + "," + _offset.y.ToString("0.00") +
               " local=" + lp.x.ToString("0.00") + "," + lp.y.ToString("0.00") +
               " orig=" + op.x.ToString("0.00") + "," + op.y.ToString("0.00") +
               " src=" + (_src != null ? _src.name : "-") + " parent=" + (_root.parent != null ? _root.parent.name : "-") +
               " shown=" + _btn.gameObject.activeInHierarchy + " origShown=" + (_orig != null && _orig.gameObject.activeInHierarchy) +
               " labels=" + _labels.Length + " glyphs=" + _glyphs.Length + " navRefs=" + _navRefs +
               " selected=" + (_mgr != null && _mgr.CurrentSelected == _btn) +
               " poll=beside:" + _beside + ",blocked:" + (_mgr != null && _mgr.IsActionsBlocked) +
               ",pane:" + (_pane != null ? _pane.name + "/" + _pane.IsPaneActive : "null") +
               ",input:" + (_paneInput != null ? _paneInput.name + "/" + _paneInput.enabled : "null") +
               ",equip:" + (_mgr != null ? _mgr.EquipState.ToString() : "-") +
               ",supers:" + _supers + ",activations:" + _activations +
               " font=" + (_labels.Length > 0 && _labels[0] != null && _labels[0].font != null ? _labels[0].font.name : "-");
    }
}
#endif
