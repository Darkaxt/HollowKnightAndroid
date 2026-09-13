using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using DualSouls.Mods;
using DualSouls.Mods.HollowKnight;
using UnityEngine;

// Fork-owned presentation for the process-owned Hollow Knight Mods session.
// The shared session/model remain the only behavior and persistence authority.
public partial class HKDualScreen
{
    sealed class ModsLabel
    {
        internal ModsLabel(
            GameObject gameObject,
            Component text,
            PropertyInfo textProperty,
            PropertyInfo colorProperty,
            MethodInfo forceMeshUpdate,
            Renderer renderer)
        {
            GameObject = gameObject;
            Text = text;
            TextProperty = textProperty;
            ColorProperty = colorProperty;
            ForceMeshUpdate = forceMeshUpdate;
            Renderer = renderer;
        }

        internal readonly GameObject GameObject;
        internal readonly Component Text;
        internal readonly PropertyInfo TextProperty;
        internal readonly PropertyInfo ColorProperty;
        internal readonly MethodInfo ForceMeshUpdate;
        internal readonly Renderer Renderer;
        internal string LastText;
        internal Color LastColor;
        internal bool HasLastColor;
    }

    sealed class ModsPresenterState
    {
        internal readonly TweakPresenterInteraction Interaction =
            new TweakPresenterInteraction();
        internal readonly TweakPresenterPaintInvalidation Paint =
            new TweakPresenterPaintInvalidation();
        internal readonly TweakPresenterLifecycle Lifecycle;

        internal ModsPresenterState()
        {
            Lifecycle = new TweakPresenterLifecycle(Paint);
        }
    }

    readonly ModsPresenterState modsPresenter = new ModsPresenterState();
    TweakPresenterInteraction modsInteraction => modsPresenter.Interaction;
    TweakPresenterPaintInvalidation modsPaint => modsPresenter.Paint;
    TweakPresenterLifecycle modsLifecycle => modsPresenter.Lifecycle;

    bool tweaksOpen;
    GameObject tweaksRoot;
    readonly List<GameObject> tweakRows = new List<GameObject>();
    readonly List<ModsLabel> tweakRowTexts = new List<ModsLabel>();
    readonly List<ModsLabel> tweakRowValues = new List<ModsLabel>();
    readonly List<ModsLabel> modsGroupHeaders = new List<ModsLabel>();
    readonly List<TweakPresenterRect> tweakRowHits =
        new List<TweakPresenterRect>();

    Transform gearT;
    SpriteRenderer gearSR;
    Texture2D gearTex;
    Sprite gearSprite;
    Vector3 hudGearAnchor;
    float hudGearH;
    bool hudGearOk;
    Bounds hudFpsB;

    HollowKnightModsSession modsSession;
    TweakMenuModel modsMenu;
    int modsSortingOrder;
    int modsBuiltEntryCount = -1;
    int modsSelectedEntry = 1;
    float modsListScroll;
    float modsDragLastY;
    bool modsDragValid;

    ModsLabel modsDetailTitleText;
    ModsLabel modsDetailText;
    ModsLabel modsStatusText;
    TweakPresenterRect modsListHit;
    readonly TweakPresenterSurfaceOwnership<GameObject, bool> modsPageVisibility =
        new TweakPresenterSurfaceOwnership<GameObject, bool>(
            ReadModsObjectVisibility, WriteModsObjectVisibility, false);
    readonly TweakPresenterSurfaceOwnership<Renderer, bool> modsFrameContentVisibility =
        new TweakPresenterSurfaceOwnership<Renderer, bool>(
            ReadModsRendererVisibility, WriteModsRendererVisibility, false);
    readonly TweakPresenterSurfaceOwnership<Camera, int> modsHudVisibility =
        new TweakPresenterSurfaceOwnership<Camera, int>(
            ReadModsCameraMask, WriteModsCameraMask, 0);
    readonly TweakPresenterSurfaceOwnership<Camera, int> modsCompanionVisibility =
        new TweakPresenterSurfaceOwnership<Camera, int>(
            ReadModsCameraMask, WriteModsCameraMask, 0);

    static bool ReadModsObjectVisibility(GameObject target)
    {
        return target != null && target.activeSelf;
    }

    static void WriteModsObjectVisibility(GameObject target, bool visible)
    {
        if (target != null && target.activeSelf != visible) target.SetActive(visible);
    }

    static bool ReadModsRendererVisibility(Renderer target)
    {
        return target != null && target.enabled;
    }

    static void WriteModsRendererVisibility(Renderer target, bool visible)
    {
        if (target != null && target.enabled != visible) target.enabled = visible;
    }

    static int ReadModsCameraMask(Camera target)
    {
        return target != null ? target.cullingMask : 0;
    }

    static void WriteModsCameraMask(Camera target, int mask)
    {
        if (target != null && target.cullingMask != mask) target.cullingMask = mask;
    }

    bool TryResolveModsPresenter(
        out HollowKnightModsSession session,
        out TweakMenuModel menu)
    {
        session = null;
        menu = null;
        var runtime = HollowKnightModsRuntime.Current;
        if (runtime == null) return false;
        session = runtime.Session;
        if (session == null || !session.IsReady) return false;
        menu = session.Menu;
        return menu != null;
    }

    void RebindModsPresenter(
        HollowKnightModsSession session,
        TweakMenuModel menu)
    {
        TweakPresenterRebindDecision decision = modsLifecycle.Rebind(
            session, menu, menu != null && menu.IsOpen);
        if (!decision.Changed) return;

        try
        {
            if (decision.ClosePreviousMenu && modsMenu != null)
                modsMenu.Close();
        }
        catch (Exception e) { WarnOnce("mods old menu close", e); }
        try
        {
            if (decision.DetachPreviousPresenter && modsSession != null)
                modsSession.SetPresenterAttached(false);
        }
        catch (Exception e) { WarnOnce("mods old presenter detach", e); }
        if (decision.RestoreCoveredContent)
            RestoreModsCoveredContentImmediately();

        DestroyModsModalView();
        modsSession = session;
        modsMenu = menu;
        modsDragValid = false;
        if (transport != null)
            modsInteraction.ResetCleanTap(transport.CleanTapSequence);
        tweaksOpen = modsLifecycle.IsOpen;
    }

