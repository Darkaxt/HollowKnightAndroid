using System;
using DualSouls.Skins.Runtime;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
#endif

namespace DualSouls.Skins.Silksong.Runtime
{
    // Kotlin owns the immutable selection and successor. This owner transports only the
    // exact managed Die bridge occurrence and stable-respawn gate.
    public sealed class SilksongSkinLibrary : IDisposable
    {
        readonly SkinLibraryRuntimeController controller;
        readonly SilksongSkinDeathAdapter death;
        readonly Func<SkinLibraryRequest> read;
        readonly Func<SkinLibraryObservation, bool> report;
        readonly Func<SkinApplyResult> observe;
        readonly Func<string, long, bool> confirm;
        readonly Func<string, long, bool> cancelOccurrence;
        readonly Func<string, bool> cancel;
        readonly Func<int> saveIdentity;
        readonly System.Collections.Generic.HashSet<long> confirmedOccurrences = new System.Collections.Generic.HashSet<long>();
        string confirmedRun;
        int saveSlot = SkinSaveIdentity.Unbound;
        bool saveChanged, saveVisualPending, backlogFaultSeen;
        public bool SaveVisualRefreshAllowed => !disposed && !saveVisualPending;
        public const float RecoveryTimeoutSeconds = 60f;
        float workDeadline = float.PositiveInfinity;
        public bool RecoveryRequired { get; private set; }
        float nextPoll;
        bool pending, settled, disposed;
        SkinLibraryObservation acceptedObservation, retryRotation;
#if UNITY_ANDROID && !UNITY_EDITOR
        readonly SilksongSkinRuntime runtime;
        AndroidJavaClass bridge;
        string lastWarning;
#endif

        public bool CanRefresh => !disposed && !RecoveryRequired && !saveChanged && (!pending || death.Ready);

        public SilksongSkinLibrary(Func<SkinLibraryRequest> read, Func<SkinPack, SkinApplyResult> apply,
            Func<SkinApplyResult> restore, Func<SkinLibraryObservation, bool> report,
            Func<SkinApplyResult> observe, SilksongSkinDeathAdapter death,
            Func<string, long, bool> confirm, Func<string, long, bool> cancelOccurrence,
            Func<string, bool> cancel, Func<int> saveIdentity = null)
        {
            this.read = read ?? throw new ArgumentNullException(nameof(read));
            this.report = report ?? throw new ArgumentNullException(nameof(report));
            this.observe = observe;
            this.saveIdentity = saveIdentity;
            this.death = death ?? throw new ArgumentNullException(nameof(death));
            this.confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));
            this.cancelOccurrence = cancelOccurrence ?? throw new ArgumentNullException(nameof(cancelOccurrence));
            this.cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));
            controller = new SkinLibraryRuntimeController(SilksongSkinTargets.RuntimeRules,
                ReadCurrent, apply, restore, ReportCurrent, observe,
                request => request.Mode == "ROTATE" && Ready(request.RotationRun, request.PendingOccurrence));
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        public SilksongSkinLibrary(SilksongSkinRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            var identity = new SkinSaveIdentity();
            saveIdentity = () => {
                var manager = GameManager.SilentInstance;
                var hero = HeroController.SilentInstance;
                return identity.Sample(manager != null ? manager : null, manager != null ? manager.profileID : -1,
                    manager != null && manager.GameState == GlobalEnums.GameState.PLAYING && hero != null && hero.isHeroInPosition,
                    manager != null && (manager.GameState == GlobalEnums.GameState.MAIN_MENU || manager.GameState == GlobalEnums.GameState.INACTIVE));
            };
            death = new SilksongSkinDeathAdapter(runtime);
            read = ReadManaged;
            report = ReportManaged;
            observe = () => runtime.LastResult;
            confirm = (run, occurrence) => Bridge.CallStatic<bool>("confirmDeath", run, occurrence);
            cancelOccurrence = (run, occurrence) => Bridge.CallStatic<bool>("cancelDeath", run, occurrence);
            cancel = run => Bridge.CallStatic<bool>("cancelRotation", run);
            controller = new SkinLibraryRuntimeController(SilksongSkinTargets.RuntimeRules,
                ReadCurrent, pack => runtime.TryApply(pack), runtime.TryRestore, ReportCurrent,
                () => runtime.LastResult,
                request => request.Mode == "ROTATE" && Ready(request.RotationRun, request.PendingOccurrence));
            runtime.SaveVisualRefreshAllowed = () => SaveVisualRefreshAllowed;
            runtime.RefreshAllowed = () => CanRefresh;
            runtime.RecoverySuspended = () => RecoveryRequired;
            runtime.AuthorityEdge = Invalidate;
        }

        public void Tick() => Tick(Time.unscaledTime);
        AndroidJavaClass Bridge => bridge ?? (bridge = new AndroidJavaClass(
            "dev.silksong.launcher.runtime.SkinLibraryRuntimeBridge"));

        SkinLibraryRequest ReadManaged() => SkinLibraryTransport.Decode(
            Bridge.CallStatic<string>("readConfiguration", saveSlot), SilksongSkinTargets.RuntimeRules);

        bool ReportManaged(SkinLibraryObservation observation)
        {
            bool failed = observation.Status == "Failed" || observation.Status == "Rejected" ||
                observation.Status == "RestoreFailed" || observation.Status == "Blocked";
            string warning = failed ? observation.Status : null; // Detail is redacted only by the Kotlin evidence writer.
            if (warning != null && warning != lastWarning)
                Debug.LogWarning("[Silksong skins library] " + warning);
            lastWarning = warning;
            if (observation.PendingOccurrence > 0)
                return Bridge.CallStatic<bool>("reportRotation", observation.ConfigSha256 ?? "",
                    observation.RotationRun ?? "", observation.PendingOccurrence,
                    observation.ActivePackId ?? "", observation.ActiveTreeSha256 ?? "",
                    observation.Status, observation.Detail ?? "", observation.SaveSlot);
            return Bridge.CallStatic<bool>("reportResult", observation.ConfigSha256 ?? "",
                observation.ActivePackId ?? "", observation.ActiveTreeSha256 ?? "",
                observation.Status, observation.Detail ?? "", observation.SaveSlot);
        }


