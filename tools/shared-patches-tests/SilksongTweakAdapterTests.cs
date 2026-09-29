using System.Collections.Generic;
using System.Linq;
using DualSouls.Mods;
using DualSouls.Mods.Silksong;
using Xunit;

namespace SharedPatches.Tests;

public sealed class SilksongTweakAdapterTests
{
    [Fact]
    public void CatalogStartsWithExactRequiredContractAndSilksongNamesThenAppendsExtras()
    {
        var rows = new SilksongTweakAdapter(new RecordingApi()).Descriptors;
        TweakDescriptor[] required = rows.Take(26).ToArray();
        string[] contractIds =
        {
            "black_background",
            "run_speed", "fast_transitions", "auto_map", "innate_compass", "bench_teleport", "secret_radar",
            "nail_damage", "damage_taken", "damage_cap", "one_hit_kills", "unlimited_soul",
            "enemy_health_bars", "damage_numbers", "boss_retry",
            "equip_anywhere", "charm_costs", "unlimited_notches",
            "state_slot", "save_to_slot", "load_from_slot", "delete_slot",
            "geo_magnet", "keep_geo_on_death", "journal_one_kill", "geo_multiplier",
        };

        Assert.Equal(contractIds, required.Select(row => row.ContractId));
        Assert.Equal("NEEDLE DAMAGE", required.Single(row => row.ContractId == "nail_damage").Title);
        Assert.Equal("UNLIMITED SILK", required.Single(row => row.ContractId == "unlimited_soul").Title);
        Assert.All(required.Skip(15).Take(3), row => Assert.Equal("CRESTS & TOOLS", row.Group));
        Assert.Equal("ROSARY MAGNET", required.Single(row => row.ContractId == "geo_magnet").Title);
        Assert.Equal("KEEP ROSARIES ON DEATH", required.Single(row => row.ContractId == "keep_geo_on_death").Title);
        Assert.Equal("ROSARY MULTIPLIER", required.Single(row => row.ContractId == "geo_multiplier").Title);
        Assert.Equal("damage_received", required.Single(row => row.ContractId == "damage_taken").Id);
        Assert.Equal("unlimited_silk", required.Single(row => row.ContractId == "unlimited_soul").Id);
        Assert.Equal(new[] { "instant_dialogue", "disable_world_rumble", "ignore_frost_slowdown" }, rows.Skip(26).Select(row => row.Id));
        Assert.All(required, row => Assert.True(row.IsAvailable, row.Id + " must be available"));
        Assert.DoesNotContain(rows, row => row.Id == "skins");
        Assert.DoesNotContain(rows, row => row.Id == "state_slots");
    }

    [Fact]
    public void EveryRequiredRowHasARealDispatch()
    {
        var api = new RecordingApi();
        var adapter = new SilksongTweakAdapter(api);

        foreach (TweakDescriptor row in adapter.Descriptors.Take(26))
        {
            var result = adapter.Apply(row.Id, row.DefaultValue);
            Assert.True(result.Success, row.Id + ": " + result.Error);
        }
    }

