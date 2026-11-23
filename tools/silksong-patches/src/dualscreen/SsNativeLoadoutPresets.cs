// SsNativeLoadoutPresets -- #69 loadout preset selector inside the game's own
// Tools pane (Start menu), the Silksong counterpart of HkNativeCharmPresets.
//
// The pane's own "Change Crest" button is cloned beside it. Confirm on the clone
// cycles preset 1 -> 2 -> 3: the loadout being left is kept in its preset first
// (DsLoadoutPresets.SwitchTo). The game's own CanChangeEquips check runs before
// anything changes, so away from a bench the pane shows its own "rest at a bench"
// message, and EQUIP ANYWHERE lifts it exactly as it does for crests.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Reflection;
using TMPro;
using UnityEngine;

public static class SsNativeLoadoutPresets
{
    const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    const int Left = (int)InventoryItemManager.SelectionDirection.Left;
    const int Right = (int)InventoryItemManager.SelectionDirection.Right;

    static readonly FieldInfo FiChange = typeof(InventoryItemToolManager).GetField("changeCrestButton", Priv);
    static readonly FieldInfo FiCrestList = typeof(InventoryItemToolManager).GetField("crestList", Priv);
    static readonly FieldInfo FiPrompt = typeof(InventoryItemSelectableButtonEvent).GetField("interactionDisplayText", Priv);
    static readonly MethodInfo MiSetup = typeof(InventoryToolCrestList).GetMethod("Setup", Priv);

    static InventoryItemToolManager _mgr;
    static InventoryItemSelectableButtonEvent _orig, _btn;
    static TMP_Text[] _labels = new TMP_Text[0];
    static float _next;
    static int _shown = -1;
    static string _last = "";
    static Vector2 _offset = new Vector2(3.2f, 0f);   // from the Change Crest button, pane-local; QA "preset pos"

    public static bool Built { get { return _btn != null; } }

    public static void Tick()
    {
        if (_btn != null)
        {
            if (!_btn.gameObject.activeSelf) _btn.gameObject.SetActive(true);
            ShowActive(false);
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

            var go = UnityEngine.Object.Instantiate(orig.gameObject, orig.transform.parent);
            go.name = "DualSouls Preset";
            var btn = go.GetComponent<InventoryItemSelectableButtonEvent>();
            if (btn == null) { UnityEngine.Object.Destroy(go); return; }
            // The game's own localised text would overwrite ours: ours is plain.
            foreach (var t in go.GetComponentsInChildren<SetTextMeshProGameText>(true)) UnityEngine.Object.DestroyImmediate(t);
            if (FiPrompt != null) FiPrompt.SetValue(btn, null);
            btn.ButtonActivated = Activate;

            // Beside the original: left goes back to it, right to whatever it led to.
            if (btn.Selectables != null && orig.Selectables != null && orig.Selectables.Length > Right)
            {
                btn.Selectables[Left] = orig;
                btn.Selectables[Right] = orig.Selectables[Right];
                orig.Selectables[Right] = btn;
            }
            _mgr = m; _orig = orig; _btn = btn;
            _labels = go.GetComponentsInChildren<TMP_Text>(true);
            Place();
            go.SetActive(true);
            ShowActive(true);
            return;
        }
    }

    static void Place()
    {
        if (_btn == null || _orig == null) return;
        var p = _orig.transform.localPosition;
        _btn.transform.localPosition = new Vector3(p.x + _offset.x, p.y + _offset.y, p.z);
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

    /// <summary>QA: "preset", "preset pos x y", "preset step N".</summary>
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
        else if (a.Length >= 3 && a[1] == "step")
        {
            int d;
            if (!int.TryParse(a[2], out d)) return "error preset step N";
            Step(d);
        }
        if (_btn == null) return "preset built=False active=" + DsLoadoutPresets.Active;
        var lp = _btn.transform.localPosition;
        var op = _orig != null ? _orig.transform.localPosition : Vector3.zero;
        return "preset built=True active=" + DsLoadoutPresets.Active + " last=\"" + _last + "\"" +
               " offset=" + _offset.x.ToString("0.00") + "," + _offset.y.ToString("0.00") +
               " local=" + lp.x.ToString("0.00") + "," + lp.y.ToString("0.00") +
               " orig=" + op.x.ToString("0.00") + "," + op.y.ToString("0.00") +
               " parent=" + (_btn.transform.parent != null ? _btn.transform.parent.name : "-") +
               " shown=" + _btn.gameObject.activeInHierarchy + " labels=" + _labels.Length;
    }
}
#endif