#endif

        bool Ready(string run, long occurrence) => run == death.Run && occurrence == death.Occurrence &&
            death.Recorded && death.Ready;

        public void Tick(float now)
        {
            if (disposed) return;
            AdmitSaveBoundary();
            if (RecoveryRequired) return;
            death.Tick();
            if (death.BacklogFaulted && !backlogFaultSeen) { backlogFaultSeen = true; nextPoll = now; }
            if (settled && !death.BacklogFaulted && death.Occurrence == 0 && death.PendingCancellationCount == 0) { workDeadline = float.PositiveInfinity; return; }
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
            if (now < nextPoll) return;
            nextPoll = now + 1f;
            if (death.BacklogFaulted)
            {
                controller.CancelPending("Typed death backlog overflow or missing occurrence; rotation cancelled.");
                string run = death.Run;
                if (run != null && cancel(run))
                {
                    death.Configure("OFF", null);
                    pending = settled = false;
                    workDeadline = float.PositiveInfinity;
                    nextPoll = 0f;
                    backlogFaultSeen = false;
                    acceptedObservation = retryRotation = null;
                }
                return;
            }
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
            death.Configure("OFF", null);
            pending = false;
            retryRotation = null;
            Invalidate();
        }
        SkinLibraryRequest ReadCurrent()
        {
            var request = read();
            if (request != null && saveIdentity != null && request.SaveSlot != saveSlot) return null;
            // Replay only the latest executed completion before consuming a possibly committed
            // successor. Retired/cancelled authority releases it so new work cannot wedge.
            if (retryRotation != null && request != null)
            {
                var retry = retryRotation;
                bool current = request.ProfileId == SilksongSkinTargets.RuntimeRules.ProfileId && request.Mode == "ROTATE" &&
                    request.RotationRun == retry.RotationRun && death.Run == retry.RotationRun &&
                    death.Occurrence == retry.PendingOccurrence && death.PendingCancellationCount == 0 &&
                    (request.ConfigSha256 == retry.ConfigSha256 ||
                     (request.LastDeath == retry.PendingOccurrence && request.PendingOccurrence == 0 &&
                      request.PackId == retry.ActivePackId && request.TreeSha256 == retry.ActiveTreeSha256) ||
                     request.PendingOccurrence > retry.PendingOccurrence);
                if (!current || !RuntimeStillComplete(retry)) retryRotation = null;
                else
                {
                    if (!Ready(retry.RotationRun, retry.PendingOccurrence)) return null;
                    ReportCurrent(retry);
                    if (retryRotation == null || request.PendingOccurrence <= retry.PendingOccurrence) return null;
                    // New pending authority supersedes a rejected old completion. Consume the
                    // retired local occurrence before admitting the newer owner-bound one.
                    retryRotation = null;
                    death.Configure("ROTATE", retry.RotationRun, retry.PendingOccurrence, 0);
                }
            }
            if (request == null || request.ProfileId != SilksongSkinTargets.RuntimeRules.ProfileId) return request;
            bool changed = false;
            var cancellations = death.PendingCancellations;
            if (cancellations.Count > 0 && request.RotationRun != death.Run)
            {
                if (!Consume(request)) return null;
                return request;
            }
            foreach (long occurrence in cancellations)
            {
                if (!cancelOccurrence(death.Run, occurrence)) return null;
                if (!death.AcknowledgeCancellation(occurrence))
                    throw new InvalidOperationException("Silksong death cancellation order changed.");
                changed = true;
            }
            if (changed)
            {
                request = read();
                if (request == null || request.ProfileId != SilksongSkinTargets.RuntimeRules.ProfileId) return null;
            }
            if (!Consume(request)) return null;
            var occurrences = death.PendingBridgeOccurrences;
            foreach (long occurrence in occurrences)
            {
                if (confirmedOccurrences.Contains(occurrence)) continue;
                if (!confirm(death.Run, occurrence)) return null;
                confirmedOccurrences.Add(occurrence);
                changed = true;
            }
            if (changed)
            {
                request = read();
                if (!Consume(request)) return null;
            }
            return request;
        }

        bool Consume(SkinLibraryRequest request)
        {
            if (request == null || (saveIdentity != null && request.SaveSlot != saveSlot)) return false;
            if (request.ProfileId != SilksongSkinTargets.RuntimeRules.ProfileId) return false;
            saveChanged = false;
            if (confirmedRun != request.RotationRun) { confirmedOccurrences.Clear(); confirmedRun = request.RotationRun; }
            confirmedOccurrences.RemoveWhere(occurrence => occurrence <= request.LastDeath);
            death.Configure(request.Mode, request.RotationRun, request.LastDeath, request.PendingOccurrence);
            pending = request.PendingOccurrence > 0;
            return true;
        }

        void ReportCurrent(SkinLibraryObservation observation)
        {
            if (saveIdentity != null && observation.SaveSlot != saveIdentity()) return;
            if (observation.Status == "Applied" || observation.Status == "Unchanged" || observation.Status == "Restored")
                saveVisualPending = false; // actual current-save visuals, not queued intent
            bool completes = observation.PendingOccurrence > 0 &&
                (observation.Status == "Applied" || observation.Status == "Unchanged" || observation.Status == "Restored");
            string run = observation.RotationRun;
            long occurrence = observation.PendingOccurrence;
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
                     (observation.Status == "Applied" || observation.Status == "Unchanged" || observation.Status == "Restored"))
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
            disposed = true;
            string run = death.Run;
            bool unfinished = death.Occurrence > 0;
            death.Dispose();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (runtime != null) {
                runtime.SaveVisualRefreshAllowed = () => false;
                runtime.RefreshAllowed = () => false;
                runtime.RecoverySuspended = () => true;
                runtime.AuthorityEdge = null;
            }
            var ownedBridge = bridge;
            bridge = null;
            try { ownedBridge?.Dispose(); }
            catch (Exception error) { Debug.LogWarning("[Silksong skins library] JNI disposal failed: " + error.Message); }
#endif
            if (unfinished && run != null)
            {
                try { cancel(run); }
                catch (Exception) { /* Process teardown cannot transfer an orphan GPU transaction. */ }
            }
        }
    }
}