    void BuildModsGear()
    {
        if (gearT != null || frameRoot == null) return;

        int highestChromeOrder = 0;
        foreach (var renderer in frameRoot.GetComponentsInChildren<Renderer>(true))
            if (renderer != null && renderer.sortingOrder > highestChromeOrder)
                highestChromeOrder = renderer.sortingOrder;
        modsSortingOrder = highestChromeOrder + 10;

        gearTex = Own(MakeGearTex(48));
        gearSprite = Own(Sprite.Create(
            gearTex,
            new Rect(0f, 0f, 48f, 48f),
            new Vector2(0.5f, 0.5f),
            100f));
        var gear = new GameObject("HKDS Mods Gear");
        gear.transform.SetParent(frameRoot.transform, false);
        SetLayerRecursive(gear.transform, ATTR_LAYER);
        gearSR = gear.AddComponent<SpriteRenderer>();
        gearSR.sprite = gearSprite;
        gearSR.sortingLayerName = "Inventory";
        gearSR.sortingOrder = modsSortingOrder;
        gearSR.enabled = false;
        gearT = gear.transform;
    }

    static Texture2D MakeGearTex(int size)
    {
        var texture = new Texture2D(
            size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        float outerRadius = size * 0.34f;
        float innerRadius = size * 0.22f;
        float holeRadius = size * 0.10f;
        float toothLength = size * 0.115f;
        var white = new Color32(255, 255, 255, 235);
        var clear = new Color32(0, 0, 0, 0);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                bool enabled = distance <= outerRadius && distance >= holeRadius;
                if (!enabled && distance < outerRadius + toothLength &&
                    distance > innerRadius)
                {
                    float angle = Mathf.Atan2(dy, dx);
                    float sector = Mathf.Repeat(
                        angle / (Mathf.PI * 2f) * 8f, 1f);
                    if (sector < 0.30f)
                        enabled = distance <= outerRadius + toothLength;
                }
                pixels[y * size + x] = enabled ? white : clear;
            }
        texture.SetPixels32(pixels);
        texture.Apply(false);
        return texture;
    }

    void PositionGear(float scale, float aspect, float tabY)
    {
        HollowKnightModsSession session;
        TweakMenuModel menu;
        bool ready = TryResolveModsPresenter(out session, out menu);
        if (!ready || frameRoot == null || attrCam == null || !hudGearOk ||
            !directDisplayActive || !attrCam.enabled ||
            ((attrCam.cullingMask & (1 << ATTR_LAYER)) == 0 &&
             !modsLifecycle.CoveredContentRestorePending))
        {
            if (gearSR != null) gearSR.enabled = false;
            if (!ready && (tweaksOpen || tweaksRoot != null || modsSession != null))
                TeardownModsPresenter();
            return;
        }

        RebindModsPresenter(session, menu);
        modsLifecycle.SynchronizeOpen(menu.IsOpen);
        tweaksOpen = modsLifecycle.IsOpen;
        BuildModsGear();
        if (gearT == null || gearSR == null) return;

        scale = Mathf.Max(0.01f, attrCam.orthographicSize);
        aspect = Mathf.Max(0.01f, attrCam.aspect);
        float height = Mathf.Max(scale * 0.085f, 0.01f);
        gearSR.enabled = true;
        gearT.localScale = Vector3.one;
        Bounds entryBounds = gearSR.bounds;
        if (entryBounds.size.y < 0.0001f) return;
        float factor = height / entryBounds.size.y;
        gearT.localScale = new Vector3(factor, factor, 1f);

        Vector3 cameraPosition = attrCam.transform.position;
        float halfWidth = Mathf.Max(0.01f, scale * aspect);
        float halfHeight = height * 0.5f;
        float minX = cameraPosition.x - halfWidth + halfHeight;
        float maxX = cameraPosition.x + halfWidth - halfHeight;
        float minY = cameraPosition.y + tabY + halfHeight;
        float maxY = cameraPosition.y + scale - halfHeight;
        float x = Mathf.Clamp(hudGearAnchor.x, minX, maxX);
        float aboveReadout = hudFpsB.max.y + halfHeight * 1.1f;
        float y = Mathf.Clamp(
            Mathf.Max(hudGearAnchor.y + hudGearH * 1.45f, aboveReadout),
            minY,
            maxY);
        Vector3 target = new Vector3(x, y, hudGearAnchor.z - 0.15f);
        gearT.position = target;
        gearT.rotation = Quaternion.identity;
        Bounds placed = gearSR.bounds;
        gearT.position += target - placed.center;
        gearSR.enabled = true;
    }

    bool GearTapN(float x, float y)
    {
        if (x < 0f || x > 1f || y < 0f || y > 1f || attrCam == null ||
            gearSR == null || !gearSR.enabled || !hudGearOk)
            return false;

        Rect viewport = attrCam.rect;
        Vector2 panelPoint = new Vector2(x, 1f - y);
        if (!viewport.Contains(panelPoint)) return false;
        float vx = (panelPoint.x - viewport.x) / Mathf.Max(0.0001f, viewport.width);
        float vy = (panelPoint.y - viewport.y) / Mathf.Max(0.0001f, viewport.height);
        Vector3 world = attrCam.ViewportToWorldPoint(new Vector3(vx, vy, 10f));

        float tolerance = Mathf.Max(0.08f, hudGearH * 0.45f);
        Bounds gearBounds = gearSR.bounds;
        gearBounds.Expand(new Vector3(tolerance, tolerance, 10f));
        if (world.x >= gearBounds.min.x && world.x <= gearBounds.max.x &&
            world.y >= gearBounds.min.y && world.y <= gearBounds.max.y)
            return true;

        Bounds fpsBounds = hudFpsB;
        fpsBounds.Expand(new Vector3(tolerance, tolerance, 10f));
        return world.x >= fpsBounds.min.x && world.x <= fpsBounds.max.x &&
               world.y >= fpsBounds.min.y && world.y <= fpsBounds.max.y;
    }

