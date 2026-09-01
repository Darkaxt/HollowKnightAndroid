// Silksong compatibility name and proven display-1 constants. The complete
// presentation technology lives in the game-neutral shared patch source.

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
            DsConfig.Int)
    {
    }
}
#endif
