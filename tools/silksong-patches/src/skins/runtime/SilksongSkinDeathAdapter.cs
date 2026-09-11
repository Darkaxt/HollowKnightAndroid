using System;
using System.Collections.Generic;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Reflection;
using UnityEngine;
using GlobalEnums;
#endif

namespace DualSouls.Skins.Silksong.Runtime
{
    public sealed class SilksongDeathOccurrence
    {
        public long Occurrence { get; }
        public object Hero { get; }
        public object Manager { get; }

        public SilksongDeathOccurrence(long occurrence, object hero, object manager)
        {
            Occurrence = occurrence;
            Hero = hero;
            Manager = manager;
        }
    }

    public sealed class SilksongDeathFrame
    {
        public object Hero, Manager, HudOwners;
        public IReadOnlyList<SilksongDeathOccurrence> BridgeOccurrences;
        public long Frame, BridgeOccurrence;
        public bool Gameplay, Playing, Paused, HeroInPosition, SceneComplete, Dead, Hazard,
            Transitioning, Loading, AcceptingInput, ControlRelinquished, TargetsAvailable;
    }

    // The injected managed bridge classifies Die. This state machine only admits its exact
    // owner occurrence and waits for a separately sampled, stable respawn.
    public sealed class SilksongSkinDeathAdapter : IDisposable
    {
        sealed class PendingDeath
        {
            public long Occurrence;
            public object Hero, Manager;
        }

        public const int MaxPendingOccurrences = 32;
        readonly Func<SilksongDeathFrame> sample;
        readonly System.Collections.Generic.Queue<PendingDeath> backlog =
            new System.Collections.Generic.Queue<PendingDeath>();
        readonly System.Collections.Generic.Queue<long> cancellations =
            new System.Collections.Generic.Queue<long>();
        long observedBridge = -1, sampledFrame = -1;
        int stableFrames;
        object deathHero, deathManager, stableHero, stableManager, stableHud;
        bool disposed;