    void ToggleTweaksPane()
    {
        HollowKnightModsSession session;
        TweakMenuModel menu;
        if (!TryResolveModsPresenter(out session, out menu))
        {
            TeardownModsPresenter();
            return;
        }

        RebindModsPresenter(session, menu);
        if (menu.IsOpen)
        {
            CloseTweaksPane();
            return;
        }

        menu.Open();
        modsLifecycle.SynchronizeOpen(menu.IsOpen);
        tweaksOpen = modsLifecycle.IsOpen;
        if (transport != null)
            modsInteraction.ResetCleanTap(transport.CleanTapSequence);
    }

    void CloseTweaksPane()
    {
        try
        {
            var runtime = HollowKnightModsRuntime.Current;
            var currentSession = runtime != null ? runtime.Session : null;
            var currentMenu = currentSession != null ? currentSession.Menu : null;
            if (currentMenu != null) currentMenu.Close();
            if (modsMenu != null && !ReferenceEquals(modsMenu, currentMenu))
                modsMenu.Close();
        }
        catch (Exception e) { WarnOnce("mods menu close", e); }
        modsDragValid = false;
        modsLifecycle.SynchronizeOpen(false);
        modsLifecycle.RequestCoveredContentRestoreAfterLayout();
        modsPaint.Invalidate();
        tweaksOpen = modsLifecycle.IsOpen;
    }

    void StowModsCoveredContent()
    {
        modsLifecycle.RequestCoveredContentStow();
        if (mapClone != null && mapClone.activeSelf)
            mapStowStamp = MapContentStamp();
        modsPageVisibility.CaptureAndHide(slideOutClone);
        modsPageVisibility.CaptureAndHide(mapClone);
        modsPageVisibility.CaptureAndHide(invCloneCache);
        modsPageVisibility.CaptureAndHide(charmCloneCache);

        if (areaNameT != null)
            modsFrameContentVisibility.CaptureAndHide(areaNameR);
        if (equipRowRoot != null)
            for (int i = 0; i < equipCharmSRs.Count; i++)
                modsFrameContentVisibility.CaptureAndHide(equipCharmSRs[i]);
        if (noMapT != null)
        {
            modsFrameContentVisibility.CaptureAndHide(noMapR);
            modsFrameContentVisibility.CaptureAndHide(benchPillSR);
        }
        for (int i = 0; i < notchSRs.Count; i++)
            modsFrameContentVisibility.CaptureAndHide(notchSRs[i]);
        modsFrameContentVisibility.CaptureAndHide(mapMaskTopR);
        modsFrameContentVisibility.CaptureAndHide(mapMaskBotR);
        if (mapResetT != null)
        {
            modsFrameContentVisibility.CaptureAndHide(mapResetR);
            modsFrameContentVisibility.CaptureAndHide(mapResetPillSR);
        }
        modsFrameContentVisibility.CaptureAndHide(selBox);
        modsFrameContentVisibility.CaptureAndHide(ctrlMyGlyph);
        Renderer ctrlVerbRenderer = ctrlMyVerbT != null
            ? ctrlMyVerbT.GetComponent<Renderer>()
            : null;
        modsFrameContentVisibility.CaptureAndHide(ctrlVerbRenderer);
        modsHudVisibility.CaptureAndHide(hudCam2);
    }

    void BeginModsCoveredContentRestore()
    {
        if (!modsLifecycle.CoveredContentRestorePending) return;
        modsCompanionVisibility.CaptureAndHide(attrCam);
        modsHudVisibility.CaptureAndHide(hudCam2);
        if (modsLifecycle.TryBeginCoveredContentRestore())
            RestoreModsCoveredContentCore();
    }

    void CompleteModsCoveredContentRestore()
    {
        if (!modsLifecycle.CoveredContentRestorePending) return;
        modsCompanionVisibility.Restore();
        modsHudVisibility.Restore();
        modsLifecycle.TryCompleteCoveredContentRestore(
            ordinaryLayoutReady: true);
    }

    void RestoreModsCoveredContentImmediately()
    {
        modsLifecycle.RequestCoveredContentRestore();
        RestoreModsCoveredContentCore();
        modsHudVisibility.Restore();
        modsCompanionVisibility.Restore();
    }

    void RestoreModsCoveredContentCore()
    {
        modsFrameContentVisibility.Restore();
        modsPageVisibility.Restore();
    }

    Component FindModsTextDonor()
    {
        for (int i = 0; i < frameTabs.Count; i++)
        {
            Component donor = frameTabs[i].tmp;
            if (donor != null) return donor;
        }
        return null;
    }

    ModsLabel BuildModsLabel(
        Component donor,
        string name,
        Transform parent,
        out GameObject labelObject)
    {
        labelObject = null;
        if (donor == null || parent == null) return null;
        var source = donor.gameObject;
        var clone = Instantiate(source, parent);
        clone.name = name;
        clone.SetActive(false);
        SanitizeDetachedTmpClone(clone);
        SetLayerRecursive(clone.transform, ATTR_LAYER);
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;

        Component text = null;
        foreach (var component in clone.GetComponentsInChildren<Component>(true))
        {
            if (!IsTextMeshProGraphic(component)) continue;
            text = component;
            break;
        }
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
        {
            renderer.gameObject.SetActive(true);
            renderer.enabled = true;
            renderer.sortingLayerName = "Inventory";
            renderer.sortingOrder = modsSortingOrder + 10;
        }
        if (text == null)
            throw new InvalidOperationException("A native Mods text clone is unavailable.");
        PropertyInfo textProperty = text.GetType().GetProperty("text");
        PropertyInfo colorProperty = text.GetType().GetProperty("color");
        MethodInfo forceMeshUpdate = text.GetType().GetMethod(
            "ForceMeshUpdate", Type.EmptyTypes);
        Renderer textRenderer = text.GetComponent<Renderer>();
        if (textProperty == null || colorProperty == null ||
            forceMeshUpdate == null || textRenderer == null)
            throw new InvalidOperationException("The native Mods text donor is incomplete.");

        clone.SetActive(true);
        labelObject = clone;
        return new ModsLabel(
            clone, text, textProperty, colorProperty, forceMeshUpdate, textRenderer);
    }