    [Fact]
    public void EveryAllowedValueUsesTheExactTypedRuntimeOwner()
    {
        var expected = new Dictionary<string, Dictionary<string, string>>
        {
            ["black_background"] = OffOn("backdrop"),
            ["run_speed"] = Map(("vanilla", "run:restore"), ("plus_25", "run:1.25"), ("plus_50", "run:1.5")),
            ["fast_transitions"] = OffOn("fast-transitions"),
            ["auto_map"] = OffOn("auto-map"),
            ["innate_compass"] = OffOn("compass"),
            ["bench_teleport"] = One("open", "bench-teleport:open"),
            ["secret_radar"] = OffOn("secret-radar"),
            ["nail_damage"] = Map(("x1", "needle:restore"), ("x2", "needle:2"), ("x3", "needle:3"), ("x5", "needle:5")),
            ["damage_received"] = Map(("vanilla", "damage:restore"), ("prevent_death", "damage:PreventDeath"), ("invincible", "damage:Invincible")),
            ["damage_cap"] = OffOn("damage-cap"),
            ["one_hit_kills"] = Map(("off", "one-hit:restore"), ("on", "one-hit:True")),
            ["unlimited_silk"] = Map(("off", "silk:restore"), ("on", "silk:True")),
            ["health_bars"] = OffOn("health-bars"),
            ["damage_numbers"] = OffOn("damage-numbers"),
            ["boss_retry"] = OffOn("boss-retry"),
            ["equip_anywhere"] = Map(("off", "equip:restore"), ("on", "equip:True")),
            ["charm_costs"] = Map(("vanilla", "tool-costs-free:False"), ("free", "tool-costs-free:True")),
            ["unlimited_notches"] = OffOn("unlimited-slots"),
            ["state_slot"] = Map(("1", "state-slot:1"), ("2", "state-slot:2"), ("3", "state-slot:3"), ("4", "state-slot:4"), ("5", "state-slot:5")),
            ["save_to_slot"] = One("run", "state:save"),
            ["load_from_slot"] = One("run", "state:load"),
            ["delete_slot"] = One("run", "state:delete"),
            ["geo_magnet"] = OffOn("rosary-magnet"),
            ["keep_geo_on_death"] = OffOn("keep-rosaries"),
            ["journal_one_kill"] = OffOn("journal-one-kill"),
            ["geo_multiplier"] = Map(("x1", "rosary-multiplier:1"), ("x2", "rosary-multiplier:2"), ("x3", "rosary-multiplier:3"), ("x5", "rosary-multiplier:5")),
            ["instant_dialogue"] = Map(("off", "dialogue:restore"), ("on", "dialogue:True")),
            ["disable_world_rumble"] = Map(("off", "rumble:restore"), ("on", "rumble:True")),
            ["ignore_frost_slowdown"] = Map(("off", "frost:restore"), ("on", "frost:True")),
        };
        var catalog = new SilksongTweakAdapter(new RecordingApi());
        TweakDescriptor[] available = catalog.Descriptors.Where(row => row.IsAvailable).ToArray();

        Assert.Equal(expected.Keys.OrderBy(id => id), available.Select(row => row.Id).OrderBy(id => id));
        foreach (TweakDescriptor row in available)
        {
            Assert.Equal(expected[row.Id].Keys, row.Values);
            foreach (string value in row.Values)
            {
                var api = new RecordingApi();
                var adapter = new SilksongTweakAdapter(api);

                TweakActionResult result = adapter.Apply(row.Id, value);

                Assert.True(result.Success, row.Id + "=" + value + ": " + result.Error);
                Assert.Equal(new[] { expected[row.Id][value] }, api.Calls);
            }
        }
    }

    [Fact]
    public void EveryChoiceValueMutatesAndPersistsWithoutGlobalGate()
    {
        var catalog = new SilksongTweakAdapter(new RecordingApi());
        foreach (TweakDescriptor row in catalog.Descriptors.Where(
                     row => row.IsAvailable && row.ControlKind == TweakControlKind.Choice))
        {
            foreach (string value in row.Values)
            {
                var store = new MemoryStore();
                var controller = new TweakController(
                    new SilksongTweakAdapter(new RecordingApi()), store);
                Assert.True(controller.Initialize().Success);

                TweakActionResult result = controller.Set(row.Id, value);

                Assert.True(result.Success, row.Id + "=" + value + ": " + result.Error);
                Assert.True(result.Pending, row.Id + " must await runtime-owner readback");
                Assert.True(controller.MutationsAvailable);
                Assert.Equal(value, controller.Value(row.Id));
                Assert.False(store.ContainsKey(
                    "dualsouls.mods.silksong.value." + row.Id));

                controller.Tick();

                Assert.Equal(value, store["dualsouls.mods.silksong.value." + row.Id]);
                TweakOperationEvidence evidence = Assert.Single(controller.OperationEvidence);
                Assert.Equal(row.Id, evidence.RowId);
                Assert.Equal(TweakOperationKind.Deferred, evidence.Operation);
                Assert.Equal(TweakReadbackKind.Choice, evidence.Readback.Kind);
                Assert.Equal(value, evidence.Readback.Value);
            }
        }
    }

