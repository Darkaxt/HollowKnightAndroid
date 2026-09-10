using System;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;
using GlobalEnums;
#endif

namespace DualSouls.Skins.HollowKnight.Runtime
{
    public sealed class SkinDeathFrame
    {
        public object Hero, Manager, Hud;
        public long Frame;
        public int SaveId, Permadeath;
        public string MapZone;
        public bool Gameplay, Playing, Paused, InPosition, WaitingToTransition, Dead, Hazard,
            Transitioning, AcceptingInput, ControlRelinquished, TargetsAvailable;
    }
    // The state machine is host-testable; the managed constructor below owns actual event bindings.
    // No save writes, native addresses, or inferred deaths from ordinary scene transitions.
    public sealed class HollowKnightSkinDeathAdapter : IDisposable
    {
        readonly Func<SkinDeathFrame> sample;
        object hero, manager, stableHero, stableManager, stableHud, armedHero, armedManager;
        long sampledFrame = -1, armedFrame, sequence;
        int saveId, stableFrames;
        bool armed, positioned, completed, cancelled, disposed;
        public string Run { get; private set; }
        public string CancellationRun { get; private set; }
        public long Occurrence { get; private set; }
        public bool Recorded { get; private set; }
        public bool Ready {
            get {
                if (disposed || cancelled || Run == null || Occurrence == 0 || !completed || stableFrames < 2) return false;
                var f = sample();
                return Stable(f) && f.SaveId == saveId && ReferenceEquals(f.Hero, stableHero) &&
                    ReferenceEquals(f.Manager, stableManager) && ReferenceEquals(f.Hud, stableHud);
            }
        }
        public HollowKnightSkinDeathAdapter(Func<SkinDeathFrame> sample) { this.sample = sample ?? throw new ArgumentNullException(nameof(sample)); }
        public void Configure(string mode, string run, long lastDeath = 0, long pendingOccurrence = 0)
        {
            if (disposed) return;
            var next = mode == "ROTATE" && !string.IsNullOrEmpty(run) ? run : null;
            if (Run != next) {
                ClearOccurrence(); Run = next; CancellationRun = null; cancelled = false; sequence = lastDeath;
                var f = sample(); saveId = f.SaveId; hero = f.Hero; manager = f.Manager;
            }
            if (Run == null || cancelled) return;
            sequence = Math.Max(sequence, lastDeath);
            if (Occurrence > 0 && lastDeath == Occurrence) {
                Recorded = true;
                if (pendingOccurrence == 0) ClearOccurrence(); // authority completed this death; no further visual apply is authorized
            }
            // A new managed owner cannot resume another owner's lost GPU/lifecycle work.
            if (pendingOccurrence > 0 && Occurrence != pendingOccurrence) Cancel();
        }
        public void OnDeath(object owner, object game)
        {
            if (disposed || cancelled || Run == null || Occurrence != 0 || armed) return;
            var f = sample();
            if (!Owners(f, owner, game) || f.Dead || !Normal(f)) return;
            // Actual OnDeath precedes the game's duplicate-dead guard. Arm only, never advance here.
            armed = true; armedHero = owner; armedManager = game; armedFrame = f.Frame;
        }
        public void HeroInPosition(object owner, object game)
        {
            var f = sample();
            if (disposed || cancelled || Occurrence == 0 || !Owners(f, owner, game)) return;
            ObserveOwners(f); // sceneLoaded can bind a replacement before the next Update
            if (!positioned) { positioned = true; completed = false; stableFrames = 0; }
        }
        public void SceneCompleted(object owner, object game)
        {
            if (disposed || cancelled || Occurrence == 0 || !positioned || !Owners(sample(), owner, game)) return;
            completed = true;
        }
        public void Tick()
        {
            if (disposed) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (managed) BindManaged();
#endif
            var f = sample();
            if (Run == null || cancelled) return;
            if (f.Manager != null && f.SaveId != saveId) { Cancel(); return; }
            ObserveOwners(f);
            if (f.Frame == sampledFrame) return;
            sampledFrame = f.Frame;
            if (armed && f.Frame > armedFrame) {
                armed = false;
                if (Owners(f, armedHero, armedManager) && f.Dead && Normal(f)) {
                    if (sequence == long.MaxValue) { Cancel(); return; }
                    Occurrence = ++sequence; Recorded = false; positioned = completed = false; stableFrames = 0;
                }
            }
            if (Occurrence == 0 || !completed || !Stable(f)) { stableFrames = 0; return; }
            bool same = ReferenceEquals(stableHero, f.Hero) && ReferenceEquals(stableManager, f.Manager) && ReferenceEquals(stableHud, f.Hud);
            stableFrames = same ? Math.Min(2, stableFrames + 1) : 1;
            stableHero = f.Hero; stableManager = f.Manager; stableHud = f.Hud;
        }
        void ObserveOwners(SkinDeathFrame f)
        {
            if (ReferenceEquals(hero, f.Hero) && ReferenceEquals(manager, f.Manager)) return;
            hero = f.Hero; manager = f.Manager; positioned = completed = false; stableFrames = 0;
            // Preserve a confirmed death through respawn rebinding, not an unconfirmed retired arm.
            if (armed && !Owners(f, armedHero, armedManager)) armed = false;
        }
        static bool Owners(SkinDeathFrame f, object h, object m) => h != null && m != null && ReferenceEquals(f.Hero, h) && ReferenceEquals(f.Manager, m);
        static bool Normal(SkinDeathFrame f) => f.Permadeath == 0 && f.MapZone != "DREAM_WORLD" && f.MapZone != "GODS_GLORY";
        static bool Stable(SkinDeathFrame f) => f.Hero != null && f.Manager != null && f.Hud != null && Normal(f) && f.Gameplay && f.Playing &&
            !f.Paused && f.InPosition && f.WaitingToTransition && !f.Dead && !f.Hazard && !f.Transitioning &&
            f.AcceptingInput && !f.ControlRelinquished && f.TargetsAvailable;
        void ClearOccurrence() { armed = positioned = completed = false; stableFrames = 0; Occurrence = 0; Recorded = false; }
        public void Cancel() { if (Run != null) CancellationRun = Run; cancelled = true; ClearOccurrence(); }
        public void Dispose()
        {
            if (disposed) return;
            Cancel(); disposed = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (managed) { UnbindManaged(); UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded; }
#endif
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        readonly bool managed;
        HeroController boundHero;
        GameManager boundManager;
        HeroController.HeroDeathEvent deathHandler;
        HeroController.HeroInPosition positionHandler;
        GameManager.EnterSceneEvent completionHandler;
        public HollowKnightSkinDeathAdapter(HollowKnightSkinRuntime runtime) : this(() => CaptureManaged(runtime))
        {
            managed = true; BindManaged(); UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }
        void OnSceneLoaded(Scene scene, LoadSceneMode mode) { BindManaged(); }
        void BindManaged()
        {
            var h = HeroController.instance; var m = GameManager.instance;
            if (ReferenceEquals(h, boundHero) && ReferenceEquals(m, boundManager)) return;
            UnbindManaged(); boundHero = h; boundManager = m;
            if (h != null && m != null) {
                deathHandler = () => OnDeath(h, m); positionHandler = _ => HeroInPosition(h, m);
                completionHandler = () => SceneCompleted(h, m);
                h.OnDeath += deathHandler; h.heroInPosition += positionHandler; m.OnFinishedEnteringScene += completionHandler;
            }
        }
        void UnbindManaged()
        {
            // ReferenceEquals permits unsubscribing managed delegates even after Unity's native owner died.
            if (!ReferenceEquals(boundHero, null)) { boundHero.OnDeath -= deathHandler; boundHero.heroInPosition -= positionHandler; }
            if (!ReferenceEquals(boundManager, null)) boundManager.OnFinishedEnteringScene -= completionHandler;
            boundHero = null; boundManager = null;
        }
        static SkinDeathFrame CaptureManaged(HollowKnightSkinRuntime runtime)
        {
            var h = HeroController.instance; var m = GameManager.instance; var cameras = GameCameras.instance;
            var hud = cameras != null ? cameras.hudCanvas : null;
            var f = new SkinDeathFrame { Frame = Time.frameCount, Hero = h != null ? h : null, Manager = m != null ? m : null,
                Hud = hud != null ? hud : null, SaveId = m != null ? m.profileID : 0 };
            if (h == null || m == null || h.cState == null || h.playerData == null) return f;
            f.Permadeath = h.playerData.permadeathMode; f.MapZone = m.GetCurrentMapZone();
            f.Gameplay = m.IsGameplayScene(); f.Playing = m.gameState == GameState.PLAYING; f.Paused = m.isPaused;
            f.InPosition = h.isHeroInPosition; f.WaitingToTransition = h.transitionState == HeroTransitionState.WAITING_TO_TRANSITION;
            f.Dead = h.cState.dead; f.Hazard = h.cState.hazardDeath || h.cState.hazardRespawning; f.Transitioning = h.cState.transitioning;
            f.AcceptingInput = h.CanInput(); f.ControlRelinquished = h.controlReqlinquished;
            f.TargetsAvailable = runtime.DeathTargetsAvailable(h, hud);
            return f;
        }
#endif
    }
}
