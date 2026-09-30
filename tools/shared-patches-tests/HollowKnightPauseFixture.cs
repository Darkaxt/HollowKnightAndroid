#pragma warning disable CS0649 // Unrelated engine/config inputs intentionally stay at their native defaults.
using System;
using System.Collections.Generic;

// Only unrelated engine calls are stand-ins. The generator compiles Tick, HUD routing,
// role masks, backdrop policy and LogoTick verbatim from the shipped sources.
namespace HkPauseContracts;

internal partial class HKDualScreen
{
    internal const int HUD_LAYER = 6, ATTR_LAYER = 3, TUT_LAYER = 7, UI_LAYER = 5;
    internal int hudLayer = HUD_LAYER, tutLayer = TUT_LAYER;
    internal readonly Layout cfg = new();
    internal readonly GameCameras Cameras = new();
    internal readonly GameManager Manager = new();
    internal Camera hudCam2 = new(), attrCam = new(), promptCam = new(), clearCam = new(), bgCaptureCam = new();
    internal readonly Transport transport = new();
    internal readonly Dimmer bgDimmer = new();
    internal bool directDisplayActive = true, dsWas = true, bgShow, popupBlack;
    internal readonly GameObject logoGo = new();
    internal bool logoNeedsBake, fleurBaked = true, wired = true, wasAtMenu, creditNow, loreDialogueOpen;
    internal bool HudFaded, Popup, InventoryOpen, Credit;
    internal int compOn = -1, lastSkinStamp, bgCullMask = 1, BOTTOM_W = 1240, BOTTOM_H = 1080;
    internal int hudLayerApplied = -1;
    internal Transform hudRootApplied;
    internal readonly Tab tab = new();
    internal GameObject mapClone = new(), invCloneCache = new(), charmCloneCache = new(), frameRoot = new();
    internal readonly List<int> routedLayers = new();
    internal int Teardowns, Updates, TouchPolls, Prewarms, FrameTeardowns;
    internal readonly GameObject Health = new(), Soul = new(), Geo = new(), Frame = new(), Controls = new(), Heal = new();

    internal HKDualScreen()
    {
        GameObject anchor = new();
        GameObject middle = new();
        middle.transform.SetParent(anchor.transform);
        Cameras.hudCanvas.transform.SetParent(middle.transform);
        foreach (GameObject go in new[] { Health, Soul, Geo }) go.transform.SetParent(Cameras.hudCanvas.transform);
        foreach (GameObject go in new[] { mapClone, invCloneCache, charmCloneCache, frameRoot, Frame, Controls })
            go.layer = ATTR_LAYER;
        Heal.layer = TUT_LAYER;
        logoGo.layer = HUD_LAYER;
        promptCam.cullingMask = 1 << TUT_LAYER;
    }

    internal void Step(bool paused, bool inventory = false)
    {
        Manager.gameState = paused ? GlobalEnums.GameState.PAUSED : GlobalEnums.GameState.PLAYING;
        InventoryOpen = inventory;
        Time.frameCount++;
        Tick();
    }

    internal bool Drawn(GameObject go) => go != null && go.activeInHierarchy &&
        ((hudCam2.enabled && (hudCam2.cullingMask & (1 << go.layer)) != 0) ||
         (attrCam.enabled && (attrCam.cullingMask & (1 << go.layer)) != 0) ||
         (promptCam.enabled && (promptCam.cullingMask & (1 << go.layer)) != 0));

