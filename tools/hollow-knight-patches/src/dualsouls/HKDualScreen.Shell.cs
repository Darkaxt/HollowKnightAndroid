using System;
using System.Collections.Generic;
using DualSouls.DualScreen;
using HollowKnightPatches.Lower;
using UnityEngine;

// [B] LOWER SCREEN — the shared DsShell frame (Silksong v2 layout) hosted on the
// direct-display content canvas. The five Hollow Knight pages live in
// tools/hollow-knight-patches/src/lower; this partial builds the shell, feeds it
// touch, and decides when it is idle, paused or hidden. HK's own live HUD keeps
// drawing on hudCam2, fitted into the shell's header band (NativeHud.cs).
public partial class HKDualScreen
{
    const float ShellIdleGrace = 0.75f;   // scene loads briefly have no hero
    const float ShellInGameRecheck = 0.25f;

    DsShell shell;
    DsInput shellInput;
    RectTransform shellRoot;
    HkFrameHost shellHost;
    bool shellFontReady;
    float shellNextFontRetry;
    float shellIdleSince = -1f;
    bool shellEverInGame;
    bool shellInGame;
    float shellNextInGameCheck;
    bool shellShown;
    readonly HkShellTouchTracker shellTouch = new HkShellTouchTracker();
    readonly List<HkShellTouchTracker.Contact> shellContacts = new List<HkShellTouchTracker.Contact>(8);
    readonly List<HkShellTouchTracker.Sample> shellSamples = new List<HkShellTouchTracker.Sample>(8);
    readonly List<DsTouch.Point> shellPoints = new List<DsTouch.Point>(8);

    // Header-band destination for the live HUD, in BOTTOM_W/BOTTOM_H pixels (top-left).
    Rect shellHudDestination;
    bool shellHudDestinationValid;
    bool shellHudWanted;
    Sprite titleLogoSprite;

    /// <summary>The per-frame lower-screen policy, after the HUD and popup routing for this frame.</summary>
    void ShellTick(GameManager gm, bool paused, bool companionOn, bool hudFaded, bool popupAny)
    {
        if (!EnsureShell()) { if (attrCam != null) attrCam.cullingMask = 0; return; }

        if (shell.LayoutChanged)
        {
            CancelShellTouch();
            RebuildShell();
        }
        if (!shellFontReady && Time.unscaledTime >= shellNextFontRetry)
        {
            shellNextFontRetry = Time.unscaledTime + 2f;
            DsTheme.ForgetFont();
            if (DsTheme.HasFont) { shellFontReady = true; RebuildShell(); }
        }

        bool inGame = ShellInGame(gm);
        if (inGame) { shellIdleSince = -1f; shellEverInGame = true; }
        else if (shellIdleSince < 0f) shellIdleSince = Time.unscaledTime;

        // Pause shows the title card (logo) and hides the HUD; HK's open inventory is not a pause.
        if (paused && !shell.Paused) CancelShellTouch();
        shell.SetPaused(paused);
        shell.SetIdle(HkShellPolicy.Idle(inGame, shellEverInGame, shellIdleSince, Time.unscaledTime, ShellIdleGrace));

        // A routed dialogue/tutorial, a gameplay HUD fade (cutscene, lore tablet) or the
        // companion switched off leaves the lower panel to those owners on black.
        bool show = HkShellPolicy.Visible(companionOn, popupAny, hudFaded, paused);
        if (show != shellShown) { shellShown = show; if (!show) CancelShellTouch(); }
        shell.SetVisible(show);
        if (attrCam != null) attrCam.cullingMask = show ? 1 << ATTR_LAYER : 0;

        if (show && !shell.Paused) FeedShellTouch();
        shell.Tick(Time.unscaledDeltaTime);
    }

    bool EnsureShell()
    {
        if (shell != null && shellRoot != null) return true;
        var root = transport != null ? transport.ContentRoot : null;
        if (root == null) return false;
        if (shellHost == null) shellHost = new HkFrameHost(this);
        DsFrame.Host = shellHost;
        if (shellInput == null) shellInput = new DsInput();
        BuildShell(root, null);
        return shell != null;
    }

