// C# 9 records require this compiler marker on netstandard2.1, not a runtime dependency.
#if NETSTANDARD2_1
namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }
#endif

namespace DualSouls.Skins.HollowKnight.Core
{
    public sealed record DeathEpoch(ulong Value);
    // Trusted caller correlates one before/after pair with a fresh monotonically increasing value.
    public sealed record DeathOccurrenceToken(ulong Value);

    // Hero identity; unchanged across deaths while the hero identity remains the same.
    public sealed record HeroBindingToken(string Value);
    public sealed record SkinBindingToken(string Value);
    public sealed record StableRespawnToken(DeathEpoch DeathEpoch, HeroBindingToken Hero, SkinBindingToken Skin);

    public sealed record HeroObservation(
        HeroBindingToken Hero, SkinBindingToken Skin, bool AcceptingInput,
        bool FullDamageMode, int Health, bool CanTakeDamage, bool Playable,
        bool Paused, bool Cutscene, bool SceneTransition);

    // Zero means no event yet. Both high-water values survive delivery and rebind.
    public sealed record LifecycleState(
        HeroBindingToken ArmedHero = null,
        DeathEpoch PendingEpoch = null,
        int StableCount = 0,
        HeroBindingToken CurrentHero = null,
        SkinBindingToken CurrentSkin = null,
        DeathEpoch LastConfirmedEpoch = null,
        SkinBindingToken ArmedSkin = null,
        DeathOccurrenceToken ArmedOccurrence = null,
        DeathOccurrenceToken OccurrenceHighWater = null)
    {
        public DeathOccurrenceToken OccurrenceHighWater { get; init; } = OccurrenceHighWater ?? new DeathOccurrenceToken(0);
        public DeathEpoch LastConfirmedEpoch { get; init; } = LastConfirmedEpoch ?? new DeathEpoch(0);
    }

    public abstract record LifecycleSignal
    {
        private LifecycleSignal() { }
        // Trusted caller boundary; updates cannot appoint binding authority.
        public sealed record Rebind(HeroBindingToken Hero, SkinBindingToken Skin) : LifecycleSignal;
        public sealed record BeforeDeath(HeroBindingToken Hero, SkinBindingToken Skin, DeathOccurrenceToken Occurrence) : LifecycleSignal;
        public sealed record AfterDeath(HeroBindingToken Hero, SkinBindingToken Skin, DeathOccurrenceToken Occurrence) : LifecycleSignal;
        public sealed record Update(HeroObservation Observation) : LifecycleSignal;
    }

    public sealed record LifecycleDecision(
        LifecycleState State, DeathEpoch ConfirmedEpoch, StableRespawnToken StableToken, string Diagnosis);
}
