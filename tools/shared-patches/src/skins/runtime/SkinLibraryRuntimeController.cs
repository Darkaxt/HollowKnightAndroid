using System;
using System.Collections.Generic;

namespace DualSouls.Skins.Runtime
{
    public sealed class SkinLibraryRequest
    {
        public string ProfileId, ConfigSha256, Mode, PackId, TreeSha256, Root, RotationRun, RotationDetail;
        public long LastDeath, PendingOccurrence;
        public IDictionary<string, string> Textures;
    }
    public sealed class SkinLibraryObservation
    {
        public string ConfigSha256, ActivePackId, ActiveTreeSha256, Status, Detail, RotationRun;
        public long PendingOccurrence;
    }

    // Kotlin alone chooses/commits successors. Live admission only gates the frozen candidate.
    public sealed class SkinLibraryRuntimeController
    {
        readonly SkinRuntimeRules rules;
        readonly Func<SkinLibraryRequest> read;
        readonly Func<SkinPack, SkinApplyResult> apply;
        readonly Func<SkinApplyResult> restore;
        readonly Action<SkinLibraryObservation> report;
        readonly Func<SkinApplyResult> observe;
        readonly Func<SkinLibraryRequest, bool> ready;
        SkinPack cached, cachedRotation;
        string cachedTree, activeId, activeTree, activeMode, preparedRun;
        long preparedOccurrence;
        bool restored, restoreRequired, pendingRestored, awaitingApply;
        public SkinLibraryRuntimeController(SkinRuntimeRules rules, Func<SkinLibraryRequest> read,
            Func<SkinPack, SkinApplyResult> apply, Func<SkinApplyResult> restore,
            Action<SkinLibraryObservation> report, Func<SkinApplyResult> observe = null,
            Func<SkinLibraryRequest, bool> ready = null)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.read = read; this.apply = apply; this.restore = restore; this.report = report;
            this.observe = observe; this.ready = ready;
        }

        public void Tick()
        {
            SkinLibraryRequest request = null;
            try
            {
                request = read();
                if (request == null) return; // nonblocking Kotlin lock was busy; next poll retries
                if (request.ProfileId != rules.ProfileId || !Digest(request.ConfigSha256) ||
                    (request.Mode != "OFF" && request.Mode != "ON" && request.Mode != "ROTATE"))
                    throw new InvalidOperationException("Invalid launched-profile skin configuration.");
                if (request.PendingOccurrence == 0 || request.PendingOccurrence != preparedOccurrence ||
                    !string.Equals(request.RotationRun, preparedRun, StringComparison.Ordinal))
                {
                    pendingRestored = false;
                    preparedRun = request.RotationRun;
                    preparedOccurrence = request.PendingOccurrence;
                }
                SkinApplyResult result;
                if (request.Mode == "OFF")
                {
                    result = restored ? new SkinApplyResult(SkinApplyStatus.Restored) : restore();
                    if (result.Status == SkinApplyStatus.Restored || result.Status == SkinApplyStatus.Unchanged)
                    { restored = true; activeId = activeTree = null; }
                }
                else if (request.PendingOccurrence > 0 && !(ready?.Invoke(request) ?? false))
                    result = new SkinApplyResult(SkinApplyStatus.AwaitingTargets, "Frozen successor awaits live stable respawn.");
                else if (request.PendingOccurrence > 0 && rules.RestoreBeforeRotation && !pendingRestored)
                {
                    restored = false; activeMode = null;
                    result = restore();
                    if (result.Status == SkinApplyStatus.Restored || result.Status == SkinApplyStatus.Unchanged)
                    {
                        activeId = activeTree = null;
                        restoreRequired = false;
                        pendingRestored = true;
                        result = ApplySelected(request);
                    }
                }
                else if (request.PendingOccurrence > 0 && restoreRequired)
                {
                    restored = false; activeMode = null;
                    result = restore();
                    if (result.Status == SkinApplyStatus.Restored || result.Status == SkinApplyStatus.Unchanged)
                    {
                        activeId = activeTree = null; restoreRequired = false;
                        result = new SkinApplyResult(SkinApplyStatus.AwaitingTargets, "Restoration recovered; retry the frozen successor next poll.");
                    }
                }
                else result = ApplySelected(request);
                if (result.Status == SkinApplyStatus.RestoreFailed || result.Status == SkinApplyStatus.Blocked) restoreRequired = true;
                if (request.Mode == "OFF" && restored) restoreRequired = false;
                var detail = result.Detail;
                if (request.Mode == "ROTATE" && request.PendingOccurrence == 0 && !string.IsNullOrEmpty(request.RotationDetail))
                    detail = (detail ?? "") + " " + request.RotationDetail;
                Publish(request, result.Status.ToString(), detail);
            }
            catch (Exception error)
            {
                Publish(request, "Failed", error.Message);
            }
        }
        SkinApplyResult ApplySelected(SkinLibraryRequest request)
        {
            if (string.IsNullOrEmpty(request.PackId) || !Digest(request.TreeSha256) || request.Textures == null ||
                request.Textures.Count < 1 || request.Textures.Count > rules.MappingLimit)
                throw new InvalidOperationException("Selected skin is unavailable or exceeds the launched profile bound.");
            if (cached == null || cached.Id != request.PackId || cachedTree != request.TreeSha256 || cached.Root != request.Root)
            { cached = new SkinPack(request.PackId, request.Root, request.Textures); cachedRotation = null; cachedTree = request.TreeSha256; }
            if (request.Mode == "ROTATE" && cachedRotation == null)
                cachedRotation = new SkinPack(request.PackId, request.Root, request.Textures, "ROTATE");
            SkinApplyResult result;
            bool directApply = !(activeId == request.PackId && activeTree == request.TreeSha256 &&
                activeMode == request.Mode && !restored && !awaitingApply);
            if (!directApply)
                result = observe?.Invoke() ?? new SkinApplyResult(SkinApplyStatus.Unchanged);
            else
            {
                // Apply may disturb originals even if it fails or throws before returning.
                restored = false; activeMode = null;
                result = apply(request.Mode == "ROTATE" ? cachedRotation : cached);
            }
            if (directApply) awaitingApply = result.Status == SkinApplyStatus.AwaitingTargets;
            if (result.Status == SkinApplyStatus.Applied || result.Status == SkinApplyStatus.Unchanged)
            {
                awaitingApply = false; restored = false; activeId = request.PackId; activeTree = request.TreeSha256; activeMode = request.Mode; }
            return result;
        }

        void Publish(SkinLibraryRequest request, string status, string detail)
        {
            try { report(new SkinLibraryObservation { ConfigSha256 = request?.ConfigSha256, ActivePackId = activeId,
                RotationRun = request?.RotationRun, PendingOccurrence = request?.PendingOccurrence ?? 0,
                ActiveTreeSha256 = activeTree, Status = status, Detail = (detail ?? "").Length > 1024 ? detail.Substring(0, 1024) : detail ?? "" }); }
            catch { /* Reporting never changes visual/configuration authority; next poll reports again. */ }
        }
        static bool Digest(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
    }
}
