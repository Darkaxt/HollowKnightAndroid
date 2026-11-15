// The pointer event the shared gesture recogniser consumes. Silksong fills it
// from its activity's JNI batch (DsTouch.cs); Hollow Knight from Unity touches.

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

public static partial class DsTouch
{
    public struct Point
    {
        public int FingerId;
        public TouchPhase Phase;
        public Vector2 Position;
        public double Time;
    }
}
#endif
