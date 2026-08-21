// DsTestCard — what the second screen shows until there is something real to
// show. Its whole job is to make M1's acceptance criteria checkable by looking
// at the panel.
//
//   * A one-pixel border and four corner blocks. If any edge or corner is
//     missing, the surface is not the whole panel -- which is the failure the
//     old implementation shipped with, letterboxing a 16:9 mirror into a
//     1240x1080 screen and wasting about 40% of it.
//   * A bar that sweeps left to right. Two captures a second apart put it in
//     different places, which distinguishes a live surface from one stale
//     presented frame. This matters more than it sounds: this device runs a
//     second-screen launcher app of its own, so "the panel is showing
//     something" is not evidence.
//   * A crosshair that follows a finger ON THIS PANEL ONLY. Touching the main
//     screen must leave it alone, exactly as touching this panel must leave the
//     game alone.
//   * A ruler of blocks every 100 px along the top, so the coordinate mapping
//     can be read off a screenshot rather than assumed.
//
// No text: the game's fonts are TMProOld assets that arrive with DsTheme in
// M2, and Unity's built-in font is not dependable across versions. Everything
// here is a coloured rect, which needs nothing.

#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class DsTestCard
{
    const int CORNER = 64;
    const int BORDER = 2;
    const int RULER = 100;

    readonly RectTransform _root;
    readonly int _w, _h;

    Image _bg;
    RectTransform _bar;
    RectTransform _cross;
    float _t;

    readonly List<Touch> _mine = new List<Touch>();

    public DsTestCard(RectTransform root, int width, int height)
    {
        _root = root; _w = width; _h = height;
        Build();
    }

    void Build()
    {
        _bg = Rect("bg", new Color(0.06f, 0.05f, 0.10f));
        Fill(_bg.rectTransform);

        // Edges. A missing one means the surface is inset from the panel.
        Edge("edge-top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -BORDER), new Vector2(0f, 0f), BORDER, true);
        Edge("edge-bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, BORDER), BORDER, true);
        Edge("edge-left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(BORDER, 0f), BORDER, false);
        Edge("edge-right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-BORDER, 0f), new Vector2(0f, 0f), BORDER, false);

        Corner("tl", new Vector2(0f, 1f), new Vector2(CORNER * 0.5f, -CORNER * 0.5f), Color.white);
        Corner("tr", new Vector2(1f, 1f), new Vector2(-CORNER * 0.5f, -CORNER * 0.5f), Color.white);
        Corner("bl", new Vector2(0f, 0f), new Vector2(CORNER * 0.5f, CORNER * 0.5f), Color.white);
        Corner("br", new Vector2(1f, 0f), new Vector2(-CORNER * 0.5f, CORNER * 0.5f), Color.white);

        // Ruler along the top: a taller tick every 500 px so a screenshot can be
        // measured without counting.
        for (int x = RULER; x < _w; x += RULER)
        {
            bool major = (x % 500) == 0;
            var tick = Rect("tick" + x, major ? new Color(1f, 0.85f, 0.2f) : new Color(0.5f, 0.5f, 0.6f));
            var rt = tick.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(major ? 6f : 3f, major ? 48f : 24f);
            rt.anchoredPosition = new Vector2(x, 0f);
        }

        var bar = Rect("bar", new Color(0.2f, 0.9f, 0.4f));
        _bar = bar.rectTransform;
        _bar.anchorMin = _bar.anchorMax = new Vector2(0f, 0.5f);
        _bar.sizeDelta = new Vector2(140f, 260f);

        var cross = Rect("touch", new Color(1f, 0.3f, 0.15f));
        _cross = cross.rectTransform;
        _cross.anchorMin = _cross.anchorMax = Vector2.zero;
        _cross.sizeDelta = new Vector2(96f, 96f);
        _cross.gameObject.SetActive(false);
    }

    public void Tick()
    {
        _t += Time.unscaledDeltaTime;

        if (_bg != null) _bg.color = Color.HSVToRGB((_t / 8f) % 1f, 0.45f, 0.16f);
        if (_bar != null) _bar.anchoredPosition = new Vector2((_t * 300f) % _w, 0f);

        // Only this panel's touches. DsTouch is what keeps the two screens
        // apart in both directions.
        DsTouch.CollectSecondScreen(_mine);
        if (_cross != null)
        {
            bool show = _mine.Count > 0;
            if (show) _cross.anchoredPosition = _mine[0].position;
            if (_cross.gameObject.activeSelf != show) _cross.gameObject.SetActive(show);
        }
    }

    // ── building blocks ────────────────────────────────────────────────────

    Image Rect(string name, Color c)
    {
        var go = new GameObject(name) { layer = DsPresentation.LAYER };
        go.transform.SetParent(_root, false);
        var img = go.AddComponent<Image>();
        img.color = c;              // a null sprite draws a plain rect, which is all this needs
        img.raycastTarget = false;
        return img;
    }

    static void Fill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    void Edge(string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, float thick, bool horizontal)
    {
        var rt = Rect(name, new Color(1f, 1f, 1f, 0.85f)).rectTransform;
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = oMin; rt.offsetMax = oMax;
        if (horizontal) rt.sizeDelta = new Vector2(rt.sizeDelta.x, thick);
        else rt.sizeDelta = new Vector2(thick, rt.sizeDelta.y);
    }

    void Corner(string name, Vector2 anchor, Vector2 pos, Color c)
    {
        var rt = Rect(name, c).rectTransform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(CORNER, CORNER);
        rt.anchoredPosition = pos;
    }
}
#endif
