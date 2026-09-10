// Silksong compatibility name and proven display-1 constants. The complete
// presentation technology lives in the game-neutral shared patch source; the
// Silksong shell supplies official v1.1.0's SurfaceView geometry and input
// capture so rendering and hit testing share one measured viewport.

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
    }

    // Surface-local fractions map into the exact authored panel viewport. The
    // returned point keeps Unity's bottom-left convention; ToLayout performs
    // the one top-left conversion used by page hit testing.
    public static Vector2 FromSurface(Vector2 normalized)
    {
        return new Vector2(normalized.x * PanelW, PanelH - normalized.y * PanelH);
    }
}
#endif
