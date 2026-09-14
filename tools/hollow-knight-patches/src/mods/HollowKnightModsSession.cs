using System;
using DualSouls.Mods;

namespace DualSouls.Mods.HollowKnight
{
    /// <summary>
    /// Hollow Knight compatibility surface over the shared process-session owner.
    /// Readiness remains the exact typed Hollow Knight API signal.
    /// </summary>
    public sealed class HollowKnightModsSession : ITweakTeardownSession
    {
        readonly TweakSession _session;

        public HollowKnightModsSession(
            IHollowKnightTweakApi api,
            ITweakStore store,
            int visibleRows)
        {
            if (api == null) throw new ArgumentNullException(nameof(api));
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (visibleRows <= 0) throw new ArgumentOutOfRangeException(nameof(visibleRows));

            _session = new TweakSession(
                () => api.IsReady,
                () => new HollowKnightTweakAdapter(api),
                store,
                visibleRows);
        }

        public bool IsReady => _session.IsReady;
        public bool RestorationPending => _session.RestorationPending;
        public bool TeardownComplete => _session.TeardownComplete;
        public string LastError => _session.LastError;
        public TweakController Controller => _session.Controller;
        public TweakMenuModel Menu => _session.Menu;

        public void Tick()
        {
            _session.Tick();
        }

        public void SetPresenterAttached(bool attached)
        {
            _session.SetPresenterAttached(attached);
        }

        public void Dispose()
        {
            _session.Dispose();
        }
    }

    internal enum HollowKnightModsCoveredSurface
    {
        FrameRoot,
        InterruptedSlide,
        MapPage,
        InventoryPage,
        CharmsPage,
        AreaName,
        EquipmentRow,
        NoMap,
        Notches,
        MapMasks,
        MapReset,
        Selection,
        DetachedPromptGlyph,
        DetachedPromptVerb,
        NativeHud,
    }

    internal enum HollowKnightModsRestoreDisposition
    {
        None,
        Deferred,
        Immediate,
    }

    internal static class HollowKnightModsPresentationFlow
    {
        static readonly HollowKnightModsCoveredSurface[] CoveredSurfaces =
        {
            HollowKnightModsCoveredSurface.FrameRoot,
            HollowKnightModsCoveredSurface.MapPage,
            HollowKnightModsCoveredSurface.InterruptedSlide,
            HollowKnightModsCoveredSurface.InventoryPage,
            HollowKnightModsCoveredSurface.CharmsPage,
            HollowKnightModsCoveredSurface.AreaName,
            HollowKnightModsCoveredSurface.EquipmentRow,
            HollowKnightModsCoveredSurface.NoMap,
            HollowKnightModsCoveredSurface.Notches,
            HollowKnightModsCoveredSurface.MapMasks,
            HollowKnightModsCoveredSurface.MapReset,
            HollowKnightModsCoveredSurface.Selection,
            HollowKnightModsCoveredSurface.DetachedPromptGlyph,
            HollowKnightModsCoveredSurface.DetachedPromptVerb,
            HollowKnightModsCoveredSurface.NativeHud,
        };

        public static int CoveredSurfaceCount => CoveredSurfaces.Length;

        public static HollowKnightModsCoveredSurface CoveredSurfaceAt(int index)
        {
            if (index < 0 || index >= CoveredSurfaces.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return CoveredSurfaces[index];
        }

        public static HollowKnightModsRestoreDisposition RequestCoveredContentRelease(
            TweakPresenterLifecycle lifecycle,
            bool coveredContentCanRender)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
            lifecycle.SynchronizeOpen(false);
            if (!lifecycle.CoveredContentStowed)
                return HollowKnightModsRestoreDisposition.None;
            if (coveredContentCanRender)
            {
                lifecycle.RequestCoveredContentRestoreAfterLayout();
                return HollowKnightModsRestoreDisposition.Deferred;
            }

            return HollowKnightModsRestoreDisposition.Immediate;
        }

        public static bool RestoreCoveredContentImmediately(
            TweakPresenterLifecycle lifecycle,
            Action restoreSurfaces,
            Action drainPendingInput)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
            if (restoreSurfaces == null) throw new ArgumentNullException(nameof(restoreSurfaces));
            if (drainPendingInput == null)
                throw new ArgumentNullException(nameof(drainPendingInput));
            if (!lifecycle.CoveredContentStowed) return false;

            restoreSurfaces();
            drainPendingInput();
            return lifecycle.RequestCoveredContentRestore();
        }

        public static bool BeginCoveredContentRestore(
            TweakPresenterLifecycle lifecycle,
            Action hideCameras,
            Action restoreSurfaces)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
            if (hideCameras == null) throw new ArgumentNullException(nameof(hideCameras));
            if (restoreSurfaces == null) throw new ArgumentNullException(nameof(restoreSurfaces));
            if (!lifecycle.CoveredContentRestorePending) return false;

            hideCameras();
            if (lifecycle.CoveredContentRestoreStarted) return false;
            restoreSurfaces();
            return lifecycle.TryBeginCoveredContentRestore();
        }

        public static bool CompleteCoveredContentRestore(
            TweakPresenterLifecycle lifecycle,
            bool ordinaryLayoutReady,
            Action drainPendingInput,
            Action revealCameras)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
            if (drainPendingInput == null)
                throw new ArgumentNullException(nameof(drainPendingInput));
            if (revealCameras == null) throw new ArgumentNullException(nameof(revealCameras));
            if (!ordinaryLayoutReady || !lifecycle.CoveredContentRestoreStarted)
                return false;

            drainPendingInput();
            revealCameras();
            return lifecycle.TryCompleteCoveredContentRestore(
                ordinaryLayoutReady: true);
        }

        public static bool RejectAndDrainLowerScreenInput(
            TweakPresenterLifecycle lifecycle,
            int tapSequence,
            int cleanTapSequence,
            ref int lastTapSequence,
            ref int lastCleanTapSequence,
            ref float pinchLastDistance,
            ref bool mapDragValid,
            ref bool modsDragValid)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
            if (!lifecycle.OwnsInput) return false;
            lastTapSequence = tapSequence;
            lastCleanTapSequence = cleanTapSequence;
            pinchLastDistance = -1f;
            mapDragValid = false;
            modsDragValid = false;
            return true;
        }

        public static bool RejectAllLowerScreenInput(
            TweakPresenterLifecycle lifecycle)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
            return lifecycle.CoveredContentRestorePending;
        }

        public static bool CanCloseFromLowerScreenInput(
            TweakPresenterLifecycle lifecycle)
        {
            if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
            return lifecycle.IsOpen && !lifecycle.CoveredContentRestorePending;
        }
    }
}
