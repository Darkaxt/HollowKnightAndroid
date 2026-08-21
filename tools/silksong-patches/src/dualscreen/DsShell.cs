// DsShell — the frame around the screens: a tab strip, one visible screen at a
// time, and the routing that gets a gesture to the right place.
//
// The shell owns nothing about what any screen shows. It knows the list, which
// one is visible, and how to switch. That is the whole reason the screens are
// separable, so it is worth keeping the file boring.
//
// Every call into a screen is wrapped. A screen that throws is disabled and the
// shell keeps running: the second screen must never be able to take the game
// down, and a broken Journal must not cost the player their map.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TmpText = TMProOld.TextMeshProUGUI;
using TmpAlign = TMProOld.TextAlignmentOptions;

public class DsShell
{
    class Entry
    {
        public IDsScreen Screen;
        public RectTransform Host;
        public RectTransform Tab;
        public Image TabFill;
        public TmpText TabLabel;
        public bool Built;
        public bool Broken;
    }

    readonly List<Entry> _entries = new List<Entry>();
    readonly RectTransform _root;
    readonly int _w, _h;

    RectTransform _tabBar;
    RectTransform _body;
    readonly DsTitleCard _title = new DsTitleCard();
    // Starts idle. The shell is built before anything is known about whether a
    // save is loaded, and defaulting to a screen meant the panel opened on an
    // empty Inventory and only corrected itself once the idle grace expired.
    // The title card is the honest answer to "no idea yet", so it is the one
    // that costs nothing to be wrong about.
    bool _idle = true;
    int _active = -1;

    public DsShell(RectTransform root, int width, int height)
    {
        _root = root; _w = width; _h = height;
        Build();
    }

    void Build()
    {
        var bg = DsWidgets.Box(_root, "shell-bg", DsTheme.Ground);
        DsWidgets.Stretch(bg.rectTransform);

        // Body first, tab strip second. uGUI draws in hierarchy order, so the
        // strip is created last to sit above the content -- belt and braces
        // alongside the grid's own clipping, since the tabs must never be
        // covered by a scrolled list.
        _body = DsWidgets.Rect(_root, "body");
        DsWidgets.Place(_body, 0f, DsTheme.TabBarHeight, _w, _h - DsTheme.TabBarHeight);

        _tabBar = DsWidgets.Rect(_root, "tabs");
        DsWidgets.Place(_tabBar, 0f, 0f, _w, DsTheme.TabBarHeight);

        var strip = DsWidgets.Box(_tabBar, "tab-bg", DsTheme.Ground);
        DsWidgets.Stretch(strip.rectTransform);

        // The boundary between the tab strip and the body is a section boundary
        // like any other, so it is the same white rule the screens use.
        var rule = DsWidgets.Box(_tabBar, "rule", DsTheme.Rule).rectTransform;
        rule.anchorMin = new Vector2(0f, 0f);
        rule.anchorMax = new Vector2(1f, 0f);
        rule.sizeDelta = new Vector2(0f, DsTheme.RuleThickness);
        rule.anchoredPosition = Vector2.zero;

        // Built last so it covers everything, because that is exactly its job:
        // outside a save there is no screen worth showing and no tab worth
        // offering, so the whole frame goes away rather than sitting there
        // greyed out.
        _title.Build(_root, _w, _h);

        // Match the initial _idle state, rather than waiting for the first
        // SetIdle to disagree with it.
        _tabBar.gameObject.SetActive(false);
        _body.gameObject.SetActive(false);
        _title.SetVisible(true);
    }

    /// <summary>
    /// Outside a save, show the game's title instead of the tabs.
    ///
    /// The shell owns this rather than each screen, because "there is no save
    /// loaded" is a fact about the whole panel, not about the Journal. It also
    /// means a screen can no longer disagree with its neighbours about how to
    /// say so, which is what five separate grey "Main menu" labels amounted to.
    /// </summary>
    public void SetIdle(bool idle)
    {
        if (idle == _idle) return;
        _idle = idle;

        _tabBar.gameObject.SetActive(!idle);
        _body.gameObject.SetActive(!idle);
        _title.SetVisible(idle);

        // Hide the active screen properly on the way out, so it stops ticking
        // and stops driving anything of the game's -- the map screen in
        // particular holds the game's map open while it is visible.
        if (_active >= 0 && _active < _entries.Count)
        {
            var e = _entries[_active];
            if (!e.Broken) Guard(e, () => { if (idle) e.Screen.OnHide(); else e.Screen.OnShow(); });
        }
    }

