// Silksong's answers to the shared lower-screen frame.

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

public sealed class SilksongFrameHost : IDsFrameHost
{
    public static readonly SilksongFrameHost Instance = new SilksongFrameHost();

    public bool InGame { get { return DsGameData.InGame; } }
    public Vector2 LayoutSize { get { return DsPresentation.LayoutSize; } }
    public int ContentLayer { get { return DsPresentation.LAYER; } }
    public DsCursorArt SelectionCursor() { return DsGameArt.SelectionCursor(); }

    /// <summary>
    /// The game's own title logo, as Hollow Knight shows vheart_title: found once on the
    /// title screen and kept by reference, so it stays loaded after the menu unloads.
    /// Null until then; the title card retries while it is shown.
    /// </summary>
    public Sprite TitleLogo()
    {
        if (_logo != null) return _logo;
        if (Time.unscaledTime < _nextLogoSearch) return null;
        _nextLogoSearch = Time.unscaledTime + 1f;
        _logo = FindTitleLogo();
        if (_logo != null) Debug.Log("[DsFrame] Silksong title logo: " + _logo.name + " " + _logo.rect.width + "x" + _logo.rect.height);
        return _logo;
    }

    Sprite _logo;
    float _nextLogoSearch;

    /// <summary>The largest "logo"/"title" sprite drawn in the title-screen scene.</summary>
    static Sprite FindTitleLogo()
    {
        Sprite best = null;
        float bestArea = 0f;
        System.Action<GameObject, Sprite> consider = (go, s) =>
        {
            if (go == null || s == null || !go.scene.IsValid() || go.scene.name != "Menu_Title") return;
            if (!Named(go.name) && !Named(s.name)) return;
            float area = s.rect.width * s.rect.height;
            if (area > bestArea) { bestArea = area; best = s; }
        };
        foreach (var r in Resources.FindObjectsOfTypeAll<SpriteRenderer>()) if (r != null) consider(r.gameObject, r.sprite);
        foreach (var i in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Image>()) if (i != null) consider(i.gameObject, i.sprite);
        return best;
    }

    static bool Named(string name)
    {
        return name != null && (name.IndexOf("logo", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("title", System.StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public IDsHeaderHud CreateHud(RectTransform header, float width, float height)
    {
        var hud = header.gameObject.AddComponent<DsHudView>();
        hud.Build(header, width, height);
        return hud;
    }
}
#endif
