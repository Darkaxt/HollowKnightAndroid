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
    readonly List<TweakPresenterRect> tweakRowHits =
        new List<TweakPresenterRect>();

    Transform gearT;
    SpriteRenderer gearSR;
    Texture2D gearTex;
    Vector3 hudGearAnchor;
    float hudGearH;
    bool hudGearOk;
    Bounds hudFpsB;

    HollowKnightModsSession modsSession;
    TweakMenuModel modsMenu;
    bool modsClosePending;
    int modsSortingOrder;
    int modsBuiltVisibleRows = -1;

    ModsLabel modsTitleText;
    ModsLabel modsMasterText;
    ModsLabel modsGroupText;
    ModsLabel modsDetailText;
    ModsLabel modsStatusText;
    ModsLabel modsResetText;
    ModsLabel modsCloseText;
    TweakPresenterRect modsMasterHit;
    TweakPresenterRect modsPreviousGroupHit;
    TweakPresenterRect modsNextGroupHit;
    TweakPresenterRect modsResetHit;
    TweakPresenterRect modsCloseHit;
    TweakPresenterHitMap modsHitMap;

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
            RestoreModsCoveredContentCore();

        DestroyModsModalView();
        modsSession = session;
        modsMenu = menu;
        modsClosePending = false;
        if (transport != null)
            modsInteraction.ResetCleanTap(transport.CleanTapSequence);
        tweaksOpen = modsLifecycle.IsOpen;
    }

    void BuildModsGear()
    {
        if (gearT != null || frameRoot == null) return;

        const int size = 64;
        const float center = (size - 1) * 0.5f;
        gearTex = Own(new Texture2D(size, size, TextureFormat.RGBA32, false));
        gearTex.name = "HKDS Mods Gear";
        gearTex.wrapMode = TextureWrapMode.Clamp;
        gearTex.filterMode = FilterMode.Bilinear;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx) + Mathf.PI;
                int tooth = Mathf.FloorToInt(angle * 12f / (Mathf.PI * 2f));
                float outer = (tooth & 1) == 0 ? 29f : 24.5f;
                bool ink = radius >= 10.5f && radius <= outer;
                pixels[y * size + x] = ink
                    ? new Color32(238, 238, 230, 255)
                    : new Color32(0, 0, 0, 0);
            }
        }
        gearTex.SetPixels32(pixels);
        gearTex.Apply(false);

        var sprite = Own(Sprite.Create(
            gearTex,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f));
        sprite.name = "HKDS Mods Gear Sprite";

        int highestChromeOrder = 0;
        foreach (var renderer in frameRoot.GetComponentsInChildren<Renderer>(true))
            if (renderer != null && renderer.sortingOrder > highestChromeOrder)
                highestChromeOrder = renderer.sortingOrder;
        modsSortingOrder = highestChromeOrder + 10;

        var go = new GameObject("HKDS Mods Gear");
        go.transform.SetParent(frameRoot.transform, false);
        go.layer = ATTR_LAYER;
        gearSR = go.AddComponent<SpriteRenderer>();
        gearSR.sprite = sprite;
        gearSR.color = Color.white;
        gearSR.sortingLayerName = "Inventory";
        gearSR.sortingOrder = modsSortingOrder;
        gearSR.enabled = false;
        gearT = go.transform;
    }

    void PositionGear(float scale, float aspect, float tabY)
    {
        HollowKnightModsSession session;
        TweakMenuModel menu;
        bool ready = TryResolveModsPresenter(out session, out menu);
        if (!ready || frameRoot == null || attrCam == null || !hudGearOk ||
            !directDisplayActive || !attrCam.enabled ||
            (attrCam.cullingMask & (1 << ATTR_LAYER)) == 0)
        {
            if (gearSR != null) gearSR.enabled = false;
            if (!ready && (tweaksOpen || tweaksRoot != null || modsSession != null))
                TeardownModsPresenter();
            return;
        }

        RebindModsPresenter(session, menu);
        modsLifecycle.SynchronizeOpen(menu.IsOpen);
        tweaksOpen = modsLifecycle.IsOpen;
        if (!tweaksOpen) RestoreModsCoveredContent();
        BuildModsGear();
        if (gearT == null || gearSR == null || gearSR.sprite == null) return;

        scale = Mathf.Max(0.01f, attrCam.orthographicSize);
        aspect = Mathf.Max(0.01f, attrCam.aspect);
        float height = Mathf.Max(hudGearH * 1.28f, scale * 0.045f);
        Vector3 spriteSize = gearSR.sprite.bounds.size;
        float factor = height / Mathf.Max(0.001f, spriteSize.y);
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
        gearT.position = new Vector3(x, y, hudGearAnchor.z - 0.15f);
        gearT.rotation = Quaternion.identity;
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
        modsClosePending = false;
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
        modsClosePending = false;
        modsLifecycle.SynchronizeOpen(false);
        modsPaint.Invalidate();
        RestoreModsCoveredContent();
        tweaksOpen = modsLifecycle.IsOpen;
        if (tweaksRoot != null) tweaksRoot.SetActive(false);
    }

    void StowModsCoveredContent()
    {
        if (!modsLifecycle.RequestCoveredContentStow()) return;
        if (slideOutClone != null) StowSlideClone();
        if (mapClone != null && mapClone.activeSelf)
        {
            mapStowStamp = MapContentStamp();
            mapClone.SetActive(false);
        }
        if (invCloneCache != null && invCloneCache.activeSelf)
            invCloneCache.SetActive(false);
        if (charmCloneCache != null && charmCloneCache.activeSelf)
            charmCloneCache.SetActive(false);
    }

    void RestoreModsCoveredContent()
    {
        if (!modsLifecycle.RequestCoveredContentRestore()) return;
        RestoreModsCoveredContentCore();
    }

    void RestoreModsCoveredContentCore()
    {
        if (tab.cur == COMP_MAP)
        {
            if (mapClone != null && !mapClone.activeSelf) mapClone.SetActive(true);
            return;
        }
        if (tab.cur == COMP_INV)
        {
            paneClone = invCloneCache;
            if (invCloneCache != null && !invCloneCache.activeSelf)
                invCloneCache.SetActive(true);
            return;
        }
        if (tab.cur == COMP_CHARM)
        {
            paneClone = charmCloneCache;
            if (charmCloneCache != null && !charmCloneCache.activeSelf)
                charmCloneCache.SetActive(true);
        }
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

    ModsLabel BuildModsLabel(Component donor, string name, out GameObject labelObject)
    {
        labelObject = null;
        if (donor == null || tweaksRoot == null) return null;
        var source = donor.gameObject;
        var clone = Instantiate(source, tweaksRoot.transform);
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
        if (tweaksRoot != null && modsBuiltVisibleRows == menu.VisibleRows) return;
        DestroyModsModalView();

        Component donor = FindModsTextDonor();
        if (donor == null) return;
        tweaksRoot = new GameObject("HKDS Mods Modal");
        tweaksRoot.transform.SetParent(compRoot, false);
        tweaksRoot.layer = ATTR_LAYER;
        tweaksRoot.SetActive(false);

        GameObject label;
        modsTitleText = BuildModsLabel(donor, "Mods Title", out label);
        modsMasterText = BuildModsLabel(donor, "Mods Master", out label);
        modsGroupText = BuildModsLabel(donor, "Mods Group", out label);
        for (int i = 0; i < menu.VisibleRows; i++)
        {
            ModsLabel rowText = BuildModsLabel(donor, "Mods Row " + i, out label);
            tweakRows.Add(label);
            tweakRowTexts.Add(rowText);
            tweakRowHits.Add(default(TweakPresenterRect));
        }
        modsDetailText = BuildModsLabel(donor, "Mods Detail", out label);
        modsStatusText = BuildModsLabel(donor, "Mods Status", out label);
        modsResetText = BuildModsLabel(donor, "Mods Reset", out label);
        modsCloseText = BuildModsLabel(donor, "Mods Close", out label);

        SanitizeDetachedTmpClone(tweaksRoot);
        SetLayerRecursive(tweaksRoot.transform, ATTR_LAYER);
        tweaksRoot.SetActive(true);
        modsBuiltVisibleRows = menu.VisibleRows;
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

    void PlaceModsText(
        ModsLabel label,
        Vector3 center,
        float targetHeight,
        float maximumWidth)
    {
        if (label == null || label.Renderer == null) return;
        Renderer renderer = label.Renderer;
        Transform transform = label.Text.transform;
        transform.localScale = Vector3.one;
        Bounds bounds = renderer.bounds;
        if (bounds.size.x < 0.0001f || bounds.size.y < 0.0001f) return;
        float factor = targetHeight / Mathf.Max(0.0001f, bounds.size.y);
        if (bounds.size.x * factor > maximumWidth)
            factor = maximumWidth / Mathf.Max(0.0001f, bounds.size.x);
        transform.localScale = Vector3.one * Mathf.Max(0.0001f, factor);
        bounds = renderer.bounds;
        transform.position += new Vector3(
            center.x - bounds.center.x,
            center.y - bounds.center.y,
            center.z - bounds.center.z);
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

        IReadOnlyList<TweakDescriptor> rows = menu.CurrentRows;
        int first = menu.WindowStart;
        int end = Math.Min(rows.Count, first + menu.VisibleRows);
        stamp = HashModsPaint(stamp, rows.Count);
        for (int i = first; i < end; i++)
        {
            TweakDescriptor descriptor = rows[i];
            stamp = HashModsPaintString(stamp, descriptor.Id);
            stamp = HashModsPaint(stamp, descriptor.IsAvailable ? 1 : 0);
            stamp = HashModsPaintString(
                stamp, session.Controller.Value(descriptor.Id));
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

        float width = right - left;
        float height = top - bottom;
        float centerX = (left + right) * 0.5f;
        float z = compRoot.position.z - 0.3f;
        float lineHeight = Mathf.Max(scale * 0.04f, height * 0.045f);

        SetModsText(modsTitleText, "MODS", Color.white);
        PlaceModsText(
            modsTitleText,
            new Vector3(centerX, top - height * 0.055f, z),
            lineHeight * 1.25f,
            width * 0.5f);

        bool masterEnabled = session.Controller.MasterEnabled;
        string master = masterEnabled ? "MASTER: ON  [TOGGLE]" : "MASTER: OFF  [TOGGLE]";
        SetModsText(modsMasterText, master, masterEnabled
            ? new Color(0.92f, 1f, 0.86f, 1f)
            : new Color(0.82f, 0.82f, 0.82f, 1f));
        float masterY = top - height * 0.14f;
        PlaceModsText(modsMasterText, new Vector3(centerX, masterY, z), lineHeight, width * 0.78f);
        modsMasterHit = new TweakPresenterRect(
            left + width * 0.18f, masterY - height * 0.04f,
            width * 0.64f, height * 0.08f);

        string groupName = menu.Groups.Count == 0
            ? "NO GROUPS"
            : menu.Groups[menu.SelectedGroupIndex].ToUpperInvariant();
        string group = "<  " + groupName + "  " +
                       (menu.Groups.Count == 0 ? "0/0" :
                        (menu.SelectedGroupIndex + 1) + "/" + menu.Groups.Count) +
                       "  >";
        float groupY = top - height * 0.225f;
        SetModsText(modsGroupText, group, new Color(0.82f, 0.88f, 1f, 1f));
        PlaceModsText(modsGroupText, new Vector3(centerX, groupY, z), lineHeight, width * 0.84f);
        modsPreviousGroupHit = new TweakPresenterRect(
            left, groupY - height * 0.045f, width * 0.28f, height * 0.09f);
        modsNextGroupHit = new TweakPresenterRect(
            right - width * 0.28f, groupY - height * 0.045f,
            width * 0.28f, height * 0.09f);

        IReadOnlyList<TweakDescriptor> rows = menu.CurrentRows;
        int first = menu.WindowStart;
        int visible = Mathf.Min(menu.VisibleRows, Mathf.Max(0, rows.Count - first));
        float rowStep = height * 0.067f;
        float firstRowY = top - height * 0.305f;
        for (int i = 0; i < tweakRows.Count; i++)
        {
            bool show = i < visible && first + i < rows.Count;
            GameObject rowObject = tweakRows[i];
            if (rowObject != null && rowObject.activeSelf != show)
                rowObject.SetActive(show);
            if (!show)
            {
                tweakRowHits[i] = default(TweakPresenterRect);
                continue;
            }

            int rowIndex = first + i;
            TweakDescriptor descriptor = rows[rowIndex];
            bool selected = rowIndex == menu.SelectedRowIndex;
            string currentValue = descriptor.IsAvailable
                ? FriendlyModsValue(session.Controller.Value(descriptor.Id))
                : "DEFERRED";
            string row = (selected ? "> " : "  ") +
                         descriptor.Title.ToUpperInvariant() + "    " + currentValue;
            Color color;
            if (!descriptor.IsAvailable)
                color = selected
                    ? new Color(1f, 0.72f, 0.38f, 1f)
                    : new Color(0.64f, 0.50f, 0.36f, 1f);
            else if (!masterEnabled)
                color = selected
                    ? new Color(0.78f, 0.78f, 0.78f, 1f)
                    : new Color(0.54f, 0.54f, 0.54f, 1f);
            else
                color = selected ? Color.white : new Color(0.72f, 0.76f, 0.82f, 1f);
            float rowY = firstRowY - i * rowStep;
            SetModsText(tweakRowTexts[i], row, color);
            PlaceModsText(tweakRowTexts[i], new Vector3(centerX, rowY, z),
                          lineHeight * 0.88f, width * 0.9f);
            tweakRowHits[i] = new TweakPresenterRect(
                left, rowY - rowStep * 0.48f, width, rowStep * 0.96f);
        }

        TweakDescriptor selectedRow = menu.Selected;
        string detail = selectedRow == null
            ? "NO MOD IS SELECTED."
            : selectedRow.Title.ToUpperInvariant() + "\n" + selectedRow.Description;
        string status = menu.Message;
        if (selectedRow != null && !selectedRow.IsAvailable)
        {
            detail += "\n" + selectedRow.TrackingId + ": " + selectedRow.UnavailableReason;
            if (string.IsNullOrEmpty(status))
                status = selectedRow.TrackingId + ": " + selectedRow.UnavailableReason;
        }
        else if (string.IsNullOrEmpty(status) && !masterEnabled)
            status = "MASTER IS OFF - ENABLE IT TO CHANGE AVAILABLE MODS.";
        if (string.IsNullOrEmpty(status))
            status = "TAP A SELECTED AVAILABLE ROW AGAIN TO CHANGE ITS VALUE.";

        detail = WrapModsText(detail, 62);
        status = WrapModsText(status, 70);
        SetModsText(modsDetailText, detail, selectedRow != null && !selectedRow.IsAvailable
            ? new Color(1f, 0.72f, 0.38f, 1f)
            : new Color(0.86f, 0.88f, 0.92f, 1f));
        PlaceModsText(modsDetailText,
                      new Vector3(centerX, bottom + height * 0.225f, z),
                      lineHeight * 1.8f, width * 0.88f);
        SetModsText(modsStatusText, status, menu.MessageIsError
            ? new Color(1f, 0.42f, 0.38f, 1f)
            : new Color(0.66f, 0.78f, 0.9f, 1f));
        PlaceModsText(modsStatusText,
                      new Vector3(centerX, bottom + height * 0.12f, z),
                      lineHeight * 1.25f, width * 0.88f);

        float actionY = bottom + height * 0.045f;
        SetModsText(modsResetText, "RESET", new Color(0.88f, 0.88f, 0.88f, 1f));
        SetModsText(modsCloseText, "CLOSE / BACK", Color.white);
        PlaceModsText(modsResetText,
                      new Vector3(left + width * 0.27f, actionY, z),
                      lineHeight, width * 0.3f);
        PlaceModsText(modsCloseText,
                      new Vector3(right - width * 0.27f, actionY, z),
                      lineHeight, width * 0.38f);
        modsResetHit = new TweakPresenterRect(
            left, bottom, width * 0.5f, height * 0.09f);
        modsCloseHit = new TweakPresenterRect(
            centerX, bottom, width * 0.5f, height * 0.09f);
        modsHitMap = new TweakPresenterHitMap(
            modsCloseHit,
            modsMasterHit,
            modsPreviousGroupHit,
            modsNextGroupHit,
            modsResetHit,
            tweakRowHits);
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

    void HandleModsCleanTap(TweakMenuModel menu, Vector2 world)
    {
        if (menu == null || modsHitMap == null) return;
        TweakPresenterAction action = TweakPresenterInteraction.ResolveAction(
            new TweakPresenterPoint(world.x, world.y), modsHitMap, menu);
        switch (action.Kind)
        {
            case TweakPresenterActionKind.Close:
                // Keep Menu.IsOpen authoritative through the raw-tap poll later
                // this frame, then close at the start of the next presenter tick.
                modsClosePending = true;
                return;
            case TweakPresenterActionKind.ToggleMaster:
                menu.ToggleMaster();
                return;
            case TweakPresenterActionKind.PreviousGroup:
                menu.MoveGroup(-1);
                return;
            case TweakPresenterActionKind.NextGroup:
                menu.MoveGroup(1);
                return;
            case TweakPresenterActionKind.SelectRow:
                menu.MoveRow(action.RowIndex - menu.SelectedRowIndex);
                return;
            case TweakPresenterActionKind.CycleSelected:
                menu.CycleSelected();
                return;
            case TweakPresenterActionKind.Reset:
                menu.Reset();
                return;
            default:
                return;
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
            if (modsClosePending)
            {
                CloseTweaksPane();
                return;
            }
            if (!tweaksOpen)
            {
                RestoreModsCoveredContent();
                if (tweaksRoot != null) tweaksRoot.SetActive(false);
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
        tweakRowHits.Clear();
        modsTitleText = null;
        modsMasterText = null;
        modsGroupText = null;
        modsDetailText = null;
        modsStatusText = null;
        modsResetText = null;
        modsCloseText = null;
        modsBuiltVisibleRows = -1;
        modsMasterHit = default(TweakPresenterRect);
        modsPreviousGroupHit = default(TweakPresenterRect);
        modsNextGroupHit = default(TweakPresenterRect);
        modsResetHit = default(TweakPresenterRect);
        modsCloseHit = default(TweakPresenterRect);
        modsHitMap = null;
        modsPaint.Invalidate();
    }

    void ClearModsFrameReferences()
    {
        gearT = null;
        gearSR = null;
        gearTex = null;
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
        tweakRowHits.Clear();
        modsTitleText = null;
        modsMasterText = null;
        modsGroupText = null;
        modsDetailText = null;
        modsStatusText = null;
        modsResetText = null;
        modsCloseText = null;
        modsBuiltVisibleRows = -1;
        modsMasterHit = default(TweakPresenterRect);
        modsPreviousGroupHit = default(TweakPresenterRect);
        modsNextGroupHit = default(TweakPresenterRect);
        modsResetHit = default(TweakPresenterRect);
        modsCloseHit = default(TweakPresenterRect);
        modsHitMap = null;
        modsPaint.Invalidate();
        Sprite gearSprite = gearSR != null ? gearSR.sprite : null;
        if (gearT != null) Destroy(gearT.gameObject);
        if (gearSprite != null)
        {
            frameAssets.Remove(gearSprite);
            Destroy(gearSprite);
        }
        if (gearTex != null)
        {
            frameAssets.Remove(gearTex);
            Destroy(gearTex);
        }
        gearT = null;
        gearSR = null;
        gearTex = null;
        modsSortingOrder = 0;
        hudGearOk = false;
        hudGearH = 0f;
        hudGearAnchor = Vector3.zero;
        hudFpsB = default(Bounds);
        modsClosePending = false;
        modsSession = null;
        modsMenu = null;
        modsInteraction.ResetCleanTap(int.MinValue);
    }
}