    [Fact]
    public void ResetRestoresProfileBaselineAndEveryChoiceDefault()
    {
        var api = new RecordingApi();
        var store = new MemoryStore();
        var controller = new TweakController(new SilksongTweakAdapter(api), store);
        Assert.True(controller.Initialize().Success);
        TweakDescriptor[] choices = controller.Descriptors
            .Where(row => row.IsAvailable && row.ControlKind == TweakControlKind.Choice)
            .ToArray();
        foreach (TweakDescriptor row in choices)
        {
            string nonDefault = row.Values.First(value => value != row.DefaultValue);
            Assert.True(controller.Set(row.Id, nonDefault).Success);
            controller.Tick();
            Assert.Equal(nonDefault, api.Readback(row.Id).Readback.Value);
        }

        TweakActionResult result = controller.Reset();

        Assert.True(result.Success);
        Assert.Equal(1, api.RestoreAllCount);
        Assert.True(controller.MutationsAvailable);
        TweakOperationEvidence[] resetEvidence = controller.OperationEvidence
            .Where(item => item.Operation == TweakOperationKind.Reset)
            .ToArray();
        Assert.Equal(choices.Length, resetEvidence.Length);
        foreach (TweakDescriptor row in choices)
        {
            Assert.Equal(row.DefaultValue, controller.Value(row.Id));
            Assert.Equal(row.DefaultValue, store["dualsouls.mods.silksong.value." + row.Id]);
            TweakActionResult ownerReadback = api.Readback(row.Id);
            Assert.True(ownerReadback.Success);
            Assert.Equal(TweakReadbackKind.Choice, ownerReadback.Readback.Kind);
            Assert.Equal(row.DefaultValue, ownerReadback.Readback.Value);
            TweakOperationEvidence evidence = Assert.Single(
                resetEvidence, item => item.RowId == row.Id);
            Assert.True(evidence.Success);
            Assert.Equal(row.DefaultValue, evidence.Readback.Value);
        }
    }

    [Fact]
    public void EveryUnavailableAndUnknownDispatchFailsWithoutCallingTheGame()
    {
        var api = new RecordingApi();
        var adapter = new SilksongTweakAdapter(api);

        foreach (TweakDescriptor row in adapter.Descriptors.Where(row => !row.IsAvailable))
            Assert.False(adapter.Apply(row.Id, row.DefaultValue).Success);
        Assert.False(adapter.Apply("unknown", "off").Success);

        Assert.Equal(0, api.TotalMutationCount);
    }

    [Theory]
    [InlineData("prevent_death", SilksongDamageMode.PreventDeath)]
    [InlineData("invincible", SilksongDamageMode.Invincible)]
    public void DamageModesMapToTheTypedApi(string value, SilksongDamageMode expected)
    {
        var api = new RecordingApi();
        var adapter = new SilksongTweakAdapter(api);

        var result = adapter.Apply("damage_received", value);

        Assert.True(result.Success);
        Assert.Equal(expected, api.DamageMode);
        Assert.Equal(0, api.RestoreDamageCount);
    }

    [Fact]
    public void DamageVanillaRestoresCapturedDamageBaseline()
    {
        var api = new RecordingApi();
        var adapter = new SilksongTweakAdapter(api);

        var result = adapter.Apply("damage_received", "vanilla");

        Assert.True(result.Success);
        Assert.Equal(1, api.RestoreDamageCount);
    }

    [Theory]
    [InlineData("unlimited_silk", nameof(RecordingApi.UnlimitedSilk))]
    [InlineData("one_hit_kills", nameof(RecordingApi.OneHitKills))]
    [InlineData("equip_anywhere", nameof(RecordingApi.EquipAnywhere))]
    [InlineData("instant_dialogue", nameof(RecordingApi.InstantDialogue))]
    [InlineData("disable_world_rumble", nameof(RecordingApi.WorldRumbleDisabled))]
    [InlineData("ignore_frost_slowdown", nameof(RecordingApi.FrostDisabled))]
    public void BooleanOptionsMapOnAndDefaultToIndividualRestore(string id, string property)
    {
        var api = new RecordingApi();
        var adapter = new SilksongTweakAdapter(api);

        Assert.True(adapter.Apply(id, "on").Success);
        Assert.True((bool)typeof(RecordingApi).GetProperty(property)!.GetValue(api)!);

        Assert.True(adapter.Apply(id, "off").Success);
        Assert.Equal(1, api.IndividualRestoreCount(id));
    }