    void BuildModsModal(TweakMenuModel menu)
    {
        if (menu == null || frameRoot == null || compRoot == null) return;
        int entryCount = TweakPresenterListLayout.EntryCount(menu);
        if (tweaksRoot != null && modsBuiltEntryCount == entryCount) return;
        DestroyModsModalView();

        Component donor = FindModsTextDonor();
        if (donor == null) return;
        tweaksRoot = new GameObject("HKDS Mods Modal");
        tweaksRoot.transform.SetParent(compRoot, false);
        tweaksRoot.layer = ATTR_LAYER;
        tweaksRoot.SetActive(false);

        for (int i = 0; i < entryCount; i++)
        {
            TweakPresenterListEntry entry =
                TweakPresenterListLayout.EntryAt(menu, i);
            GameObject labelObject;
            ModsLabel label = BuildModsLabel(
                donor, "Mods Entry " + i, tweaksRoot.transform, out labelObject);
            tweakRows.Add(labelObject);
            tweakRowTexts.Add(label);
            tweakRowHits.Add(default(TweakPresenterRect));
            if (entry.Kind == TweakPresenterListEntryKind.Header)
                modsGroupHeaders.Add(label);

            ModsLabel value = null;
            if (entry.Kind != TweakPresenterListEntryKind.Header)
                value = BuildModsLabel(
                    donor, "Mods Value " + i, tweaksRoot.transform, out labelObject);
            tweakRowValues.Add(value);
        }
        GameObject detailObject;
        modsDetailTitleText = BuildModsLabel(
            donor, "Mods Detail Title", tweaksRoot.transform, out detailObject);
        modsDetailText = BuildModsLabel(
            donor, "Mods Detail", tweaksRoot.transform, out detailObject);
        modsStatusText = BuildModsLabel(
            donor, "Mods Status", tweaksRoot.transform, out detailObject);

        SanitizeDetachedTmpClone(tweaksRoot);
        SetLayerRecursive(tweaksRoot.transform, ATTR_LAYER);
        tweaksRoot.SetActive(true);
        modsBuiltEntryCount = entryCount;
        modsLifecycle.MarkViewBuilt();
        if (modsSession != null)
        {
            modsSession.SetPresenterAttached(true);
            modsLifecycle.MarkPresenterAttached();
        }
    }

    void SetModsText(ModsLabel label, string value, Color color)
    {
        if (label == null)
            throw new InvalidOperationException("A native Mods text clone is unavailable.");
        string wanted = value ?? "";
        if (!string.Equals(label.LastText, wanted, StringComparison.Ordinal))
        {
            label.TextProperty.SetValue(label.Text, wanted, null);
            label.ForceMeshUpdate.Invoke(label.Text, null);
            NeutralizeDetachedTmpClip(label.GameObject);
            label.LastText = wanted;
        }
        if (!label.HasLastColor || label.LastColor != color)
        {
            label.ColorProperty.SetValue(label.Text, color, null);
            label.LastColor = color;
            label.HasLastColor = true;
        }
        label.Renderer.enabled = true;
        label.Renderer.sortingLayerName = "Inventory";
        label.Renderer.sortingOrder = modsSortingOrder + 10;
    }

    void PlaceModsTextLeft(
        ModsLabel label,
        Vector3 leftCenter,
        float targetLineHeight,
        float maximumWidth)
    {
        Bounds bounds;
        if (!ScaleModsText(label, targetLineHeight, maximumWidth, out bounds)) return;
        label.Text.transform.position += new Vector3(
            leftCenter.x - bounds.min.x,
            leftCenter.y - bounds.center.y,
            leftCenter.z - bounds.center.z);
    }

    void PlaceModsTextRight(
        ModsLabel label,
        Vector3 rightCenter,
        float targetLineHeight,
        float maximumWidth)
    {
        Bounds bounds;
        if (!ScaleModsText(label, targetLineHeight, maximumWidth, out bounds)) return;
        label.Text.transform.position += new Vector3(
            rightCenter.x - bounds.max.x,
            rightCenter.y - bounds.center.y,
            rightCenter.z - bounds.center.z);
    }

    void PlaceModsTextTopLeft(
        ModsLabel label,
        Vector3 topLeft,
        float targetLineHeight,
        float maximumWidth,
        float maximumHeight = float.MaxValue)
    {
        Bounds bounds;
        if (!ScaleModsText(
                label, targetLineHeight, maximumWidth, maximumHeight, out bounds))
            return;
        label.Text.transform.position += new Vector3(
            topLeft.x - bounds.min.x,
            topLeft.y - bounds.max.y,
            topLeft.z - bounds.center.z);
    }

    void PlaceModsTextBottomLeft(
        ModsLabel label,
        Vector3 bottomLeft,
        float targetLineHeight,
        float maximumWidth,
        float maximumHeight)
    {
        Bounds bounds;
        if (!ScaleModsText(
                label, targetLineHeight, maximumWidth, maximumHeight, out bounds))
            return;
        label.Text.transform.position += new Vector3(
            bottomLeft.x - bounds.min.x,
            bottomLeft.y - bounds.min.y,
            bottomLeft.z - bounds.center.z);
    }

    bool ScaleModsText(
        ModsLabel label,
        float targetLineHeight,
        float maximumWidth,
        out Bounds bounds)
    {
        return ScaleModsText(
            label, targetLineHeight, maximumWidth, float.MaxValue, out bounds);
    }

