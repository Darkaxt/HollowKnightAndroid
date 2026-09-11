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
        readonly Func<string, long, bool> confirm;
        readonly Func<string, long, bool> cancelOccurrence;
        readonly Func<string, bool> cancel;
        float nextPoll;
        bool pending, disposed;
#if UNITY_ANDROID && !UNITY_EDITOR
        readonly SilksongSkinRuntime runtime;
        AndroidJavaClass bridge;
        string lastWarning;
#endif

        public bool CanRefresh => !disposed && (!pending || death.Ready);

        public SilksongSkinLibrary(Func<SkinLibraryRequest> read, Func<SkinPack, SkinApplyResult> apply,
            Func<SkinApplyResult> restore, Func<SkinLibraryObservation, bool> report,
            Func<SkinApplyResult> observe, SilksongSkinDeathAdapter death,
            Func<string, long, bool> confirm, Func<string, long, bool> cancelOccurrence,
            Func<string, bool> cancel)
        {
            this.read = read ?? throw new ArgumentNullException(nameof(read));
            this.report = report ?? throw new ArgumentNullException(nameof(report));
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
            death = new SilksongSkinDeathAdapter(runtime);
            read = ReadManaged;
            report = ReportManaged;
            confirm = (run, occurrence) => Bridge.CallStatic<bool>("confirmDeath", run, occurrence);
            cancelOccurrence = (run, occurrence) => Bridge.CallStatic<bool>("cancelDeath", run, occurrence);
            cancel = run => Bridge.CallStatic<bool>("cancelRotation", run);
            controller = new SkinLibraryRuntimeController(SilksongSkinTargets.RuntimeRules,
                ReadCurrent, pack => runtime.TryApply(pack), runtime.TryRestore, ReportCurrent,
                () => runtime.LastResult,
                request => request.Mode == "ROTATE" && Ready(request.RotationRun, request.PendingOccurrence));
            runtime.RefreshAllowed = () => CanRefresh;
        }

        public void Tick() => Tick(Time.unscaledTime);
        AndroidJavaClass Bridge => bridge ?? (bridge = new AndroidJavaClass(
            "dev.silksong.launcher.runtime.SkinLibraryRuntimeBridge"));

        SkinLibraryRequest ReadManaged()
        {
            string json = Bridge.CallStatic<string>("readConfiguration");
            if (json == null || json.Length > 262144)
                throw new InvalidOperationException("Invalid Silksong skin configuration transport length.");
            var wire = JsonUtility.FromJson<WireRequest>(json);
            if (wire == null) throw new InvalidOperationException("Missing Silksong skin configuration response.");
            if (!wire.ok)
            {
                if (wire.code == "LIFECYCLE_BLOCKED") return null;
                throw new InvalidOperationException(wire.code + ": " + wire.detail);
            }
            var textures = new Dictionary<string, string>(StringComparer.Ordinal);
            if (wire.textures != null)
            {
                if (wire.textures.Length > SilksongSkinTargets.RuntimeRules.MappingLimit)
                    throw new InvalidOperationException("Silksong skin mapping bound exceeded.");
                foreach (var texture in wire.textures) textures.Add(texture.target, texture.path);
            }
            return new SkinLibraryRequest { ProfileId = wire.profileId, ConfigSha256 = wire.configSha256,
                Mode = wire.mode, PackId = wire.packId, TreeSha256 = wire.treeSha256, Root = wire.root,
                Textures = textures, RotationRun = wire.rotationRun, LastDeath = wire.lastDeath,
                PendingOccurrence = wire.pendingOccurrence, RotationDetail = wire.rotationDetail };
        }

        bool ReportManaged(SkinLibraryObservation observation)
        {
            bool failed = observation.Status == "Failed" || observation.Status == "Rejected" ||
                observation.Status == "RestoreFailed" || observation.Status == "Blocked";
            string warning = failed ? observation.Status + ": " + observation.Detail : null;
            if (warning != null && warning != lastWarning)
                Debug.LogWarning("[Silksong skins library] " + warning);
            lastWarning = warning;
            if (observation.PendingOccurrence > 0 &&
                (observation.Status == "Applied" || observation.Status == "Unchanged"))
                return Bridge.CallStatic<bool>("reportRotation", observation.ConfigSha256 ?? "",
                    observation.RotationRun ?? "", observation.PendingOccurrence,
                    observation.ActivePackId ?? "", observation.ActiveTreeSha256 ?? "",
                    observation.Status, observation.Detail ?? "");
            return Bridge.CallStatic<bool>("reportResult", observation.ConfigSha256 ?? "",
                observation.ActivePackId ?? "", observation.ActiveTreeSha256 ?? "",
                observation.Status, observation.Detail ?? "");
        }

#pragma warning disable CS0649
        [Serializable] sealed class WireTexture { public string target, path; }
        [Serializable] sealed class WireRequest
        {
            public bool ok;
            public string code, detail, profileId, configSha256, mode, packId, treeSha256, root,
                rotationRun, rotationDetail;
            public long lastDeath, pendingOccurrence;
            public WireTexture[] textures;
        }
#pragma warning restore CS0649
#endif

        bool Ready(string run, long occurrence) => run == death.Run && occurrence == death.Occurrence &&
            death.Recorded && death.Ready;

        public void Tick(float now)
        {
            if (disposed) return;
            death.Tick();
            if (death.BacklogFaulted)
            {
                string run = death.Run;
                if (run != null && cancel(run))
                {
                    death.Configure("OFF", null);
                    pending = false;
                }
                return;
            }
            if (now < nextPoll) return;
            nextPoll = now + 1f;
            controller.Tick();
        }

        SkinLibraryRequest ReadCurrent()
        {
            var request = read();
            if (request == null || request.ProfileId != SilksongSkinTargets.RuntimeRules.ProfileId) return request;
            bool changed = false;
            var cancellations = death.PendingCancellations;
            if (cancellations.Count > 0 && request.RotationRun != death.Run) return null;
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
                if (!confirm(death.Run, occurrence)) return null;
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
            if (request == null) return false;
            if (request.ProfileId != SilksongSkinTargets.RuntimeRules.ProfileId) return false;
            death.Configure(request.Mode, request.RotationRun, request.LastDeath, request.PendingOccurrence);
            pending = request.PendingOccurrence > 0;
            return true;
        }

        void ReportCurrent(SkinLibraryObservation observation)
        {
            bool completes = observation.PendingOccurrence > 0 &&
                (observation.Status == "Applied" || observation.Status == "Unchanged");
            string run = observation.RotationRun;
            long occurrence = observation.PendingOccurrence;
            if (report(observation) && completes && run == death.Run && occurrence == death.Occurrence)
            {
                death.Configure("ROTATE", run, occurrence, 0);
                pending = false;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            string run = death.Run;
            bool unfinished = death.Occurrence > 0;
            death.Dispose();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (runtime != null) runtime.RefreshAllowed = () => false;
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