    public void Register(IDsScreen screen)
    {
        var host = DsWidgets.Rect(_body, "screen-" + screen.Id);
        DsWidgets.Stretch(host);
        host.gameObject.SetActive(false);

        _entries.Add(new Entry { Screen = screen, Host = host });
    }

    /// <summary>Lay the tabs out once every screen has registered.</summary>
    public void Finish(string preferredId)
    {
        LayoutTabs();

        int start = _entries.FindIndex(e => e.Screen.Id == preferredId);
        if (start < 0) start = 0;
        Show(start);
    }

    void LayoutTabs()
    {
        int n = _entries.Count;
        if (n == 0) return;
        float w = _w / (float)n;

        for (int i = 0; i < n; i++)
        {
            var e = _entries[i];
            e.Tab = DsWidgets.Rect(_tabBar, "tab-" + e.Screen.Id);
            DsWidgets.Place(e.Tab, i * w, 0f, w, DsTheme.TabBarHeight);

            e.TabFill = DsWidgets.Box(e.Tab, "fill", Color.clear);
            DsWidgets.Stretch(e.TabFill.rectTransform);

            string title = "?";
            try { title = e.Screen.Title; } catch { }
            e.TabLabel = DsWidgets.Label(e.Tab, "label", title, DsTheme.BodySize,
                                         DsTheme.InkDim, TmpAlign.Center, display: true);
            if (e.TabLabel != null) DsWidgets.Stretch(e.TabLabel.rectTransform);
        }
    }

    public void Show(int index)
    {
        if (index < 0 || index >= _entries.Count) return;
        if (index == _active) return;

        if (_active >= 0 && _active < _entries.Count)
        {
            var prev = _entries[_active];
            prev.Host.gameObject.SetActive(false);
            Guard(prev, () => prev.Screen.OnHide());
        }

        _active = index;
        var e = _entries[index];

        // Built on first show, not at startup: an unused screen costs nothing,
        // and one that throws while building disables only itself.
        if (!e.Built && !e.Broken)
        {
            e.Built = true;
            Guard(e, () => e.Screen.Build(e.Host));
        }

        e.Host.gameObject.SetActive(true);
        Guard(e, () => e.Screen.OnShow());
        Paint();
    }

    public void Next(int direction)
    {
        if (_entries.Count == 0) return;
        int i = _active;
        for (int step = 0; step < _entries.Count; step++)
        {
            i = (i + direction + _entries.Count) % _entries.Count;
            var e = _entries[i];
            if (e.Broken) continue;
            bool ok = true;
            try { ok = e.Screen.Available; } catch { ok = false; }
            if (ok) { Show(i); return; }
        }
    }

    /// <summary>The id of the visible screen, for persisting across runs.</summary>
    public string ActiveId => (_active >= 0 && _active < _entries.Count) ? _entries[_active].Screen.Id : null;

    void Paint()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            bool on = i == _active;
            if (e.TabFill != null) e.TabFill.color = on ? DsTheme.Panel : Color.clear;
            if (e.TabLabel != null) e.TabLabel.color = e.Broken ? DsTheme.InkFaint
                                                    : on ? DsTheme.Ink : DsTheme.InkDim;
        }
    }

    public void Tick(float dt)
    {
        if (_idle) { _title.Tick(); return; }
        if (_active < 0 || _active >= _entries.Count) return;
        var e = _entries[_active];
        if (e.Broken) return;
        Guard(e, () => e.Screen.Tick(dt));
    }

    public void OnGesture(DsGesture g)
    {
        // Nothing to press on the title card.
        if (_idle) return;

        // A tap in the tab strip switches screens. The strip is at the TOP of
        // the panel, and panel coordinates have y up, so that is high y.
        if (g.Type == DsGestureType.Tap && g.Position.y >= _h - DsTheme.TabBarHeight)
        {
            int n = _entries.Count;
            if (n > 0)
            {
                int idx = Mathf.Clamp((int)(g.Position.x / (_w / (float)n)), 0, n - 1);
                Show(idx);
            }
            return;
        }

        if (_active < 0 || _active >= _entries.Count) return;
        var e = _entries[_active];
        if (e.Broken) return;
        Guard(e, () => e.Screen.OnGesture(g));
    }

    // One place where a screen's exception is turned into that screen being
    // switched off, so the failure is contained and visible rather than fatal.
    void Guard(Entry e, Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            e.Broken = true;
            if (e.Host != null) e.Host.gameObject.SetActive(false);
            Debug.LogError("[DualScreen] screen '" + e.Screen.Id + "' disabled after error: " + ex);
            Paint();
        }
    }
}
#endif