    bool ScaleModsText(
        ModsLabel label,
        float targetLineHeight,
        float maximumWidth,
        float maximumHeight,
        out Bounds bounds)
    {
        bounds = default(Bounds);
        if (label == null || label.Renderer == null) return false;
        Transform transform = label.Text.transform;
        transform.localScale = Vector3.one;
        bounds = label.Renderer.bounds;
        if (bounds.size.x < 0.0001f || bounds.size.y < 0.0001f) return false;
        int lines = 1;
        string text = label.LastText ?? "";
        for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lines++;
        float singleLineHeight = bounds.size.y / Mathf.Max(1, lines);
        float factor = targetLineHeight / Mathf.Max(0.0001f, singleLineHeight);
        if (bounds.size.x * factor > maximumWidth)
            factor = maximumWidth / Mathf.Max(0.0001f, bounds.size.x);
        if (bounds.size.y * factor > maximumHeight)
            factor = maximumHeight / Mathf.Max(0.0001f, bounds.size.y);
        transform.localScale = Vector3.one * Mathf.Max(0.0001f, factor);
        bounds = label.Renderer.bounds;
        return bounds.size.x >= 0.0001f && bounds.size.y >= 0.0001f;
    }

    bool TryGetModsGeometry(
        out float left,
        out float right,
        out float bottom,
        out float top,
        out float scale)
    {
        left = right = bottom = top = 0f;
        scale = 0f;
        if (attrCam == null || frameRoot == null) return false;
        scale = attrCam.orthographicSize;
        float aspect = attrCam.aspect;
        Vector3 cameraPosition = attrCam.transform.position;
        float topFraction = float.IsNaN(frameInnerTopFrac)
            ? cfg.compSepTopY : frameInnerTopFrac;
        float bottomFraction = float.IsNaN(frameInnerBotFrac)
            ? cfg.compTabY + 0.4f : frameInnerBotFrac;
        top = cameraPosition.y + topFraction * scale;
        bottom = cameraPosition.y + bottomFraction * scale;
        float horizontalInset = scale * aspect * 0.08f;
        left = cameraPosition.x - scale * aspect + horizontalInset;
        right = cameraPosition.x + scale * aspect - horizontalInset;
        return right > left && top > bottom;
    }

    static long HashModsPaint(long hash, int value)
    {
        return unchecked((hash ^ (uint)value) * 1099511628211L);
    }

    static long HashModsPaintString(long hash, string value)
    {
        if (value == null) return HashModsPaint(hash, 0);
        hash = HashModsPaint(hash, value.Length);
        for (int i = 0; i < value.Length; i++)
            hash = HashModsPaint(hash, value[i]);
        return hash;
    }

    long ComputeModsModelPaintStamp(
        HollowKnightModsSession session,
        TweakMenuModel menu)
    {
        if (session == null || menu == null) return 0L;
        long stamp = 1469598103934665603L;
        stamp = HashModsPaint(stamp, RuntimeHelpers.GetHashCode(session));
        stamp = HashModsPaint(stamp, RuntimeHelpers.GetHashCode(menu));
        stamp = HashModsPaint(stamp, menu.IsOpen ? 1 : 0);
        stamp = HashModsPaint(stamp, menu.SelectedGroupIndex);
        stamp = HashModsPaint(stamp, menu.SelectedRowIndex);
        stamp = HashModsPaint(stamp, menu.WindowStart);
        stamp = HashModsPaint(stamp, menu.VisibleRows);
        stamp = HashModsPaintString(stamp, menu.Message);
        stamp = HashModsPaint(stamp, menu.MessageIsError ? 1 : 0);
        stamp = HashModsPaint(stamp, session.Controller.MasterEnabled ? 1 : 0);
        stamp = HashModsPaint(stamp, menu.Groups.Count);
        if (menu.Groups.Count > 0)
            stamp = HashModsPaintString(
                stamp, menu.Groups[menu.SelectedGroupIndex]);

        stamp = HashModsPaint(stamp, modsSelectedEntry);
        stamp = HashModsPaint(stamp, TweakPresenterListLayout.EntryCount(menu));
        for (int group = 0; group < menu.Groups.Count; group++)
        {
            IReadOnlyList<TweakDescriptor> rows = menu.RowsForGroup(group);
            stamp = HashModsPaintString(stamp, menu.Groups[group]);
            stamp = HashModsPaint(stamp, rows.Count);
            for (int row = 0; row < rows.Count; row++)
            {
                TweakDescriptor descriptor = rows[row];
                stamp = HashModsPaintString(stamp, descriptor.Id);
                stamp = HashModsPaint(stamp, descriptor.IsAvailable ? 1 : 0);
                stamp = HashModsPaintString(
                    stamp, session.Controller.Value(descriptor.Id));
            }
        }
        return stamp;
    }

    long ComputeModsGeometryPaintStamp()
    {
        float left, right, bottom, top, scale;
        if (!TryGetModsGeometry(out left, out right, out bottom, out top, out scale))
            return 0L;
        Rect viewport = attrCam.rect;
        Vector3 position = attrCam.transform.position;
        return TweakPresenterGeometryPaintStamp.Compute(
            left, right, bottom, top, scale,
            viewport.x, viewport.y, viewport.width, viewport.height,
            position.x, position.y, position.z,
            attrCam.orthographicSize, attrCam.aspect);
    }