    bool TryResolveSceneManagers(out GameCameras gc, out GameManager gm) { gc = Cameras; gm = Manager; return true; }
    bool TryRunLowerHudFixture(GameCameras gc, GameManager gm) => false;
    void LoadConfig(bool force) { }
    void SyncDumpHook() { }
    void PushInputSettings() { }
    bool PollInventoryToggle(GameCameras gc, bool paused) => InventoryOpen;
    internal static bool GameInventoryOpen;
    void PollCompanionCombo() { }
    void StripPrivateLayers() { }
    bool HudFadedInGameplay(GameCameras gc) => HudFaded;
    void TeardownCompanion() { Teardowns++; mapClone = invCloneCache = charmCloneCache = frameRoot = null; tab.built = -1; }
    void RestoreRoutedLayers() { }
    void RestoreNameCard() { }
    void PublishLegacyLifebloodFlashMode() { }
    void FixWipedTilemap() { }
    internal GameObject RoutedHudPrompt;
    void MainGameHooks(GameCameras gc)
    {
        // Model the existing routing hook's effect, not any pause/mask policy.
        if (RoutedHudPrompt != null) SetLayerRecursive(RoutedHudPrompt.transform, tutLayer);
    }
    bool CreditShowing() => Credit;
    bool BottomOverlayActive() => Popup;
    void SetupBgCapture(GameCameras gc) { }
    void ApplyMainFocusLift() { }
    void FixEmptyCounterDetail() { }
    void InvalidateCompanionClones() => throw new InvalidOperationException("unexpected skin invalidation");
    void InvStateDiag() { }
    void DebugPopupTick() { }
    void SyncBottomFade() { }
    void PrewarmTick() => Prewarms++;
    void UpdateCompanion(Camera src) => Updates++;
    void PollTouch() => TouchPolls++;
    void CenterAttribution() { }
    void ApplyHalo() { }
    void SetupLogo() => throw new InvalidOperationException("unexpected logo rebake");
    void TryBakeTabFleurs() => throw new InvalidOperationException("unexpected fleur rebake");
    void TeardownFrame() => FrameTeardowns++;
    void PushToBottom() { }
    void CenterDialogue() { }
    void CenterTutorial() { }
    void Dbg(string text) { }
    void SetLayerRecursive(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root.Children) SetLayerRecursive(child, layer);
    }
}

internal sealed class Layout
{
    internal int dualScreen = 1, companion = 1, compTab = 1, debug, compPopupBlack = 1, bgMask;
    internal float dim = .5f, bgBlur = 2, panX, panY, zoomMul = 1, creditScale = 1;
}
internal sealed class Tab { internal int tap = 2, cur = 2, built = 2, lastCfg = 1; }
internal sealed class Transport { internal int TargetDisplayIndex = 1; internal void SetProductEnabled(bool requested) { } }
internal sealed class Dimmer { internal float Brightness, BlurFactor; }
internal sealed class GameCameras
{
    internal GameObject hudCanvas = new();
    internal readonly Camera hudCamera = new(), mainCamera = new();
}
internal sealed class GameManager
{
    internal GlobalEnums.GameState gameState = GlobalEnums.GameState.PLAYING;
    internal string MenuState = "GAMEPLAY";
    // Exact HK pause authority: Options changes menu state, not GameState.PAUSED.
    internal bool IsGamePaused() => gameState == GlobalEnums.GameState.PAUSED;
}
internal static class GlobalEnums { internal enum GameState { PLAYING, PAUSED, MAIN_MENU } }
internal static class HkStageHooks
{
    internal static int SkinStamp;
    internal static bool BlackBackground;
    internal static void Tick(Layout cfg, bool debug) { }
}
internal static class Time { internal static int frameCount; }
internal static class Mathf { internal static float Max(float a, float b) => Math.Max(a, b); }
internal enum CameraClearFlags { SolidColor, Depth }
internal struct Color { internal static readonly Color black = new(); }
internal struct Vector3
{
    internal float x, y, z;
    internal Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
}
internal sealed class Camera
{
    internal bool enabled = true;
    internal int cullingMask, targetDisplay;
    internal float aspect = 1, orthographicSize = 8, depth;
    internal object targetTexture;
    internal Color backgroundColor;
    internal CameraClearFlags clearFlags;
    internal readonly Transform transform = new();
    internal void CopyFrom(Camera source) { }
}
internal sealed class GameObject
{
    internal bool activeSelf = true;
    internal bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
    internal int layer;
    internal readonly Transform transform;
    internal GameObject() { transform = new(this); }
    internal void SetActive(bool active) => activeSelf = active;
}
internal sealed class Transform
{
    internal readonly GameObject gameObject;
    internal readonly List<Transform> Children = new();
    internal Transform parent;
    internal Vector3 position;
    internal object rotation;
    internal Transform(GameObject go = null) { gameObject = go; }
    internal void SetParent(Transform value) { parent = value; value.Children.Add(this); }
}
