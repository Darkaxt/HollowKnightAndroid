using System;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Reflection;
using UnityEngine;
using GlobalEnums;
#endif

namespace DualSouls.Skins.Silksong.Runtime
{
    public sealed class SilksongDeathClassification
    {
        public bool NonLethal, MemoryForcedNonLethal, Permadeath, DemoTerminal, DuplicateDie,
            Hazard, Paused, Cinematic, Transitioning, Loading;
        public bool Gameplay = true;
    }

    public static class SilksongDeathBridgePolicy
    {
        public static bool IsStrictNormal(SilksongDeathClassification value) => value != null &&
            !value.NonLethal && !value.MemoryForcedNonLethal && !value.Permadeath &&
            !value.DemoTerminal && !value.DuplicateDie && !value.Hazard && !value.Paused &&
            !value.Cinematic && value.Gameplay && !value.Transitioning && !value.Loading;
    }

    public sealed class SilksongDeathFrame
    {
        public object Hero, Manager, HudOwners, BridgeHero, BridgeManager;
        public long Frame, BridgeOccurrence;
        public bool Gameplay, Playing, Paused, HeroInPosition, SceneComplete, Dead, Hazard,
            Transitioning, Loading, AcceptingInput, ControlRelinquished, TargetsAvailable;
    }

    // The injected managed bridge classifies Die. This state machine only admits its exact
    // owner occurrence and waits for a separately sampled, stable respawn.
    public sealed class SilksongSkinDeathAdapter : IDisposable
    {
        readonly Func<SilksongDeathFrame> sample;
        long observedBridge = -1, sampledFrame = -1;
        int stableFrames;
        object deathHero, deathManager, stableHero, stableManager, stableHud;
        bool disposed;

        public string Run { get; private set; }
        public long Occurrence { get; private set; }
        public bool Recorded { get; private set; }
        public bool Ready
        {
            get
            {
                if (disposed || Run == null || Occurrence == 0 || !Recorded || stableFrames < 2) return false;
                var frame = sample();
                return Stable(frame) && SameDeathOwners(frame) &&
                    ReferenceEquals(frame.Hero, stableHero) && ReferenceEquals(frame.Manager, stableManager) &&
                    ReferenceEquals(frame.HudOwners, stableHud);
            }
        }

        public SilksongSkinDeathAdapter(Func<SilksongDeathFrame> sample)
        {
            this.sample = sample ?? throw new ArgumentNullException(nameof(sample));
        }

        public void Configure(string mode, string run, long lastDeath = 0, long pendingOccurrence = 0)
        {
            if (disposed) return;
            var next = mode == "ROTATE" && !string.IsNullOrEmpty(run) ? run : null;
            if (!string.Equals(Run, next, StringComparison.Ordinal))
            {
                ClearOccurrence();
                Run = next;
                observedBridge = sample().BridgeOccurrence;
            }
            if (Run == null) return;
            if (Occurrence > 0 && lastDeath == Occurrence)
            {
                if (!Recorded) stableFrames = 0;
                Recorded = true;
                if (pendingOccurrence == 0) ClearOccurrence();
            }
            if (pendingOccurrence > 0 && Occurrence != pendingOccurrence)
                ClearOccurrence();
        }

        public void Tick()
        {
            if (disposed || Run == null) return;
            var frame = sample();
            if (frame.BridgeOccurrence > observedBridge)
            {
                observedBridge = frame.BridgeOccurrence;
                if (Occurrence == 0 && frame.BridgeOccurrence > 0 &&
                    frame.BridgeHero != null && frame.BridgeManager != null &&
                    ReferenceEquals(frame.Hero, frame.BridgeHero) && ReferenceEquals(frame.Manager, frame.BridgeManager))
                {
                    Occurrence = frame.BridgeOccurrence;
                    deathHero = frame.BridgeHero;
                    deathManager = frame.BridgeManager;
                    Recorded = false;
                    stableFrames = 0;
                }
            }
            if (Occurrence == 0) return;
            if (!Recorded && !SameDeathOwners(frame))
            {
                ClearOccurrence();
                return;
            }
            if (frame.Frame == sampledFrame) return;
            sampledFrame = frame.Frame;
            if (!Recorded || !Stable(frame) || !SameDeathOwners(frame))
            {
                stableFrames = 0;
                return;
            }
            bool same = ReferenceEquals(frame.Hero, stableHero) && ReferenceEquals(frame.Manager, stableManager) &&
                ReferenceEquals(frame.HudOwners, stableHud);
            stableFrames = same ? Math.Min(2, stableFrames + 1) : 1;
            stableHero = frame.Hero;
            stableManager = frame.Manager;
            stableHud = frame.HudOwners;
        }