    void RepaintModsModal(
        HollowKnightModsSession session,
        TweakMenuModel menu)
    {
        if (session == null || menu == null || tweaksRoot == null)
            throw new InvalidOperationException("The Mods presentation owner is unavailable.");
        float left, right, bottom, top, scale;
        if (!TryGetModsGeometry(out left, out right, out bottom, out top, out scale))
            throw new InvalidOperationException("The Mods context geometry is unavailable.");

        int entryCount = TweakPresenterListLayout.EntryCount(menu);
        if (entryCount == 0 || tweakRowTexts.Count != entryCount)
            throw new InvalidOperationException("The Mods list geometry is incomplete.");
        modsSelectedEntry = Mathf.Clamp(modsSelectedEntry, 1, entryCount - 1);

        float width = right - left;
        float height = top - bottom;
        float split = left + width * TweakPresenterListLayout.LeftFraction;
        float listLeft = left + width * 0.025f;
        float listRight = split - width * 0.025f;
        float listTop = top - height * 0.025f;
        float listBottom = bottom + height * 0.025f;
        float visibleHeight = listTop - listBottom;
        float rowStep = height * 0.115f;
        float lineHeight = rowStep * 0.45f;
        float z = compRoot.position.z - 0.3f;
        modsListScroll = TweakPresenterListLayout.ClampScroll(
            modsListScroll, entryCount, rowStep, visibleHeight);
        modsListHit = new TweakPresenterRect(
            left, listBottom, split - left, visibleHeight);

        bool masterEnabled = session.Controller.MasterEnabled;
        for (int i = 0; i < entryCount; i++)
        {
            TweakPresenterListEntry entry =
                TweakPresenterListLayout.EntryAt(menu, i);
            float y = listTop - rowStep * 0.5f - i * rowStep + modsListScroll;
            bool shown = TweakPresenterListLayout.RowFits(
                y - rowStep * 0.5f,
                y + rowStep * 0.5f,
                listBottom,
                listTop);
            GameObject rowObject = tweakRows[i];
            if (rowObject != null && rowObject.activeSelf != shown)
                rowObject.SetActive(shown);
            ModsLabel valueLabel = tweakRowValues[i];
            if (valueLabel != null && valueLabel.GameObject.activeSelf != shown)
                valueLabel.GameObject.SetActive(shown);
            if (!shown)
            {
                tweakRowHits[i] = default(TweakPresenterRect);
                continue;
            }

            bool selected = i == modsSelectedEntry;
            string title;
            string value = "";
            Color color;
            if (entry.Kind == TweakPresenterListEntryKind.Header)
            {
                title = entry.GroupIndex < 0
                    ? "GENERAL"
                    : menu.Groups[entry.GroupIndex].ToUpperInvariant();
                color = new Color(0.85f, 0.76f, 0.52f, 0.82f);
            }
            else if (entry.Kind == TweakPresenterListEntryKind.Master)
            {
                title = (selected ? "> " : "  ") + "MASTER";
                value = masterEnabled ? "ON" : "OFF";
                color = selected ? new Color(1f, 1f, 0.82f, 1f) : Color.white;
            }
            else if (entry.Kind == TweakPresenterListEntryKind.Reset)
            {
                title = (selected ? "> " : "  ") + "RESET ALL MODS";
                color = selected ? new Color(1f, 0.85f, 0.52f, 1f) : Color.white;
            }
            else
            {
                TweakDescriptor descriptor =
                    menu.RowsForGroup(entry.GroupIndex)[entry.RowIndex];
                title = (selected ? "> " : "  ") +
                        descriptor.Title.ToUpperInvariant();
                value = descriptor.IsAvailable
                    ? FriendlyModsValue(session.Controller.Value(descriptor.Id))
                    : "DEFERRED";
                color = !descriptor.IsAvailable
                    ? (selected ? new Color(1f, 0.72f, 0.38f, 1f)
                                : new Color(0.64f, 0.50f, 0.36f, 1f))
                    : !masterEnabled
                        ? (selected ? new Color(0.78f, 0.78f, 0.78f, 1f)
                                    : new Color(0.54f, 0.54f, 0.54f, 1f))
                        : (selected ? new Color(1f, 1f, 0.82f, 1f)
                                    : new Color(0.82f, 0.84f, 0.88f, 1f));
            }

            SetModsText(tweakRowTexts[i], title, color);
            PlaceModsTextLeft(
                tweakRowTexts[i], new Vector3(listLeft, y, z),
                entry.Kind == TweakPresenterListEntryKind.Header
                    ? lineHeight * 0.78f : lineHeight,
                (listRight - listLeft) * 0.72f);
            if (valueLabel != null)
            {
                SetModsText(valueLabel, value, color);
                PlaceModsTextRight(
                    valueLabel, new Vector3(listRight, y, z), lineHeight,
                    (listRight - listLeft) * 0.30f);
            }
            tweakRowHits[i] = entry.Kind == TweakPresenterListEntryKind.Header
                ? default(TweakPresenterRect)
                : new TweakPresenterRect(
                    left, y - rowStep * 0.5f, split - left, rowStep);
        }

        TweakPresenterListEntry selectedEntry =
            TweakPresenterListLayout.EntryAt(menu, modsSelectedEntry);
        TweakDescriptor selectedRow = selectedEntry.Kind == TweakPresenterListEntryKind.Row
            ? menu.RowsForGroup(selectedEntry.GroupIndex)[selectedEntry.RowIndex]
            : null;
        string detailTitle;
        string detail;
        if (selectedEntry.Kind == TweakPresenterListEntryKind.Master)
        {
            detailTitle = "MASTER";
            detail = "The gate for every available mod. OFF restores the game baseline."
                   + "\n\nTap the row again to change.";
        }
        else if (selectedEntry.Kind == TweakPresenterListEntryKind.Reset)
        {
            detailTitle = "RESET ALL MODS";
            detail = "Return every mod value to its default."
                   + "\n\nTap the row again to reset.";
        }
        else if (selectedRow != null)
        {
            detailTitle = selectedRow.Title.ToUpperInvariant();
            detail = selectedRow.Description;
            if (!selectedRow.IsAvailable)
                detail += "\n\n" + selectedRow.TrackingId + ": " +
                          selectedRow.UnavailableReason;
            else
                detail += "\n\nTap the row again to change.";
        }
        else
        {
            detailTitle = "MODS";
            detail = "Choose a row on the left.";
        }

        string status = menu.Message;
        if (string.IsNullOrEmpty(status) && selectedRow != null &&
            !selectedRow.IsAvailable)
            status = selectedRow.TrackingId + ": " + selectedRow.UnavailableReason;
        else if (string.IsNullOrEmpty(status) && !masterEnabled &&
                 selectedEntry.Kind == TweakPresenterListEntryKind.Row)
            status = "MASTER IS OFF. ENABLE IT BEFORE CHANGING AVAILABLE MODS.";
        if (string.IsNullOrEmpty(status))
            status = "GEAR OR TAB: CLOSE";

        float detailLeft = split + width * 0.035f;
        float detailWidth = right - detailLeft - width * 0.025f;
        float statusBottom = bottom + height * 0.025f;
        float statusHeight = height * 0.16f;
        float detailTop = listTop - rowStep * 1.15f;
        float detailBottom = statusBottom + statusHeight + height * 0.035f;
        SetModsText(modsDetailTitleText, detailTitle, Color.white);
        PlaceModsTextTopLeft(
            modsDetailTitleText, new Vector3(detailLeft, listTop, z),
            lineHeight * 1.05f, detailWidth);
        SetModsText(modsDetailText, WrapModsText(detail, 25),
            selectedRow != null && !selectedRow.IsAvailable
                ? new Color(1f, 0.72f, 0.38f, 1f)
                : new Color(0.86f, 0.88f, 0.92f, 1f));
        PlaceModsTextTopLeft(
            modsDetailText,
            new Vector3(detailLeft, detailTop, z),
            lineHeight * 0.72f, detailWidth,
            Mathf.Max(lineHeight, detailTop - detailBottom));
        SetModsText(modsStatusText, WrapModsText(status, 25),
            menu.MessageIsError
                ? new Color(1f, 0.42f, 0.38f, 1f)
                : new Color(0.66f, 0.78f, 0.9f, 1f));
        PlaceModsTextBottomLeft(
            modsStatusText,
            new Vector3(detailLeft, statusBottom, z),
            lineHeight * 0.68f, detailWidth, statusHeight);
    }

