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
        readonly Func<string, long, bool> confirm;
        readonly Func<string, bool> cancel;
        readonly Func<bool> readyToPoll;
        float nextPoll;
        bool pending, settled, disposed;
        public bool CanRefresh => !disposed && (!pending || death.Ready);

        public HollowKnightSkinLibrary(Func<SkinLibraryRequest> read, Func<SkinPack, SkinApplyResult> apply,
            Func<SkinApplyResult> restore, Func<SkinLibraryObservation, bool> report, Func<SkinApplyResult> observe,
            HollowKnightSkinDeathAdapter death, Func<string, long, bool> confirm, Func<string, bool> cancel,
            Func<bool> readyToPoll = null)
        {
            this.read = read; this.report = report; this.death = death; this.confirm = confirm; this.cancel = cancel;
            this.readyToPoll = readyToPoll ?? (() => true);
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
            death.Tick(); // actual owner/event/frame sampling must precede the one-second JNI throttle
            if (settled && death.Occurrence == 0 && death.CancellationRun == null) return;
            if (!readyToPoll()) return;
            if (now < nextPoll) return;
            nextPoll = now + 1f;
            controller.Tick();
        }
        SkinLibraryRequest ReadCurrent()
        {
            var request = read();
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
                if (request.ProfileId != "hollow-knight") { death.Cancel(); RetryCancellation(); return false; }
                death.Configure(request.Mode, request.RotationRun, request.LastDeath, request.PendingOccurrence);
                pending = request.PendingOccurrence > 0;
            }
            if (death.CancellationRun != null) { RetryCancellation(); return false; }
            return request != null;
        }
        void RetryCancellation() { if (death.CancellationRun != null) cancel(death.CancellationRun); }
        void ReportCurrent(SkinLibraryObservation observation)
        {
            if (observation.PendingOccurrence > 0 && (observation.Status == "Applied" || observation.Status == "Unchanged") &&
                !Ready(observation.RotationRun, observation.PendingOccurrence))
            {
                observation.Status = "AwaitingTargets";
                observation.Detail = "Frozen successor awaits live stable respawn.";
            }
            bool completes = observation.PendingOccurrence > 0 && (observation.Status == "Applied" || observation.Status == "Unchanged");
            string run = observation.RotationRun; long occurrence = observation.PendingOccurrence;
            // Consume only the matching accepted rotation commit, not ordinary status/reportResult success.
            // Clear now so a real death before the next poll is not masked; false/busy stays frozen.
            bool accepted = report(observation);
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
        public void Invalidate()
        {
            if (disposed) return;
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
            if (runtime != null) runtime.RefreshAllowed = () => false; // retired owner cannot refresh orphan work
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
            death = new HollowKnightSkinDeathAdapter(runtime);
            read = ReadManaged; report = ReportManaged;
            confirm = (run, occurrence) => Bridge.CallStatic<bool>("confirmDeath", run, occurrence);
            cancel = run => Bridge.CallStatic<bool>("cancelRotation", run);
            controller = CreateController(pack => runtime.TryApply(pack), runtime.TryRestore, () => runtime.LastResult);
            runtime.RefreshAllowed = () => CanRefresh;
        }
        public void Tick() => Tick(Time.unscaledTime);
        AndroidJavaClass Bridge => bridge ?? (bridge = new AndroidJavaClass("dev.silksong.launcher.runtime.SkinLibraryRuntimeBridge"));
        SkinLibraryRequest ReadManaged()
        {
            string json = Bridge.CallStatic<string>("readConfiguration");
            if (json == null || json.Length > 262144) throw new InvalidOperationException("Invalid skin configuration transport length.");
            var wire = JsonUtility.FromJson<WireRequest>(json);
            if (wire == null) throw new InvalidOperationException("Missing skin configuration response.");
            if (!wire.ok)
            {
                if (wire.code == "LIFECYCLE_BLOCKED") return null;
                if (wire.code == "PROFILE_REJECTED") { death.Cancel(); RetryCancellation(); }
                throw new InvalidOperationException(wire.code + ": " + wire.detail);
            }
            var textures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (wire.textures != null)
            {
                if (wire.textures.Length > 205) throw new InvalidOperationException("Skin mapping bound exceeded.");
                foreach (var texture in wire.textures) textures.Add(texture.target, texture.path);
            }
            return new SkinLibraryRequest { ProfileId = wire.profileId, ConfigSha256 = wire.configSha256, Mode = wire.mode,
                PackId = wire.packId, TreeSha256 = wire.treeSha256, Root = wire.root, Textures = textures,
                RotationRun = wire.rotationRun, LastDeath = wire.lastDeath, PendingOccurrence = wire.pendingOccurrence, RotationDetail = wire.rotationDetail };
        }
        bool ReportManaged(SkinLibraryObservation observation)
        {
            bool failed = observation.Status == "Failed" || observation.Status == "Rejected" ||
                observation.Status == "RestoreFailed" || observation.Status == "Blocked";
            string warning = failed ? observation.Status + ": " + observation.Detail : null;
            if (warning != null && warning != lastWarning) Debug.LogWarning("[HK skins library] " + warning);
            lastWarning = warning;
            if (observation.PendingOccurrence > 0 && (observation.Status == "Applied" || observation.Status == "Unchanged"))
                return Bridge.CallStatic<bool>("reportRotation", observation.ConfigSha256 ?? "", observation.RotationRun ?? "",
                    observation.PendingOccurrence, observation.ActivePackId ?? "", observation.ActiveTreeSha256 ?? "", observation.Status, observation.Detail ?? "");
            return Bridge.CallStatic<bool>("reportResult", observation.ConfigSha256 ?? "", observation.ActivePackId ?? "",
                observation.ActiveTreeSha256 ?? "", observation.Status, observation.Detail ?? "");
        }
        // JsonUtility fills these fields from Kotlin's bounded transport, not C# assignments.
#pragma warning disable CS0649
        [Serializable] sealed class WireTexture { public string target, path; }
        [Serializable] sealed class WireRequest
        {
            public bool ok;
            public string code, detail, profileId, configSha256, mode, packId, treeSha256, root, rotationRun, rotationDetail;
            public long lastDeath, pendingOccurrence;
            public WireTexture[] textures;
        }
#pragma warning restore CS0649
#endif
    }
}
