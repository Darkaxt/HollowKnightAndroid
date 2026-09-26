#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.UI;

public class DsToolTween
{
    struct Landing
    {
        public float Pop, Fade, Length, Glow;
    }

    static readonly Landing ToolLanding = new Landing
    {
        Pop = 1f / 24f, Fade = 1f / 3f, Length = 1f / 3f, Glow = 0.5f,
    };

    static readonly Landing SkillLanding = new Landing
    {
        Pop = 1f / 35f, Fade = 8f / 35f, Length = 9f / 35f, Glow = 0f,
    };

    const float PopScale = 2f / 1.3f;

    static float FlySeconds => Mathf.Clamp(DsConfig.Int("equip_fly_ms", 100), 0, 2000) / 1000f;

    static float GlowSize => Mathf.Clamp(DsConfig.Int("equip_glow_pct", 100), 0, 400) / 100f;

    RectTransform _root, _glowBox;
    Image _icon, _glow;
    static Material _flash;

    Rect _from, _to;
    Vector2 _centre;
    Color _tint;
    Landing _landing;
    float _t, _fly, _flashNow;
    int _startFrame;
    bool _running, _lands, _arrived, _flashing;
    Action _onArrive;

    public void Build(RectTransform host)
    {
        _root = DsWidgets.Rect(host, "tool-tween");
        DsWidgets.Stretch(_root);
        _glowBox = DsWidgets.Rect(_root, "glow");
        _glow = DsWidgets.Icon(_glowBox, "art", null, Color.clear);
        _icon = DsWidgets.Icon(_root, "tool", null, Color.clear);
        _root.gameObject.SetActive(false);
    }

    public void Place(Sprite sprite, Rect from, Rect to, ToolItemType type, Action onArrive)
    {
        _tint = DsTheme.ToolTypeColor(type);
        Begin(sprite, from, to, onArrive,
              type == ToolItemType.Skill ? SkillLanding : ToolLanding, lands: true);
    }

    public void Return(Sprite sprite, Rect from, Rect to)
    {
        Begin(sprite, from, to, null, default(Landing), lands: false);
    }

    public void Cancel()
    {
        if (!_running) return;
        Stop();
        Deliver();
    }

    public void Tick(float dt)
    {
        if (!_running) return;
        if (Time.frameCount != _startFrame) _t += dt;

        if (!_arrived)
        {
            if (_t < _fly) { Fly(_t / _fly); return; }
            _arrived = true;
            Fly(1f);
            Deliver();
            if (!_running) return;
            if (!_lands) { Stop(); return; }
            _t = _fly;
            BeginLanding();
        }

        float u = _t - _fly;
        if (u >= _landing.Length) { Stop(); return; }
        Land(u);
    }

    void Begin(Sprite sprite, Rect from, Rect to, Action onArrive, Landing landing, bool lands)
    {
        Cancel();
        _fly = FlySeconds;
        if (_root == null || sprite == null || (!lands && _fly <= 0f))
        {
            if (onArrive != null) onArrive();
            return;
        }

        _from = from;
        _to = to;
        _onArrive = onArrive;
        _landing = landing;
        _lands = lands;
        _arrived = false;
        _flashing = false;
        _t = 0f;
        _startFrame = Time.frameCount;

        _icon.sprite = sprite;
        _icon.useSpriteMesh = true;
        _icon.preserveAspect = true;
        _icon.material = null;
        DsWidgets.SetActive(_glow, false);
        _root.SetAsLastSibling();
        _root.gameObject.SetActive(true);
        _running = true;
        Fly(_fly > 0f ? 0f : 1f);
    }

    void Deliver()
    {
        var done = _onArrive;
        _onArrive = null;
        if (done != null) done();
    }

    void Stop()
    {
        _running = false;
        if (_icon != null) _icon.material = null;
        if (_root != null) _root.gameObject.SetActive(false);
    }

    void Fly(float k)
    {
        float e = Mathf.SmoothStep(0f, 1f, k);
        PlaceCentred(_icon.rectTransform, new Rect(
            Mathf.Lerp(_from.x, _to.x, e), Mathf.Lerp(_from.y, _to.y, e),
            Mathf.Lerp(_from.width, _to.width, e), Mathf.Lerp(_from.height, _to.height, e)));
        _icon.color = Color.white;
    }

    void BeginLanding()
    {
        _centre = DsWidgets.InkRect(_icon, _to).center;

        var art = DsGameArt.ToolTweenArt();
        if (_flash == null && art != null && art.Flash != null)
            _flash = new Material(art.Flash) { hideFlags = HideFlags.HideAndDontSave };
        _flashing = _flash != null;
        if (_flashing)
        {
            _flashNow = 0f;
            _flash.SetFloat("_FlashAmount", 0f);
            _icon.material = _flash;
        }

        Vector2 extent = Vector2.zero;
        Sprite glow = art != null ? art.Glow : null;
        float perUnit = PixelsPerUnit(_icon.sprite, _to);
        if (glow != null && _landing.Glow > 0f && perUnit > 0f)
            extent = Vector2.Scale(glow.bounds.size, art.GlowScale) * (perUnit * GlowSize);

        if (extent.x > 0f && extent.y > 0f)
        {
            PlaceCentred(_glowBox, new Rect(_centre - extent * 0.5f, extent));
            DsWidgets.FitInk(_glow, glow, extent.x, extent.y);
            _glow.color = Color.clear;
            DsWidgets.SetActive(_glow, true);
        }

        Debug.Log("[DualScreen] tool landing: flash=" + _flashing + " glow=" +
                  Mathf.RoundToInt(extent.x) + "x" + Mathf.RoundToInt(extent.y));
    }

    void Land(float u)
    {
        float pop = Mathf.SmoothStep(0f, 1f, u / _landing.Pop);
        float fade = Mathf.SmoothStep(0f, 1f, (u - _landing.Pop) / (_landing.Fade - _landing.Pop));
        float alpha = 1f - fade;

        PlaceCentred(_icon.rectTransform, ScaleAbout(_to, _centre, Mathf.Lerp(PopScale, 1f, pop)));
        _icon.color = new Color(1f, 1f, 1f, alpha);

        if (_flashing && pop != _flashNow)
        {
            _flashNow = pop;
            _flash.SetFloat("_FlashAmount", pop);
            _icon.SetMaterialDirty();
        }

        if (_glow.gameObject.activeSelf)
        {
            float glow = u < _landing.Pop ? 0f : _landing.Glow * alpha * alpha;
            _glow.color = new Color(_tint.r, _tint.g, _tint.b, _tint.a * glow);
        }
    }

    static float PixelsPerUnit(Sprite sprite, Rect box)
    {
        if (sprite == null) return 0f;
        var full = sprite.rect;
        float units = sprite.bounds.size.x;
        if (full.width <= 0f || full.height <= 0f || units <= 0f) return 0f;
        return Mathf.Min(box.width, box.height * full.width / full.height) / units;
    }

    static Rect ScaleAbout(Rect r, Vector2 centre, float scale)
    {
        Vector2 size = r.size * scale;
        return new Rect(centre + (r.center - centre) * scale - size * 0.5f, size);
    }

    static void PlaceCentred(RectTransform rt, Rect r)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(r.center.x, -r.center.y);
        rt.sizeDelta = r.size;
    }
}
#endif