    void BuildShell(RectTransform contentRoot, string preferredId)
    {
        var go = new GameObject("hk-shell", typeof(RectTransform)) { layer = ATTR_LAYER };
        shellRoot = (RectTransform)go.transform;
        shellRoot.SetParent(contentRoot, false);
        shellRoot.anchorMin = Vector2.zero; shellRoot.anchorMax = Vector2.one;
        shellRoot.offsetMin = Vector2.zero; shellRoot.offsetMax = Vector2.zero;
        shellRoot.pivot = new Vector2(0f, 1f);
        shell = new DsShell(shellRoot);
        shell.Register(new HkInventoryScreen(), HkInventoryScreen.TabArt);
        shell.Register(new HkCharmsScreen(), HkCharmsScreen.TabArt);
        shell.Register(new HkGuideScreen(), HkGuideScreen.TabArt);
        shell.Register(new HkJournalScreen(), HkJournalScreen.TabArt);
        shell.Register(new HkMapScreen(), HkMapScreen.TabArt);
        shell.Finish(preferredId ?? "map");
        shellShown = false;
        shell.SetVisible(false);
    }

    void RebuildShell()
    {
        var contentRoot = shellRoot != null ? shellRoot.parent as RectTransform : null;
        string keep = shell != null ? shell.ActiveId : null;
        DisposeShell();
        if (contentRoot != null) BuildShell(contentRoot, keep);
    }

    /// <summary>Retires the shell and its pages; the next Tick rebuilds it on demand.</summary>
    void DisposeShell()
    {
        CancelShellTouch();
        if (shell != null)
        {
            try { shell.Dispose(); }
            catch (Exception e) { WarnOnce("shell dispose", e); }
        }
        shell = null;
        if (shellRoot != null) Destroy(shellRoot.gameObject);
        shellRoot = null;
        shellShown = false;
        shellHudWanted = false;
        if (attrCam != null) attrCam.cullingMask = 0;
    }

    /// <summary>Hides the lower screen without discarding its page state.</summary>
    void HideShell()
    {
        if (shell != null)
        {
            if (shellShown) CancelShellTouch();
            shellShown = false;
            shell.SetVisible(false);
        }
        shellHudWanted = false;
        if (attrCam != null) attrCam.cullingMask = 0;
    }

    bool ShellInGame(GameManager gm)
    {
        if (Time.unscaledTime < shellNextInGameCheck) return shellInGame;
        shellNextInGameCheck = Time.unscaledTime + ShellInGameRecheck;
        bool inGame = false;
        try
        {
            inGame = gm != null && HeroController.SilentInstance != null &&
                gm.gameState != GlobalEnums.GameState.MAIN_MENU && !gm.IsNonGameplayScene();
        }
        catch { }
        shellInGame = inGame;
        return inGame;
    }

    // Unity contacts arrive as an id set per frame; the recogniser wants phases.
    void FeedShellTouch()
    {
        if (transport == null || shellInput == null) return;
        if (transport.TouchCanceledThisFrame) { CancelShellTouch(); return; }
        var contacts = transport.PixelContacts;
        bool fresh = transport.TouchFrame == Time.frameCount;
        Vector2 size = DsFrame.LayoutSize;
        float w = transport.PanelWidth, h = transport.PanelHeight;
        shellContacts.Clear();
        if (fresh)
            for (int i = 0; i < contacts.Count; i++)
            {
                var c = contacts[i].ToTopLeftNormalized(w, h);
                shellContacts.Add(new HkShellTouchTracker.Contact(c.Id, c.X, c.Y));
            }
        shellSamples.Clear();
        shellTouch.Update(shellContacts, fresh, size.x, size.y, DsFrame.InputHeight, shellSamples);
        if (shellSamples.Count == 0) return;
        double now = Time.realtimeSinceStartupAsDouble;
        shellPoints.Clear();
        for (int i = 0; i < shellSamples.Count; i++)
        {
            var sample = shellSamples[i];
            shellPoints.Add(new DsTouch.Point
            {
                FingerId = sample.Id,
                Phase = sample.Phase == HkShellTouchTracker.Phase.Began ? TouchPhase.Began
                      : sample.Phase == HkShellTouchTracker.Phase.Moved ? TouchPhase.Moved : TouchPhase.Ended,
                Position = new Vector2(sample.X, sample.Y),
                Time = now,
            });
        }
        shellInput.Poll(shellPoints, shellTouch.Generation);
        var gestures = shellInput.Gestures;
        for (int i = 0; i < gestures.Count; i++) shell.OnGesture(gestures[i]);
    }

