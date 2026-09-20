using System;

namespace DualSouls.Mods.Silksong
{
    /// <summary>
    /// Pure identity/generation gate for Unity menu transitions. A lease from an
    /// old binding cannot complete or mutate a later binding.
    /// </summary>
    public sealed class SilksongNativeMenuLifecycle<TBinding> where TBinding : class
    {
        int _generation;

        public TBinding Current { get; private set; }
        public bool Transitioning { get; private set; }

        public void Bind(TBinding binding)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            _generation = unchecked(_generation + 1);
            Current = binding;
            Transitioning = false;
        }

        public bool TryBegin(out SilksongNativeMenuTransition<TBinding> transition)
        {
            if (Current == null || Transitioning)
            {
                transition = default;
                return false;
            }

            Transitioning = true;
            transition = new SilksongNativeMenuTransition<TBinding>(Current, _generation);
            return true;
        }

        public bool IsCurrent(SilksongNativeMenuTransition<TBinding> transition,
                              TBinding expected)
        {
            return Transitioning && expected != null &&
                   ReferenceEquals(Current, expected) &&
                   ReferenceEquals(transition.Binding, expected) &&
                   transition.Generation == _generation;
        }

        public bool Complete(SilksongNativeMenuTransition<TBinding> transition,
                             TBinding expected)
        {
            if (!IsCurrent(transition, expected)) return false;
            Transitioning = false;
            return true;
        }

        public bool Cancel(SilksongNativeMenuTransition<TBinding> transition,
                           TBinding expected)
        {
            return Complete(transition, expected);
        }

        public void Clear()
        {
            _generation = unchecked(_generation + 1);
            Current = null;
            Transitioning = false;
        }
    }

    public readonly struct SilksongNativeMenuTransition<TBinding> where TBinding : class
    {
        internal SilksongNativeMenuTransition(TBinding binding, int generation)
        {
            Binding = binding;
            Generation = generation;
        }

        internal TBinding Binding { get; }
        internal int Generation { get; }
    }
}
