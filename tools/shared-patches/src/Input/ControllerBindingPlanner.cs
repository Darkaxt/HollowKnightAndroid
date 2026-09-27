using System;
using System.Collections.Generic;

namespace DualSouls.Controllers
{
    public readonly struct ControllerBinding<TGamepad, TBinding>
        where TGamepad : class
    {
        public ControllerBinding(TGamepad gamepad, TBinding binding, bool recoveryOwned)
        {
            Gamepad = gamepad ?? throw new ArgumentNullException(nameof(gamepad));
            Binding = binding;
            RecoveryOwned = recoveryOwned;
        }

        public TGamepad Gamepad { get; }
        public TBinding Binding { get; }
        public bool RecoveryOwned { get; }
    }

    public sealed class ControllerBindingPlan<TGamepad, TBinding>
        where TGamepad : class
    {
        internal ControllerBindingPlan(
            IReadOnlyList<TGamepad> attachGamepads,
            IReadOnlyList<TBinding> detachBindings)
        {
            AttachGamepads = attachGamepads;
            DetachBindings = detachBindings;
        }

        public IReadOnlyList<TGamepad> AttachGamepads { get; }
        public IReadOnlyList<TBinding> DetachBindings { get; }
    }

    public static class ControllerBindingPlanner
    {
        public static ControllerBindingPlan<TGamepad, TBinding> Create<TGamepad, TBinding>(
            IReadOnlyList<TGamepad> currentGamepads,
            IReadOnlyList<ControllerBinding<TGamepad, TBinding>> bindings)
            where TGamepad : class
        {
            if (currentGamepads == null)
                throw new ArgumentNullException(nameof(currentGamepads));
            if (bindings == null)
                throw new ArgumentNullException(nameof(bindings));

            var detach = new List<TBinding>();
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                ControllerBinding<TGamepad, TBinding> binding = bindings[bindingIndex];
                if (!ContainsReference(currentGamepads, binding.Gamepad))
                    detach.Add(binding.Binding);
            }

            var attach = new List<TGamepad>();
            for (int gamepadIndex = 0; gamepadIndex < currentGamepads.Count; gamepadIndex++)
            {
                TGamepad gamepad = currentGamepads[gamepadIndex];
                bool found = false;
                bool builtInBindingFound = false;
                for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
                {
                    ControllerBinding<TGamepad, TBinding> binding = bindings[bindingIndex];
                    if (!ReferenceEquals(binding.Gamepad, gamepad)) continue;
                    found = true;
                    if (!binding.RecoveryOwned) builtInBindingFound = true;
                }

                if (!found)
                {
                    attach.Add(gamepad);
                    continue;
                }

                bool keptRecoveryBinding = false;
                for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
                {
                    ControllerBinding<TGamepad, TBinding> binding = bindings[bindingIndex];
                    if (!ReferenceEquals(binding.Gamepad, gamepad) ||
                        !binding.RecoveryOwned)
                        continue;
                    if (builtInBindingFound || keptRecoveryBinding)
                        detach.Add(binding.Binding);
                    else
                        keptRecoveryBinding = true;
                }
            }

            return new ControllerBindingPlan<TGamepad, TBinding>(attach, detach);
        }

        static bool ContainsReference<T>(IReadOnlyList<T> values, T target)
            where T : class
        {
            for (int i = 0; i < values.Count; i++)
                if (ReferenceEquals(values[i], target)) return true;
            return false;
        }
    }
}
