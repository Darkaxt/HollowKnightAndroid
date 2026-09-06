using DualSouls.Skins.HollowKnight.Core;
using Xunit;

public sealed class HollowKnightSkinLifecycleCoreTests
{
    private readonly SkinLifecycleCore core = new();
    private readonly HeroBindingToken hero = new("hero-1");
    private readonly SkinBindingToken skin = new("skin-1");
    private readonly HeroBindingToken nextHero = new("hero-2");
    private readonly SkinBindingToken nextSkin = new("skin-2");
    private LifecycleState Bind() => core.Observe(new LifecycleState(), new LifecycleSignal.Rebind(hero, skin)).State;
    private LifecycleSignal.BeforeDeath Before(ulong n = 1, HeroBindingToken h = null, SkinBindingToken s = null) =>
        new(h ?? hero, s ?? skin, new DeathOccurrenceToken(n));
    private LifecycleSignal.AfterDeath After(ulong n = 1, HeroBindingToken h = null, SkinBindingToken s = null) =>
        new(h ?? hero, s ?? skin, new DeathOccurrenceToken(n));
    private HeroObservation Good(HeroBindingToken h = null, SkinBindingToken s = null) =>
        new(h ?? hero, s ?? skin, true, true, 1, true, true, false, false, false);
    private LifecycleDecision Update(LifecycleState state, HeroObservation observation = null) =>
        core.Observe(state, new LifecycleSignal.Update(observation ?? Good()));
    private LifecycleDecision Confirm(LifecycleState state = null, ulong n = 1)
    {
        state ??= Bind();
        var armed = core.Observe(state, Before(n, state.CurrentHero, state.CurrentSkin)).State;
        return core.Observe(armed, After(n, state.CurrentHero, state.CurrentSkin));
    }
    private LifecycleDecision Deliver(LifecycleState state, HeroObservation observation = null) =>
        Update(Update(Update(state, observation).State, observation).State, observation);
    private void Unchanged(LifecycleState state, LifecycleSignal signal, string diagnosis)
    {
        var result = core.Observe(state, signal);
        Assert.Equal(state, result.State);
        Assert.Null(result.ConfirmedEpoch);
        Assert.Null(result.StableToken);
        Assert.Equal(diagnosis, result.Diagnosis);
    }

    [Fact] public void matchesRespawnEpochHeroAndSkin()
    {
        var confirmed = Confirm(n: 7);
        Assert.Equal(new DeathEpoch(1), confirmed.ConfirmedEpoch);
        Assert.Equal(new DeathOccurrenceToken(7), confirmed.State.OccurrenceHighWater);
        Assert.Null(confirmed.StableToken);
        var delivered = Deliver(confirmed.State);
        Assert.Equal(new StableRespawnToken(new DeathEpoch(1), hero, skin), delivered.StableToken);
        Assert.Null(delivered.State.PendingEpoch);
        Assert.Equal(new DeathEpoch(1), delivered.State.LastConfirmedEpoch);
    }

    [Fact] public void requiresThreeStableMatchingUpdates()
    {
        var armed = core.Observe(Bind(), Before()).State;
        var preconfirmation = Deliver(armed);
        Assert.Null(preconfirmation.StableToken);
        Assert.Equal(0, preconfirmation.State.StableCount);
        var one = Update(core.Observe(preconfirmation.State, After()).State);
        Assert.Equal(1, one.State.StableCount);
        Assert.Null(one.StableToken);
        var two = Update(one.State);
        Assert.Equal(2, two.State.StableCount);
        Assert.Null(two.StableToken);
        Assert.NotNull(Update(two.State).StableToken);
    }

    [Fact] public void rebindRetainsPendingEpochAndRejectsOldTokens()
    {
        var two = Update(Update(Confirm().State).State).State;
        foreach (var h in new[] { hero, nextHero })
        {
            var rebound = core.Observe(two, new LifecycleSignal.Rebind(h, nextSkin)).State;
            Assert.Equal(two.PendingEpoch, rebound.PendingEpoch);
            Assert.Equal(two.LastConfirmedEpoch, rebound.LastConfirmedEpoch);
            Assert.Equal(two.OccurrenceHighWater, rebound.OccurrenceHighWater);
            Assert.Equal(0, rebound.StableCount);
            Unchanged(rebound, After(), "stale-binding");
            Unchanged(rebound, new LifecycleSignal.Update(Good()), "stale-observation");
            Assert.Equal(new StableRespawnToken(new DeathEpoch(1), h, nextSkin), Deliver(rebound, Good(h, nextSkin)).StableToken);
        }
    }

