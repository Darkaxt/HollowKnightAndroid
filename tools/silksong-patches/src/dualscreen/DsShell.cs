// DsShell — the shared Dual Souls HUD/frame on Silksong's direct display.
//
// The game-neutral CompanionShellLayout owns the regions and hit targets. This
// adapter supplies Silksong fonts, state and pages, while every call into a page
// or the Mods overlay remains fail-contained.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using DualSouls.DualScreen;
using UnityEngine;
using TmpText = TMProOld.TextMeshProUGUI;
using TmpAlign = TMProOld.TextAlignmentOptions;

public sealed class DsShell
{
    sealed class Entry
    {
        public IDsScreen Screen;
        public RectTransform Host;
        public RectTransform Tab;
        public RectTransform TopFleur;
        public RectTransform BottomFleur;
        public TmpText TabLabel;
        public bool Built;
        public bool Broken;
    }

    readonly List<Entry> _entries = new List<Entry>();
    readonly RectTransform _root;
    readonly int _w;
    readonly int _h;
    readonly DsTitleCard _title = new DsTitleCard();

    CompanionShellLayout _layout;
    RectTransform _body;
    RectTransform _chrome;
    RectTransform _modsGear;
    TmpText _status;
    TmpText _battery;
    DsHudStrip _hud;
    DsModsScreen _mods;
    bool _modsBroken;
    bool _idle = true;
    int _active = -1;
    float _fps;
    float _nextStatus;

    public DsShell(RectTransform root, int width, int height)
    {
        _root = root;
        _w = width;
        _h = height;
        BuildBase();
    }

    void BuildBase()
    {
        var bg = DsWidgets.Box(_root, "shell-bg", DsTheme.Ground);
        DsWidgets.Stretch(bg.rectTransform);

        _body = DsWidgets.Rect(_root, "context-box");
        DsWidgets.Stretch(_body);

        // Chrome is a sibling above every page, so map sprites and scrolled
        // lists can never cover the HUD, separators or tab selection.
        _chrome = DsWidgets.Rect(_root, "dual-souls-chrome");
        DsWidgets.Stretch(_chrome);

        _title.Build(_root, _w, _h);

        _body.gameObject.SetActive(false);
        _chrome.gameObject.SetActive(false);
        _title.SetVisible(true);
    }

    public void Register(IDsScreen screen)
    {
        var host = DsWidgets.Rect(_body, "screen-" + screen.Id);
        DsWidgets.Stretch(host);
        host.gameObject.SetActive(false);
        _entries.Add(new Entry { Screen = screen, Host = host });
    }

    /// <summary>Freeze the shared geometry after every supported page exists.</summary>
    public void Finish(string preferredId)
    {
        var ids = new string[_entries.Count];
        for (int i = 0; i < _entries.Count; i++) ids[i] = _entries[i].Screen.Id;
        _layout = CompanionShellLayout.Create(_w, _h, ids);
        DsTheme.SetContentGeometry(_layout.Content.Top, _layout.Content.Height);
        DsWidgets.Place(_body, _layout.Content.Left, _layout.Content.Top,
                        _layout.Content.Width, _layout.Content.Height);

        BuildChrome();
        GuardMods(() => _mods = new DsModsScreen(_root, _w, _layout.Content.Height,
                                                  _layout.Content.Top));

        int start = _entries.FindIndex(e => e.Screen.Id == preferredId);
        if (start < 0) start = 0;
        Show(start);
    }

