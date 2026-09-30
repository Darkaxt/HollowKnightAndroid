using System;
using DualSouls.Skins.Runtime;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
#endif

namespace DualSouls.Skins.HollowKnight.Runtime
{
    // Kotlin owns selection and disk writes; this owner only transports one actual death lifecycle.
    public sealed class HollowKnightSkinLibrary : IDisposable
    {
        readonly SkinLibraryRuntimeController controller;
        readonly HollowKnightSkinDeathAdapter death;
        readonly Func<SkinLibraryRequest> read;
        readonly Func<SkinLibraryObservation, bool> report;
        readonly Func<SkinApplyResult> observe;
        readonly Func<string, long, bool> confirm;
        readonly Func<string, bool> cancel;
        readonly Func<bool> readyToPoll;
        readonly Func<int> saveIdentity;
        int saveSlot = SkinSaveIdentity.Unbound;
        bool saveChanged, saveVisualPending;
        public bool SaveVisualRefreshAllowed => !disposed && !saveVisualPending;
        public const float RecoveryTimeoutSeconds = 60f;
        float workDeadline = float.PositiveInfinity;
        public bool RecoveryRequired { get; private set; }
        float nextPoll;
        bool pending, settled, disposed;
        SkinLibraryObservation acceptedObservation, retryRotation;
        public bool CanRefresh => !disposed && !RecoveryRequired && !saveChanged && (!pending || death.Ready);

