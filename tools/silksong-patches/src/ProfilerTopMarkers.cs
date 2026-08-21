// ProfilerTopMarkers — auto-discover the heaviest per-frame profiler markers and
// log them as plain numbers, so we get "what uses the most CPU each frame" in a
// form that can be analysed offline from logcat — no Profiler GUI needed.
//
// In a DEVELOPMENT Player (make game-dev) every ProfilerMarker the engine emits
// is enumerable via ProfilerRecorderHandle.GetAvailable(). We start a recorder
// for each TIME marker, accumulate its per-frame value, and every few seconds log
// the top markers by average ms/frame. Marker time is TOTAL (marker + children),
// so the ranking surfaces the heaviest subsystems (Camera.Render, Physics2D,
// ScriptRunBehaviourUpdate, animation, etc.) even without self-time.
//
// Does nothing useful in a release Player (no markers registered) — harmless.

#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

public class ProfilerTopMarkers : MonoBehaviour
{
    const float REPORT_INTERVAL = 3f;   // seconds between top-marker reports
    const int TOP_N = 28;               // how many heaviest markers to log

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Bootstrap()
    {
        var go = new GameObject("ProfilerTopMarkers");
        DontDestroyOnLoad(go);
        go.AddComponent<ProfilerTopMarkers>();
    }

    class Marker
    {
        public string name;
        public ProfilerRecorder rec;
        public double accNs;   // accumulated total time this window
    }

    readonly List<Marker> _markers = new List<Marker>();
    float _nextReport;
    int _frames;

    void Start()
    {
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        int timeMarkers = 0;
        foreach (var h in handles)
        {
            ProfilerRecorderDescription d;
            try { d = ProfilerRecorderHandle.GetDescription(h); }
            catch { continue; }
            // Only time-valued markers (skip counters: counts, bytes, %).
            if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
            if (string.IsNullOrEmpty(d.Name)) continue;
            var rec = new ProfilerRecorder(h, 1);
            rec.Start();
            _markers.Add(new Marker { name = d.Name, rec = rec });
            timeMarkers++;
        }
        Debug.Log("[ProfTop] tracking " + timeMarkers + " time markers (dev build)");
        _nextReport = Time.unscaledTime + REPORT_INTERVAL;
    }

    void Update()
    {
        _frames++;
        for (int i = 0; i < _markers.Count; i++)
        {
            var m = _markers[i];
            if (m.rec.Valid) m.accNs += m.rec.LastValue;
        }

        if (Time.unscaledTime < _nextReport || _frames == 0) return;

        _markers.Sort((a, b) => b.accNs.CompareTo(a.accNs));
        var sb = new StringBuilder(1024);
        sb.Append("[ProfTop] top ").Append(TOP_N).Append(" markers over ")
          .Append(_frames).Append(" frames — avg ms/frame (TOTAL incl children):\n");
        int n = 0;
        for (int i = 0; i < _markers.Count && n < TOP_N; i++)
        {
            var m = _markers[i];
            if (m.accNs <= 0) continue;
            double msPerFrame = (m.accNs / 1e6) / _frames;
            sb.Append("  ").Append(msPerFrame.ToString("F2")).Append("  ").Append(m.name).Append('\n');
            n++;
        }
        Debug.Log(sb.ToString());

        for (int i = 0; i < _markers.Count; i++) _markers[i].accNs = 0;
        _frames = 0;
        _nextReport = Time.unscaledTime + REPORT_INTERVAL;
    }

    void OnDestroy()
    {
        for (int i = 0; i < _markers.Count; i++) _markers[i].rec.Dispose();
        _markers.Clear();
    }
}
#endif