    void BuildChrome()
    {
        _hud = new DsHudStrip(_chrome, _layout.Hud);

        // Full-width symmetric ornaments frame the context box. Adapters may
        // replace these with resident sprites later without changing geometry.
        var top = DsWidgets.Fleur(_chrome, "content-fleur-top", DsTheme.Ink);
        DsWidgets.Place(top, 36f, _layout.TopOrnament.Top,
                        _w - 72f, _layout.TopOrnament.Height);
        var bottom = DsWidgets.Fleur(_chrome, "content-fleur-bottom", DsTheme.Ink);
        DsWidgets.Place(bottom, 36f, _layout.BottomOrnament.Top,
                        _w - 72f, _layout.BottomOrnament.Height);

        DsWidgets.VRule(_chrome, "frame-left", 16f, _layout.Content.Top + 8f,
                        _layout.Content.Height - 16f);
        DsWidgets.VRule(_chrome, "frame-right", _w - 16f,
                        _layout.Content.Top + 8f, _layout.Content.Height - 16f);

        _modsGear = DsWidgets.Gear(_chrome, "mods-gear", DsTheme.InkDim);
        Place(_modsGear, _layout.ModsGear.Bounds);

        _status = DsWidgets.Label(_chrome, "fps-status", "", DsTheme.SmallSize,
                                  DsTheme.InkDim, TmpAlign.Center, display: true);
        if (_status != null) Place(_status.rectTransform, _layout.Status.Bounds);
        _battery = DsWidgets.Label(_chrome, "battery-status", "", DsTheme.SmallSize,
                                   DsTheme.InkDim, TmpAlign.Center, display: true);
        if (_battery != null) Place(_battery.rectTransform, _layout.Battery.Bounds);

        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            var tab = _layout.Tabs[i];
            entry.Tab = DsWidgets.Rect(_chrome, "tab-" + entry.Screen.Id);
            Place(entry.Tab, tab.Bounds);
            string title = "?";
            try { title = entry.Screen.Title; } catch { }
            entry.TabLabel = DsWidgets.Label(entry.Tab, "label", title, DsTheme.SmallSize,
                                             DsTheme.InkDim, TmpAlign.Center, display: true);
            if (entry.TabLabel != null) DsWidgets.Stretch(entry.TabLabel.rectTransform, 8f);

            var selection = _layout.SelectionFor(i);
            entry.TopFleur = DsWidgets.Fleur(_chrome, "tab-fleur-top-" + entry.Screen.Id, DsTheme.Ink);
            Place(entry.TopFleur, selection.Top);
            entry.BottomFleur = DsWidgets.Fleur(_chrome, "tab-fleur-bottom-" + entry.Screen.Id, DsTheme.Ink);
            Place(entry.BottomFleur, selection.Bottom);
        }
        Paint();
    }

    static void Place(RectTransform target, CompanionRect rect)
    {
        DsWidgets.Place(target, rect.Left, rect.Top, rect.Width, rect.Height);
    }

    public void SetIdle(bool idle)
    {
        if (idle == _idle) return;
        _idle = idle;
        _chrome.gameObject.SetActive(!idle);
        _body.gameObject.SetActive(!idle);
        _title.SetVisible(idle);
        if (idle && _mods != null) GuardMods(() => _mods.SetVisible(false));

        if (_active >= 0 && _active < _entries.Count)
        {
            var entry = _entries[_active];
            if (!entry.Broken)
                Guard(entry, () => { if (idle) entry.Screen.OnHide(); else entry.Screen.OnShow(); });
        }
    }

    public void Show(int index)
    {
        if (index < 0 || index >= _entries.Count || index == _active) return;

        if (_active >= 0 && _active < _entries.Count)
        {
            var previous = _entries[_active];
            previous.Host.gameObject.SetActive(false);
            Guard(previous, () => previous.Screen.OnHide());
        }

        _active = index;
        var entry = _entries[index];
        if (!entry.Built && !entry.Broken)
        {
            entry.Built = true;
            Guard(entry, () => entry.Screen.Build(entry.Host));
        }
        entry.Host.gameObject.SetActive(true);
        Guard(entry, () => entry.Screen.OnShow());
        Paint();
    }

    public void Next(int direction)
    {
        if (_entries.Count == 0) return;
        int index = _active;
        for (int step = 0; step < _entries.Count; step++)
        {
            index = (index + direction + _entries.Count) % _entries.Count;
            var entry = _entries[index];
            if (entry.Broken) continue;
            bool available = true;
            try { available = entry.Screen.Available; } catch { available = false; }
            if (available) { Show(index); return; }
        }
    }

    public string ActiveId => _active >= 0 && _active < _entries.Count
        ? _entries[_active].Screen.Id : null;
    public bool ModsOpen => !_modsBroken && _mods != null && _mods.Visible;

    void Paint()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            bool selected = i == _active;
            if (entry.TabLabel != null)
                entry.TabLabel.color = entry.Broken ? DsTheme.InkFaint
                                     : selected ? DsTheme.Ink : DsTheme.InkDim;
            if (entry.TopFleur != null) entry.TopFleur.gameObject.SetActive(selected);
            if (entry.BottomFleur != null) entry.BottomFleur.gameObject.SetActive(selected);
        }
        DsWidgets.SetGearColor(_modsGear, _modsBroken ? DsTheme.InkFaint
                                           : ModsOpen ? DsTheme.Accent : DsTheme.InkDim);
    }

    public void Tick(float dt)
    {
        if (_mods != null) GuardMods(() => _mods.Tick());
        if (_idle) { _title.Tick(); return; }

        if (_hud != null) _hud.Tick();
        TickStatus(dt);
        if (ModsOpen) return;
        if (_active < 0 || _active >= _entries.Count) return;
        var entry = _entries[_active];
        if (!entry.Broken) Guard(entry, () => entry.Screen.Tick(dt));
    }

    void TickStatus(float dt)
    {
        if (dt > 0f)
        {
            float sample = 1f / dt;
            _fps = _fps <= 0f ? sample : Mathf.Lerp(_fps, sample, 0.08f);
        }
        if (Time.unscaledTime < _nextStatus) return;
        _nextStatus = Time.unscaledTime + 0.5f;
        if (_status != null) _status.text = "FPS  " + Mathf.RoundToInt(_fps);
        if (_battery != null)
        {
            float value = SystemInfo.batteryLevel;
            string charge = SystemInfo.batteryStatus == BatteryStatus.Charging ? " +" : "";
            _battery.text = value < 0f ? "BAT  --" : "BAT  " + Mathf.RoundToInt(value * 100f) + "%" + charge;
        }
    }

    public void OnGesture(DsGesture gesture)
    {
        if (_idle || _layout == null) return;

        if (ModsOpen)
        {
            GuardMods(() => _mods.OnGesture(gesture));
            Paint();
            return;
        }

        if (gesture.Type == DsGestureType.Tap)
        {
            Vector2 point = DsPresentation.ToLayout(gesture.Position);
            CompanionHit hit = _layout.HitTest(point.x, point.y);
            if (hit.Target == CompanionHitTarget.Mods)
            {
                if (_mods != null) GuardMods(() => _mods.SetVisible(true));
                Paint();
                return;
            }
            if (hit.Target == CompanionHitTarget.Tab)
            {
                Show(hit.TabIndex);
                return;
            }
        }

        if (_active < 0 || _active >= _entries.Count) return;
        var entry = _entries[_active];
        if (!entry.Broken) Guard(entry, () => entry.Screen.OnGesture(gesture));
    }

    void Guard(Entry entry, Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            entry.Broken = true;
            if (entry.Host != null) entry.Host.gameObject.SetActive(false);
            Debug.LogError("[DualScreen] screen '" + entry.Screen.Id + "' disabled after error: " + ex);
            Paint();
        }
    }

    void GuardMods(Action action)
    {
        if (_modsBroken) return;
        try { action(); }
        catch (Exception ex)
        {
            _modsBroken = true;
            try { if (_mods != null) _mods.SetVisible(false); } catch { }
            Debug.LogError("[DualScreen] Mods overlay disabled after error: " + ex);
            Paint();
        }
    }
}
#endif
