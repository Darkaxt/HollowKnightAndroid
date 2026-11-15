// DsZoomSlider — where the map's zoom slider sits, and what a drag on it means.
//
// No UI in here, so the tests can run it: DsMapScreen draws the art at the
// rects this hands back, and the size constants are that art's own, from
// docs/Slider.webp and docs/Slider_Bar.webp. The middle of the track is the
// framing the map opens at, as the design draws it, so each half is its own
// logarithmic scale: down to the minimum zoom below the middle, up to the
// maximum above it.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;

public sealed class DsZoomSlider
{
    public const float TrackWidth = 29f;
    public const float CapHeight = 33f;
    public const float ThumbWidth = 40f;
    public const float ThumbHeight = 15f;
    public const float HitWidth = 88f;

    readonly float _lnMin, _lnMax;
    float _grabY, _grabPosition;

    /// <summary>The whole track, arrowheads included. Top-left origin, y down.</summary>
    public Rect Track { get; }
    /// <summary>Where a finger grabs the slider: the track's height, a thumb's width across.</summary>
    public Rect Hit { get; }
    public bool Held { get; private set; }

    public DsZoomSlider(Rect track, float minZoom, float maxZoom)
    {
        if (!(minZoom > 0f && minZoom < 1f))
            throw new ArgumentOutOfRangeException(nameof(minZoom));
        if (!(maxZoom > 1f) || float.IsInfinity(maxZoom))
            throw new ArgumentOutOfRangeException(nameof(maxZoom));

        Track = track;
        Hit = new Rect(track.center.x - HitWidth * 0.5f, track.yMin, HitWidth, track.height);
        _lnMin = Mathf.Log(minZoom);
        _lnMax = Mathf.Log(maxZoom);
    }

    float Top => Track.yMin + CapHeight + ThumbHeight * 0.5f;
    float Travel => Mathf.Max(1f, Track.height - (CapHeight + ThumbHeight * 0.5f) * 2f);

    /// <summary>0 at the bottom of the track, 1 at the top, 0.5 at a zoom of 1.</summary>
    public float Position(float zoom)
    {
        if (!(zoom > 0f) || float.IsInfinity(zoom)) return 0.5f;
        float ln = Mathf.Log(zoom);
        return Mathf.Clamp01(ln < 0f ? 0.5f - 0.5f * ln / _lnMin : 0.5f + 0.5f * ln / _lnMax);
    }

    public float Zoom(float position)
    {
        float t = Mathf.Clamp01(position);
        return Mathf.Exp(t < 0.5f ? (1f - 2f * t) * _lnMin : (2f * t - 1f) * _lnMax);
    }

    /// <summary>The thumb for <paramref name="zoom"/>, on whole pixels so it stays crisp.</summary>
    public Rect Thumb(float zoom)
    {
        float y = Top + (1f - Position(zoom)) * Travel;
        return new Rect(Mathf.Round(Track.center.x - ThumbWidth * 0.5f),
                        Mathf.Round(y - ThumbHeight * 0.5f), ThumbWidth, ThumbHeight);
    }

    /// <summary>
    /// Take hold of the slider if <paramref name="point"/> is on it. The thumb
    /// then moves with the finger from where it already is, wherever on the
    /// track it was grabbed, so a touch alone never changes the zoom.
    /// </summary>
    public bool Grab(Vector2 point, float zoom)
    {
        Held = Hit.Contains(point);
        if (Held)
        {
            _grabY = point.y;
            _grabPosition = Position(zoom);
        }
        return Held;
    }

    /// <summary>The zoom for a held finger now at <paramref name="point"/>. Up zooms in.</summary>
    public float Drag(Vector2 point)
    {
        return Zoom(_grabPosition + (_grabY - point.y) / Travel);
    }

    public void Release() { Held = false; }
}
#endif
