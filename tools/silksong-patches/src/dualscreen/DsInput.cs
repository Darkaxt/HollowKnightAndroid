// DsInput — turning this panel's raw touches into gestures.
//
// The old implementation had Android's GestureDetector do this, on the far side
// of a JNI bridge, because the panel's touches only existed inside a
// Presentation window. Unity now receives them directly (DsTouch tells us which
// are ours), so the bridge is gone and the recognition moves here.
//
// That is a real loss worth naming: Android's detectors already know this
// device's touch slop, its fling velocity threshold and its long-press timing.
// Reimplementing that badly is how the old code ended up with a hand-tuned
// tolerance constant and gestures that felt slightly wrong. The thresholds
// below are therefore expressed in the panel's own pixels and kept in one
// place, and the density (~369 dpi) is the reason they look large.
//
// Single finger only, for now. Pinch arrives with the map screen, which is the
// first thing that needs it.

#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public enum DsGestureType { Down, Drag, Up, Tap, Fling, Pinch }

public struct DsGesture
{
    public DsGestureType Type;
    /// <summary>Panel pixels, origin bottom-left, matching our canvas.</summary>
    public Vector2 Position;
    /// <summary>Movement since the last event (Drag), or velocity (Fling).</summary>
    public Vector2 Delta;
    /// <summary>Pinch only: the scale change since the last event. 1 is no change.</summary>
    public float Scale;

    public override string ToString() =>
        Type + "@" + Position.x.ToString("F0") + "," + Position.y.ToString("F0");
}

public class DsInput
{
    // ~10 mm at this panel's density: below it a finger is holding still, above
    // it the user meant to drag.
    const float TapSlop = 34f;
    const float FlingMinSpeed = 900f;      // panel px/s

    readonly List<Touch> _touches = new List<Touch>();
    readonly List<DsGesture> _out = new List<DsGesture>();

    bool _down;
    int _finger = -1;
    Vector2 _start, _last;
    float _startTime;
    Vector2 _velocity;
    bool _moved;

    bool _pinching;
    float _pinchDist;

    /// <summary>Gestures produced this frame. Valid until the next Poll.</summary>
    public IList<DsGesture> Gestures => _out;

    public void Poll()
    {
        _out.Clear();
        DsTouch.CollectSecondScreen(_touches);

        // Two fingers is a pinch, and a pinch is not a drag. The map screen is
        // the first thing here that needs one, which is why this arrived with it.
        if (_touches.Count >= 2) { PollPinch(); return; }
        if (_pinching)
        {
            _pinching = false;
            // Do NOT resume the drag with whichever finger is left: it is
            // somewhere else entirely by now, and picking it up mid-gesture
            // makes the map jump. The single-finger path below only starts on a
            // Began phase, so a lingering finger is ignored until it lifts.
        }

        // Track one finger: whichever went down first, until it leaves.
        Touch? active = null;
        for (int i = 0; i < _touches.Count; i++)
        {
            if (_down && _touches[i].fingerId == _finger) { active = _touches[i]; break; }
            if (!_down) { active = _touches[i]; break; }
        }

        if (!_down)
        {
            if (active.HasValue && active.Value.phase == TouchPhase.Began)
            {
                var t = active.Value;
                _down = true; _finger = t.fingerId;
                _start = _last = t.position;
                _startTime = Time.unscaledTime;
                _velocity = Vector2.zero;
                _moved = false;
                Emit(DsGestureType.Down, t.position, Vector2.zero);
            }
            return;
        }

        if (!active.HasValue)
        {
            // The finger left without us seeing an Ended phase (it can happen
            // when a touch is cancelled): treat it as a release at the last
            // known point, so a drag can never get stuck on.
            Release(_last);
            return;
        }

        var cur = active.Value;
        Vector2 delta = cur.position - _last;

        switch (cur.phase)
        {
            case TouchPhase.Moved:
            case TouchPhase.Stationary:
                if (delta.sqrMagnitude > 0f)
                {
                    float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
                    // Smoothed so a single jittery frame cannot produce a fling.
                    _velocity = Vector2.Lerp(_velocity, delta / dt, 0.4f);
                    _last = cur.position;
                    if ((cur.position - _start).sqrMagnitude > TapSlop * TapSlop) _moved = true;
                    Emit(DsGestureType.Drag, cur.position, delta);
                }
                break;

            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                Release(cur.position);
                break;
        }
    }

    /// <summary>
    /// Two fingers: emit the change in their separation as a scale.
    ///
    /// A ratio rather than an absolute distance, so the consumer never has to
    /// know what the gesture started from and a dropped frame cannot accumulate
    /// error. The midpoint rides along as the position, which is what a zoom
    /// wants to zoom about.
    /// </summary>
    void PollPinch()
    {
        Vector2 a = _touches[0].position;
        Vector2 b = _touches[1].position;
        float dist = Vector2.Distance(a, b);
        Vector2 mid = (a + b) * 0.5f;

        if (!_pinching)
        {
            // A drag in progress has to be closed out, or whatever it was
            // driving keeps moving from a finger that now means something else.
            if (_down)
            {
                Emit(DsGestureType.Up, _last, Vector2.zero);
                _down = false; _finger = -1; _moved = false;
                _velocity = Vector2.zero;
            }
            _pinching = true;
            _pinchDist = dist;
            return;
        }

        // Below a couple of pixels the ratio is noise divided by noise.
        if (_pinchDist > 2f && dist > 2f)
        {
            float scale = dist / _pinchDist;
            if (Mathf.Abs(scale - 1f) > 0.002f)
                _out.Add(new DsGesture
                {
                    Type = DsGestureType.Pinch,
                    Position = mid,
                    Delta = Vector2.zero,
                    Scale = scale,
                });
        }
        _pinchDist = dist;
    }

    void Release(Vector2 pos)
    {
        Emit(DsGestureType.Up, pos, Vector2.zero);

        // A tap is a release that never travelled far.
        if (!_moved) Emit(DsGestureType.Tap, pos, Vector2.zero);
        else if (_moved && _velocity.magnitude >= FlingMinSpeed) Emit(DsGestureType.Fling, pos, _velocity);

        _down = false; _finger = -1; _moved = false;
        _velocity = Vector2.zero;
    }

    void Emit(DsGestureType type, Vector2 pos, Vector2 delta)
    {
        _out.Add(new DsGesture { Type = type, Position = pos, Delta = delta, Scale = 1f });
    }
}
#endif