        public HollowKnightSkinLibrary(Func<SkinLibraryRequest> read, Func<SkinPack, SkinApplyResult> apply,
            Func<SkinApplyResult> restore, Func<SkinLibraryObservation, bool> report, Func<SkinApplyResult> observe,
            HollowKnightSkinDeathAdapter death, Func<string, long, bool> confirm, Func<string, bool> cancel,
            Func<bool> readyToPoll = null, Func<int> saveIdentity = null)
        {
            this.read = read; this.report = report; this.observe = observe; this.death = death; this.confirm = confirm; this.cancel = cancel;
            this.readyToPoll = readyToPoll ?? (() => true);
            this.saveIdentity = saveIdentity;
            controller = CreateController(apply, restore, observe);
        }
        SkinLibraryRuntimeController CreateController(Func<SkinPack, SkinApplyResult> apply,
            Func<SkinApplyResult> restore, Func<SkinApplyResult> observe) =>
            new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules, ReadCurrent, apply, restore, ReportCurrent, observe,
                request => request.Mode == "ROTATE" && Ready(request.RotationRun, request.PendingOccurrence));
        bool Ready(string run, long occurrence) => run == death.Run && occurrence == death.Occurrence && death.Recorded && death.Ready;
        public void Tick(float now)
        {
            if (disposed) return;
            AdmitSaveBoundary();
            if (RecoveryRequired) return;
            death.Tick(); // actual owner/event/frame sampling must precede the one-second JNI throttle
            if (settled && death.Occurrence == 0 && death.CancellationRun == null) { workDeadline = float.PositiveInfinity; return; }
            if (float.IsPositiveInfinity(workDeadline)) workDeadline = now + RecoveryTimeoutSeconds;
            if (now >= workDeadline)
            {
                controller.CancelPending("Skin operation timed out; change configuration or re-enter the loaded save to retry.");
                death.Configure("OFF", null);
                pending = false;
                retryRotation = null;
                RecoveryRequired = true;
                return;
            }
            if (!readyToPoll()) return;
            if (now < nextPoll) return;
            nextPoll = now + 1f;
            controller.Tick();
        }
        public void AdmitSaveBoundary()
        {
            if (disposed || saveIdentity == null) return;
            int current = saveIdentity();
            if (current == saveSlot) return;
            saveSlot = current;
            saveChanged = saveVisualPending = true;
            controller.InvalidateSave();
            death.Configure("OFF", null); // typed occurrence owner is retired before any new-slot work
            pending = false;
            retryRotation = null;
            Invalidate();
        }
        SkinLibraryRequest ReadCurrent()
        {
            var request = read();
            if (request != null && saveIdentity != null && request.SaveSlot != saveSlot) return null;
            // Keep one executed completion until Kotlin accepts its evidence, even if selection
            // already committed. Retired/cancelled authority must not block new configuration.
            if (retryRotation != null && request != null)
            {
                var retry = retryRotation;
                bool current = request.ProfileId == "hollow-knight" && request.Mode == "ROTATE" &&
                    request.RotationRun == retry.RotationRun && death.Run == retry.RotationRun &&
                    death.Occurrence == retry.PendingOccurrence && death.CancellationRun == null &&
                    (request.ConfigSha256 == retry.ConfigSha256 ||
                     (request.LastDeath == retry.PendingOccurrence && request.PendingOccurrence == 0 &&
                      request.PackId == retry.ActivePackId && request.TreeSha256 == retry.ActiveTreeSha256));
                if (!current || !RuntimeStillComplete(retry)) retryRotation = null;
                else
                {
                    if (Ready(retry.RotationRun, retry.PendingOccurrence)) ReportCurrent(retry);
                    return null; // accepted settles locally; false/throw remains retryable next tick
                }
            }
            if (!Consume(request)) return request != null && request.ProfileId != "hollow-knight" ? request : null;
            if (death.Occurrence > 0 && !death.Recorded)
            {
                if (!confirm(death.Run, death.Occurrence)) return null; // busy: retain the exact occurrence
                request = read(); // consume the Kotlin-frozen successor, never choose one locally
                if (!Consume(request)) return null;
            }
            return request;
        }
        bool Consume(SkinLibraryRequest request)
        {
            if (request != null)
            {
                if (saveIdentity != null && request.SaveSlot != saveSlot) return false;
                if (request.ProfileId != "hollow-knight") { death.Cancel(); RetryCancellation(); return false; }
                saveChanged = false;
                death.Configure(request.Mode, request.RotationRun, request.LastDeath, request.PendingOccurrence);
                pending = request.PendingOccurrence > 0;
            }
            if (death.CancellationRun != null) { RetryCancellation(); return false; }
            return request != null;
        }
        void RetryCancellation() { if (death.CancellationRun != null) cancel(death.CancellationRun); }
        void ReportCurrent(SkinLibraryObservation observation)
        {
            if (saveIdentity != null && observation.SaveSlot != saveIdentity()) return;
            if (observation.PendingOccurrence > 0 &&
                (observation.Status == "Applied" || observation.Status == "Unchanged" || observation.Status == "Restored") &&
                !Ready(observation.RotationRun, observation.PendingOccurrence))
            {
                observation.Status = "AwaitingTargets";
                observation.Detail = "Frozen successor awaits live stable respawn.";
            }
            if (observation.Status == "Applied" || observation.Status == "Unchanged" || observation.Status == "Restored")
                saveVisualPending = false; // actual current-save visuals, never selection or transport acceptance
            bool completes = observation.PendingOccurrence > 0 &&
                (observation.Status == "Applied" || observation.Status == "Unchanged" || observation.Status == "Restored");
            string run = observation.RotationRun; long occurrence = observation.PendingOccurrence;
            // Consume only the matching accepted rotation commit, not ordinary status/reportResult success.
            // Clear now so a real death before the next poll is not masked; false/busy stays frozen.
            if (completes) retryRotation = observation;
            bool accepted = SameObservation(acceptedObservation, observation) || report(observation);
            if (accepted)
            {
                acceptedObservation = observation;
                if (completes) retryRotation = null;
            }
            if (accepted && completes && run == death.Run && occurrence == death.Occurrence)
            {
                death.Configure("ROTATE", run, occurrence, 0);
                pending = false;
                settled = true;
            }
            else if (accepted && observation.PendingOccurrence == 0 &&
                     (observation.Status == "Applied" || observation.Status == "Unchanged" ||
                      observation.Status == "Restored"))
                settled = true;
        }
        bool RuntimeStillComplete(SkinLibraryObservation observation)
        {
            var current = observe?.Invoke();
            return current != null && (current.Status == SkinApplyStatus.Unchanged ||
                current.Status == (string.IsNullOrEmpty(observation.ActivePackId)
                    ? SkinApplyStatus.Restored : SkinApplyStatus.Applied));
        }
        static bool SameObservation(SkinLibraryObservation left, SkinLibraryObservation right) => left != null &&
            left.ConfigSha256 == right.ConfigSha256 && left.SaveSlot == right.SaveSlot && left.RotationRun == right.RotationRun &&
            left.PendingOccurrence == right.PendingOccurrence && left.ActivePackId == right.ActivePackId &&
            left.ActiveTreeSha256 == right.ActiveTreeSha256 && left.Status == right.Status && left.Detail == right.Detail;
        public void Invalidate()
        {
            if (disposed) return;
            acceptedObservation = null;
            workDeadline = float.PositiveInfinity;
            RecoveryRequired = false;
            settled = false;
            nextPoll = 0f;
        }
        public void Dispose()
        {
            if (disposed) return;
            death.Dispose(); disposed = true;
            try { RetryCancellation(); }
            catch (Exception) { /* Local cancellation is final; a new owner rejects any orphan pending work. */ }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (runtime != null) {
                runtime.SaveVisualRefreshAllowed = () => false;
                runtime.RefreshAllowed = () => false;
                runtime.RecoverySuspended = () => true;
                runtime.AuthorityEdge = null;
            } // retired owner cannot refresh orphan work
            var owned = bridge; bridge = null;
            try { owned?.Dispose(); }
            catch (Exception error) { Debug.LogWarning("[HK skins library] JNI transport disposal failed: " + error.Message); }
#endif
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        readonly HollowKnightSkinRuntime runtime;
        AndroidJavaClass bridge;
        string lastWarning;
        public HollowKnightSkinLibrary(HollowKnightSkinRuntime runtime)
        {
            this.runtime = runtime;
            readyToPoll = () => runtime.TargetsReady;
            var identity = new SkinSaveIdentity();
            saveIdentity = () => {
                var manager = GameManager.UnsafeInstance;
                var hero = HeroController.UnsafeInstance;
                return identity.Sample(manager != null ? manager : null, manager != null ? manager.profileID : -1,
                    manager != null && manager.gameState == GlobalEnums.GameState.PLAYING && hero != null && hero.isHeroInPosition,
                    manager != null && (manager.gameState == GlobalEnums.GameState.MAIN_MENU || manager.gameState == GlobalEnums.GameState.INACTIVE));
            };
            death = new HollowKnightSkinDeathAdapter(runtime);
            read = ReadManaged; report = ReportManaged; observe = () => runtime.LastResult;
            confirm = (run, occurrence) => Bridge.CallStatic<bool>("confirmDeath", run, occurrence);
            cancel = run => Bridge.CallStatic<bool>("cancelRotation", run);
            controller = CreateController(pack => runtime.TryApply(pack), runtime.TryRestore, () => runtime.LastResult);
            runtime.SaveVisualRefreshAllowed = () => SaveVisualRefreshAllowed;
            runtime.RefreshAllowed = () => CanRefresh;
            runtime.RecoverySuspended = () => RecoveryRequired;
            runtime.AuthorityEdge = Invalidate;
        }
        public void Tick() => Tick(Time.unscaledTime);
        AndroidJavaClass Bridge => bridge ?? (bridge = new AndroidJavaClass("dev.silksong.launcher.runtime.SkinLibraryRuntimeBridge"));
        SkinLibraryRequest ReadManaged() => SkinLibraryTransport.Decode(
            Bridge.CallStatic<string>("readConfiguration", saveSlot), HollowKnightSkinPolicy.RuntimeRules);

        bool ReportManaged(SkinLibraryObservation observation)
        {
            bool failed = observation.Status == "Failed" || observation.Status == "Rejected" ||
                observation.Status == "RestoreFailed" || observation.Status == "Blocked";
            string warning = failed ? observation.Status : null; // Detail is redacted only by the Kotlin evidence writer.
            if (warning != null && warning != lastWarning) Debug.LogWarning("[HK skins library] " + warning);
            lastWarning = warning;
            if (observation.PendingOccurrence > 0)
                return Bridge.CallStatic<bool>("reportRotation", observation.ConfigSha256 ?? "", observation.RotationRun ?? "",
                    observation.PendingOccurrence, observation.ActivePackId ?? "", observation.ActiveTreeSha256 ?? "", observation.Status, observation.Detail ?? "", observation.SaveSlot);
            return Bridge.CallStatic<bool>("reportResult", observation.ConfigSha256 ?? "", observation.ActivePackId ?? "",
                observation.ActiveTreeSha256 ?? "", observation.Status, observation.Detail ?? "", observation.SaveSlot);
        }

#endif
    }
}