        public string Run { get; private set; }
        public long Occurrence { get; private set; }
        public bool Recorded { get; private set; }
        public int PendingOccurrences => backlog.Count;
        public bool BacklogFaulted { get; private set; }
        public IReadOnlyList<long> PendingCancellations =>
            new List<long>(cancellations).AsReadOnly();
        public IReadOnlyList<long> PendingBridgeOccurrences
        {
            get
            {
                var result = new List<long>();
                if (Occurrence > 0 && !Recorded) result.Add(Occurrence);
                foreach (var item in backlog) result.Add(item.Occurrence);
                return result.AsReadOnly();
            }
        }
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
                ClearAll();
                Run = next;
                observedBridge = sample().BridgeOccurrence;
            }
            if (Run == null || BacklogFaulted) return;
            if (pendingOccurrence > 0)
            {
                if (Occurrence == 0 || Occurrence != pendingOccurrence || lastDeath != Occurrence)
                {
                    FailBacklog();
                    return;
                }
                if (!Recorded) stableFrames = 0;
                Recorded = true;
                return;
            }
            while (Occurrence > 0 && lastDeath >= Occurrence)
            {
                ClearActive();
                ActivateNext();
            }
        }

        public void Tick()
        {
            if (disposed || Run == null || BacklogFaulted) return;
            var frame = sample();
            CaptureOccurrences(frame);
            if (BacklogFaulted) return;
            ActivateNext();
            while (Occurrence != 0 && frame.Hero != null && frame.Manager != null && !SameDeathOwners(frame))
            {
                CancelActive();
                ActivateNext();
            }
            if (Occurrence == 0) return;
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

        void CaptureOccurrences(SilksongDeathFrame frame)
        {
            if (frame == null || frame.BridgeOccurrence <= observedBridge) return;
            long delta = frame.BridgeOccurrence - observedBridge;
            int occupied = cancellations.Count + backlog.Count + (Occurrence == 0 ? 0 : 1);
            if (observedBridge < 0 || delta > MaxPendingOccurrences - occupied)
            {
                FailBacklog();
                return;
            }
            var owners = new Dictionary<long, SilksongDeathOccurrence>();
            if (frame.BridgeOccurrences != null)
            {
                foreach (var item in frame.BridgeOccurrences)
                {
                    if (item == null || item.Occurrence <= 0 || owners.ContainsKey(item.Occurrence))
                    {
                        FailBacklog();
                        return;
                    }
                    owners.Add(item.Occurrence, item);
                }
            }
            for (long occurrence = observedBridge + 1; occurrence <= frame.BridgeOccurrence; occurrence++)
            {
                if (!owners.TryGetValue(occurrence, out var item) || item.Hero == null || item.Manager == null)
                {
                    FailBacklog();
                    return;
                }
                backlog.Enqueue(new PendingDeath { Occurrence = occurrence,
                    Hero = item.Hero, Manager = item.Manager });
            }
            observedBridge = frame.BridgeOccurrence;
        }

        void ActivateNext()
        {
            if (Occurrence != 0 || backlog.Count == 0 || BacklogFaulted) return;
            var next = backlog.Dequeue();
            Occurrence = next.Occurrence;
            deathHero = next.Hero;
            deathManager = next.Manager;
            Recorded = false;
            stableFrames = 0;
        }

        void CancelActive()
        {
            if (Occurrence == 0) return;
            cancellations.Enqueue(Occurrence);
            ClearActive();
        }

        public bool AcknowledgeCancellation(long occurrence)
        {
            if (cancellations.Count == 0 || cancellations.Peek() != occurrence) return false;
            cancellations.Dequeue();
            return true;
        }

        bool SameDeathOwners(SilksongDeathFrame frame) =>
            ReferenceEquals(frame.Hero, deathHero) && ReferenceEquals(frame.Manager, deathManager);

        static bool Stable(SilksongDeathFrame frame) => frame != null && frame.Hero != null &&
            frame.Manager != null && frame.HudOwners != null && frame.Gameplay && frame.Playing && !frame.Paused &&
            frame.HeroInPosition && frame.SceneComplete && !frame.Dead && !frame.Hazard &&
            !frame.Transitioning && !frame.Loading && frame.AcceptingInput &&
            !frame.ControlRelinquished && frame.TargetsAvailable;

        void ClearActive()
        {
            Occurrence = 0;
            Recorded = false;
            stableFrames = 0;
            deathHero = deathManager = stableHero = stableManager = stableHud = null;
        }

        void FailBacklog()
        {
            ClearActive();
            backlog.Clear();
            cancellations.Clear();
            BacklogFaulted = true;
        }

        void ClearAll()
        {
            ClearActive();
            backlog.Clear();
            cancellations.Clear();
            BacklogFaulted = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            ClearAll();
            Run = null;
            disposed = true;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        const string OccurrenceFieldName = "__dsNormalDeathOccurrence";
        const string HeroesFieldName = "__dsNormalDeathHeroes";
        const string ManagersFieldName = "__dsNormalDeathManagers";
        const string TokensFieldName = "__dsNormalDeathTokens";
        static readonly FieldInfo OccurrenceField = typeof(GameManager).GetField(OccurrenceFieldName,
            BindingFlags.Public | BindingFlags.Static);
        static readonly FieldInfo HeroesField = typeof(GameManager).GetField(HeroesFieldName,
            BindingFlags.Public | BindingFlags.Static);
        static readonly FieldInfo ManagersField = typeof(GameManager).GetField(ManagersFieldName,
            BindingFlags.Public | BindingFlags.Static);
        static readonly FieldInfo TokensField = typeof(GameManager).GetField(TokensFieldName,
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
            if (OccurrenceField != null && HeroesField != null && ManagersField != null && TokensField != null)
            {
                frame.BridgeOccurrence = (long)OccurrenceField.GetValue(null);
                var heroes = HeroesField.GetValue(null) as HeroController[];
                var managers = ManagersField.GetValue(null) as GameManager[];
                var tokens = TokensField.GetValue(null) as long[];
                var occurrences = new List<SilksongDeathOccurrence>();
                if (heroes != null && managers != null && tokens != null &&
                    heroes.Length == MaxPendingOccurrences && managers.Length == MaxPendingOccurrences &&
                    tokens.Length == MaxPendingOccurrences)
                {
                    long first = Math.Max(1, frame.BridgeOccurrence - MaxPendingOccurrences + 1);
                    for (long occurrence = first; occurrence <= frame.BridgeOccurrence; occurrence++)
                    {
                        int index = (int)((occurrence - 1) % MaxPendingOccurrences);
                        if (tokens[index] != occurrence || heroes[index] == null || managers[index] == null) continue;
                        occurrences.Add(new SilksongDeathOccurrence(occurrence, heroes[index], managers[index]));
                    }
                }
                frame.BridgeOccurrences = occurrences.AsReadOnly();
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
