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

    public IDsHeaderHud CreateHud(RectTransform header, float width, float height)
    {
        var hud = header.gameObject.AddComponent<DsHudView>();
        hud.Build(header, width, height);
        return hud;
    }
}
#endif
