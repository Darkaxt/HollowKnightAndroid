// DsHudStrip — the persistent Dual Souls-style game context above every page.
//
// It reads only public PlayerData/GameManager state. The second display never
// drives the combat HUD and never hides the primary HUD; this is a companion
// summary, not a second authority over the game.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using DualSouls.DualScreen;
using UnityEngine;
using UnityEngine.UI;
using TmpText = TMProOld.TextMeshProUGUI;
using TmpAlign = TMProOld.TextAlignmentOptions;

public sealed class DsHudStrip
{
    const int MaxHealthGlyphs = 12;
    const float SilkTrackX = 120f;
    const float SilkTrackW = 276f;

    readonly List<Image> _health = new List<Image>();
    RectTransform _root;
    Image _silkFill;
    TmpText _silkValue;
    TmpText _crest;
    TmpText _resources;
    TmpText _area;
    string _last = "";
    float _nextRefresh;

    public DsHudStrip(RectTransform parent, CompanionRect bounds)
    {
        Build(parent, bounds);
    }

    void Build(RectTransform parent, CompanionRect bounds)
    {
        _root = DsWidgets.Rect(parent, "hud-strip");
        DsWidgets.Place(_root, bounds.Left, bounds.Top, bounds.Width, bounds.Height);

        var healthTitle = DsWidgets.Label(_root, "health-title", "HEALTH", DsTheme.SmallSize,
                                          DsTheme.InkDim, TmpAlign.Left, display: true);
        if (healthTitle != null) DsWidgets.Place(healthTitle.rectTransform, 20f, 6f, 150f, 32f);

        for (int i = 0; i < MaxHealthGlyphs; i++)
        {
            var glyph = DsWidgets.Circle(_root, "health-" + i, DsTheme.InkFaint);
            DsWidgets.Place(glyph.rectTransform, 20f + i * 32f, 42f, 24f, 24f);
            _health.Add(glyph);
        }

        var silkTitle = DsWidgets.Label(_root, "silk-title", "SILK", DsTheme.SmallSize,
                                        DsTheme.InkDim, TmpAlign.Left, display: true);
        if (silkTitle != null) DsWidgets.Place(silkTitle.rectTransform, 20f, 88f, 96f, 36f);

        var silkTrack = DsWidgets.Box(_root, "silk-track", DsTheme.PanelEdge);
        DsWidgets.Place(silkTrack.rectTransform, SilkTrackX, 101f, SilkTrackW, 8f);
        _silkFill = DsWidgets.Box(_root, "silk-fill", DsTheme.Ink);
        DsWidgets.Place(_silkFill.rectTransform, SilkTrackX, 97f, 0f, 16f);
        _silkValue = DsWidgets.Label(_root, "silk-value", "", DsTheme.SmallSize,
                                     DsTheme.Ink, TmpAlign.Right, display: true);
        if (_silkValue != null) DsWidgets.Place(_silkValue.rectTransform, 300f, 112f, 96f, 30f);

        DsWidgets.VRule(_root, "left-divider", 430f, 18f, 118f);
        DsWidgets.VRule(_root, "right-divider", 780f, 18f, 118f);

        var crestTitle = DsWidgets.Label(_root, "crest-title", "CREST", DsTheme.SmallSize,
                                         DsTheme.InkDim, TmpAlign.Center, display: true);
        if (crestTitle != null) DsWidgets.Place(crestTitle.rectTransform, 450f, 6f, 310f, 32f);
        _crest = DsWidgets.Label(_root, "crest", "", DsTheme.BodySize,
                                 DsTheme.Ink, TmpAlign.Center, display: true);
        if (_crest != null) DsWidgets.Place(_crest.rectTransform, 450f, 38f, 310f, 46f);
        _resources = DsWidgets.Label(_root, "resources", "", DsTheme.SmallSize,
                                     DsTheme.InkDim, TmpAlign.Center, display: true);
        if (_resources != null) DsWidgets.Place(_resources.rectTransform, 442f, 96f, 326f, 36f);

        var areaTitle = DsWidgets.Label(_root, "area-title", "AREA", DsTheme.SmallSize,
                                        DsTheme.InkDim, TmpAlign.Center, display: true);
        if (areaTitle != null) DsWidgets.Place(areaTitle.rectTransform, 800f, 6f, bounds.Width - 820f, 32f);
        _area = DsWidgets.Label(_root, "area", "", DsTheme.TitleSize,
                                DsTheme.Ink, TmpAlign.Center);
        if (_area != null) DsWidgets.Place(_area.rectTransform, 800f, 40f, bounds.Width - 820f, 86f);
    }

    public void Tick()
    {
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 0.2f;

        if (!DsGameData.InGame)
        {
            Clear();
            return;
        }

        try
        {
            var pd = PlayerData.instance;
            int maxHealth = Mathf.Max(0, pd.CurrentMaxHealth);
            int health = Mathf.Clamp(pd.health, 0, maxHealth);
            int silkMax = Mathf.Max(0, pd.silkMax);
            int silk = Mathf.Clamp(pd.silk, 0, silkMax);
            string crest = Friendly(pd.CurrentCrestID);
            string area = AreaName();
            string key = health + "/" + maxHealth + "/" + silk + "/" + silkMax + "/" +
                         pd.geo + "/" + pd.ShellShards + "/" + crest + "/" + area;
            if (key == _last) return;
            _last = key;

            for (int i = 0; i < _health.Count; i++)
            {
                bool present = i < Mathf.Min(maxHealth, MaxHealthGlyphs);
                _health[i].gameObject.SetActive(present);
                if (present) _health[i].color = i < health ? DsTheme.Ink : DsTheme.PanelEdge;
            }

            float ratio = silkMax > 0 ? silk / (float)silkMax : 0f;
            DsWidgets.Place(_silkFill.rectTransform, SilkTrackX, 97f, SilkTrackW * ratio, 16f);
            Set(_silkValue, silk + " / " + silkMax);
            Set(_crest, string.IsNullOrEmpty(crest) ? "UNBOUND" : crest);
            Set(_resources, "ROSARIES  " + pd.geo + "     SHELLS  " + pd.ShellShards);
            Set(_area, area);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DualScreen] HUD read failed: " + e.Message);
        }
    }

    void Clear()
    {
        if (_last.Length == 0) return;
        _last = "";
        for (int i = 0; i < _health.Count; i++) _health[i].gameObject.SetActive(false);
        if (_silkFill != null) DsWidgets.Place(_silkFill.rectTransform, SilkTrackX, 97f, 0f, 16f);
        Set(_silkValue, "");
        Set(_crest, "");
        Set(_resources, "");
        Set(_area, "");
    }

    static string AreaName()
    {
        try
        {
            var gm = GameManager.instance;
            if (gm == null) return "";
            string name = gm.GetFormattedMapZoneString(gm.GetCurrentMapZoneEnum());
            return string.IsNullOrEmpty(name) ? Friendly(gm.GetCurrentMapZone()) : name;
        }
        catch { return ""; }
    }

    static string Friendly(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace('_', ' ').Trim().ToUpperInvariant();
    }

    static void Set(TmpText text, string value)
    {
        if (text != null) text.text = value ?? "";
    }
}
#endif
