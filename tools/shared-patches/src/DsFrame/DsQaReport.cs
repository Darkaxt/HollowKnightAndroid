// DsQaReport — the pieces of the command channel's one-shot `report` that both
// games share: game-loop frame pacing, recent Unity errors, a reflection read of
// PlayerData fields, and a generic reflection call for QA actions whose game
// signatures differ between Hollow Knight and Silksong.
//
// Recording costs one float write per frame and an error-only log callback.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

public static class DsQaReport
{
    const int FrameWindow = 600;
    static readonly float[] _dt = new float[FrameWindow];
    static int _count, _head, _lastFrame = -1;

    static readonly object _gate = new object();
    static readonly string[] _recent = new string[6];
    static int _recentHead, _errors, _exceptions;
    static bool _hooked;
    static readonly float _startedAt = Time.realtimeSinceStartup;

    /// <summary>Record this frame; extra calls in the same frame are ignored.</summary>
    public static void Tick()
    {
        if (!_hooked)
        {
            _hooked = true;
            Application.logMessageReceivedThreaded += OnLog;
        }
        int frame = Time.frameCount;
        if (frame == _lastFrame) return;
        _lastFrame = frame;
        _dt[_head] = Time.unscaledDeltaTime;
        _head = (_head + 1) % FrameWindow;
        if (_count < FrameWindow) _count++;
    }

    static void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        string line = message ?? "";
        int cut = line.IndexOf('\n');
        if (cut >= 0) line = line.Substring(0, cut);
        if (line.Length > 160) line = line.Substring(0, 160);
        lock (_gate)
        {
            if (type == LogType.Exception) _exceptions++; else _errors++;
            _recent[_recentHead] = DateTime.Now.ToString("HH:mm:ss ", CultureInfo.InvariantCulture) + type + ": " + line;
            _recentHead = (_recentHead + 1) % _recent.Length;
        }
    }

    /// <summary>Game-loop frame times over the last ~10 s (both displays render from this loop).</summary>
    public static string Frames()
    {
        int n = _count;
        if (n < 2) return "frames: none recorded";
        var sorted = new float[n];
        Array.Copy(_dt, sorted, n);
        Array.Sort(sorted);
        double sum = 0;
        int over25 = 0, over50 = 0;
        for (int i = 0; i < n; i++)
        {
            sum += sorted[i];
            if (sorted[i] > 0.025f) over25++;
            if (sorted[i] > 0.050f) over50++;
        }
        Func<double, double> pct = p => sorted[Math.Min(n - 1, (int)(p * (n - 1)))] * 1000.0;
        return string.Format(CultureInfo.InvariantCulture,
            "frames: n={0} fps={1:F1} p50={2:F1}ms p95={3:F1}ms p99={4:F1}ms max={5:F1}ms >25ms={6} >50ms={7} targetFps={8} vSync={9}",
            n, n / sum, pct(0.50), pct(0.95), pct(0.99), sorted[n - 1] * 1000.0, over25, over50,
            Application.targetFrameRate, QualitySettings.vSyncCount);
    }

    /// <summary>Unity errors/exceptions since the patches loaded, with the latest few lines.</summary>
    public static string Errors()
    {
        var sb = new StringBuilder();
        lock (_gate)
        {
            sb.Append("errors: errors=").Append(_errors).Append(" exceptions=").Append(_exceptions)
              .Append(" since=").Append((Time.realtimeSinceStartup - _startedAt).ToString("F0", CultureInfo.InvariantCulture)).Append('s');
            for (int i = 0; i < _recent.Length; i++)
            {
                string line = _recent[(_recentHead + i) % _recent.Length];
                if (line != null) sb.Append("\n  ").Append(line);
            }
        }
        return sb.ToString();
    }

    /// <summary>"name=value" for each public instance field or property of <paramref name="target"/>; "?" when absent.</summary>
    public static string Fields(object target, params string[] names)
    {
        if (target == null) return "none";
        var sb = new StringBuilder();
        Type type = target.GetType();
        foreach (string name in names)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(name).Append('=');
            object value = null;
            bool found = false;
            try
            {
                FieldInfo f = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (f != null) { value = f.GetValue(target); found = true; }
                else
                {
                    PropertyInfo p = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (p != null && p.GetIndexParameters().Length == 0) { value = p.GetValue(target, null); found = true; }
                }
            }
            catch (Exception e) { sb.Append("!").Append(e.GetType().Name); continue; }
            sb.Append(!found ? "?" : Format(value));
        }
        return sb.ToString();
    }

    /// <summary>QA: set a public bool field of <paramref name="target"/> (e.g. a map flag on a disposable save).</summary>
    public static string SetBool(object target, string name, string value)
    {
        if (target == null) return "error no target";
        bool on;
        if (value == "true" || value == "on" || value == "1") on = true;
        else if (value == "false" || value == "off" || value == "0") on = false;
        else return "error value must be true|false";
        FieldInfo f = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (f == null || f.FieldType != typeof(bool)) return "error no bool field " + name;
        f.SetValue(target, on);
        return "ok " + Fields(target, name);
    }

    static string Format(object value)
    {
        if (value == null) return "null";
        if (value is string s) return s;
        if (value is IEnumerable list)
        {
            var sb = new StringBuilder("[");
            int k = 0;
            foreach (object item in list)
            {
                if (k++ > 0) sb.Append(',');
                if (k > 40) { sb.Append("…"); break; }
                sb.Append(Convert.ToString(item, CultureInfo.InvariantCulture));
            }
            return sb.Append(']').ToString();
        }
        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Calls the first public instance method <paramref name="name"/> on <paramref name="target"/> whose leading
    /// parameter types accept <paramref name="leading"/>; remaining parameters get their defaults, and enum
    /// parameters named in <paramref name="enumNames"/> (by type name) take that member. A returned IEnumerator
    /// is started as a coroutine on <paramref name="runner"/>.
    /// </summary>
    public static string Invoke(MonoBehaviour runner, object target, string name, object[] leading, params string[] enumNames)
    {
        if (target == null) return "error no target for " + name;
        leading = leading ?? new object[0];
        foreach (MethodInfo m in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (m.Name != name) continue;
            ParameterInfo[] ps = m.GetParameters();
            if (ps.Length < leading.Length) continue;
            bool fits = true;
            for (int i = 0; i < leading.Length && fits; i++)
                fits = leading[i] == null ? !ps[i].ParameterType.IsValueType : ps[i].ParameterType.IsInstanceOfType(leading[i]);
            if (!fits) continue;
            var args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                if (i < leading.Length) { args[i] = leading[i]; continue; }
                Type t = ps[i].ParameterType;
                if (ps[i].HasDefaultValue) args[i] = ps[i].DefaultValue;
                else if (t.IsEnum) args[i] = EnumMember(t, enumNames);
                else if (t == typeof(int)) args[i] = 1;
                else if (t == typeof(float)) args[i] = 1f;
                else if (t.IsValueType) args[i] = Activator.CreateInstance(t);
                else args[i] = null;
            }
            object result = m.Invoke(target, args);
            if (result is IEnumerator routine && runner != null) runner.StartCoroutine(routine);
            return "ok " + name + "(" + ps.Length + " args)";
        }
        return "error no matching " + name;
    }

    static object EnumMember(Type t, string[] names)
    {
        if (names != null)
            foreach (string n in names)
            {
                int dot = n.IndexOf('.');
                if (dot > 0 && n.Substring(0, dot) == t.Name)
                    try { return Enum.Parse(t, n.Substring(dot + 1), true); } catch (ArgumentException) { }
            }
        return Activator.CreateInstance(t);
    }
}
#endif