    [Fact]
    public void AppliedChoiceReadbackRemainsEnabledWhenCapturedRuntimeBaselineAlreadyMatches()
    {
        var state = new SilksongAppliedChoiceState();

        state.SetDamageMode(SilksongDamageMode.Invincible);
        state.SetOneHitKills(true);
        state.SetEquipAnywhere(true);
        state.SetInstantDialogue(true);
        state.SetWorldRumbleDisabled(true);
        state.SetFrostDisabled(true);

        AssertChoice(state.Readback("damage_received"), "invincible");
        AssertChoice(state.Readback("one_hit_kills"), "on");
        AssertChoice(state.Readback("equip_anywhere"), "on");
        AssertChoice(state.Readback("instant_dialogue"), "on");
        AssertChoice(state.Readback("disable_world_rumble"), "on");
        AssertChoice(state.Readback("ignore_frost_slowdown"), "on");
    }

    [Fact]
    public void CaptureAndFullRestoreDelegateToTheTypedApi()
    {
        var api = new RecordingApi();
        var adapter = new SilksongTweakAdapter(api);

        adapter.CaptureBaseline();
        Assert.True(adapter.Apply("unlimited_silk", "on").Success);
        adapter.RestoreBaseline();

        Assert.Equal(1, api.CaptureCount);
        Assert.Equal(1, api.RestoreAllCount);
        adapter.Tick();
        Assert.Equal(1, api.GameplayTickCount);
    }

    [Fact]
    public void TickAlwaysDelegatesGeneralGameplayMaintenance()
    {
        var api = new RecordingApi();
        var adapter = new SilksongTweakAdapter(api);

        adapter.Tick();
        Assert.True(adapter.Apply("unlimited_silk", "on").Success);
        adapter.Tick();
        Assert.True(adapter.Apply("unlimited_silk", "off").Success);
        adapter.Tick();

        Assert.Equal(3, api.GameplayTickCount);
    }

    [Fact]
    public void UnknownIdsAndValuesFailWithoutCallingTheGame()
    {
        var api = new RecordingApi();
        var adapter = new SilksongTweakAdapter(api);

        Assert.False(adapter.Apply("missing", "on").Success);
        Assert.False(adapter.Apply("one_hit_kills", "maybe").Success);
        Assert.Equal(0, api.TotalMutationCount);
    }

    [Fact]
    public void ActionsFailClosedWhileTypedOwnersAreUnavailable()
    {
        var api = new RecordingApi { IsReady = false };
        var adapter = new SilksongTweakAdapter(api);

        var result = adapter.Apply("damage_received", "invincible");
        adapter.Tick();

        Assert.False(result.Success);
        Assert.Contains("not ready", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, api.TotalMutationCount);
        Assert.Equal(1, api.GameplayTickCount);
    }

    [Fact]
    public void TypedApiExceptionsBecomeVisibleApplyFailures()
    {
        var api = new RecordingApi { ThrowOnMutation = true };
        var adapter = new SilksongTweakAdapter(api);

        var result = adapter.Apply("one_hit_kills", "on");

        Assert.False(result.Success);
        Assert.Contains("game rejected", result.Error);
    }

    [Fact]
    public void IdleTickWithOutcomesReturnsTheSharedEmptyCollection()
    {
        var adapter = new SilksongTweakAdapter(new RecordingApi());

        IReadOnlyList<TweakAdapterCompletion> first = adapter.TickWithOutcomes();
        IReadOnlyList<TweakAdapterCompletion> second = adapter.TickWithOutcomes();

        Assert.Empty(first);
        Assert.Same(first, second);
    }

    static Dictionary<string, string> One(string value, string call) =>
        new() { [value] = call };

    static Dictionary<string, string> OffOn(string prefix) =>
        Map(("off", prefix + ":False"), ("on", prefix + ":True"));

    static Dictionary<string, string> Map(params (string Value, string Call)[] entries) =>
        entries.ToDictionary(entry => entry.Value, entry => entry.Call, StringComparer.Ordinal);

    static void AssertChoice(TweakActionResult result, string expected)
    {
        Assert.True(result.Success, result.Error);
        Assert.Equal(TweakReadbackKind.Choice, result.Readback.Kind);
        Assert.Equal(expected, result.Readback.Value);
    }

    static void AssertDescriptor(TweakDescriptor row, string id, string defaultValue, params string[] values)
    {
        Assert.Equal(id, row.Id);
        Assert.Equal(defaultValue, row.DefaultValue);
        Assert.Equal(values, row.Values);
    }