    [Fact] public void ignoresDuplicateStaleAndPreconfirmationSignals()
    {
        var bound = Bind();
        Unchanged(bound, After(), "unarmed-after-death");
        Unchanged(bound, new LifecycleSignal.Update(Good()), "no-pending-epoch");
        Unchanged(bound, Before(h: nextHero), "stale-binding");
        Unchanged(bound, Before(s: nextSkin), "stale-binding");
        var armed = core.Observe(bound, Before()).State;
        Unchanged(armed, Before(), "consumed-occurrence");
        Unchanged(armed, Before(2), "candidate-already-armed");
        Unchanged(armed, After(h: nextHero), "stale-binding");
        Unchanged(armed, After(s: nextSkin), "stale-binding");
        var confirmed = core.Observe(armed, After()).State;
        Unchanged(confirmed, After(), "consumed-occurrence");
        Unchanged(confirmed, Before(), "consumed-occurrence");
    }

    [Fact] public void everyFalsePredicateResetsWindow()
    {
        var g = Good();
        var failures = new[] { g with { AcceptingInput = false }, g with { FullDamageMode = false },
            g with { Health = 0 }, g with { Health = -1 }, g with { CanTakeDamage = false },
            g with { Playable = false }, g with { Paused = true }, g with { Cutscene = true }, g with { SceneTransition = true } };
        foreach (var bad in failures)
        {
            var two = Update(Update(Confirm().State).State).State;
            var reset = Update(two, bad);
            Assert.Equal("unstable-observation", reset.Diagnosis);
            Assert.Null(reset.StableToken);
            Assert.Equal(0, reset.State.StableCount);
            var second = Update(Update(reset.State).State);
            Assert.Null(second.StableToken);
            Assert.NotNull(Update(second.State).StableToken);
        }
    }

    [Fact] public void emittedEpochCannotEmitAgain()
    {
        var emitted = Deliver(Confirm().State).State;
        for (var i = 0; i < 5; i++) Unchanged(emitted, new LifecycleSignal.Update(Good()), "no-pending-epoch");
        Unchanged(emitted, Before(), "consumed-occurrence");
        Unchanged(emitted, After(), "consumed-occurrence");
        Assert.Equal(new DeathOccurrenceToken(1), emitted.OccurrenceHighWater);
    }

    [Fact] public void repeatedSameHeroDeathConfirmsEpochTwo()
    {
        var emitted = Deliver(Confirm().State).State;
        var second = Confirm(emitted, 2);
        Assert.Equal(new DeathEpoch(2), second.ConfirmedEpoch);
        Assert.Equal(hero, second.State.CurrentHero);
        Assert.Equal(skin, second.State.CurrentSkin);
        Assert.Equal(new StableRespawnToken(new DeathEpoch(2), hero, skin), Deliver(second.State).StableToken);
    }

    [Fact] public void skinOnlyRebindAcceptsFirstDeath()
    {
        var rebound = core.Observe(Bind(), new LifecycleSignal.Rebind(hero, nextSkin)).State;
        var confirmed = Confirm(rebound);
        Assert.Equal(new DeathEpoch(1), confirmed.ConfirmedEpoch);
        Assert.Equal(new StableRespawnToken(new DeathEpoch(1), hero, nextSkin), Deliver(confirmed.State, Good(hero, nextSkin)).StableToken);
    }

    [Fact] public void skinOnlyRebindAfterDeliveryAcceptsFreshOccurrence()
    {
        var rebound = core.Observe(Deliver(Confirm().State).State, new LifecycleSignal.Rebind(hero, nextSkin)).State;
        foreach (var s in new[] { skin, nextSkin })
        {
            var diagnosis = s == skin ? "stale-binding" : "consumed-occurrence";
            Unchanged(rebound, Before(s: s), diagnosis);
            Unchanged(rebound, After(s: s), diagnosis);
        }
        var second = Confirm(rebound, 2);
        Assert.Equal(new DeathEpoch(2), second.ConfirmedEpoch);
        Assert.NotNull(Deliver(second.State, Good(hero, nextSkin)).StableToken);
    }

