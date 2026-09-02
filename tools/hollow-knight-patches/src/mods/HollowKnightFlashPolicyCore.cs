using System;

namespace DualSouls.Mods.HollowKnight
{
    internal enum HollowKnightFlashAuthority
    {
        None,
        Master,
        Legacy,
    }

    internal readonly struct HollowKnightFlashDecision
    {
        internal const float DefaultSoftAlpha = 0.35f;

        internal HollowKnightFlashDecision(
            HollowKnightFlashAuthority authority,
            HollowKnightFlashMode mode,
            float softAlpha)
        {
            Authority = authority;
            Mode = mode;
            SoftAlpha = softAlpha;
        }

        internal static HollowKnightFlashDecision None =>
            new HollowKnightFlashDecision(
                HollowKnightFlashAuthority.None,
                HollowKnightFlashMode.Vanilla,
                DefaultSoftAlpha);

        internal HollowKnightFlashAuthority Authority { get; }
        internal HollowKnightFlashMode Mode { get; }
        internal float SoftAlpha { get; }
        internal bool HasOwner => Authority != HollowKnightFlashAuthority.None;
    }

    internal static class HollowKnightFlashDecisionResolver
    {
        internal static HollowKnightFlashDecision Resolve(
            bool sessionReady,
            bool masterEnabled,
            string controllerValue,
            HollowKnightFlashMode? legacyMode,
            float? legacySoftAlpha)
        {
            if (sessionReady && masterEnabled)
            {
                HollowKnightFlashMode mode;
                switch (controllerValue)
                {
                    case "soft":
                        mode = HollowKnightFlashMode.Soft;
                        break;
                    case "off":
                        mode = HollowKnightFlashMode.Off;
                        break;
                    case "vanilla":
                    default:
                        mode = HollowKnightFlashMode.Vanilla;
                        break;
                }
                return new HollowKnightFlashDecision(
                    HollowKnightFlashAuthority.Master,
                    mode,
                    HollowKnightFlashDecision.DefaultSoftAlpha);
            }

            if (!legacyMode.HasValue) return HollowKnightFlashDecision.None;
            HollowKnightFlashMode resolvedLegacy =
                legacyMode.Value == HollowKnightFlashMode.Soft
                    ? HollowKnightFlashMode.Soft
                    : HollowKnightFlashMode.Vanilla;
            return new HollowKnightFlashDecision(
                HollowKnightFlashAuthority.Legacy,
                resolvedLegacy,
                resolvedLegacy == HollowKnightFlashMode.Soft
                    ? ClampLegacyAlpha(legacySoftAlpha)
                    : HollowKnightFlashDecision.DefaultSoftAlpha);
        }

        static float ClampLegacyAlpha(float? alpha)
        {
            if (!alpha.HasValue || float.IsNaN(alpha.Value) ||
                float.IsInfinity(alpha.Value))
                return HollowKnightFlashDecision.DefaultSoftAlpha;
            if (alpha.Value < 0f) return 0f;
            if (alpha.Value > 1f) return 1f;
            return alpha.Value;
        }
    }

    internal readonly struct HollowKnightFlashRgba : IEquatable<HollowKnightFlashRgba>
    {
        internal HollowKnightFlashRgba(float r, float g, float b, float a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        internal float R { get; }
        internal float G { get; }
        internal float B { get; }
        internal float A { get; }

        internal HollowKnightFlashRgba WithAlpha(float alpha)
        {
            return new HollowKnightFlashRgba(R, G, B, alpha);
        }

        public bool Equals(HollowKnightFlashRgba other)
        {
            return R == other.R && G == other.G &&
                   B == other.B && A == other.A;
        }

        public override bool Equals(object obj)
        {
            return obj is HollowKnightFlashRgba other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = R.GetHashCode();
                hash = (hash * 397) ^ G.GetHashCode();
                hash = (hash * 397) ^ B.GetHashCode();
                return (hash * 397) ^ A.GetHashCode();
            }
        }

        public static bool operator ==(
            HollowKnightFlashRgba left,
            HollowKnightFlashRgba right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            HollowKnightFlashRgba left,
            HollowKnightFlashRgba right)
        {
            return !left.Equals(right);
        }
    }