    private sealed class MemoryStore : Dictionary<string, string>, ITweakStore
    {
        public string Read(string key) => TryGetValue(key, out string value) ? value : null;
        public void Write(string key, string value) => this[key] = value;
        public void Flush() { }
    }

    private sealed class RecordingApi : ISilksongTweakApi
    {
        public bool IsReady { get; set; } = true;
        public int CaptureCount { get; private set; }
        public int RestoreAllCount { get; private set; }
        public int RestoreDamageCount { get; private set; }
        public int RestoreSilkCount { get; private set; }
        public int RestoreOneHitCount { get; private set; }
        public int RestoreEquipCount { get; private set; }
        public int RestoreInstantDialogueCount { get; private set; }
        public int RestoreWorldRumbleCount { get; private set; }
        public int RestoreFrostCount { get; private set; }
        public int GameplayTickCount { get; private set; }
        public int TotalMutationCount { get; private set; }
        public SilksongDamageMode? DamageMode { get; private set; }
        public bool UnlimitedSilk { get; private set; }
        public bool OneHitKills { get; private set; }
        public bool EquipAnywhere { get; private set; }
        public bool InstantDialogue { get; private set; }
        public bool WorldRumbleDisabled { get; private set; }
        public bool FrostDisabled { get; private set; }
        public bool ThrowOnMutation { get; set; }
        public List<string> Calls { get; } = new();
        readonly Dictionary<string, string> _actual = DefaultValues();

        public void CaptureBaseline() => CaptureCount++;

        public void RestoreBaseline()
        {
            RestoreAllCount++;
            UnlimitedSilk = false;
            OneHitKills = false;
            EquipAnywhere = false;
            InstantDialogue = false;
            WorldRumbleDisabled = false;
            FrostDisabled = false;
            _actual.Clear();
            foreach (KeyValuePair<string, string> pair in DefaultValues())
                _actual[pair.Key] = pair.Value;
        }

        public void OpenSkins() => Mutate("skins:open");
        public void SetCompanionBackdropBlack(bool black) => Mutate($"backdrop:{black}");
        public void SetRunSpeedMultiplier(float multiplier) => Mutate($"run:{multiplier}");
        public void RestoreRunSpeed() => Mutate("run:restore");
        public void SetFastTransitions(bool enabled) => Mutate($"fast-transitions:{enabled}");
        public void SetAutoMap(bool enabled) => Mutate($"auto-map:{enabled}");
        public void SetInnateCompass(bool enabled) => Mutate($"compass:{enabled}");
        public void OpenBenchTeleport(long operationToken) => Mutate("bench-teleport:open");
        public void SetSecretRadar(bool enabled) => Mutate($"secret-radar:{enabled}");
        public void SetNeedleDamageMultiplier(int multiplier) => Mutate($"needle:{multiplier}");
        public void RestoreNeedleDamage() => Mutate("needle:restore");
        public void SetDamageCap(bool enabled) => Mutate($"damage-cap:{enabled}");
        public void SetEnemyHealthBars(bool enabled) => Mutate($"health-bars:{enabled}");
        public void SetDamageNumbers(bool enabled) => Mutate($"damage-numbers:{enabled}");
        public void SetBossRetry(bool enabled) => Mutate($"boss-retry:{enabled}");
        public void SetToolCostsFree(bool enabled) => Mutate($"tool-costs-free:{enabled}");
        public void SetUnlimitedToolSlots(bool enabled) => Mutate($"unlimited-slots:{enabled}");
        public void SetStateSlot(int slot) => Mutate($"state-slot:{slot}");
        public void SaveState() => Mutate("state:save");
        public void LoadState(long operationToken) => Mutate("state:load");
        public void DeleteState() => Mutate("state:delete");
        public void SetRosaryMagnet(bool enabled) => Mutate($"rosary-magnet:{enabled}");
        public void SetKeepRosariesOnDeath(bool enabled) => Mutate($"keep-rosaries:{enabled}");
        public void SetJournalOneKill(bool enabled) => Mutate($"journal-one-kill:{enabled}");
        public void SetRosaryMultiplier(int multiplier) => Mutate($"rosary-multiplier:{multiplier}");