    static string FriendlyModsValue(string value)
    {
        return string.IsNullOrEmpty(value)
            ? "UNKNOWN"
            : value.Replace('_', ' ').Replace('-', ' ').ToUpperInvariant();
    }

    static string WrapModsText(string value, int columns)
    {
        if (string.IsNullOrEmpty(value) || columns < 2) return value ?? "";
        var output = new StringBuilder(value.Length + 16);
        int lineLength = 0;
        int index = 0;
        while (index < value.Length)
        {
            if (value[index] == '\n')
            {
                output.Append('\n');
                lineLength = 0;
                index++;
                continue;
            }
            while (index < value.Length && value[index] == ' ') index++;
            int start = index;
            while (index < value.Length && value[index] != ' ' && value[index] != '\n') index++;
            int length = index - start;
            if (length == 0) continue;
            if (lineLength > 0 && lineLength + 1 + length > columns)
            {
                output.Append('\n');
                lineLength = 0;
            }
            else if (lineLength > 0)
            {
                output.Append(' ');
                lineLength++;
            }
            output.Append(value, start, length);
            lineLength += length;
        }
        return output.ToString();
    }

    bool TryModsTouchWorld(float x, float y, out Vector2 world)
    {
        world = Vector2.zero;
        if (attrCam == null) return false;
        Rect viewport = attrCam.rect;
        var presenterViewport = new TweakPresenterRect(
            viewport.x, viewport.y, viewport.width, viewport.height);
        TweakPresenterPoint local;
        if (!TweakPresenterInteraction.TryMapNormalizedTopLeft(
                x, y, presenterViewport, out local))
            return false;
        Vector3 point = attrCam.ViewportToWorldPoint(
            new Vector3(local.X, local.Y, 10f));
        world = new Vector2(point.x, point.y);
        return true;
    }

    void TweaksTouchTick(TweakMenuModel menu)
    {
        if (menu == null || transport == null || attrCam == null) return;
        int touchCount = transport.TouchCount;
        if (touchCount != 1)
        {
            modsDragValid = false;
            return;
        }

        float nx = transport.T0X;
        float ny = transport.T0Y;
        Vector2 world;
        bool onList = TryModsTouchWorld(nx, ny, out world) &&
                      world.x >= modsListHit.X &&
                      world.x <= modsListHit.X + modsListHit.Width;
        if (!onList)
        {
            modsDragValid = false;
            return;
        }
        if (modsDragValid)
        {
            float oldScroll = modsListScroll;
            float left, right, bottom, top, scale;
            if (TryGetModsGeometry(out left, out right, out bottom, out top, out scale))
            {
                float height = top - bottom;
                modsListScroll += (modsDragLastY - ny) * 2f * attrCam.orthographicSize;
                modsListScroll = TweakPresenterListLayout.ClampScroll(
                    modsListScroll,
                    TweakPresenterListLayout.EntryCount(menu),
                    height * 0.115f,
                    height * 0.95f);
            }
            if (Mathf.Abs(modsListScroll - oldScroll) > 0.0001f)
                modsPaint.Invalidate();
        }
        modsDragLastY = ny;
        modsDragValid = true;
    }

    void HandleModsCleanTap(TweakMenuModel menu, Vector2 world)
    {
        if (menu == null) return;
        var point = new TweakPresenterPoint(world.x, world.y);
        int hit = -1;
        for (int i = 0; i < tweakRowHits.Count; i++)
            if (tweakRowHits[i].Contains(point)) { hit = i; break; }
        if (hit < 0) return;

        TweakPresenterListEntry entry =
            TweakPresenterListLayout.EntryAt(menu, hit);
        if (hit != modsSelectedEntry)
        {
            modsSelectedEntry = hit;
            if (entry.Kind == TweakPresenterListEntryKind.Row)
            {
                menu.MoveGroup(entry.GroupIndex - menu.SelectedGroupIndex);
                menu.MoveRow(entry.RowIndex - menu.SelectedRowIndex);
            }
            modsPaint.Invalidate();
            return;
        }

        if (entry.Kind == TweakPresenterListEntryKind.Master)
            menu.ToggleMaster();
        else if (entry.Kind == TweakPresenterListEntryKind.Reset)
            menu.Reset();
        else if (entry.Kind == TweakPresenterListEntryKind.Row)
        {
            menu.MoveGroup(entry.GroupIndex - menu.SelectedGroupIndex);
            menu.MoveRow(entry.RowIndex - menu.SelectedRowIndex);
            if (menu.Selected != null && menu.Selected.IsAvailable)
                menu.CycleSelected();
        }
    }

