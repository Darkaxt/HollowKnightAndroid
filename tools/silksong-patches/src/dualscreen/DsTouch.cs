// The Android surface consumes secondary touches before Unity can turn them
// into global touches, mouse clicks or anyKeyDown. JNI carries only pointer
// events; Unity still renders directly to its own secondary surface.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public static class DsTouch
{
    public struct Point
    {
        public int FingerId;
        public TouchPhase Phase;
        public Vector2 Position;
        public double Time;
    }

    const int Header = 4, Stride = 5;
    static AndroidJavaObject _activity;
    static double[] _batch = Array.Empty<double>();
    static int _frame = -1;

    public static bool Ready
    {
        get { Refresh(); return _batch.Length >= Header && _batch[3] != 0; }
    }

    public static long Generation
    {
        get { Refresh(); return _batch.Length >= Header ? (long)_batch[0] : -1; }
    }

    public static Vector2 SurfaceSize
    {
        get
        {
            Refresh();
            return _batch.Length >= Header
                ? new Vector2((float)_batch[1], (float)_batch[2]) : Vector2.zero;
        }
    }

    public static bool Begin()
    {
        if (_activity != null) return true;
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                _activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            if (_activity == null) throw new InvalidOperationException("No player activity");
            _activity.Call("setSecondaryDisplayEnabled", true);
            _frame = -1;
            _batch = Array.Empty<double>();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[DsTouch] cannot capture secondary input: " + e);
            Stop();
            return false;
        }
    }

    public static void Stop()
    {
        if (_activity != null)
        {
            try { _activity.Call("setSecondaryDisplayEnabled", false); }
            catch (Exception e) { Debug.LogError("[DsTouch] cannot hide secondary window: " + e); }
            _activity.Dispose();
            _activity = null;
        }
        _frame = -1;
        _batch = Array.Empty<double>();
    }

    static void Refresh()
    {
        if (_activity == null || _frame == Time.frameCount) return;
        _frame = Time.frameCount;
        try
        {
            _batch = _activity.Call<double[]>("pollSecondaryDisplayTouches");
            if (_batch == null || _batch.Length < Header || (_batch.Length - Header) % Stride != 0)
                throw new InvalidOperationException("Invalid secondary touch batch");
        }
        catch (Exception e)
        {
            Debug.LogError("[DsTouch] secondary input stopped: " + e);
            Stop();
        }
    }

    public static void CollectSecondScreen(List<Point> into)
    {
        into.Clear();
        if (!Ready) return;
        for (int i = Header; i < _batch.Length; i += Stride)
        {
            into.Add(new Point
            {
                FingerId = (int)_batch[i],
                Phase = (TouchPhase)(int)_batch[i + 1],
                Position = DsPresentation.FromSurface(
                    new Vector2((float)_batch[i + 2], (float)_batch[i + 3])),
                Time = _batch[i + 4] / 1000.0,
            });
        }
    }

    // Surface-local fractions and the actual canvas rect share the same
    // viewport. No display-height guess, screen-point projection or main-screen
    // resolution enters this mapping, even with system bars or display scaling.
    public static Vector2 MapToCanvas(Vector2 normalized, Vector2 canvasSize, float layoutHeight)
    {
        return new Vector2(normalized.x * canvasSize.x, layoutHeight - normalized.y * canvasSize.y);
    }
}
#endif