    internal readonly struct HollowKnightFlashSample : IEquatable<HollowKnightFlashSample>
    {
        internal HollowKnightFlashSample(
            bool enabled,
            HollowKnightFlashRgba color)
        {
            Enabled = enabled;
            Color = color;
        }

        internal bool Enabled { get; }
        internal HollowKnightFlashRgba Color { get; }

        public bool Equals(HollowKnightFlashSample other)
        {
            return Enabled == other.Enabled && Color == other.Color;
        }

        public override bool Equals(object obj)
        {
            return obj is HollowKnightFlashSample other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Enabled.GetHashCode() * 397) ^ Color.GetHashCode();
            }
        }

        public static bool operator ==(
            HollowKnightFlashSample left,
            HollowKnightFlashSample right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            HollowKnightFlashSample left,
            HollowKnightFlashSample right)
        {
            return !left.Equals(right);
        }
    }

    internal readonly struct HollowKnightFlashTransition
    {
        internal HollowKnightFlashTransition(
            bool writeEnabled,
            bool writeColor,
            HollowKnightFlashSample sample)
        {
            WriteEnabled = writeEnabled;
            WriteColor = writeColor;
            Sample = sample;
        }

        internal bool WriteEnabled { get; }
        internal bool WriteColor { get; }
        internal HollowKnightFlashSample Sample { get; }
        internal bool HasWrites => WriteEnabled || WriteColor;
    }

    internal sealed class HollowKnightFlashStateTracker
    {
        bool _gameEnabled;
        HollowKnightFlashRgba _gameColor;
        bool _enabledOwned;
        bool _colorOwned;
        bool _lastPolicyEnabled;
        HollowKnightFlashRgba _lastPolicyColor;

        internal HollowKnightFlashStateTracker(HollowKnightFlashSample initial)
        {
            _gameEnabled = initial.Enabled;
            _gameColor = initial.Color;
        }

        internal HollowKnightFlashTransition Apply(
            HollowKnightFlashSample live,
            HollowKnightFlashDecision decision)
        {
            bool colorUpdatedByGame;
            Observe(live, out colorUpdatedByGame);

            if (!decision.HasOwner ||
                decision.Mode == HollowKnightFlashMode.Vanilla)
                return RestoreObserved(live);

            if (decision.Mode == HollowKnightFlashMode.Off)
            {
                if (colorUpdatedByGame) _colorOwned = false;
                var output = new HollowKnightFlashSample(false, live.Color);
                bool writeEnabled = live.Enabled;
                _enabledOwned = true;
                _lastPolicyEnabled = false;
                return new HollowKnightFlashTransition(
                    writeEnabled,
                    false,
                    output);
            }

            HollowKnightFlashRgba softened = _gameColor;
            if (_colorOwned && live.Color.A < softened.A)
                softened = softened.WithAlpha(live.Color.A);
            float alpha = decision.SoftAlpha;
            if (float.IsNaN(alpha) || float.IsInfinity(alpha))
                alpha = HollowKnightFlashDecision.DefaultSoftAlpha;
            else if (alpha < 0f) alpha = 0f;
            else if (alpha > 1f) alpha = 1f;
            if (softened.A > alpha) softened = softened.WithAlpha(alpha);

            var softOutput = new HollowKnightFlashSample(
                _gameEnabled,
                softened);
            bool writeSoftEnabled = live.Enabled != softOutput.Enabled;
            bool writeSoftColor = live.Color != softOutput.Color;
            _enabledOwned = true;
            _colorOwned = true;
            _lastPolicyEnabled = softOutput.Enabled;
            _lastPolicyColor = softOutput.Color;
            return new HollowKnightFlashTransition(
                writeSoftEnabled,
                writeSoftColor,
                softOutput);
        }

        internal HollowKnightFlashTransition Release(
            HollowKnightFlashSample live)
        {
            bool ignored;
            Observe(live, out ignored);
            return RestoreObserved(live);
        }

        void Observe(
            HollowKnightFlashSample live,
            out bool colorUpdatedByGame)
        {
            if (!_enabledOwned || live.Enabled != _lastPolicyEnabled)
                _gameEnabled = live.Enabled;

            colorUpdatedByGame =
                _colorOwned && live.Color != _lastPolicyColor;
            if (!_colorOwned || colorUpdatedByGame)
                _gameColor = live.Color;
        }

        HollowKnightFlashTransition RestoreObserved(
            HollowKnightFlashSample live)
        {
            var restored = new HollowKnightFlashSample(
                _enabledOwned ? _gameEnabled : live.Enabled,
                _colorOwned ? _gameColor : live.Color);
            bool writeEnabled =
                _enabledOwned && live.Enabled != restored.Enabled;
            bool writeColor =
                _colorOwned && live.Color != restored.Color;
            _enabledOwned = false;
            _colorOwned = false;
            _gameEnabled = restored.Enabled;
            _gameColor = restored.Color;
            return new HollowKnightFlashTransition(
                writeEnabled,
                writeColor,
                restored);
        }
    }
}
