namespace DualSouls.Skins.HollowKnight.Core
{
    // Pure reducer. The caller owns state, binding authority, and before/after occurrence correlation.
    public sealed class SkinLifecycleCore
    {
        public LifecycleDecision Observe(LifecycleState state, LifecycleSignal signal)
        {
            if (!Valid(state)) return Decision(state, "invalid-state");
            if (!ValidSignal(signal)) return Decision(state, "invalid-signal");
            if (signal is LifecycleSignal.Rebind binding)
            {
                if (binding.Hero == state.CurrentHero && binding.Skin == state.CurrentSkin)
                    return Decision(state, "same-binding");
                return Decision(state with
                {
                    CurrentHero = binding.Hero, CurrentSkin = binding.Skin, StableCount = 0,
                    ArmedHero = null, ArmedSkin = null, ArmedOccurrence = null
                }, "rebound");
            }
            if (state.CurrentHero == null) return Decision(state, "unbound");
            return signal switch
            {
                LifecycleSignal.BeforeDeath before => BeforeDeath(state, before),
                LifecycleSignal.AfterDeath after => AfterDeath(state, after),
                LifecycleSignal.Update update => Update(state, update.Observation),
                _ => Decision(state, "invalid-signal")
            };
        }

        private static LifecycleDecision BeforeDeath(LifecycleState state, LifecycleSignal.BeforeDeath signal)
        {
            if (signal.Hero != state.CurrentHero || signal.Skin != state.CurrentSkin) return Decision(state, "stale-binding");
            if (signal.Occurrence.Value == 0) return Decision(state, "invalid-occurrence");
            if (signal.Occurrence.Value <= state.OccurrenceHighWater.Value) return Decision(state, "consumed-occurrence");
            if (state.ArmedOccurrence != null) return Decision(state, "candidate-already-armed");
            if (state.PendingEpoch != null) return Decision(state, "pending-epoch");
            // Consume at arm, not confirmation: even a subsequently invalidated candidate cannot replay.
            return Decision(state with { ArmedHero = signal.Hero, ArmedSkin = signal.Skin,
                ArmedOccurrence = signal.Occurrence, OccurrenceHighWater = signal.Occurrence }, "armed");
        }

        private static LifecycleDecision AfterDeath(LifecycleState state, LifecycleSignal.AfterDeath signal)
        {
            if (signal.Hero != state.CurrentHero || signal.Skin != state.CurrentSkin) return Decision(state, "stale-binding");
            if (signal.Occurrence.Value == 0) return Decision(state, "invalid-occurrence");
            if (state.ArmedOccurrence == null)
                return Decision(state, signal.Occurrence.Value <= state.OccurrenceHighWater.Value ?
                    "consumed-occurrence" : "unarmed-after-death");
            if (signal.Occurrence != state.ArmedOccurrence || signal.Hero != state.ArmedHero || signal.Skin != state.ArmedSkin)
                return Decision(state, "mismatched-occurrence");
            if (state.LastConfirmedEpoch.Value == ulong.MaxValue)
                return Decision(state with { ArmedHero = null, ArmedSkin = null, ArmedOccurrence = null }, "epoch-overflow");
            var epoch = new DeathEpoch(state.LastConfirmedEpoch.Value + 1);
            return new LifecycleDecision(state with
            {
                ArmedHero = null, ArmedSkin = null, ArmedOccurrence = null,
                PendingEpoch = epoch, StableCount = 0, LastConfirmedEpoch = epoch
            }, epoch, null, "death-confirmed");
        }

        private static LifecycleDecision Update(LifecycleState state, HeroObservation observation)
        {
            if (observation.Hero != state.CurrentHero || observation.Skin != state.CurrentSkin)
                return Decision(state, "stale-observation");
            var epoch = state.PendingEpoch;
            if (epoch == null) return Decision(state, "no-pending-epoch");
            var stable = observation.AcceptingInput && observation.FullDamageMode && observation.Health > 0 &&
                observation.CanTakeDamage && observation.Playable && !observation.Paused &&
                !observation.Cutscene && !observation.SceneTransition;
            if (!stable) return Decision(state with { StableCount = 0 }, "unstable-observation");
            if (state.StableCount < 2) return Decision(state with { StableCount = state.StableCount + 1 }, "stabilizing");
            return new LifecycleDecision(state with { PendingEpoch = null, StableCount = 0 }, null,
                new StableRespawnToken(epoch, observation.Hero, observation.Skin), "stable-respawn");
        }

        // Reject malformed caller snapshots rather than repairing them into fresh authority/proof.
        private static bool Valid(LifecycleState state)
        {
            if (state == null || state.LastConfirmedEpoch == null || state.OccurrenceHighWater == null) return false;
            if ((state.CurrentHero == null) != (state.CurrentSkin == null)) return false;
            if ((state.CurrentHero != null && !ValidHero(state.CurrentHero)) ||
                (state.CurrentSkin != null && !ValidSkin(state.CurrentSkin)) ||
                (state.ArmedHero != null && !ValidHero(state.ArmedHero)) ||
                (state.ArmedSkin != null && !ValidSkin(state.ArmedSkin))) return false;
            if (state.LastConfirmedEpoch.Value > state.OccurrenceHighWater.Value) return false;
            if (state.StableCount < 0 || state.StableCount > 2) return false;
            if (state.PendingEpoch == null && state.StableCount != 0) return false;
            if (state.PendingEpoch != null && (state.PendingEpoch.Value == 0 || state.PendingEpoch != state.LastConfirmedEpoch)) return false;
            if ((state.ArmedHero == null) != (state.ArmedOccurrence == null) ||
                (state.ArmedSkin == null) != (state.ArmedOccurrence == null)) return false;
            if (state.CurrentHero == null && (state.OccurrenceHighWater.Value != 0 || state.ArmedOccurrence != null || state.PendingEpoch != null)) return false;
            if (state.ArmedOccurrence != null && (state.ArmedHero != state.CurrentHero || state.ArmedSkin != state.CurrentSkin ||
                state.ArmedOccurrence.Value <= state.LastConfirmedEpoch.Value ||
                state.ArmedOccurrence != state.OccurrenceHighWater || state.PendingEpoch != null)) return false;
            return true;
        }

        // CLR references can contain null even with nullable annotations disabled; Kotlin values cannot.
        private static bool ValidSignal(LifecycleSignal signal) => signal switch
        {
            LifecycleSignal.Rebind binding => ValidHero(binding.Hero) && ValidSkin(binding.Skin),
            LifecycleSignal.BeforeDeath before => ValidHero(before.Hero) && ValidSkin(before.Skin) && before.Occurrence != null,
            LifecycleSignal.AfterDeath after => ValidHero(after.Hero) && ValidSkin(after.Skin) && after.Occurrence != null,
            LifecycleSignal.Update update => update.Observation != null &&
                ValidHero(update.Observation.Hero) && ValidSkin(update.Observation.Skin),
            _ => false
        };

        private static bool ValidHero(HeroBindingToken hero) => hero != null && hero.Value != null;
        private static bool ValidSkin(SkinBindingToken skin) => skin != null && skin.Value != null;
        private static LifecycleDecision Decision(LifecycleState state, string diagnosis) =>
            new LifecycleDecision(state, null, null, diagnosis);
    }
}
