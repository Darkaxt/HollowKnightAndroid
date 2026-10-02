using System;
using System.Linq;
using DualSouls.Mods.Silksong;
using Native = ModsRuntimeNativeModel;
using Xunit;
namespace SharedPatches.Tests;
[CollectionDefinition("Mods runtime native model", DisableParallelization = true)]
public sealed class ModsRuntimeNativeCollection { }
[Collection("Mods runtime native model")]
public sealed class ModsRuntimeGenerationTests : IDisposable
{
    public ModsRuntimeGenerationTests() { Drain(); SetNative(false); Native.PlayerData.instance = new(); Native.HeroController.instance = new(); Native.GameManager.SilentInstance = new(); }
    [Fact]
    public void RecreatedFullRuntimeCapturesOnlyItsCurrentCheatManagerBaseline()
    {
        Start();
        var first = Native.SilksongModsRuntime.Current;
        ApplyAll(first);
        first.gameObject.Destroy();
        AssertNative(false);
        SetNative(true);
        Start();
        var second = Native.SilksongModsRuntime.Current;
        ApplyAll(second);
        second.gameObject.Destroy();
        AssertNative(true);
    }
    [Fact]
    public void SameRuntimeResetNeverRecapturesItsOwnModifiedState()
    {
        Start(); var runtime = Native.SilksongModsRuntime.Current;
        ApplyAll(runtime);
        runtime.Session.Controller.Reset();
        AssertNative(false);
        ApplyAll(runtime);
        runtime.gameObject.Destroy();
        AssertNative(false);
    }
    [Fact]
    public void FailedRetirementBlocksReplacementUntilExactOwnerRestores()
    {
        Start(); var first = Native.SilksongModsRuntime.Current;
        ApplyAll(first); Native.SilksongGameplayFeatures.FailRestore = true;
        first.gameObject.Destroy();
        Assert.True(Native.SilksongModsRestorePump.BlocksReplacement);
        Native.SilksongModsRuntime.EnsureStarted();
        Assert.Null(Native.SilksongModsRuntime.Current);
        Native.SilksongGameplayFeatures.FailRestore = false;
        var pump = Native.GameObject.Components.OfType<Native.SilksongModsRestorePump>().Single();
        Native.GameObject.Invoke(pump,"Update");
        Assert.False(Native.SilksongModsRestorePump.BlocksReplacement);
        AssertNative(false);
        SetNative(true); Start(); ApplyAll(Native.SilksongModsRuntime.Current);
        Native.SilksongModsRuntime.Current.gameObject.Destroy(); AssertNative(true);
    }
    [Fact]
    public void SecondApiCannotCaptureOrRestoreAnotherLiveOwnerBaseline()
    {
        var owner = new Native.SilksongGameTweakApi(); owner.CaptureBaseline();
        owner.SetDamageMode(SilksongDamageMode.Invincible);
        var other = new Native.SilksongGameTweakApi();
        Assert.Throws<InvalidOperationException>(() => other.CaptureBaseline());
        other.RestoreBaseline();
        Assert.Equal(Native.CheatManager.InvincibilityStates.FullInvincible, Native.CheatManager.Invincibility);
        owner.RestoreBaseline();
    }
    [Fact]
    public void ApplyFailureRollbackRetainsSameGenerationBaseline()
    {
        Start(); var runtime = Native.SilksongModsRuntime.Current;
        ApplyAll(runtime); Native.LineFileTweakStore.FailFlush = true;
        var result = runtime.Session.Controller.Set("instant_dialogue", "off");
        Assert.True(result.Pending);
        Native.GameObject.Invoke(runtime,"Update");
        Assert.False(runtime.Session.Controller.MutationsAvailable);
        Native.LineFileTweakStore.FailFlush = false;
        runtime.gameObject.Destroy(); AssertNative(false);
    }
    [Fact]
    public void CompletedRetainedSessionStillBlocksReplacementUntilOwnerRetirement()
    {
        Start(); var first = Native.SilksongModsRuntime.Current;
        var session = first.Session;
        ApplyAll(first); Native.SilksongGameplayFeatures.FailRestore = true;
        first.gameObject.Destroy();
        Native.SilksongGameplayFeatures.FailRestore = false;
        session.Tick(); Assert.True(session.TeardownComplete);
        Assert.True(Native.SilksongModsRestorePump.BlocksReplacement);
        Native.SilksongModsRuntime.EnsureStarted();
        Assert.Null(Native.SilksongModsRuntime.Current);
        var pump = Native.GameObject.Components.OfType<Native.SilksongModsRestorePump>().Single();
        Native.GameObject.Invoke(pump,"Update");
        Assert.False(Native.SilksongModsRestorePump.BlocksReplacement);
        SetNative(true); Start(); ApplyAll(Native.SilksongModsRuntime.Current);
        Native.SilksongModsRuntime.Current.gameObject.Destroy(); AssertNative(true);
    }
    [Fact]
    public void OutgoingApiRestoreAndRetirementCannotClearReplacementOwnedState()
    {
        var old = new Native.SilksongGameTweakApi(); old.CaptureBaseline();
        old.SetDamageMode(SilksongDamageMode.Invincible);
        old.RestoreBaseline(); old.RetireBaseline();
        SetNative(true);
        var current = new Native.SilksongGameTweakApi(); current.CaptureBaseline();
        current.SetDamageMode(SilksongDamageMode.Invincible);
        current.SetOneHitKills(true); current.SetBossRetry(true);
        var prepare = Native.SilksongGameplayFeatures.StateTransferPreparation;
        old.RestoreBaseline(); old.RetireBaseline();
        Assert.Equal(Native.CheatManager.InvincibilityStates.FullInvincible, Native.CheatManager.Invincibility);
        Assert.Same(prepare, Native.SilksongGameplayFeatures.StateTransferPreparation);
        Assert.Throws<InvalidOperationException>(() => old.CaptureBaseline());
        current.RestoreBaseline(); current.RetireBaseline(); AssertNative(true);
    }
    [Fact]
    public void DuplicateRuntimeDestructionDoesNotRestoreCurrentGeneration()
    {
        Start(); var current = Native.SilksongModsRuntime.Current; ApplyAll(current);
        var duplicateObject = new Native.GameObject("duplicate");
        duplicateObject.AddComponent<Native.SilksongModsRuntime>();
        Assert.Same(current, Native.SilksongModsRuntime.Current);
        Assert.Equal(Native.CheatManager.InvincibilityStates.FullInvincible,Native.CheatManager.Invincibility);
        current.gameObject.Destroy(); AssertNative(false);
    }
    static void Start() { Native.SilksongModsRuntime.EnsureStarted(); Native.GameObject.Invoke(Native.SilksongModsRuntime.Current,"Update"); Assert.True(Native.SilksongModsRuntime.Current.Session.IsReady); }
    static void ApplyAll(Native.SilksongModsRuntime runtime)
    {
        foreach (var (id,value) in new[] {("damage_received","invincible"),("one_hit_kills","on"),("unlimited_silk","on"),("equip_anywhere","on"),("instant_dialogue","on"),("disable_world_rumble","on"),("ignore_frost_slowdown","on")})
            Assert.True(runtime.Session.Controller.Set(id,value).Success);
        Native.GameObject.Invoke(runtime,"Update");
    }
    static void SetNative(bool value)
    {
        Native.CheatManager.Invincibility = value ? Native.CheatManager.InvincibilityStates.PreventDeath : Native.CheatManager.InvincibilityStates.Off;
        Native.CheatManager.NailDamage = value ? Native.CheatManager.NailDamageStates.InstaKill : Native.CheatManager.NailDamageStates.Normal;
        Native.CheatManager.IsSilkDrainDisabled = Native.CheatManager.CanChangeEquipsAnywhere = Native.CheatManager.IsTextPrintSkipEnabled = Native.CheatManager.IsWorldRumbleDisabled = Native.CheatManager.IsFrostDisabled = value;
    }
    static void AssertNative(bool value)
    {
        Assert.Equal(value ? Native.CheatManager.InvincibilityStates.PreventDeath : Native.CheatManager.InvincibilityStates.Off, Native.CheatManager.Invincibility);
        Assert.Equal(value ? Native.CheatManager.NailDamageStates.InstaKill : Native.CheatManager.NailDamageStates.Normal, Native.CheatManager.NailDamage);
        Assert.Equal(value, Native.CheatManager.IsSilkDrainDisabled); Assert.Equal(value,Native.CheatManager.CanChangeEquipsAnywhere);
        Assert.Equal(value, Native.CheatManager.IsTextPrintSkipEnabled); Assert.Equal(value,Native.CheatManager.IsWorldRumbleDisabled); Assert.Equal(value,Native.CheatManager.IsFrostDisabled);
    }
    public void Dispose() => Drain();
    static void Drain()
    {
        Native.LineFileTweakStore.FailFlush = false; Native.SilksongGameplayFeatures.FailRestore = false;
        Native.SilksongModsRuntime.Current?.gameObject.Destroy();
        foreach (var pump in Native.GameObject.Components.OfType<Native.SilksongModsRestorePump>().ToArray()) Native.GameObject.Invoke(pump,"Update");
        // Model isolation only: static ownership is reset between tests, never during production execution.
        foreach (var field in typeof(Native.SilksongGameTweakApi).GetFields(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic))
            if (!field.IsInitOnly) field.SetValue(null, field.FieldType.IsValueType ? Activator.CreateInstance(field.FieldType) : null);
    }
}