    void TweaksPaneTick(Camera source)
    {
        try
        {
            HollowKnightModsSession session;
            TweakMenuModel menu;
            if (source == null || transport == null || attrCam == null ||
                !TryResolveModsPresenter(out session, out menu))
            {
                TeardownModsPresenter();
                return;
            }

            RebindModsPresenter(session, menu);
            modsLifecycle.SynchronizeOpen(menu.IsOpen);
            tweaksOpen = modsLifecycle.IsOpen;
            if (!tweaksOpen)
            {
                modsLifecycle.RequestCoveredContentRestoreAfterLayout();
                return;
            }

            EnsureCompRoot();
            StowModsCoveredContent();
            ApplyCompanionCamera(compRoot.position);
            if (cfg.compFrame == 1)
            {
                BuildFrame();
                PositionFrame();
            }
            StowModsCoveredContent();
            if (frameRoot == null)
            {
                CloseTweaksPane();
                return;
            }

            BuildModsModal(menu);
            if (tweaksRoot == null)
            {
                CloseTweaksPane();
                return;
            }
            if (!tweaksRoot.activeSelf) tweaksRoot.SetActive(true);

            TweaksTouchTick(menu);
            long geometryStamp = ComputeModsGeometryPaintStamp();
            int sequence = transport.CleanTapSequence;
            if (modsPaint.HasCurrentGeometry(geometryStamp) &&
                modsInteraction.TryAcceptCleanTap(sequence))
            {
                Vector2 world;
                if (TryModsTouchWorld(
                        transport.CleanTapX,
                        transport.CleanTapY,
                        out world))
                    HandleModsCleanTap(menu, world);
                modsLifecycle.SynchronizeOpen(menu.IsOpen);
                tweaksOpen = modsLifecycle.IsOpen;
            }

            long modelStamp = ComputeModsModelPaintStamp(session, menu);
            if (modsPaint.ShouldPaint(modelStamp, geometryStamp))
            {
                RepaintModsModal(session, menu);
                modsPaint.Acknowledge(modelStamp, geometryStamp);
            }
            if (!tweaksOpen && tweaksRoot != null)
                tweaksRoot.SetActive(false);
        }
        catch (Exception e)
        {
            WarnOnce("mods presenter", e);
            TeardownModsPresenter();
        }
    }

    void DestroyModsModalView()
    {
        if (tweaksRoot != null) Destroy(tweaksRoot);
        tweaksRoot = null;
        tweakRows.Clear();
        tweakRowTexts.Clear();
        tweakRowValues.Clear();
        modsGroupHeaders.Clear();
        tweakRowHits.Clear();
        modsDetailTitleText = null;
        modsDetailText = null;
        modsStatusText = null;
        modsBuiltEntryCount = -1;
        modsListHit = default(TweakPresenterRect);
        modsDragValid = false;
        modsPaint.Invalidate();
    }

    void ClearModsFrameReferences()
    {
        gearT = null;
        gearSR = null;
        gearTex = null;
        gearSprite = null;
        modsSortingOrder = 0;
        hudGearOk = false;
        hudGearH = 0f;
        hudGearAnchor = Vector3.zero;
        hudFpsB = default(Bounds);
        modsPaint.Invalidate();
    }

    void TeardownModsPresenter()
    {
        HollowKnightModsSession currentSession = null;
        TweakMenuModel currentMenu = null;
        try
        {
            var runtime = HollowKnightModsRuntime.Current;
            currentSession = runtime != null ? runtime.Session : null;
            currentMenu = currentSession != null ? currentSession.Menu : null;
        }
        catch (Exception e) { WarnOnce("mods current owner", e); }
        try { if (modsMenu != null) modsMenu.Close(); }
        catch (Exception e) { WarnOnce("mods attached menu close", e); }
        try
        {
            if (currentMenu != null && !ReferenceEquals(currentMenu, modsMenu))
                currentMenu.Close();
        }
        catch (Exception e) { WarnOnce("mods current menu close", e); }
        RestoreModsCoveredContentImmediately();
        bool detachAttachedPresenter = modsLifecycle.PresenterAttached;
        modsLifecycle.Detach();
        try
        {
            if (detachAttachedPresenter && modsSession != null)
                modsSession.SetPresenterAttached(false);
        }
        catch (Exception e) { WarnOnce("mods attached session detach", e); }

        tweaksOpen = false;
        if (tweaksRoot != null) { Destroy(tweaksRoot); tweaksRoot = null; }
        tweakRows.Clear();
        tweakRowTexts.Clear();
        tweakRowValues.Clear();
        modsGroupHeaders.Clear();
        tweakRowHits.Clear();
        modsDetailTitleText = null;
        modsDetailText = null;
        modsStatusText = null;
        modsBuiltEntryCount = -1;
        modsListHit = default(TweakPresenterRect);
        modsPaint.Invalidate();
        if (gearT != null) Destroy(gearT.gameObject);
        if (gearSprite != null) Destroy(gearSprite);
        if (gearTex != null) Destroy(gearTex);
        gearT = null;
        gearSR = null;
        gearTex = null;
        gearSprite = null;
        modsSortingOrder = 0;
        hudGearOk = false;
        hudGearH = 0f;
        hudGearAnchor = Vector3.zero;
        hudFpsB = default(Bounds);
        modsDragValid = false;
        modsListScroll = 0f;
        modsSelectedEntry = 1;
        modsSession = null;
        modsMenu = null;
        modsInteraction.ResetCleanTap(int.MinValue);
    }
}