    [Fact] public void invalidatedArmedOccurrenceCannotReplayAfterRebind()
    {
        var armed = core.Observe(Bind(), Before(5)).State;
        foreach (var h in new[] { hero, nextHero })
        {
            var rebound = core.Observe(armed, new LifecycleSignal.Rebind(h, nextSkin)).State;
            Assert.Null(rebound.ArmedOccurrence);
            Assert.Null(rebound.ArmedHero);
            Assert.Null(rebound.ArmedSkin);
            Assert.Equal(new DeathOccurrenceToken(5), rebound.OccurrenceHighWater);
            Assert.Equal(new DeathEpoch(0), rebound.LastConfirmedEpoch);
            Unchanged(rebound, Before(5, h, nextSkin), "consumed-occurrence");
            Unchanged(rebound, After(5, h, nextSkin), "consumed-occurrence");
            Unchanged(rebound, After(5), "stale-binding");
            Assert.Equal(new DeathEpoch(1), Confirm(rebound, 6).ConfirmedEpoch);
        }
    }

    [Fact] public void outOfOrderAfterNeverReplacesArmedOccurrence()
    {
        var bound = Bind();
        Unchanged(bound, After(10), "unarmed-after-death");
        var armed = core.Observe(bound, Before(5)).State;
        foreach (var n in new[] { 1UL, 4UL, 6UL, ulong.MaxValue }) Unchanged(armed, After(n), "mismatched-occurrence");
        Assert.Equal(new DeathEpoch(1), core.Observe(armed, After(5)).ConfirmedEpoch);
    }

    [Fact] public void epochOverflowBlocks()
    {
        var near = Bind() with { LastConfirmedEpoch = new DeathEpoch(ulong.MaxValue - 1),
            OccurrenceHighWater = new DeathOccurrenceToken(ulong.MaxValue - 1) };
        var maximum = Confirm(near, ulong.MaxValue);
        Assert.Equal(new DeathEpoch(ulong.MaxValue), maximum.ConfirmedEpoch);
        var delivered = Deliver(maximum.State).State;
        Unchanged(delivered, Before(0), "invalid-occurrence");
        Unchanged(delivered, Before(ulong.MaxValue), "consumed-occurrence");
        var exhausted = delivered with { ArmedHero = hero, ArmedSkin = skin, ArmedOccurrence = new DeathOccurrenceToken(ulong.MaxValue) };
        // At epoch exhaustion no unsigned occurrence can remain strictly newer; forged arming is invalid.
        Unchanged(exhausted, After(ulong.MaxValue), "invalid-state");
    }

    [Fact] public void occurrenceHighWaterExhaustionNeverWraps()
    {
        var bound = Bind();
        Unchanged(bound, Before(0), "invalid-occurrence");
        Unchanged(bound, After(0), "invalid-occurrence");
        var armed = core.Observe(bound, Before(ulong.MaxValue)).State;
        Assert.Equal(new DeathOccurrenceToken(ulong.MaxValue), armed.OccurrenceHighWater);
        var confirmed = core.Observe(armed, After(ulong.MaxValue));
        Assert.Equal(new DeathEpoch(1), confirmed.ConfirmedEpoch);
        var delivered = Deliver(confirmed.State).State;
        foreach (var n in new[] { 0UL, 1UL, ulong.MaxValue - 1, ulong.MaxValue })
            Unchanged(delivered, Before(n), n == 0 ? "invalid-occurrence" : "consumed-occurrence");
        var rebound = core.Observe(delivered, new LifecycleSignal.Rebind(nextHero, nextSkin)).State;
        Unchanged(rebound, Before(1, nextHero, nextSkin), "consumed-occurrence");
    }

    [Fact] public void repeatedBindPreservesWindow()
    {
        var armed = core.Observe(Bind(), Before()).State;
        Unchanged(armed, new LifecycleSignal.Rebind(hero, skin), "same-binding");
        var two = Update(Update(core.Observe(armed, After()).State).State).State;
        Unchanged(two, new LifecycleSignal.Rebind(new HeroBindingToken("hero-1"), new SkinBindingToken("skin-1")), "same-binding");
        Assert.NotNull(Update(two).StableToken);
    }

    [Fact] public void unboundUpdatesCannotCreateAuthority()
    {
        foreach (var signal in new LifecycleSignal[] { new LifecycleSignal.Update(Good()), Before(), After() })
            Unchanged(new LifecycleState(), signal, "unbound");
    }

