// DsFrame — the seam between the game-neutral lower-screen frame (DsShell and
// its widgets) and the game that hosts it. Each game installs one host before
// it builds a shell; the frame asks the host for the few things only the game
// can answer (is a save being played, the game's cursor art, the resident HUD)
// and never names a game type itself.

#if UNITY_ANDROID && !UNITY_EDITOR
using DualSouls.DualScreen;
using UnityEngine;

/// <summary>The resident HUD drawn in the shell's header band.</summary>
public interface IDsHeaderHud
{
    void SetVisible(bool visible);
    void Stop();
    /// <summary>Free header space for the page title, in header-local top-left units.</summary>
    Rect TitleSpace { get; }
}

/// <summary>The game's selection-cursor art; Ok is false until it is found.</summary>
public sealed class DsCursorArt
{
    public Sprite Corner, Glow;
    /// <summary>The tint the game restores when the selection asks for none of its own.</summary>
    public Color GlowColor = new Color(1f, 0.94f, 0.72f, 0.30f);
    public bool Ok { get { return Corner != null; } }
}

public interface IDsFrameHost
{
    /// <summary>A save is loaded and the player is in a gameplay scene.</summary>
    bool InGame { get; }
    /// <summary>The canvas' logical size; zero while the presentation is not built.</summary>
    Vector2 LayoutSize { get; }
    /// <summary>The private display-1 layer the shell's canvas draws on.</summary>
    int ContentLayer { get; }
    DsCursorArt SelectionCursor();
    /// <summary>Build the resident HUD inside the header; null for none.</summary>
    IDsHeaderHud CreateHud(RectTransform header, float width, float height);
}

public static class DsFrame
{
    static readonly DsCursorArt NoCursor = new DsCursorArt();

    public static IDsFrameHost Host { get; set; }

    /// <summary>
    /// The Mods menu's General → Black background row. On, the lower screen
    /// is the opaque ground; off, the host may show the game world behind it.
    /// </summary>
    public static bool BlackBackground { get; set; } = true;

    public static bool InGame { get { return Host != null && Host.InGame; } }

    public static int ContentLayer { get { return Host != null ? Host.ContentLayer : 0; } }

    public static Vector2 LayoutSize
    {
        get
        {
            Vector2 size = Host != null ? Host.LayoutSize : Vector2.zero;
            if (size.x > 0f && size.y > 0f) return size;
            return new Vector2(DirectDisplayPresentation.PanelW > 0 ? DirectDisplayPresentation.PanelW : 1240,
                               DirectDisplayPresentation.PanelH > 0 ? DirectDisplayPresentation.PanelH : 1080);
        }
    }

    public static float InputHeight
    {
        get { return DirectDisplayPresentation.PanelH > 0 ? DirectDisplayPresentation.PanelH : LayoutSize.y; }
    }

    public static Vector2 ToLayout(Vector2 panelPoint) { return DirectDisplayPresentation.ToLayout(panelPoint); }

    public static DsCursorArt SelectionCursor()
    {
        if (Host == null) return NoCursor;
        try { return Host.SelectionCursor() ?? NoCursor; }
        catch (System.Exception e)
        {
            Debug.LogWarning("[DsFrame] cursor art unavailable: " + e.Message);
            return NoCursor;
        }
    }
}
#endif
