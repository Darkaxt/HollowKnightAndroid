#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using InControlBinding = InControl.NewUnityInputDevice;
using InControlInputManager = InControl.InputManager;
using InputDeviceChange = UnityEngine.InputSystem.InputDeviceChange;
using UnityGamepad = UnityEngine.InputSystem.Gamepad;
using UnityInputDevice = UnityEngine.InputSystem.InputDevice;
using UnityInputSystem = UnityEngine.InputSystem.InputSystem;

namespace DualSouls.Controllers
{
    /// <summary>
    /// Restores the public Input System -> InControl binding invariant after
    /// Android re-enumerates a physical controller. The game's event-only
    /// NewUnityInputDeviceManager has no periodic reconciliation path, so a
    /// missed or incomplete remove/add sequence otherwise remains broken until
    /// the process restarts.
    /// </summary>
    public sealed class ControllerRecovery : MonoBehaviour
    {
        const float ReconcileInterval = 0.25f;
        const string Tag = "[ControllerRecovery] ";

        static ControllerRecovery _instance;

        readonly List<UnityGamepad> _gamepads = new List<UnityGamepad>();
        readonly List<ControllerBinding<UnityGamepad, InControlBinding>> _bindings =
            new List<ControllerBinding<UnityGamepad, InControlBinding>>();
        readonly List<InControlBinding> _ownedBindings = new List<InControlBinding>();
        float _nextReconcile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("ControllerRecovery");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ControllerRecovery>();
        }

        void OnEnable()
        {
            // Remove first so an enable replay cannot duplicate the callback.
            UnityInputSystem.onDeviceChange -= OnDeviceChange;
            UnityInputSystem.onDeviceChange += OnDeviceChange;
            _nextReconcile = 0f;
        }

        void OnDisable()
        {
            UnityInputSystem.onDeviceChange -= OnDeviceChange;
        }

        void OnDeviceChange(UnityInputDevice device, InputDeviceChange change)
        {
            if (!(device is UnityGamepad gamepad)) return;

            // A recovery-owned wrapper must not survive its Unity device. It is
            // outside the game's manager table, so only this component can
            // release it on the same removal notification.
            if (change == InputDeviceChange.Removed ||
                change == InputDeviceChange.Disconnected)
            {
                for (int i = _ownedBindings.Count - 1; i >= 0; i--)
                {
                    InControlBinding binding = _ownedBindings[i];
                    if (!ReferenceEquals(binding.UnityGamepad, gamepad)) continue;
                    InControlInputManager.DetachDevice(binding);
                    _ownedBindings.RemoveAt(i);
                }
            }

            // Added/reconnected/configuration changes reconcile after every
            // Input System listener, including the game's manager, has run.
            _nextReconcile = 0f;
        }

        void Update()
        {
            if (Time.unscaledTime < _nextReconcile) return;
            _nextReconcile = Time.unscaledTime + ReconcileInterval;

            try
            {
                Reconcile();
            }
            catch (Exception e)
            {
                // Keep the next interval retryable. Controller recovery must
                // never interrupt gameplay or direct-display lifecycle work.
                Debug.LogWarning(Tag + "reconciliation failed; will retry: " + e.Message);
            }
        }

        void Reconcile()
        {
            if (!InControlInputManager.IsSetup)
            {
                _ownedBindings.Clear();
                return;
            }

            for (int i = _ownedBindings.Count - 1; i >= 0; i--)
                if (!_ownedBindings[i].IsAttached) _ownedBindings.RemoveAt(i);

            _gamepads.Clear();
            var unityDevices = UnityInputSystem.devices;
            for (int i = 0; i < unityDevices.Count; i++)
                if (unityDevices[i] is UnityGamepad gamepad) _gamepads.Add(gamepad);

            _bindings.Clear();
            var inControlDevices = InControlInputManager.Devices;
            if (inControlDevices != null)
            {
                for (int i = 0; i < inControlDevices.Count; i++)
                {
                    if (!(inControlDevices[i] is InControlBinding binding) ||
                        !binding.IsAttached)
                        continue;
                    _bindings.Add(
                        new ControllerBinding<UnityGamepad, InControlBinding>(
                            binding.UnityGamepad,
                            binding,
                            IsOwned(binding)));
                }
            }

            ControllerBindingPlan<UnityGamepad, InControlBinding> plan =
                ControllerBindingPlanner.Create(_gamepads, _bindings);
            for (int i = 0; i < plan.DetachBindings.Count; i++)
                DetachBinding(plan.DetachBindings[i]);
            for (int i = 0; i < plan.AttachGamepads.Count; i++)
                AttachBinding(plan.AttachGamepads[i]);
        }

        bool IsOwned(InControlBinding binding)
        {
            for (int i = 0; i < _ownedBindings.Count; i++)
                if (ReferenceEquals(_ownedBindings[i], binding)) return true;
            return false;
        }

        void DetachBinding(InControlBinding binding)
        {
            bool owned = IsOwned(binding);
            InControlInputManager.DetachDevice(binding);
            if (owned) _ownedBindings.Remove(binding);
            Debug.LogWarning(
                Tag + "detached " + (owned ? "superseded recovery" : "stale") +
                " binding for '" + binding.Name + "'");
        }

        void AttachBinding(UnityGamepad gamepad)
        {
            var binding = new InControlBinding(gamepad);
            InControlInputManager.AttachDevice(binding);
            if (!binding.IsAttached)
                throw new InvalidOperationException(
                    "InControl rejected recovery binding for '" + gamepad.displayName + "'");
            _ownedBindings.Add(binding);
            Debug.LogWarning(
                Tag + "restored gameplay binding for '" + gamepad.displayName +
                "' (device " + gamepad.deviceId + ")");
        }
    }
}
#endif