    void CancelShellTouch()
    {
        if (shellInput != null)
        {
            shellInput.Cancel();
            if (shell != null)
            {
                var gestures = shellInput.Gestures;
                for (int i = 0; i < gestures.Count; i++) shell.OnGesture(gestures[i]);
            }
        }
        shellTouch.Cancel();
    }

    // The header's free band below the HUD hosts the page title.
    void SetShellHudBand(float headerW, float headerH)
    {
        Vector2 layout = DsFrame.LayoutSize;
        float x, y, w, hgt;
        shellHudDestinationValid = HkShellPolicy.HudBand(layout.x, layout.y, headerW, headerH,
            BOTTOM_W, BOTTOM_H, DsTheme.Pad, HudBandWidth, HudBandHeight, out x, out y, out w, out hgt);
        if (shellHudDestinationValid) shellHudDestination = new Rect(x, y, w, hgt);
    }

    // The right of the header belongs to the page's action column.
    const float HudBandWidth = 0.62f, HudBandHeight = 0.62f;

    Sprite ShellTitleLogo() { return titleLogoSprite; }

    DsCursorArt shellCursor;
    float shellCursorRetry;

    DsCursorArt ShellSelectionCursor()
    {
        if (shellCursor != null && shellCursor.Ok) return shellCursor;
        if (Time.unscaledTime < shellCursorRetry) return shellCursor;
        shellCursorRetry = Time.unscaledTime + 2f;
        var art = new DsCursorArt();
        try
        {
            var g = GameManager.instance;
            var inv = g != null ? g.inventoryFSM : null;
            var root = inv != null ? inv.transform.root : null;
            var cursor = root != null ? FindDeep(root, "Cursor") : null;
            var tl = cursor != null ? FindDeep(cursor, "TL") : null;
            var glow = cursor != null ? FindDeep(cursor, "Glow") : null;
            var corner = tl != null ? tl.GetComponentInChildren<SpriteRenderer>(true) : null;
            var glowSr = glow != null ? glow.GetComponentInChildren<SpriteRenderer>(true) : null;
            if (corner != null) art.Corner = corner.sprite;
            if (glowSr != null && glowSr.sprite != null) { art.Glow = glowSr.sprite; art.GlowColor = glowSr.color; }
        }
        catch (Exception e) { WarnOnce("shell cursor", e); }
        shellCursor = art;
        return art;
    }

    sealed class HkFrameHost : IDsFrameHost
    {
        readonly HKDualScreen _owner;
        public HkFrameHost(HKDualScreen owner) { _owner = owner; }

        public bool InGame { get { return _owner.shellInGame; } }

        public Vector2 LayoutSize
        {
            get
            {
                var root = _owner.transport != null ? _owner.transport.ContentRoot : null;
                return root != null ? root.rect.size : Vector2.zero;
            }
        }

        public int ContentLayer { get { return ATTR_LAYER; } }
        public DsCursorArt SelectionCursor() { return _owner.ShellSelectionCursor(); }
        public Sprite TitleLogo() { return _owner.ShellTitleLogo(); }

        public IDsHeaderHud CreateHud(RectTransform header, float width, float height)
        {
            _owner.SetShellHudBand(width, height);
            return new HkHeaderHud(_owner, width, height);
        }
    }

    // HK's live HUD draws through hudCam2 into the header band; this only gates it.
    sealed class HkHeaderHud : IDsHeaderHud
    {
        readonly HKDualScreen _owner;
        readonly float _width, _height;
        bool _stopped;

        public HkHeaderHud(HKDualScreen owner, float width, float height)
        { _owner = owner; _width = width; _height = height; }

        public void SetVisible(bool visible) { if (!_stopped) _owner.shellHudWanted = visible; }

        public void Stop() { _stopped = true; _owner.shellHudWanted = false; }

        public Rect TitleSpace
        {
            get
            {
                float hudBottom = DsTheme.Pad + _height * HudBandHeight - DsTheme.Pad;
                return new Rect(0f, hudBottom, _width, Mathf.Max(0f, _height - hudBottom));
            }
        }
    }
}
