// Silksong compatibility name and proven display-1 constants. The complete
// presentation technology lives in the game-neutral shared patch source; the
// Silksong shell supplies the SurfaceView geometry and input capture so
// rendering and hit testing share one measured viewport.

#if UNITY_ANDROID && !UNITY_EDITOR
using DualSouls.DualScreen;
using UnityEngine;

public sealed class DsPresentation : DirectDisplayPresentation
{
    public const int DISPLAY = 1;
    public const int CONTENT_LAYER = 6;
    public const int OVERLAY_LAYER = 3;
    public const int LAYER = CONTENT_LAYER;

    const int FALLBACK_W = 1240;
    const int FALLBACK_H = 1080;

    static DsPresentation _current;

    public DsPresentation(Transform parent)
        : base(
            parent,
            DISPLAY,
            CONTENT_LAYER,
            OVERLAY_LAYER,
            FALLBACK_W,
            FALLBACK_H,
            DsConfig.Int,
            DsTouch.Begin,
            () => DsTouch.Ready,
            () => DsTouch.SurfaceSize,
            DsTouch.Stop)
    {
        _current = this;
    }

    /// <summary>
    /// The canvas' current logical size. The measured panel remains the fallback
    /// while the presentation is being built or after it has been released.
    /// </summary>
    public static Vector2 LayoutSize
    {
        get
        {
            if (_current != null && _current.Root != null)
            {
                Vector2 size = _current.Root.rect.size;
                if (size.x > 0f && size.y > 0f) return size;
            }
            return new Vector2(PanelW > 0 ? PanelW : FALLBACK_W,
                               PanelH > 0 ? PanelH : FALLBACK_H);
        }
    }

    // Surface-local fractions map into the exact authored canvas viewport. The
    // returned point keeps Unity's bottom-left convention; ToLayout performs
    // the one top-left conversion used by page hit testing.
    public static Vector2 FromSurface(Vector2 normalized)
    {
        Vector2 size = LayoutSize;
        float height = PanelH > 0 ? PanelH : size.y;
        return DsTouch.MapToCanvas(normalized, size, height);
    }

    public new void Dispose()
    {
        if (ReferenceEquals(_current, this)) _current = null;
        base.Dispose();
    }
}
#endif
