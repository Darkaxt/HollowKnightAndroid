// Gestures from ordered Android pointer events. A complete down/up between
// Unity frames is still a tap; cancellation, resizing and pausing are not.

#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public enum DsGestureType { Down, Drag, Up, Tap, Fling, Pinch }

public struct DsGesture
{
    public DsGestureType Type;
    /// <summary>Layout units, y up; use DsPresentation.ToLayout for top-left coordinates.</summary>
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
    const float TapSlop = 34f;
    const float FlingMinSpeed = 900f;

    readonly List<DsTouch.Point> _events = new List<DsTouch.Point>();
    readonly List<DsTouch.Point> _active = new List<DsTouch.Point>();
    readonly List<DsGesture> _out = new List<DsGesture>();
    long _generation = -1;
    bool _down, _moved, _pinching;
    int _finger = -1;
    Vector2 _start, _last, _velocity;
    double _lastMoveTime;
    float _pinchDist;

    public IList<DsGesture> Gestures => _out;

    public void Poll()
    {
        DsTouch.CollectSecondScreen(_events);
        Poll(_events, DsTouch.Generation);
    }

    public void Poll(IList<DsTouch.Point> events, long generation)
    {
        _out.Clear();
        if (_generation != generation)
        {
            CancelState();
            _generation = generation;
        }
        for (int i = 0; i < events.Count; i++) Process(events[i]);
    }

    public void Cancel()
    {
        _out.Clear();
        CancelState();
    }

    void CancelState()
    {
        if (_down) Emit(DsGestureType.Up, _last, Vector2.zero);
        _active.Clear();
        _down = _moved = _pinching = false;
        _finger = -1;
        _velocity = Vector2.zero;
    }

    void Process(DsTouch.Point point)
    {
        if (point.Phase == TouchPhase.Canceled) { CancelState(); return; }

        int index = -1;
        for (int i = 0; i < _active.Count; i++)
            if (_active[i].FingerId == point.FingerId) { index = i; break; }

        if (point.Phase == TouchPhase.Began)
        {
            if (index >= 0) return;
            if (_active.Count == 0)
            {
                _down = true;
                _finger = point.FingerId;
                _start = _last = point.Position;
                _lastMoveTime = point.Time;
                _moved = false;
                _velocity = Vector2.zero;
                Emit(DsGestureType.Down, point.Position, Vector2.zero);
            }
            _active.Add(point);
            if (_active.Count == 2)
            {
                if (_down) Emit(DsGestureType.Up, _last, Vector2.zero);
                _down = false;
                _finger = -1;
                _pinching = true;
                _pinchDist = Vector2.Distance(_active[0].Position, _active[1].Position);
            }
            return;
        }

        // A held finger after a pause, resize or pinch cannot start a new tap.
        if (index < 0) return;
        _active[index] = point;

        if (point.Phase == TouchPhase.Ended)
        {
            if (_down && point.FingerId == _finger)
            {
                Move(point);
                Emit(DsGestureType.Up, point.Position, Vector2.zero);
                if (!_moved) Emit(DsGestureType.Tap, point.Position, Vector2.zero);
                else if (point.Time - _lastMoveTime <= 0.12 && _velocity.magnitude >= FlingMinSpeed)
                    Emit(DsGestureType.Fling, point.Position, _velocity);
                _down = false;
                _finger = -1;
            }
            _active.RemoveAt(index);
            _pinching = _active.Count >= 2;
            if (_pinching)
                _pinchDist = Vector2.Distance(_active[0].Position, _active[1].Position);
            return;
        }

        if (_pinching)
        {
            Vector2 a = _active[0].Position, b = _active[1].Position;
            float dist = Vector2.Distance(a, b);
            if (_pinchDist > 2f && dist > 2f)
            {
                float scale = dist / _pinchDist;
                if (Mathf.Abs(scale - 1f) > 0.002f)
                    _out.Add(new DsGesture {
                        Type = DsGestureType.Pinch, Position = (a + b) * 0.5f, Scale = scale,
                    });
            }
            _pinchDist = dist;
        }
        else if (_down && point.FingerId == _finger) Move(point);
    }

    void Move(DsTouch.Point point)
    {
        Vector2 delta = point.Position - _last;
        if (delta.sqrMagnitude <= 0f) return;
        float dt = Mathf.Max((float)(point.Time - _lastMoveTime), 0.001f);
        _velocity = Vector2.Lerp(_velocity, delta / dt, 0.4f);
        _last = point.Position;
        _lastMoveTime = point.Time;
        if ((point.Position - _start).sqrMagnitude > TapSlop * TapSlop) _moved = true;
        Emit(DsGestureType.Drag, point.Position, delta);
    }

    void Emit(DsGestureType type, Vector2 pos, Vector2 delta)
    {
        _out.Add(new DsGesture { Type = type, Position = pos, Delta = delta, Scale = 1f });
    }
}
#endif
