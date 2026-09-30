using System;
using System.IO;
using Xunit;

namespace SharedPatches.Tests;

public sealed class NativeSkinsSourceContractTests
{
    [Theory]
    [InlineData("HollowKnightNativeModsMenu.cs", "HollowKnightNativeModsMenu")]
    [InlineData("SilksongNativeModsMenu.cs", "SilksongNativeModsMenu")]
    public void ExistingOptionsOwnerOwnsBothRoutes(string file, string owner)
    {
        string source = Source(file);

        Assert.Contains("enum NativeMenuRoute { Mods, Skins }", source, StringComparison.Ordinal);
        Assert.Contains("NativeMenuRoute.Mods", source, StringComparison.Ordinal);
        Assert.Contains("NativeMenuRoute.Skins", source, StringComparison.Ordinal);
        Assert.Contains("\"MODS\"", source, StringComparison.Ordinal);
        Assert.Contains("\"SKINS\"", source, StringComparison.Ordinal);
        Assert.Contains("Open(NativeMenuRoute route)", source, StringComparison.Ordinal);
        Assert.Equal(1, Count(source, "public sealed class " + owner + " : MonoBehaviour"));
    }

    [Theory]
    [InlineData("HollowKnightNativeModsMenu.cs")]
    [InlineData("SilksongNativeModsMenu.cs")]
    public void SkinsRouteUsesBoundedProfileCheckedBridgeAndRefreshesAfterEveryMutation(string file)
    {
        string source = Source(file);

        Assert.Contains("SkinLibraryRuntimeBridge", source, StringComparison.Ordinal);
        Assert.Contains("readMenuSnapshot", source, StringComparison.Ordinal);
        Assert.Contains("setMode", source, StringComparison.Ordinal);
        Assert.Contains("setSpriteScope", source, StringComparison.Ordinal);
        Assert.Contains("confirmPack", source, StringComparison.Ordinal);
        Assert.Contains("ApplySkinMutation", source, StringComparison.Ordinal);
        Assert.Contains("RefreshSkinMenu();", source, StringComparison.Ordinal);
        Assert.Contains("InvalidateSkinLibrary()", source, StringComparison.Ordinal);
        Assert.Contains("MaximumSkinSnapshotBytes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("library.json", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PlayerPrefs", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LineFileTweakStore", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HollowKnightNativeModsMenu.cs")]
    [InlineData("SilksongNativeModsMenu.cs")]
    public void SkinsRouteHasControllerPointerFocusAndNativeCancelSemantics(string file)
    {
        string source = Source(file);

        Assert.Contains("MoveDirection.Left", source, StringComparison.Ordinal);
        Assert.Contains("MoveDirection.Right", source, StringComparison.Ordinal);
        Assert.Contains("IPointerClickHandler", source, StringComparison.Ordinal);
        Assert.Contains("ISelectHandler", source, StringComparison.Ordinal);
        Assert.Contains("ICancelHandler", source, StringComparison.Ordinal);
        Assert.Contains("SelectSkin", source, StringComparison.Ordinal);
        Assert.Contains("FocusSkin", source, StringComparison.Ordinal);
        Assert.Contains("Close();", source, StringComparison.Ordinal);
        Assert.Contains("UIState.PAUSED", source, StringComparison.Ordinal);
        Assert.Contains("BindingIsAlive", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HollowKnightNativeModsMenu.cs")]
    [InlineData("SilksongNativeModsMenu.cs")]
    public void SkinsRouteReadsOnlyTheBoundedLatestObservationAndPresentsEvidenceState(string file)
    {
        string source = Source(file);

        Assert.Equal(1, Count(source, "readMenuSnapshot"));
        Assert.Contains("if (_skinTransport.Due(Time.unscaledTime)) RefreshSkinMenu();", source, StringComparison.Ordinal);
        Assert.Contains("_skinTransport.CompleteEvidence(wire.evidenceState);", source, StringComparison.Ordinal);
        Assert.Contains("_skinTransport.Cancel();", source, StringComparison.Ordinal);
        Assert.Contains("WireSkinObservation", source, StringComparison.Ordinal);
        Assert.Contains("evidenceState", source, StringComparison.Ordinal);
        Assert.Contains("TERMINAL", source, StringComparison.Ordinal);
        Assert.Contains("PENDING", source, StringComparison.Ordinal);
        Assert.Contains("STALE", source, StringComparison.Ordinal);
        Assert.Contains("operationGeneration", source, StringComparison.Ordinal);
        Assert.Contains("resultingConfigSha256", source, StringComparison.Ordinal);
        Assert.Contains("string terminalCorrelation = (wire.observation.featureId ?? \"\")", source,
            StringComparison.Ordinal);
        Assert.Contains("wire.observation.operationId", source, StringComparison.Ordinal);
        Assert.Contains("wire.observation.operationGeneration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("outcomes", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SkinOperationJournal", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateSkin", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HollowKnightSkinLibrary.cs")]
    [InlineData("SilksongSkinLibrary.cs")]
    public void EveryPendingRotationOutcomeUsesTheCorrelatedRotationReport(string file)
    {
        string source = Source(file);

        int start = source.IndexOf("bool ReportManaged", StringComparison.Ordinal);
        int end = source.IndexOf("#endif", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        string transport = source.Substring(start, end - start);
        Assert.Contains("if (observation.PendingOccurrence > 0)\n", transport, StringComparison.Ordinal);
        Assert.DoesNotContain("observation.PendingOccurrence > 0 &&", transport, StringComparison.Ordinal);
        Assert.Contains("CallStatic<bool>(\"reportRotation\"", transport, StringComparison.Ordinal);
        Assert.Contains("observation.SaveSlot", transport, StringComparison.Ordinal);
        Assert.Contains("SkinLibraryTransport.Decode", source, StringComparison.Ordinal);
        int warningStart = transport.IndexOf("string warning", StringComparison.Ordinal);
        int warningEnd = transport.IndexOf("lastWarning = warning;", warningStart, StringComparison.Ordinal);
        Assert.True(warningStart >= 0 && warningEnd > warningStart);
        Assert.DoesNotContain("observation.Detail", transport.Substring(warningStart, warningEnd - warningStart), StringComparison.Ordinal);
        Assert.Contains("string warning = failed ? observation.Status : null;", transport, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HollowKnightSkinLibrary.cs")]
    [InlineData("SilksongSkinLibrary.cs")]
    public void EvidenceWarningsDoNotLogUnredactedRuntimeDetail(string file)
    {
        string source = Source(file);
        int start = source.IndexOf("bool ReportManaged", StringComparison.Ordinal);
        int transport = source.IndexOf("if (observation.PendingOccurrence > 0)", start, StringComparison.Ordinal);
        string warning = source.Substring(start, transport - start);
        Assert.Contains("failed ? observation.Status : null", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("observation.Detail", warning, StringComparison.Ordinal);
        Assert.Contains("Debug.LogWarning", warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HollowKnightModsRuntime.cs")]
    [InlineData("SilksongModsRuntime.cs")]
    public void RuntimeExposesOnlyInvalidationNotSkinPersistence(string file)
    {
        string source = Source(file);

        Assert.Contains("internal void InvalidateSkinLibrary()", source, StringComparison.Ordinal);
        Assert.Contains("skinLibrary.Invalidate()", source, StringComparison.Ordinal);
        int boundary = source.IndexOf("skinLibrary.AdmitSaveBoundary();", StringComparison.Ordinal);
        int visual = source.IndexOf("Skins.Tick();", StringComparison.Ordinal);
        int authority = source.IndexOf("skinLibrary.Tick();", StringComparison.Ordinal);
        Assert.True(boundary >= 0 && boundary < visual && visual < authority, "Save admission must precede both profiles' visual tick.");
        Assert.DoesNotContain("library.json", source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("HollowKnightModsRuntime.cs")]
    [InlineData("SilksongModsRuntime.cs")]
    public void ResumeInvalidatesSettledSkinAuthorityWithoutPolling(string file)
    {
        string source = Source(file);
        Assert.Contains("void OnApplicationPause(bool paused)", source, StringComparison.Ordinal);
        Assert.Contains("if (!paused) InvalidateSkinLibrary();", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HollowKnight")]
    [InlineData("Silksong")]
    public void Save_visual_gate_precedes_discovery_without_reinterpreting_normal_refresh_permission(string profile)
    {
        string runtime = Source(profile + "SkinRuntime.cs");
        string owner = Source(profile + "SkinLibrary.cs");
        int tick = runtime.IndexOf("public void Tick()", StringComparison.Ordinal);
        int gate = runtime.IndexOf("SaveVisualRefreshAllowed?.Invoke()", tick, StringComparison.Ordinal);
        string discovery = profile == "HollowKnight" ? "refreshSchedule.Tick(" : "ownerRefresh.ShouldPoll(";
        int refresh = runtime.IndexOf(discovery, tick, StringComparison.Ordinal);
        Assert.True(tick >= 0 && gate > tick && gate < refresh);
        Assert.Contains("runtime.SaveVisualRefreshAllowed = () => SaveVisualRefreshAllowed;", owner, StringComparison.Ordinal);
        Assert.Contains("controller.InvalidateSave();", owner, StringComparison.Ordinal);
        if (profile == "Silksong") Assert.Contains("VisualRefreshRequired(normalRefresh)", runtime, StringComparison.Ordinal);
        else Assert.True(runtime.IndexOf("hero = nextHero; hud = nextHud;", tick, StringComparison.Ordinal) < gate);
    }

    static int Count(string value, string token)
    {
        int count = 0;
        for (int index = 0; (index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0;
             index += token.Length) count++;
        return count;
    }

    static string Source(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "native-skin-contracts", name));
}