        public void SetDamageMode(SilksongDamageMode mode)
        {
            Mutate($"damage:{mode}");
            DamageMode = mode;
        }

        public void RestoreDamageMode()
        {
            Mutate("damage:restore");
            RestoreDamageCount++;
            DamageMode = null;
        }

        public void SetUnlimitedSilk(bool enabled)
        {
            Mutate($"silk:{enabled}");
            UnlimitedSilk = enabled;
        }

        public void RestoreUnlimitedSilk()
        {
            Mutate("silk:restore");
            RestoreSilkCount++;
            UnlimitedSilk = false;
        }

        public void SetOneHitKills(bool enabled)
        {
            Mutate($"one-hit:{enabled}");
            OneHitKills = enabled;
        }

        public void RestoreOneHitKills()
        {
            Mutate("one-hit:restore");
            RestoreOneHitCount++;
            OneHitKills = false;
        }

        public void SetEquipAnywhere(bool enabled)
        {
            Mutate($"equip:{enabled}");
            EquipAnywhere = enabled;
        }

        public void RestoreEquipAnywhere()
        {
            Mutate("equip:restore");
            RestoreEquipCount++;
            EquipAnywhere = false;
        }

        public void SetInstantDialogue(bool enabled)
        {
            Mutate($"dialogue:{enabled}");
            InstantDialogue = enabled;
        }

        public void RestoreInstantDialogue()
        {
            Mutate("dialogue:restore");
            RestoreInstantDialogueCount++;
            InstantDialogue = false;
        }

        public void SetWorldRumbleDisabled(bool enabled)
        {
            Mutate($"rumble:{enabled}");
            WorldRumbleDisabled = enabled;
        }

        public void RestoreWorldRumbleDisabled()
        {
            Mutate("rumble:restore");
            RestoreWorldRumbleCount++;
            WorldRumbleDisabled = false;
        }

        public void SetFrostDisabled(bool enabled)
        {
            Mutate($"frost:{enabled}");
            FrostDisabled = enabled;
        }

        public void RestoreFrostDisabled()
        {
            Mutate("frost:restore");
            RestoreFrostCount++;
            FrostDisabled = false;
        }

        public void TickGameplay() => GameplayTickCount++;

        public TweakActionResult Readback(string id)
        {
            return _actual.TryGetValue(id, out string value)
                ? TweakActionResult.Ok(TweakReadback.Choice(value))
                : TweakActionResult.Ok(TweakReadback.Integer(1));
        }

        public IReadOnlyList<TweakAdapterCompletion> DrainCompletedOperations() =>
            Array.Empty<TweakAdapterCompletion>();

        static Dictionary<string, string> DefaultValues() => new()
        {
            ["black_background"] = "off",
            ["run_speed"] = "vanilla",
            ["fast_transitions"] = "off",
            ["auto_map"] = "off",
            ["innate_compass"] = "off",
            ["secret_radar"] = "off",
            ["nail_damage"] = "x1",
            ["damage_received"] = "vanilla",
            ["damage_cap"] = "off",
            ["one_hit_kills"] = "off",
            ["unlimited_silk"] = "off",
            ["health_bars"] = "off",
            ["damage_numbers"] = "off",
            ["boss_retry"] = "off",
            ["equip_anywhere"] = "off",
            ["charm_costs"] = "vanilla",
            ["unlimited_notches"] = "off",
            ["state_slot"] = "1",
            ["geo_magnet"] = "off",
            ["keep_geo_on_death"] = "off",
            ["journal_one_kill"] = "off",
            ["geo_multiplier"] = "x1",
            ["instant_dialogue"] = "off",
            ["disable_world_rumble"] = "off",
            ["ignore_frost_slowdown"] = "off",
        };

