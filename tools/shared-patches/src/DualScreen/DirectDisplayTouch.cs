// Shared display-attributed touch routing and primary-display input fencing.
// This file contains only the proven Unity Input System/legacy bridge.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DualSouls.DualScreen
{
    public static class DirectDisplayTouch
    {
        static int _targetDisplay = 1;

        /// <summary>The Unity display index whose touches this router owns.</summary>
        public static int TargetDisplay => _targetDisplay;

        // Filtering is off until the transport is fully active, preserving the
        // game's original input behavior on a single-display device.
        public static bool Enabled { get; set; }

        // Rebuilt at most once per frame because uGUI may read during a
        // different update phase from the companion content.
        static int _frame = -1;
        static readonly HashSet<int> _ids = new HashSet<int>();
        static readonly List<Vector2> _positions = new List<Vector2>();
        static bool _inputSystemOk = true;

        public static void ConfigureTargetDisplay(int displayIndex)
        {
            if (displayIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(displayIndex));
            if (_targetDisplay == displayIndex) return;

            _targetDisplay = displayIndex;
            _frame = -1;
            _ids.Clear();
            _positions.Clear();
            _inputSystemOk = true;
        }

        static void Refresh()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            _ids.Clear();
            _positions.Clear();
            if (!_inputSystemOk) return;

            try
            {
                var touchscreen = UnityEngine.InputSystem.Touchscreen.current;
                if (touchscreen == null) return;
                var touches = touchscreen.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    var touch = touches[i];
                    var phase = touch.phase.ReadValue();
                    if (phase == UnityEngine.InputSystem.TouchPhase.None ||
                        phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                        phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                        continue;
                    if (touch.displayIndex.ReadValue() != _targetDisplay) continue;
                    _ids.Add(touch.touchId.ReadValue());
                    _positions.Add(touch.position.ReadValue());
                }
            }
            catch (Exception e)
            {
                // Fail open: if display attribution is unavailable, every touch
                // remains the game's rather than risking an unplayable primary UI.
                _inputSystemOk = false;
                Debug.LogWarning("[DirectDisplayTouch] Input System unavailable, no filtering: " +
                                 e.Message);
            }
        }

        public static bool IsTargetDisplay(Touch touch)
        {
            if (!Enabled) return false;
            Refresh();
            if (_ids.Count == 0) return false;

            // The proven streams normally differ by one in pointer id. Position
            // matching retains the original fallback if that relationship moves.
            if (_ids.Contains(touch.fingerId + 1) || _ids.Contains(touch.fingerId))
                return true;

            for (int i = 0; i < _positions.Count; i++)
                if ((_positions[i] - touch.position).sqrMagnitude <= 16f * 16f)
                    return true;

            return false;
        }

        public static void CollectTargetDisplay(List<Touch> into)
        {
            into.Clear();
            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                if (IsTargetDisplay(touch)) into.Add(touch);
            }
        }

        /// <summary>
        /// The input source uGUI reads, excluding target-display touches and the
        /// mouse Unity synthesizes from a target-display primary touch.
        /// </summary>
        class FilteredInput : BaseInput
        {
            readonly List<Touch> _keep = new List<Touch>();
            int _built = -1;
            bool _mouseIsTargetDisplay;
            Vector2 _lastRealMouse;

            void Rebuild()
            {
                if (_built == Time.frameCount) return;
                _built = Time.frameCount;

                _keep.Clear();
                int target = 0;
                int total = Input.touchCount;
                for (int i = 0; i < total; i++)
                {
                    var touch = Input.GetTouch(i);
                    if (IsTargetDisplay(touch)) target++;
                    else _keep.Add(touch);
                }

                _mouseIsTargetDisplay = total > 0 && target == total;
                if (!_mouseIsTargetDisplay) _lastRealMouse = Input.mousePosition;
            }

            public override int touchCount
            {
                get { Rebuild(); return _keep.Count; }
            }

            public override Touch GetTouch(int index)
            {
                Rebuild();
                return index >= 0 && index < _keep.Count ? _keep[index] : default;
            }

            // Freeze rather than zero the pointer so filtering does not itself
            // create a hover/selection change in the game's event system.
            public override Vector2 mousePosition
            {
                get
                {
                    Rebuild();
                    return _mouseIsTargetDisplay ? _lastRealMouse :
                        (Vector2)Input.mousePosition;
                }
            }

            public override bool GetMouseButton(int button)
            {
                Rebuild();
                return !_mouseIsTargetDisplay && Input.GetMouseButton(button);
            }

            public override bool GetMouseButtonDown(int button)
            {
                Rebuild();
                return !_mouseIsTargetDisplay && Input.GetMouseButtonDown(button);
            }

            public override bool GetMouseButtonUp(int button)
            {
                Rebuild();
                return !_mouseIsTargetDisplay && Input.GetMouseButtonUp(button);
            }
        }

        static FilteredInput _filter;
        static readonly List<StandaloneInputModule> _fenced =
            new List<StandaloneInputModule>();

        /// <summary>
        /// Points the active game uGUI modules at filtered input. The cached
        /// EventSystem path is cheap enough for periodic fence maintenance.
        /// </summary>
        public static void InstallFence(GameObject host)
        {
            if (_filter == null)
            {
                _filter = host.AddComponent<FilteredInput>();
                Debug.Log("[DirectDisplayTouch] filtered input created");
            }

            var eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                var current = eventSystem.currentInputModule as StandaloneInputModule;
                if (current != null && current.inputOverride == _filter) return;
            }

            Rescan();
        }

        static void Rescan()
        {
#pragma warning disable 0618 // Retain the proven API across both exact Unity targets.
            var modules = UnityEngine.Object.FindObjectsOfType<StandaloneInputModule>();
#pragma warning restore 0618
            for (int i = 0; i < modules.Length; i++)
            {
                var module = modules[i];
                if (module == null || module.inputOverride == _filter) continue;
                module.inputOverride = _filter;
                if (!_fenced.Contains(module)) _fenced.Add(module);
                Debug.Log("[DirectDisplayTouch] fenced " + module.GetType().Name +
                          " on '" + module.gameObject.name + "'");
            }
        }

        public static void RemoveFence()
        {
            for (int i = 0; i < _fenced.Count; i++)
                if (_fenced[i] != null && _fenced[i].inputOverride == _filter)
                    _fenced[i].inputOverride = null;
            _fenced.Clear();
            Enabled = false;
        }
    }
}
#endif