        bool SameDeathOwners(SilksongDeathFrame frame) =>
            ReferenceEquals(frame.Hero, deathHero) && ReferenceEquals(frame.Manager, deathManager);

        static bool Stable(SilksongDeathFrame frame) => frame != null && frame.Hero != null &&
            frame.Manager != null && frame.HudOwners != null && frame.Gameplay && frame.Playing && !frame.Paused &&
            frame.HeroInPosition && frame.SceneComplete && !frame.Dead && !frame.Hazard &&
            !frame.Transitioning && !frame.Loading && frame.AcceptingInput &&
            !frame.ControlRelinquished && frame.TargetsAvailable;

        void ClearOccurrence()
        {
            Occurrence = 0;
            Recorded = false;
            stableFrames = 0;
            deathHero = deathManager = stableHero = stableManager = stableHud = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            ClearOccurrence();
            Run = null;
            disposed = true;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        const string OccurrenceFieldName = "__dsNormalDeathOccurrence";
        const string HeroFieldName = "__dsNormalDeathHero";
        const string ManagerFieldName = "__dsNormalDeathManager";
        static readonly FieldInfo OccurrenceField = typeof(GameManager).GetField(OccurrenceFieldName,
            BindingFlags.Public | BindingFlags.Static);
        static readonly FieldInfo HeroField = typeof(GameManager).GetField(HeroFieldName,
            BindingFlags.Public | BindingFlags.Static);
        static readonly FieldInfo ManagerField = typeof(GameManager).GetField(ManagerFieldName,
            BindingFlags.Public | BindingFlags.Static);

        public SilksongSkinDeathAdapter(SilksongSkinRuntime runtime) : this(() => CaptureManaged(runtime)) { }

        static SilksongDeathFrame CaptureManaged(SilksongSkinRuntime runtime)
        {
            var hero = HeroController.SilentInstance;
            var manager = GameManager.SilentInstance;
            var frame = new SilksongDeathFrame {
                Frame = Time.frameCount,
                Hero = hero != null ? hero : null,
                Manager = manager != null ? manager : null,
                HudOwners = runtime != null ? runtime.HudOwnerIdentity : null,
            };
            if (OccurrenceField != null && HeroField != null && ManagerField != null)
            {
                frame.BridgeOccurrence = (long)OccurrenceField.GetValue(null);
                frame.BridgeHero = HeroField.GetValue(null);
                frame.BridgeManager = ManagerField.GetValue(null);
            }
            if (hero == null || manager == null || hero.cState == null) return frame;
            frame.Gameplay = manager.IsGameplayScene();
            frame.Playing = manager.GameState == GameState.PLAYING;
            frame.Paused = manager.isPaused;
            frame.HeroInPosition = hero.isHeroInPosition;
            frame.SceneComplete = manager.HasFinishedEnteringScene;
            frame.Dead = hero.cState.dead;
            frame.Hazard = hero.cState.hazardDeath || hero.cState.hazardRespawning;
            frame.Transitioning = hero.cState.transitioning || manager.IsInSceneTransition;
            frame.Loading = manager.IsLoadingSceneTransition;
            frame.AcceptingInput = hero.CanInput();
            frame.ControlRelinquished = hero.controlReqlinquished;
            frame.TargetsAvailable = runtime != null && runtime.DeathTargetsAvailable(hero, manager);
            return frame;
        }
#endif
    }
}
