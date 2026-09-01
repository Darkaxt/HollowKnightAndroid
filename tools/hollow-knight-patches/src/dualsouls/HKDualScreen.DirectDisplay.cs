using System;
using System.Collections.Generic;
using HollowKnightPatches;
using UnityEngine;

public partial class HKDualScreen
{
    HkDirectDisplayAdapter transport;
    bool directDisplayActive;
    bool directDisplayShuttingDown;
    bool directDisplayRestorePending;
    bool directDisplayFinalTeardownPending;

    internal Camera ClearCamera => clearCam;

    internal void BindDirectDisplay(HkDirectDisplayAdapter adapter)
    {
        if (adapter == null) throw new ArgumentNullException(nameof(adapter));
        if (transport != null && !ReferenceEquals(transport, adapter))
            throw new InvalidOperationException("HKDualScreen already has a transport owner");
        transport = adapter;
        OnDirectPanelGeometry(adapter.PanelWidth, adapter.PanelHeight);
        directDisplayActive = adapter.IsTransportActive;
    }

    internal void OnDirectPanelGeometry(float width, float height)
    {
        bottomWidth = Mathf.Max(1, Mathf.RoundToInt(width));
        bottomHeight = Mathf.Max(1, Mathf.RoundToInt(height));
        float aspect = (float)bottomWidth / bottomHeight;
        if (clearCam != null) clearCam.aspect = aspect;
        if (attrCam != null) attrCam.aspect = aspect;
        if (hudCam2 != null) hudCam2.aspect = aspect;
        if (promptCam != null) promptCam.aspect = aspect;
    }

    internal void SetDirectDisplayActive(bool active)
    {
        directDisplayActive = active;
        if (!active)
        {
            var failures = new List<Exception>();
            TryDirectStep(RestoreReferenceRouting, failures);
            TryDirectStep(() => SetRoleCamerasEnabled(false), failures);
            directDisplayRestorePending = failures.Count > 0;
            if (failures.Count == 1) throw failures[0];
            if (failures.Count > 1)
                throw new AggregateException("Dual Souls route-back failed", failures);
            return;
        }

        SetRoleCamerasEnabled(true);
        directDisplayRestorePending = false;
    }

    void SetRoleCamerasEnabled(bool active)
    {
        if (clearCam != null) clearCam.enabled = active;
        if (attrCam != null) attrCam.enabled = active;
        if (hudCam2 != null) hudCam2.enabled = active;
        if (promptCam != null) promptCam.enabled = active;
        if (!active && bgCaptureCam != null) bgCaptureCam.enabled = false;
    }

    void RestoreReferenceRouting()
    {
        var failures = new List<Exception>();
        TryDirectStep(() =>
        {
            var cameras = GameCameras.instance;
            if (cameras != null) RelayerHud(cameras, true);
        }, failures);
        TryDirectStep(RestoreRoutedLayers, failures);
        TryDirectStep(RestoreNameCardOrThrow, failures);
        TryDirectStep(RestoreDialogueShapeOrThrow, failures);
        if (failures.Count == 1) throw failures[0];
        if (failures.Count > 1)
            throw new AggregateException("Dual Souls object restoration failed", failures);
    }

    static void TryDirectStep(Action step, List<Exception> failures)
    {
        try { step(); }
        catch (Exception e) { failures.Add(e); }
    }

    internal void ShutdownDirectDisplayAndRestore()
    {
        if (directDisplayShuttingDown) return;
        directDisplayActive = false;
        directDisplayFinalTeardownPending = true;

        var failures = new List<Exception>();
        TryDirectStep(RestoreReferenceRouting, failures);
        TryDirectStep(() => SetRoleCamerasEnabled(false), failures);
        if (failures.Count > 0)
        {
            directDisplayRestorePending = true;
            if (failures.Count == 1) throw failures[0];
            throw new AggregateException(
                "Dual Souls final route-back failed", failures);
        }

        directDisplayRestorePending = false;
        CompleteDirectDisplayTeardown();
    }

    void RetryPendingDirectDisplayRestore()
    {
        RestoreReferenceRouting();
        SetRoleCamerasEnabled(false);
        directDisplayRestorePending = false;
        if (directDisplayFinalTeardownPending)
            CompleteDirectDisplayTeardown();
        else if (transport != null)
            transport.OnReferenceRestoreCompleted();
    }

    void CompleteDirectDisplayTeardown()
    {
        if (directDisplayShuttingDown) return;
        directDisplayShuttingDown = true;
        directDisplayFinalTeardownPending = false;

        var failures = new List<Exception>();
        TryDirectStep(TeardownCompanion, failures);
        TryDirectStep(() =>
        {
            if (bgDimmer != null) Destroy(bgDimmer);
            bgDimmer = null;
            bgCaptureCam = null;
            bgSetup = false;
        }, failures);
        TryDirectStep(() =>
        {
            if (logoGo != null) Destroy(logoGo);
            logoGo = null;
        }, failures);

        var completedTransport = transport;
        if (completedTransport != null)
            TryDirectStep(
                () => completedTransport.OnReferenceTeardownComplete(this),
                failures);
        transport = null;
        if (ReferenceEquals(activeInstance, this)) activeInstance = null;
        started = false;
        Destroy(gameObject);

        if (failures.Count > 0)
            Debug.LogError(new AggregateException(
                "Dual Souls final cleanup failed", failures));
    }
}