        void Track(string call)
        {
            string BoolValue(string prefix) =>
                call == prefix + ":True" ? "on" : "off";
            if (call.StartsWith("backdrop:", StringComparison.Ordinal)) _actual["black_background"] = BoolValue("backdrop");
            else if (call.StartsWith("run:", StringComparison.Ordinal)) _actual["run_speed"] = call == "run:1.25" ? "plus_25" : call == "run:1.5" ? "plus_50" : "vanilla";
            else if (call.StartsWith("fast-transitions:", StringComparison.Ordinal)) _actual["fast_transitions"] = BoolValue("fast-transitions");
            else if (call.StartsWith("auto-map:", StringComparison.Ordinal)) _actual["auto_map"] = BoolValue("auto-map");
            else if (call.StartsWith("compass:", StringComparison.Ordinal)) _actual["innate_compass"] = BoolValue("compass");
            else if (call.StartsWith("secret-radar:", StringComparison.Ordinal)) _actual["secret_radar"] = BoolValue("secret-radar");
            else if (call.StartsWith("needle:", StringComparison.Ordinal)) _actual["nail_damage"] = call == "needle:2" ? "x2" : call == "needle:3" ? "x3" : call == "needle:5" ? "x5" : "x1";
            else if (call.StartsWith("damage:", StringComparison.Ordinal)) _actual["damage_received"] = call == "damage:PreventDeath" ? "prevent_death" : call == "damage:Invincible" ? "invincible" : "vanilla";
            else if (call.StartsWith("damage-cap:", StringComparison.Ordinal)) _actual["damage_cap"] = BoolValue("damage-cap");
            else if (call.StartsWith("one-hit:", StringComparison.Ordinal)) _actual["one_hit_kills"] = call == "one-hit:True" ? "on" : "off";
            else if (call.StartsWith("silk:", StringComparison.Ordinal)) _actual["unlimited_silk"] = call == "silk:True" ? "on" : "off";
            else if (call.StartsWith("health-bars:", StringComparison.Ordinal)) _actual["health_bars"] = BoolValue("health-bars");
            else if (call.StartsWith("damage-numbers:", StringComparison.Ordinal)) _actual["damage_numbers"] = BoolValue("damage-numbers");
            else if (call.StartsWith("boss-retry:", StringComparison.Ordinal)) _actual["boss_retry"] = BoolValue("boss-retry");
            else if (call.StartsWith("equip:", StringComparison.Ordinal)) _actual["equip_anywhere"] = call == "equip:True" ? "on" : "off";
            else if (call.StartsWith("tool-costs-free:", StringComparison.Ordinal)) _actual["charm_costs"] = call == "tool-costs-free:True" ? "free" : "vanilla";
            else if (call.StartsWith("unlimited-slots:", StringComparison.Ordinal)) _actual["unlimited_notches"] = BoolValue("unlimited-slots");
            else if (call.StartsWith("state-slot:", StringComparison.Ordinal)) _actual["state_slot"] = call.Substring("state-slot:".Length);
            else if (call.StartsWith("rosary-magnet:", StringComparison.Ordinal)) _actual["geo_magnet"] = BoolValue("rosary-magnet");
            else if (call.StartsWith("keep-rosaries:", StringComparison.Ordinal)) _actual["keep_geo_on_death"] = BoolValue("keep-rosaries");
            else if (call.StartsWith("journal-one-kill:", StringComparison.Ordinal)) _actual["journal_one_kill"] = BoolValue("journal-one-kill");
            else if (call.StartsWith("rosary-multiplier:", StringComparison.Ordinal)) _actual["geo_multiplier"] = "x" + call.Substring("rosary-multiplier:".Length);
            else if (call.StartsWith("dialogue:", StringComparison.Ordinal)) _actual["instant_dialogue"] = call == "dialogue:True" ? "on" : "off";
            else if (call.StartsWith("rumble:", StringComparison.Ordinal)) _actual["disable_world_rumble"] = call == "rumble:True" ? "on" : "off";
            else if (call.StartsWith("frost:", StringComparison.Ordinal)) _actual["ignore_frost_slowdown"] = call == "frost:True" ? "on" : "off";
        }

        public int IndividualRestoreCount(string id) => id switch
        {
            "unlimited_silk" => RestoreSilkCount,
            "one_hit_kills" => RestoreOneHitCount,
            "equip_anywhere" => RestoreEquipCount,
            "instant_dialogue" => RestoreInstantDialogueCount,
            "disable_world_rumble" => RestoreWorldRumbleCount,
            "ignore_frost_slowdown" => RestoreFrostCount,
            _ => 0,
        };

        void Mutate(string call)
        {
            Calls.Add(call);
            Track(call);
            TotalMutationCount++;
            if (ThrowOnMutation) throw new InvalidOperationException("game rejected mutation");
        }
    }
}
