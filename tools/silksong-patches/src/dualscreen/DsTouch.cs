// DsTouch — which screen a touch came from, and keeping the two apart.
//
// The second panel has its own touchscreen. Unity delivers its touches into
// the SAME `Input.touches` stream as the main screen's, with no attribution
// and in a different coordinate space:
//
//     main-screen tap (1500,900) of 1920x1080 -> Input.touches raw (1000,120)
//                                                SCALED into Unity's 1280x720
//     panel tap       (300,300)  of 1240x1080 -> Input.touches raw (300,780)
//                                                UNSCALED panel pixels, y flipped
//
// The ranges overlap, so coordinates alone cannot say which screen a touch came
// from, and `Display.RelativeMouseAt` returns (0,0,0) for everything on this
// platform. Measured on an AYN Thor.
//
// The new Input System CAN say: `Touchscreen.current.touches[i].displayIndex`
// reported 0 for every main-screen touch and 1 for every panel touch, with no
// crossover. That is the whole basis of this file. Legacy `Input` is still what
// the game and our own gesture code read, so this maps one stream onto the
// other.
//
// Why a fence is needed at all: the old second-screen implementation put an
// Android Presentation window on the panel, which swallowed its touches before
// Unity ever saw them. Rendering to the panel with Unity's own multi-display
// support removes that window -- so panel touches started operating the game.
// Pressing the bottom screen must only affect the bottom screen.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public static class DsTouch
{
    /// <summary>The display index Unity gives the second panel.</summary>
    public const int SECOND_DISPLAY = 1;

    // Only filter while the second screen is actually being driven. Off, every
    // touch belongs to the game exactly as it always did -- so this file can
    // never change behaviour on a device with one screen.
    public static bool Enabled { get; set; }

    // Rebuilt at most once per frame, lazily, because uGUI reads input from
    // inside its own update rather than from ours and the order is not fixed.
    static int _frame = -1;
    static readonly HashSet<int> _ids = new HashSet<int>();
    static readonly List<Vector2> _positions = new List<Vector2>();
    static bool _inputSystemOk = true;

    static void Refresh()
    {
        if (_frame == Time.frameCount) return;
        _frame = Time.frameCount;
        _ids.Clear();
        _positions.Clear();
        if (!_inputSystemOk) return;

        try
        {
            var ts = UnityEngine.InputSystem.Touchscreen.current;
            if (ts == null) return;
            var touches = ts.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var t = touches[i];
                var phase = t.phase.ReadValue();
                if (phase == UnityEngine.InputSystem.TouchPhase.None ||
                    phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                    phase == UnityEngine.InputSystem.TouchPhase.Canceled) continue;
                if (t.displayIndex.ReadValue() != SECOND_DISPLAY) continue;
                _ids.Add(t.touchId.ReadValue());
                _positions.Add(t.position.ReadValue());
            }
        }
        catch (Exception e)
        {
            // If the Input System is unavailable we must fail OPEN: treating
            // every touch as the game's keeps the game playable, where the
            // reverse would silently eat all input.
            _inputSystemOk = false;
            Debug.LogWarning("[DsTouch] Input System unavailable, no filtering: " + e.Message);
        }
    }

    /// <summary>Did this legacy touch happen on the second panel?</summary>
    public static bool IsSecondScreen(Touch t)
    {
        if (!Enabled) return false;
        Refresh();
        if (_ids.Count == 0) return false;

        // The Input System's ids look like the same pointers, one-based: a
        // legacy fingerId of 0 arrived as touchId 1. Cheap to check, and if the
        // relationship ever differs the position match below still catches it.
        if (_ids.Contains(t.fingerId + 1) || _ids.Contains(t.fingerId)) return true;

        // Both streams report panel touches in panel pixels, so an exact-ish
        // position match is a reliable second opinion.
        for (int i = 0; i < _positions.Count; i++)
            if ((_positions[i] - t.position).sqrMagnitude <= 16f * 16f) return true;

        return false;
    }

    /// <summary>Touches the second screen owns, for our own UI to consume.</summary>
    public static void CollectSecondScreen(List<Touch> into)
    {
        into.Clear();
        for (int i = 0; i < Input.touchCount; i++)
        {
            var t = Input.GetTouch(i);
            if (IsSecondScreen(t)) into.Add(t);
        }
    }

    // ── fencing the game off ────────────────────────────────────────────────

    /// <summary>
    /// The input source uGUI reads, minus anything that happened on the panel.
    ///
    /// Filtering touches is not enough, and the reason is worth recording. The
    /// game's module is HollowKnightInputModule, whose Process() never looks at
    /// touches at all -- it calls ProcessMouseEvent() and nothing else. On
    /// Android with no mouse attached, Unity SYNTHESISES the mouse from the
    /// primary touch, so a finger on the second panel reaches the game's menus
    /// as a mouse click. So the mouse has to be filtered too, and that is what
    /// actually stops the bottom screen from operating the top one.
    /// </summary>
    class FilteredInput : BaseInput
    {
        readonly List<Touch> _keep = new List<Touch>();
        int _built = -1;
        bool _mouseIsPanel;
        Vector2 _lastRealMouse;

        void Rebuild()
        {
            if (_built == Time.frameCount) return;
            _built = Time.frameCount;

            _keep.Clear();
            int panel = 0, total = Input.touchCount;
            for (int i = 0; i < total; i++)
            {
                var t = Input.GetTouch(i);
                if (IsSecondScreen(t)) panel++;
                else _keep.Add(t);
            }

            // The synthesised mouse follows the primary touch, so it belongs to
            // the panel exactly when every live touch does. With no touches at
            // all this is false, which leaves a real mouse (or a trackpad)
            // working normally.
            _mouseIsPanel = total > 0 && panel == total;
            if (!_mouseIsPanel) _lastRealMouse = Input.mousePosition;
        }

        public override int touchCount
        {
            get { Rebuild(); return _keep.Count; }
        }

        public override Touch GetTouch(int index)
        {
            Rebuild();
            return (index >= 0 && index < _keep.Count) ? _keep[index] : default;
        }

        // Frozen rather than zeroed: moving the pointer to the origin would
        // itself be an event, dropping the game's current hover/selection.
        public override Vector2 mousePosition
        {
            get { Rebuild(); return _mouseIsPanel ? _lastRealMouse : (Vector2)Input.mousePosition; }
        }

        public override bool GetMouseButton(int button)
        {
            Rebuild();
            return !_mouseIsPanel && Input.GetMouseButton(button);
        }

        public override bool GetMouseButtonDown(int button)
        {
            Rebuild();
            return !_mouseIsPanel && Input.GetMouseButtonDown(button);
        }

        public override bool GetMouseButtonUp(int button)
        {
            Rebuild();
            return !_mouseIsPanel && Input.GetMouseButtonUp(button);
        }
    }

    static FilteredInput _filter;
    static readonly List<StandaloneInputModule> _fenced = new List<StandaloneInputModule>();

    /// <summary>
    /// Point the game's uGUI event system at the filtered input, so panel
    /// touches stop operating the game's menus.
    ///
    /// The game's module is HollowKnightInputModule, which derives from
    /// StandaloneInputModule and therefore inherits `inputOverride` -- a public,
    /// supported way to replace where uGUI reads pointers from. Nothing in the
    /// game is modified; one property is set.
    ///
    /// Safe and CHEAP to call every frame: the common case is a single static
    /// property read and a reference comparison. Event systems are rebuilt with
    /// scenes, so this has to keep checking -- but it must not pay for a scene
    /// scan to discover that nothing has changed, which is what an earlier
    /// version did (FindObjectsOfType, every frame, forever).
    /// </summary>
    public static void InstallFence(GameObject host)
    {
        if (_filter == null)
        {
            _filter = host.AddComponent<FilteredInput>();
            Debug.Log("[DsTouch] filtered input created");
        }

        // Fast path: the active event system is the one that matters, and
        // EventSystem.current is a cached static.
        var es = EventSystem.current;
        if (es != null)
        {
            var current = es.currentInputModule as StandaloneInputModule;
            if (current != null && current.inputOverride == _filter) return;
        }

        Rescan();
    }

    // The slow path, run only when the active module is not already fenced --
    // i.e. at startup and after a scene load has built a new event system.
    static void Rescan()
    {
        var modules = UnityEngine.Object.FindObjectsOfType<StandaloneInputModule>();
        for (int i = 0; i < modules.Length; i++)
        {
            var m = modules[i];
            if (m == null || m.inputOverride == _filter) continue;
            m.inputOverride = _filter;
            if (!_fenced.Contains(m)) _fenced.Add(m);
            Debug.Log("[DsTouch] fenced " + m.GetType().Name + " on '" + m.gameObject.name + "'");
        }
    }

    /// <summary>Give the game its own input back.</summary>
    public static void RemoveFence()
    {
        for (int i = 0; i < _fenced.Count; i++)
            if (_fenced[i] != null && _fenced[i].inputOverride == _filter)
                _fenced[i].inputOverride = null;
        _fenced.Clear();
        Enabled = false;
    }
}
#endif