    [Fact] public void pendingEpochCannotBeOverwritten()
    {
        var pending = Confirm().State;
        Unchanged(pending, Before(2), "pending-epoch");
        Unchanged(pending, After(2), "unarmed-after-death");
        Assert.Equal(new DeathOccurrenceToken(1), pending.OccurrenceHighWater);
    }

    [Fact] public void staleObservationPreservesConsecutiveWindow()
    {
        var two = Update(Update(Confirm().State).State).State;
        foreach (var stale in new[] { Good(nextHero), Good(s: nextSkin), Good(nextHero, nextSkin) with { Health = 0 } })
            Unchanged(two, new LifecycleSignal.Update(stale), "stale-observation");
        Assert.NotNull(Update(two).StableToken);
    }

    [Fact] public void repeatedStaleInterleavingCannotStarveRespawn()
    {
        var state = Confirm().State;
        for (var step = 1; step <= 3; step++)
        {
            for (var i = 0; i < 8; i++)
            {
                var stale = Update(state, Good(nextHero, nextSkin) with { Paused = true });
                Assert.Equal(state, stale.State);
                Assert.Null(stale.StableToken);
                state = stale.State;
            }
            var current = Update(state);
            Assert.Equal(step == 3, current.StableToken != null);
            state = current.State;
        }
    }

    [Fact] public void invalidStatesFailClosed()
    {
        var pending = Confirm().State;
        var invalid = new[] { pending with { PendingEpoch = null, ArmedHero = hero, ArmedSkin = skin,
            ArmedOccurrence = new DeathOccurrenceToken(1) }, pending with { StableCount = -1 }, pending with { StableCount = 3 },
            pending with { PendingEpoch = new DeathEpoch(0) }, pending with { LastConfirmedEpoch = new DeathEpoch(2) },
            pending with { CurrentSkin = null }, pending with { ArmedHero = hero },
            pending with { OccurrenceHighWater = new DeathOccurrenceToken(0) }, Bind() with { StableCount = 1 },
            Bind() with { ArmedHero = nextHero }, new LifecycleState(OccurrenceHighWater: new DeathOccurrenceToken(1)),
            Bind() with { ArmedHero = hero, ArmedSkin = skin, ArmedOccurrence = new DeathOccurrenceToken(2),
                OccurrenceHighWater = new DeathOccurrenceToken(1) } };
        foreach (var state in invalid)
            foreach (var signal in new LifecycleSignal[] { new LifecycleSignal.Update(Good()), new LifecycleSignal.Rebind(nextHero, nextSkin), Before(), After() })
                Unchanged(state, signal, "invalid-state");
    }

    [Fact] public void reducerIsDeterministicAndValuesHaveStructuralEquality()
    {
        var state = Confirm().State;
        var signal = new LifecycleSignal.Update(Good());
        Assert.Equal(core.Observe(state, signal), new SkinLifecycleCore().Observe(state with { }, signal with { Observation = Good() }));
        Assert.Equal(0, state.StableCount);
        Assert.Equal(Before(), Before(1, new HeroBindingToken("hero-1"), new SkinBindingToken("skin-1")));
        Assert.NotEqual(Before(), Before(2));
        var a = new StableRespawnToken(new DeathEpoch(1), hero, skin);
        var b = new StableRespawnToken(new DeathEpoch(1), new HeroBindingToken("hero-1"), new SkinBindingToken("skin-1"));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, b with { DeathEpoch = new DeathEpoch(2) });
    }

    [Fact] public void malformedClrValuesFailClosed()
    {
        var bound = Bind();
        foreach (var signal in new LifecycleSignal[] { null, new LifecycleSignal.Update(null),
            new LifecycleSignal.Rebind(null, skin), new LifecycleSignal.Rebind(hero, new SkinBindingToken(null)),
            new LifecycleSignal.BeforeDeath(null, skin, new DeathOccurrenceToken(1)),
            new LifecycleSignal.AfterDeath(hero, null, new DeathOccurrenceToken(1)),
            new LifecycleSignal.BeforeDeath(hero, skin, null), new LifecycleSignal.AfterDeath(hero, skin, null),
            new LifecycleSignal.Update(Good() with { Hero = new HeroBindingToken(null) }) })
            Unchanged(bound, signal, "invalid-signal");
        Unchanged(null, Before(), "invalid-state");
        Unchanged(bound with { CurrentHero = new HeroBindingToken(null) }, new LifecycleSignal.Update(Good()), "invalid-state");
        Unchanged(bound with { OccurrenceHighWater = null }, Before(), "invalid-state");
    }
}
