using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DualSouls.Skins.HollowKnight.Runtime;
using DualSouls.Skins.Runtime;
using Xunit;

public sealed class HollowKnightSkinRuntimeTests
{
    [Fact]
    public void Shared_session_uses_explicit_profile_target_and_mode_rules()
    {
        var loader = new Loader();
        var hornet = new Slot("Hornet.png", new object(), null);
        var knight = new Slot("Knight.png", new object(), null);
        var rules = new SkinRuntimeRules("silksong", 11,
            target => target == "Hornet.png", (mode, target) => mode == "ON" && target == "Hornet.png");
        using var session = new SkinRuntimeSession(loader,
            () => new[] { hornet.Binding(), knight.Binding() }, 512L * 1024 * 1024, rules);
        var root = Path.Combine(AppContext.BaseDirectory, "runtime-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "hornet.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg=="));
        File.WriteAllBytes(Path.Combine(root, "knight.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg=="));

        var result = session.TryApply(new SkinPack("ss", root, new Dictionary<string, string>
            { ["Hornet.png"] = "hornet.png", ["Knight.png"] = "knight.png" }));

        Assert.Equal(SkinApplyStatus.Applied, result.Status);
        Assert.NotSame(hornet.Original, hornet.Value);
        Assert.Same(knight.Original, knight.Value);
        Assert.Contains("Knight.png", result.UnsupportedTargets);
    }

    [Fact]
    public void Off_A_B_Off_restores_missing_sheets_and_originals()
    {
        var rig = new Rig(); var knight = rig.Add("Knight.png"); var sprint = rig.Add("Sprint.png");
        var a = rig.Pack("a", "Knight.png", "Sprint.png"); var b = rig.Pack("b", "Knight.png");
        Assert.Equal(SkinApplyStatus.Unchanged, rig.Session.TryRestore().Status);
        Assert.Equal(SkinApplyStatus.Applied, rig.Session.TryApply(a).Status);
        var aKnight = knight.Value; var aSprint = sprint.Value;
        Assert.Equal(SkinApplyStatus.Applied, rig.Session.TryApply(b).Status);
        Assert.NotSame(aKnight, knight.Value); Assert.Same(sprint.Original, sprint.Value);
        Assert.True(rig.Loader.Created.Single(x => ReferenceEquals(x.Value, aKnight)).Released);
        Assert.True(rig.Loader.Created.Single(x => ReferenceEquals(x.Value, aSprint)).Released);
        Assert.Equal(SkinApplyStatus.Restored, rig.Session.TryRestore().Status);
        Assert.Same(knight.Original, knight.Value); Assert.Equal(3, rig.Session.SkinStamp);
        Assert.All(rig.Loader.Created, x => Assert.Equal(1, x.ReleaseCount));
    }

    [Fact]
    public void Omitted_sheet_is_restored_once_then_no_longer_owned()
    {
        var rig = new Rig(); rig.Add("Knight.png"); var sprint = rig.Add("Sprint.png");
        rig.Session.TryApply(rig.Pack("a", "Knight.png", "Sprint.png"));
        rig.Session.TryApply(rig.Pack("b", "Knight.png")); Assert.Same(sprint.Original, sprint.Value);
        var gameReplacement = new object(); sprint.Value = gameReplacement;
        rig.Session.Refresh(); Assert.Same(gameReplacement, sprint.Value);
        rig.Session.TryRestore(); Assert.Same(gameReplacement, sprint.Value);
    }

    [Fact]
    public void Same_pack_cancellation_does_not_rebind_new_target()
    {
        var rig = new Rig(); rig.Add("Knight.png"); var pack = rig.Pack("a", "Knight.png");
        rig.Session.TryApply(pack); var fresh = rig.Add("Knight.png");
        var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Equal(SkinApplyStatus.Cancelled, rig.Session.TryApply(pack, cancel.Token).Status);
        Assert.Same(fresh.Original, fresh.Value); Assert.Equal(1, rig.Session.SkinStamp);
    }

    [Fact]
    public void Qualified_inventory_and_charm_slots_switch_and_restore_independently()
    {
        var rig = new Rig(); var geo = rig.Add("Geo.png"); var inv = rig.Add("Inventory/Geo.png");
        var fragile = rig.Add("Charms/Charm_23_Fragile.png"); var variant = rig.Add("Charms/Charm_40_5.png");
        Assert.Equal(SkinApplyStatus.Applied, rig.Session.TryApply(rig.Pack("a", "Geo.png", "Inventory/Geo.png", "Charms/Charm_23_Fragile.png", "Charms/Charm_40_5.png")).Status);
        Assert.NotSame(inv.Original, inv.Value); Assert.NotSame(geo.Value, inv.Value);
        Assert.NotSame(fragile.Original, fragile.Value); Assert.NotSame(variant.Original, variant.Value);
        rig.Session.TryApply(rig.Pack("b", "Inventory/Geo.png"));
        Assert.Same(geo.Original, geo.Value); Assert.Same(fragile.Original, fragile.Value); Assert.Same(variant.Original, variant.Value);
        rig.Session.TryRestore(); Assert.Same(inv.Original, inv.Value);
    }

    [Fact]
    public void Reference_continuation_mapping_contract_preserves_variants_and_fsm_indices()
    {
        Assert.Equal("Charms/Charm_23_Fragile.png", HollowKnightSkinTargets.CharmListTarget(23));
        Assert.Null(HollowKnightSkinTargets.CharmListTarget(36));
        Assert.Equal("unbreakableHeart", HollowKnightSkinTargets.CharmFields["Charms/Charm_23_Unbreakable.png"]);
        Assert.Equal("nymmCharm", HollowKnightSkinTargets.CharmFields["Charms/Charm_40_5.png"]);
        Assert.Equal("Nail|level5", HollowKnightSkinTargets.InventoryObjects["Inventory/Nail_5.png"]);
        Assert.Equal("Equipment>Build Equipment List>Dash>16", HollowKnightSkinTargets.InventoryFsms["Inventory/Cloak_2.png"]);
        Assert.Equal("23>Glass HP>2>brokenGlassHP", HollowKnightSkinTargets.CharmFsms["Charms/Charm_23_Broken.png"]);
        Assert.Null(HollowKnightSkinTargets.AtlasTarget("Geo")); // ambiguous basename must not guess inventory vs collection
        Assert.Equal("Inventory/Geo.png", HollowKnightSkinTargets.AtlasTarget("Inventory/Geo"));
        Assert.False(HollowKnightSkinTargets.IsSupported("Inventory/SpellBG.png"));
        Assert.False(HollowKnightSkinTargets.IsSupported("SaveHud/geoIcon.png"));
        Assert.False(HollowKnightSkinTargets.IsSupported("AreaBackgrounds/CITY.png"));
    }

    [Fact]
    public void Decode_failure_keeps_previous_visual_and_stamp()
    {
        var rig = new Rig(); var knight = rig.Add("Knight.png");
        rig.Session.TryApply(rig.Pack("a", "Knight.png")); var previous = knight.Value;
        rig.Loader.FailDecode = true;
        Assert.Equal(SkinApplyStatus.Failed, rig.Session.TryApply(rig.Pack("b", "Knight.png")).Status);
        Assert.Same(previous, knight.Value); Assert.Equal(1, rig.Session.SkinStamp);
        Assert.False(rig.Loader.Created[0].Released);
    }

    [Fact]
    public void Invalid_header_is_rejected_before_decode()
    {
        var rig = new Rig(); rig.Add("Knight.png"); var pack = rig.Pack("bad", "Knight.png");
        File.WriteAllBytes(Path.Combine(pack.Root, "0.png"), new byte[40]);
        Assert.Equal(SkinApplyStatus.Rejected, rig.Session.TryApply(pack).Status);
        Assert.Empty(rig.Loader.Created); Assert.Equal(0, rig.Session.SkinStamp);
    }

    [Fact]
    public void Encoded_cap_and_decoded_memory_are_admitted_before_decode()
    {
        var rig = new Rig(100); rig.Add("Knight.png");
        var a = rig.Pack("a", "Knight.png"); Assert.Equal(SkinApplyStatus.Applied, rig.Session.TryApply(a).Status);
        var previous = rig.Slots[0].Value;
        var b = rig.Pack("b", "Knight.png", "Sprint.png");
        Assert.Equal(SkinApplyStatus.Rejected, rig.Session.TryApply(b).Status);
        Assert.Same(previous, rig.Slots[0].Value); Assert.Single(rig.Loader.Created);
        using (var file = new FileStream(Path.Combine(b.Root, "0.png"), FileMode.Open)) file.SetLength(16L * 1024 * 1024 + 1);
        Assert.Equal(SkinApplyStatus.Rejected, rig.Session.TryApply(b).Status);
        Assert.Single(rig.Loader.Created);
    }

    [Fact]
    public void Postdecode_dimensions_must_equal_preflight_dimensions()
    {
        var rig = new Rig(); rig.Add("Knight.png"); rig.Loader.WrongDimensions = true;
        Assert.Equal(SkinApplyStatus.Failed, rig.Session.TryApply(rig.Pack("bad", "Knight.png")).Status);
        Assert.All(rig.Loader.Created, x => Assert.Equal(1, x.ReleaseCount));
        Assert.Equal(0, rig.Session.SkinStamp);
    }

    [Fact]
    public void Cancellation_before_and_after_decode_never_touches_previous()
    {
        var rig = new Rig(); var slot = rig.Add("Knight.png"); rig.Session.TryApply(rig.Pack("a", "Knight.png"));
        var previous = slot.Value; var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Equal(SkinApplyStatus.Cancelled, rig.Session.TryApply(rig.Pack("b", "Knight.png"), cancel.Token).Status);
        cancel = new CancellationTokenSource(); rig.Loader.AfterDecode = cancel.Cancel;
        Assert.Equal(SkinApplyStatus.Cancelled, rig.Session.TryApply(rig.Pack("c", "Knight.png"), cancel.Token).Status);
        Assert.Same(previous, slot.Value); Assert.Equal(1, rig.Session.SkinStamp);
        Assert.Equal(1, rig.Loader.Created.Last().ReleaseCount);
    }

    [Fact]
    public void Midapply_throw_after_write_rolls_back_prior_visual()
    {
        var rig = new Rig(); var knight = rig.Add("Knight.png"); var sprint = rig.Add("Sprint.png");
        rig.Session.TryApply(rig.Pack("a", "Knight.png", "Sprint.png"));
        var oldKnight = knight.Value; var oldSprint = sprint.Value; sprint.FailNextWrite = true;
        Assert.Equal(SkinApplyStatus.Failed, rig.Session.TryApply(rig.Pack("b", "Knight.png", "Sprint.png")).Status);
        Assert.Same(oldKnight, knight.Value); Assert.Same(oldSprint, sprint.Value);
        Assert.Equal(1, rig.Session.SkinStamp);
        Assert.All(rig.Loader.Created.Take(2), x => Assert.False(x.Released));
        Assert.All(rig.Loader.Created.Skip(2), x => Assert.Equal(1, x.ReleaseCount));
    }

    [Fact]
    public void Restore_failure_blocks_switch_and_retains_resources_until_retry()
    {
        var rig = new Rig(); var slot = rig.Add("Knight.png"); rig.Session.TryApply(rig.Pack("a", "Knight.png"));
        slot.AlwaysFail = true;
        Assert.Equal(SkinApplyStatus.RestoreFailed, rig.Session.TryRestore().Status);
        Assert.Equal(SkinApplyStatus.Blocked, rig.Session.TryApply(rig.Pack("b", "Knight.png")).Status);
        Assert.False(rig.Loader.Created[0].Released); Assert.Equal(1, rig.Session.SkinStamp);
        slot.AlwaysFail = false;
        Assert.Equal(SkinApplyStatus.Restored, rig.Session.TryRestore().Status);
        Assert.Equal(1, rig.Loader.Created[0].ReleaseCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pulse_active_state_restores_exact_original(bool active)
    {
        var rig = new Rig(); var pulse = rig.Add("OrbFull.png", active, (_, __) => false);
        rig.Session.TryApply(rig.Pack("orb", "OrbFull.png")); Assert.Equal(false, pulse.Value);
        rig.Session.TryRestore(); Assert.Equal(active, pulse.Value);
    }

    [Fact]
    public void New_target_identity_rebinds_and_restores_its_own_original()
    {
        var rig = new Rig(); var first = rig.Add("Knight.png"); rig.Session.TryApply(rig.Pack("a", "Knight.png"));
        first.Alive = false; var second = rig.Add("Knight.png");
        Assert.Equal(SkinApplyStatus.Applied, rig.Session.Refresh().Status);
        Assert.NotSame(second.Original, second.Value); Assert.Equal(2, rig.Session.SkinStamp);
        Assert.Equal(SkinApplyStatus.Unchanged, rig.Session.Refresh().Status);
        rig.Session.TryRestore(); Assert.Same(second.Original, second.Value);
    }

    [Fact]
    public void Inherited_owned_texture_is_never_captured_as_original()
    {
        var rig = new Rig(); var first = rig.Add("Knight.png"); rig.Session.TryApply(rig.Pack("a", "Knight.png"));
        rig.Add("Knight.png", first.Value);
        Assert.Equal(SkinApplyStatus.RestoreFailed, rig.Session.TryRestore().Status);
        Assert.False(rig.Loader.Created[0].Released);
    }

    [Fact]
    public void Pending_destroy_remains_charged_and_dispose_is_idempotent()
    {
        var rig = new Rig(); rig.Loader.DelayedRelease = true; rig.Add("Knight.png");
        rig.Session.TryApply(rig.Pack("a", "Knight.png")); rig.Session.TryRestore();
        Assert.True(rig.Session.AccountedBytes > 0); Assert.Equal(1, rig.Loader.Created[0].ReleaseCount);
        rig.Loader.Created[0].Released = true; rig.Session.Refresh();
        Assert.Equal(0, rig.Session.AccountedBytes);
        rig.Session.Dispose(); rig.Session.Dispose(); Assert.Equal(1, rig.Loader.Created[0].ReleaseCount);
    }

    [Fact]
    public void Retirement_exception_never_retires_newly_published_visual_and_retries()
    {
        var rig = new Rig(); var slot = rig.Add("Knight.png");
        rig.Session.TryApply(rig.Pack("a", "Knight.png")); var previous = slot.Value;
        rig.Loader.FailRelease = true; var pack = rig.Pack("b", "Knight.png");
        var result = rig.Session.TryApply(pack);
        Assert.Equal(SkinApplyStatus.Applied, result.Status);
        Assert.Contains("injected release failure", result.Detail);
        Assert.Same(pack, rig.Session.CurrentPack); Assert.NotSame(previous, slot.Value);
        Assert.False(rig.Loader.Created[0].Released); Assert.False(rig.Loader.Created[1].Released);
        Assert.Equal(16, rig.Session.AccountedBytes); Assert.Equal(2, rig.Session.SkinStamp);
        rig.Loader.FailRelease = false; rig.Session.Refresh();
        Assert.True(rig.Loader.Created[0].Released); Assert.False(rig.Loader.Created[1].Released);
        Assert.Equal(8, rig.Session.AccountedBytes);
    }

    [Fact]
    public void Teardown_keeps_owner_retryable_until_pending_destruction_completes()
    {
        var rig = new Rig(); rig.Loader.DelayedRelease = true; var slot = rig.Add("Knight.png");
        rig.Session.TryApply(rig.Pack("a", "Knight.png"));
        Assert.Throws<InvalidOperationException>(() => rig.Session.Dispose());
        Assert.Same(slot.Original, slot.Value); Assert.Equal(8, rig.Session.AccountedBytes);
        rig.Loader.Created[0].Released = true; rig.Session.Dispose(); rig.Session.Dispose();
        Assert.Equal(0, rig.Session.AccountedBytes); Assert.Equal(1, rig.Loader.Created[0].ReleaseCount);
    }

    [Fact]
    public void Canonical_qualified_targets_do_not_collide_and_unsupported_is_reported()
    {
        var rig = new Rig(); var geo = rig.Add("Geo.png");
        var result = rig.Session.TryApply(rig.Pack("a", "Geo.png", "Inventory/Geo.png", "SaveHud/geoIcon.png"));
        Assert.Equal(SkinApplyStatus.Applied, result.Status); Assert.NotSame(geo.Original, geo.Value);
        Assert.Contains("SaveHud/geoIcon.png", result.UnsupportedTargets); Assert.Equal(2, rig.Loader.Created.Count);
    }

    [Fact]
    public void No_targets_returns_waiting_without_publishing_pack_or_stamp()
    {
        var rig = new Rig();
        Assert.Equal(SkinApplyStatus.AwaitingTargets, rig.Session.TryApply(rig.Pack("a", "Knight.png")).Status);
        Assert.Null(rig.Session.CurrentPack); Assert.Equal(0, rig.Session.SkinStamp);
        Assert.All(rig.Loader.Created, x => Assert.Equal(1, x.ReleaseCount));
    }

    [Fact]
    public void Literal_reference_mapping_contract_preserves_pages_aliases_and_effects()
    {
        Assert.Equal("Knight.png", HollowKnightSkinTargets.CollectionTarget("Knight", 0));
        Assert.Equal("Sprint.png", HollowKnightSkinTargets.CollectionTarget("Knight", 1));
        Assert.Null(HollowKnightSkinTargets.CollectionTarget("Knight", 2));
        Assert.Equal("Hud.png", HollowKnightSkinTargets.CollectionTarget("HUD Cln", 0));
        Assert.Equal("VoidSpells.png", HollowKnightSkinTargets.CollectionTarget("Spell Effects Neutral", 0));
        Assert.Equal("DreamArrival.png", HollowKnightSkinTargets.CollectionTarget("Knight Dream Cutscene Cln", 0));
        Assert.Null(HollowKnightSkinTargets.CollectionTarget("HUD_SoulOrb_Fills", 0));
        Assert.Equal(new[] { "Scr Heads", "Scr Base" }, HollowKnightSkinTargets.HeroObjects["Wraiths.png"]);
        Assert.Equal(new[] { "Ash L", "Ash R" }, HollowKnightSkinTargets.HeroObjects["DeathAsh.png"]);
        Assert.True(HollowKnightSkinTargets.IsSupported("Inventory/Geo.png"));
    }

    [Fact]
    public void Atlas_A_B_Off_uses_one_backup_and_releases_only_after_restore()
    {
        var rig = new Rig(); var atlas = new Atlas();
        rig.ExtraSlots.Add(new SkinAtlasSlot(atlas, "Knight.png", rig.Session.AllocateAuxiliary).Binding());
        Assert.Equal(SkinApplyStatus.Applied, rig.Session.TryApply(rig.Pack("a", "Knight.png")).Status);
        var a = atlas.Visual; Assert.Equal(1, atlas.Captures); Assert.Equal(264, rig.Session.AccountedBytes);
        Assert.Equal(SkinApplyStatus.Applied, rig.Session.TryApply(rig.Pack("b", "Knight.png")).Status);
        Assert.NotSame(a, atlas.Visual); Assert.Equal(1, atlas.Captures); Assert.False(atlas.Created[0].Released);
        Assert.Equal(SkinApplyStatus.Restored, rig.Session.TryRestore().Status);
        Assert.Same(atlas.Vanilla, atlas.Visual); Assert.All(atlas.Created, x => Assert.True(x.Released));
        Assert.Equal(0, rig.Session.AccountedBytes); Assert.Equal(3, rig.Session.SkinStamp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Atlas_convert_false_rolls_back_or_retains_backup_when_rollback_also_fails(bool rollbackFails)
    {
        var rig = new Rig(); var atlas = new Atlas();
        rig.ExtraSlots.Add(new SkinAtlasSlot(atlas, "Knight.png", rig.Session.AllocateAuxiliary).Binding());
        rig.Session.TryApply(rig.Pack("a", "Knight.png")); var prior = atlas.Visual;
        atlas.FailCopies = rollbackFails ? 2 : 1;
        var result = rig.Session.TryApply(rig.Pack("b", "Knight.png"));
        Assert.Equal(rollbackFails ? SkinApplyStatus.RestoreFailed : SkinApplyStatus.Failed, result.Status);
        Assert.False(atlas.Created[0].Released); Assert.Equal(1, rig.Session.SkinStamp);
        if (rollbackFails) Assert.Equal(SkinApplyStatus.Blocked, rig.Session.TryApply(rig.Pack("c", "Knight.png")).Status);
        else Assert.Same(prior, atlas.Visual);
        Assert.Equal(SkinApplyStatus.Restored, rig.Session.TryRestore().Status);
        Assert.Same(atlas.Vanilla, atlas.Visual); Assert.Equal(0, rig.Session.AccountedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Atlas_initial_partial_apply_and_undo_failure_requires_real_restore(bool throwCopies)
    {
        var rig = new Rig(); var atlas = new Atlas { FailCopies = 2, ThrowCopies = throwCopies };
        var binding = new SkinAtlasSlot(atlas, "Knight.png", rig.Session.AllocateAuxiliary);
        rig.ExtraSlots.Add(binding.Binding()); var pack = rig.Pack("a", "Knight.png");
        Assert.Equal(SkinApplyStatus.RestoreFailed, rig.Session.TryApply(pack).Status);
        Assert.Equal(2, atlas.Copies); Assert.Same(atlas.Corrupt, atlas.Visual);
        Assert.Null(rig.Session.CurrentPack); Assert.Equal(0, rig.Session.SkinStamp);
        Assert.False(binding.OriginalPixels.ReleaseRequested); Assert.Equal(264, rig.Session.AccountedBytes);
        Assert.All(atlas.Created, x => Assert.False(x.Released)); Assert.False(rig.Loader.Created[0].Released);
        Assert.Equal(SkinApplyStatus.Blocked, rig.Session.TryApply(pack).Status);
        Assert.Equal(SkinApplyStatus.Blocked, rig.Session.Refresh().Status); Assert.Equal(2, atlas.Copies);

        Assert.Equal(SkinApplyStatus.Restored, rig.Session.TryRestore().Status);
        Assert.Equal(3, atlas.Copies); Assert.Same(atlas.Vanilla, atlas.Visual);
        Assert.Equal(1, atlas.Captures); Assert.Equal(1, rig.Session.SkinStamp);
        Assert.True(binding.OriginalPixels.ReleaseRequested); Assert.Equal(0, rig.Session.AccountedBytes);
        Assert.All(atlas.Created, x => Assert.True(x.Released)); Assert.True(rig.Loader.Created[0].Released);
        Assert.Equal(SkinApplyStatus.Unchanged, rig.Session.TryRestore().Status);
        Assert.Equal(3, atlas.Copies); Assert.Equal(1, rig.Session.SkinStamp);

        // Successful recovery really unblocks switching; the next capture is healthy vanilla.
        Assert.Equal(SkinApplyStatus.Applied, rig.Session.TryApply(pack).Status);
        Assert.Equal(2, atlas.Captures); Assert.Equal(4, atlas.Copies); Assert.Equal(2, rig.Session.SkinStamp);
        rig.Session.Dispose(); rig.Session.Dispose();
        Assert.Same(atlas.Vanilla, atlas.Visual); Assert.Equal(5, atlas.Copies);
        Assert.Equal(3, rig.Session.SkinStamp); Assert.Equal(0, rig.Session.AccountedBytes);
        Assert.All(atlas.Created, x => Assert.True(x.Released));
        Assert.All(rig.Loader.Created, x => Assert.Equal(1, x.ReleaseCount));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Atlas_uncertain_restore_and_dispose_failures_keep_backup_until_healthy_retry(bool throwCopies)
    {
        var rig = new Rig(); var atlas = new Atlas { FailCopies = 2, ThrowCopies = throwCopies };
        var binding = new SkinAtlasSlot(atlas, "Knight.png", rig.Session.AllocateAuxiliary);
        rig.ExtraSlots.Add(binding.Binding()); var pack = rig.Pack("a", "Knight.png");
        Assert.Equal(SkinApplyStatus.RestoreFailed, rig.Session.TryApply(pack).Status);
        var backup = binding.OriginalPixels;
        atlas.FailCopies = 2;
        Assert.Equal(SkinApplyStatus.RestoreFailed, rig.Session.TryRestore().Status);
        Assert.Equal(4, atlas.Copies); Assert.Same(atlas.Corrupt, atlas.Visual);
        Assert.Same(backup, binding.OriginalPixels); Assert.False(backup.ReleaseRequested);
        Assert.Equal(264, rig.Session.AccountedBytes); Assert.Equal(0, rig.Session.SkinStamp);
        Assert.Equal(SkinApplyStatus.Blocked, rig.Session.TryApply(pack).Status);
        atlas.FailCopies = 2;
        Assert.Throws<InvalidOperationException>(() => rig.Session.Dispose());
        Assert.Equal(6, atlas.Copies); Assert.Same(atlas.Corrupt, atlas.Visual);
        Assert.False(backup.ReleaseRequested); Assert.Equal(264, rig.Session.AccountedBytes);
        Assert.Equal(0, rig.Session.SkinStamp); Assert.Equal(SkinApplyStatus.Blocked, rig.Session.Refresh().Status);
        Assert.All(atlas.Created, x => Assert.False(x.Released)); Assert.False(rig.Loader.Created[0].Released);

        Assert.Equal(SkinApplyStatus.Restored, rig.Session.TryRestore().Status);
        Assert.Equal(7, atlas.Copies); Assert.Same(atlas.Vanilla, atlas.Visual);
        Assert.Equal(1, atlas.Captures); Assert.Equal(1, rig.Session.SkinStamp);
        Assert.True(backup.ReleaseRequested); Assert.Equal(0, rig.Session.AccountedBytes);
        rig.Session.Dispose(); rig.Session.Dispose(); Assert.Equal(7, atlas.Copies);
        Assert.All(atlas.Created, x => Assert.True(x.Released));
        Assert.All(rig.Loader.Created, x => Assert.Equal(1, x.ReleaseCount));
    }

    [Fact]
    public void Atlas_backup_and_transient_admission_precedes_capture_or_visual_write()
    {
        var rig = new Rig(200); var atlas = new Atlas();
        rig.ExtraSlots.Add(new SkinAtlasSlot(atlas, "Knight.png", rig.Session.AllocateAuxiliary).Binding());
        Assert.Equal(SkinApplyStatus.Failed, rig.Session.TryApply(rig.Pack("a", "Knight.png")).Status);
        Assert.Equal(0, atlas.Captures); Assert.Equal(0, atlas.Copies); Assert.Same(atlas.Vanilla, atlas.Visual);
        Assert.Equal(0, rig.Session.AccountedBytes);
    }

    [Fact]
    public void Atlas_capture_failure_releases_prepared_texture_without_writing_live_atlas()
    {
        var rig = new Rig(); var atlas = new Atlas { FailCapture = true };
        rig.ExtraSlots.Add(new SkinAtlasSlot(atlas, "Knight.png", rig.Session.AllocateAuxiliary).Binding());
        Assert.Equal(SkinApplyStatus.Failed, rig.Session.TryApply(rig.Pack("a", "Knight.png")).Status);
        Assert.Equal(0, atlas.Copies); Assert.Same(atlas.Vanilla, atlas.Visual);
        Assert.All(atlas.Created, x => Assert.True(x.Released)); Assert.Equal(0, rig.Session.AccountedBytes);
    }

    sealed class Atlas : ISkinAtlasSurface
    {
        public object Identity => this;
        public bool IsAlive => Alive;
        public int Width => 4;
        public int Height => 4;
        public bool Alive = true, FailCapture, ThrowCopies;
        public int Captures, Copies, FailCopies;
        public readonly object Vanilla = new object(), Corrupt = new object(); public object Visual;
        public readonly List<Owned> Created = new List<Owned>();
        public Atlas() { Visual = Vanilla; }
        public SkinTexture Capture() { Captures++; return Image(Visual, !FailCapture); }
        public SkinTexture Fit(SkinTexture source) => Image(source.Value, true);
        SkinTexture Image(object value, bool okay)
        {
            var owned = new Owned(); Created.Add(owned);
            return new SkinTexture(value, Width, Height, () => owned.Released = true, () => owned.Released, okay);
        }
        public bool CopyFrom(SkinTexture image)
        {
            Copies++;
            if (FailCopies > 0)
            {
                FailCopies--; Visual = Corrupt; // failure leaves neither requested nor prior complete pixels
                if (ThrowCopies) throw new IOException("injected partial atlas copy failure");
                return false;
            }
            Visual = image.Value;
            return true;
        }
        public sealed class Owned { public bool Released; }
    }

    [Fact]
    public void Hud_repair_reuses_painted_sibling_fits_vanilla_and_falls_back_for_other_blanks()
    {
        var source = new Pixels(12, 4); var vanilla = new Pixels(12, 4); var output = new Pixels(12, 4);
        source.Fill(4, 0, 4, 4, Pixels.Red); vanilla.Fill(0, 0, 12, 4, Pixels.Blue);
        output.Copy(source);
        HollowKnightHudRepair.Repair(source, vanilla, output, new[] {
            new SkinHudRect("idle_v020001", 0, 0, 1f/3, 1), new SkinHudRect("appear_v020004", 1f/3, 0, 2f/3, 1),
            new SkinHudRect("unrelated0001", 2f/3, 0, 1, 1) });
        Assert.Equal(Pixels.Red, output.At(1, 1)); Assert.Equal(Pixels.Blue, output.At(10, 1));
        Assert.Equal(default(SkinHudPixel), source.At(1, 1)); // candidate input is never edited in place
    }

    [Fact]
    public void Hud_repair_limits_medoid_correction_to_coin_family()
    {
        var source = new Pixels(12, 4); var vanilla = new Pixels(12, 4); var output = new Pixels(12, 4);
        source.Fill(0, 0, 8, 4, Pixels.Red); source.Fill(8, 0, 4, 4, Pixels.Blue); output.Copy(source);
        HollowKnightHudRepair.Repair(source, vanilla, output, new[] {
            new SkinHudRect("HUD_coin_v020001", 0, 0, 1f/3, 1), new SkinHudRect("HUD_coin_v020002", 1f/3, 0, 2f/3, 1),
            new SkinHudRect("HUD_coin_v020003", 2f/3, 0, 1, 1) });
        Assert.Equal(Pixels.Red, output.At(10, 1));
        output.Copy(source);
        HollowKnightHudRepair.Repair(source, vanilla, output, new[] {
            new SkinHudRect("other0001", 0, 0, 1f/3, 1), new SkinHudRect("other0002", 1f/3, 0, 2f/3, 1),
            new SkinHudRect("other0003", 2f/3, 0, 1, 1) });
        Assert.Equal(Pixels.Blue, output.At(10, 1));
    }

    [Fact]
    public void Hud_repair_failure_releases_preparation_and_retains_previous_visual()
    {
        var rig = new Rig(); var source = new Pixels(4, 4); var vanilla = new Pixels(4, 4);
        vanilla.Fill(0, 0, 4, 4, Pixels.Red); bool fail = false;
        var outputs = new List<Pixels>();
        var hud = rig.Add("Hud.png", prepare: (skin, original) => {
            var output = new Pixels(4, 4) { FailWrite = fail }; outputs.Add(output);
            var image = rig.Session.AllocateAuxiliary(128, () => new SkinTexture(output, 4, 4,
                () => output.Released = true, () => output.Released));
            HollowKnightHudRepair.Repair(source, vanilla, output, new[] { new SkinHudRect("blank", 0, 0, 1, 1) });
            return image.Value;
        });
        Assert.Equal(SkinApplyStatus.Applied, rig.Session.TryApply(rig.Pack("a", "Hud.png")).Status);
        var prior = hud.Value; fail = true;
        Assert.Equal(SkinApplyStatus.Failed, rig.Session.TryApply(rig.Pack("b", "Hud.png")).Status);
        Assert.Same(prior, hud.Value); Assert.False(outputs[0].Released); Assert.True(outputs[1].Released);
        Assert.Equal(1, rig.Session.SkinStamp); rig.Session.TryRestore(); Assert.All(outputs, x => Assert.True(x.Released));
    }

    sealed class Pixels : ISkinHudPixels
    {
        public int Width { get; } public int Height { get; }
        readonly SkinHudPixel[,] values;
        public bool FailWrite, Released;
        public static readonly SkinHudPixel Red = new SkinHudPixel(1, 0, 0, 1), Blue = new SkinHudPixel(0, 0, 1, 1);
        public Pixels(int width, int height) { Width = width; Height = height; values = new SkinHudPixel[width, height]; }
        public SkinHudPixel At(int x, int y) => values[x, y];
        public SkinHudPixel Pixel(int x, int y) => values[x, y];
        public void Fill(int x, int y, int width, int height, SkinHudPixel value)
        { for (int j = y; j < y + height; j++) for (int i = x; i < x + width; i++) values[i, j] = value; }
        public void Copy(Pixels source) { Array.Copy(source.values, values, values.Length); }
        public SkinHudPixel Sample(float u, float v) => values[Math.Clamp((int)(u * Width), 0, Width - 1), Math.Clamp((int)(v * Height), 0, Height - 1)];
        public void WriteRow(int x, int y, SkinHudPixel[] row, int count)
        {
            if (FailWrite) throw new IOException("injected HUD repair write failure");
            for (int i = 0; i < count; i++) values[x + i, y] = row[i];
        }
    }

    [Fact]
    public void Scratch_admission_is_bounded_and_released_in_finally()
    {
        var rig = new Rig(100); bool ran = false;
        Assert.Throws<InvalidDataException>(() => rig.Session.WithScratch(101, () => ran = true));
        Assert.False(ran); Assert.Equal(0, rig.Session.AccountedBytes);
        Assert.Throws<IOException>(() => rig.Session.WithScratch(90, () => {
            Assert.Equal(90, rig.Session.AccountedBytes);
            Assert.Throws<InvalidDataException>(() => rig.Session.AllocateAuxiliary(11, () => null));
            throw new IOException("pixel failure");
        }));
        Assert.Equal(0, rig.Session.AccountedBytes);
    }

    [Fact]
    public void Atlas_refresh_rebinds_new_identity_and_off_failure_keeps_backup_for_retry()
    {
        var rig = new Rig(); var atlas = new Atlas();
        rig.ExtraSlots.Add(new SkinAtlasSlot(atlas, "Knight.png", rig.Session.AllocateAuxiliary).Binding());
        rig.Session.TryApply(rig.Pack("a", "Knight.png"));
        Assert.Equal(SkinApplyStatus.Unchanged, rig.Session.Refresh().Status); Assert.Equal(1, atlas.Captures);
        atlas.Alive = false; var next = new Atlas();
        rig.ExtraSlots.Add(new SkinAtlasSlot(next, "Knight.png", rig.Session.AllocateAuxiliary).Binding());
        Assert.Equal(SkinApplyStatus.Applied, rig.Session.Refresh().Status);
        Assert.Equal(1, next.Captures); Assert.All(atlas.Created, x => Assert.True(x.Released));
        var prior = next.Visual; next.FailCopies = 1;
        Assert.Equal(SkinApplyStatus.RestoreFailed, rig.Session.TryRestore().Status);
        Assert.Same(prior, next.Visual); Assert.False(next.Created[0].Released);
        Assert.Equal(SkinApplyStatus.Restored, rig.Session.TryRestore().Status);
        Assert.Same(next.Vanilla, next.Visual); Assert.All(next.Created, x => Assert.True(x.Released));
        Assert.Equal(0, rig.Session.AccountedBytes);
    }

    [Fact]
    public void Atlas_failure_also_rolls_back_material_slot_in_same_real_session()
    {
        var rig = new Rig(); var material = rig.Add("Knight.png"); var atlas = new Atlas();
        rig.ExtraSlots.Add(new SkinAtlasSlot(atlas, "Knight.png", rig.Session.AllocateAuxiliary).Binding());
        rig.Session.TryApply(rig.Pack("a", "Knight.png")); var priorMaterial = material.Value; var priorAtlas = atlas.Visual;
        atlas.FailCopies = 1;
        Assert.Equal(SkinApplyStatus.Failed, rig.Session.TryApply(rig.Pack("b", "Knight.png")).Status);
        Assert.Same(priorMaterial, material.Value); Assert.Same(priorAtlas, atlas.Visual);
        Assert.Equal(1, rig.Session.SkinStamp); Assert.False(rig.Loader.Created[0].Released);
        rig.Session.TryRestore(); Assert.Equal(0, rig.Session.AccountedBytes);
    }

    [Fact]
    public void Runtime_support_scope_is_exact_and_omissions_are_not_claimed_as_205_coverage()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "docs/superpowers/specs/data/hollow-knight-skin-catalog-v1.txt"))) root = root.Parent;
        Assert.NotNull(root);
        var catalog = File.ReadAllLines(Path.Combine(root.FullName, "docs/superpowers/specs/data/hollow-knight-skin-catalog-v1.txt"));
        var supported = HollowKnightSkinTargets.SupportedTargets().Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var omitted = catalog.Except(supported, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(159, supported.Length); Assert.Empty(supported.Except(catalog, StringComparer.Ordinal)); Assert.Equal(46, omitted.Length);
        Assert.Equal(9, omitted.Count(x => x.StartsWith("SaveHud/", StringComparison.Ordinal)));
        Assert.Equal(34, omitted.Count(x => x.StartsWith("AreaBackgrounds/", StringComparison.Ordinal)));
        Assert.Equal(new[] { "Charms/back.png", "Charms/front.png", "Inventory/SpellBG.png" },
            omitted.Where(x => !x.StartsWith("SaveHud/", StringComparison.Ordinal) && !x.StartsWith("AreaBackgrounds/", StringComparison.Ordinal)).ToArray());
        var evidence = Path.Combine(AppContext.BaseDirectory, "runtime-fixtures"); Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "runtime-support-scope.json"), System.Text.Json.JsonSerializer.Serialize(new {
            catalogCount = catalog.Length, supported, omitted, inventory = HollowKnightSkinTargets.InventoryObjects,
            inventoryFsms = HollowKnightSkinTargets.InventoryFsms, charmFields = HollowKnightSkinTargets.CharmFields,
            charmFsms = HollowKnightSkinTargets.CharmFsms, ambiguousAtlasBasenames = new[] { "Geo" },
            limitation = "Mechanism mappings only; actual live target presence and GPU rendering are not established by host tests."
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public void Hud_invalid_uv_rejects_private_preparation_without_touching_input()
    {
        var source = new Pixels(4, 4); var output = new Pixels(4, 4); var vanilla = new Pixels(4, 4);
        Assert.Throws<InvalidDataException>(() => HollowKnightHudRepair.Repair(source, vanilla, output,
            new[] { new SkinHudRect("bad", float.NaN, 0, 1, 1) }));
        Assert.Equal(default(SkinHudPixel), source.At(0, 0));
    }

    [Fact]
    public void Companion_clones_are_derived_consumers_not_original_capture_authority()
    {
        Assert.False(HollowKnightSkinTargets.IsAuthoritativeHierarchy(new[] { "Knight", "HKCompanionRoot" }));
        Assert.False(HollowKnightSkinTargets.IsAuthoritativeHierarchy(new[] { "CharmDisplay", "HKPaneCloneStaging" }));
        Assert.False(HollowKnightSkinTargets.IsAuthoritativeHierarchy(new[] { "HUD Cln", "HKCompFrame" }));
        Assert.True(HollowKnightSkinTargets.IsAuthoritativeHierarchy(new[] { "CharmDisplay", "HudCamera", "_GameCameras" }));
    }

    [Theory]
    [InlineData(false, "Inventory/Nail_1.png", "level1")]
    [InlineData(true, "Inventory/Nail_1.png", "level1")]
    [InlineData(false, "Inventory/DreamNail_1.png", "activeSprite")]
    [InlineData(true, "Inventory/DreamNail_1.png", "activeSprite")]
    [InlineData(false, "Inventory/DreamNail_0.png", "inactiveSprite")]
    [InlineData(true, "Inventory/DreamNail_0.png", "inactiveSprite")]
    public void Inventory_consumed_sprite_moves_before_A_to_B_or_Off_retirement(bool switchPack, string target, string member)
    {
        var inv = new InventoryRig(target, member); var a = inv.Pack("a");
        Assert.Equal(SkinApplyStatus.Applied, inv.Rig.Session.TryApply(a).Status);
        inv.Consume(); var old = inv.Renderer.Value;
        var result = switchPack ? inv.Rig.Session.TryApply(inv.Pack("b")) : inv.Rig.Session.TryRestore();
        Assert.Equal(switchPack ? SkinApplyStatus.Applied : SkinApplyStatus.Restored, result.Status);
        Assert.Same(inv.Field.Value, inv.Renderer.Value); Assert.NotSame(old, inv.Renderer.Value);
        Assert.True(inv.Rig.Loader.Created[0].Released); Assert.Equal(2, inv.Rig.Session.SkinStamp);
        Assert.Equal(SkinApplyStatus.Unchanged, inv.Rig.Session.Refresh().Status);
        inv.Rig.Session.TryRestore(); Assert.Same(inv.Field.Original, inv.Renderer.Value);
        Assert.Equal(0, inv.Rig.Session.AccountedBytes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Inventory_consumer_write_failure_rolls_back_field_and_renderer(bool restoring, bool afterWrite)
    {
        var inv = new InventoryRig(); inv.Rig.Session.TryApply(inv.Pack("a")); inv.Consume();
        var before = inv.Renderer.Value; inv.FailWrites = 1; inv.FailAfterWrite = afterWrite;
        var result = restoring ? inv.Rig.Session.TryRestore() : inv.Rig.Session.TryApply(inv.Pack("b"));
        Assert.Equal(restoring ? SkinApplyStatus.RestoreFailed : SkinApplyStatus.Failed, result.Status);
        Assert.Same(before, inv.Field.Value); Assert.Same(before, inv.Renderer.Value);
        Assert.False(inv.Rig.Loader.Created[0].Released); Assert.Equal(1, inv.Rig.Session.SkinStamp);
        Assert.Equal(SkinApplyStatus.Restored, inv.Rig.Session.TryRestore().Status);
        Assert.Same(inv.Field.Original, inv.Renderer.Value); Assert.Equal(0, inv.Rig.Session.AccountedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inventory_failed_consumer_undo_blocks_and_retains_until_restore(bool afterWrite)
    {
        var inv = new InventoryRig(); inv.Rig.Session.TryApply(inv.Pack("a")); inv.Consume();
        inv.FailWrites = 2; inv.FailAfterWrite = afterWrite;
        Assert.Equal(SkinApplyStatus.RestoreFailed, inv.Rig.Session.TryApply(inv.Pack("b")).Status);
        Assert.Equal(SkinApplyStatus.Blocked, inv.Rig.Session.TryApply(inv.Pack("c")).Status);
        Assert.All(inv.Rig.Loader.Created, x => Assert.False(x.Released));
        Assert.Equal(16, inv.Rig.Session.AccountedBytes); Assert.Equal(1, inv.Rig.Session.SkinStamp);
        Assert.Equal(SkinApplyStatus.Restored, inv.Rig.Session.TryRestore().Status);
        Assert.Same(inv.Field.Original, inv.Field.Value); Assert.Same(inv.Field.Original, inv.Renderer.Value);
        Assert.Equal(2, inv.Rig.Session.SkinStamp); Assert.Equal(0, inv.Rig.Session.AccountedBytes);
    }

    [Fact]
    public void Inventory_late_consumption_uses_source_vanilla_not_generated_renderer_as_original()
    {
        var inv = new InventoryRig(); var other = new object(); inv.Renderer.Value = other;
        inv.Rig.Session.TryApply(inv.Pack("a")); Assert.Same(other, inv.Renderer.Value);
        inv.Consume();
        Assert.Equal(SkinApplyStatus.Restored, inv.Rig.Session.TryRestore().Status);
        Assert.Same(inv.Field.Original, inv.Renderer.Value); Assert.Equal(0, inv.Rig.Session.AccountedBytes);
    }

    [Fact]
    public void Inventory_cancellation_leaves_consumed_sprite_and_resources_current()
    {
        var inv = new InventoryRig(); inv.Rig.Session.TryApply(inv.Pack("a")); inv.Consume();
        var before = inv.Renderer.Value; var cancellation = new CancellationTokenSource();
        inv.Rig.Loader.AfterDecode = cancellation.Cancel;
        Assert.Equal(SkinApplyStatus.Cancelled, inv.Rig.Session.TryApply(inv.Pack("b"), cancellation.Token).Status);
        Assert.Same(before, inv.Field.Value); Assert.Same(before, inv.Renderer.Value);
        Assert.False(inv.Rig.Loader.Created[0].Released); Assert.True(inv.Rig.Loader.Created[1].Released);
        inv.Rig.Session.TryRestore(); Assert.Same(inv.Field.Original, inv.Renderer.Value);
    }

    [Fact]
    public void Inventory_changed_selected_field_restores_its_own_vanilla_and_omission_releases_ownership()
    {
        var inv = new InventoryRig(); var second = new Slot("Inventory/Nail_2.png", new object(), null);
        inv.BindField(second, "level2");
        inv.Rig.Session.TryApply(inv.Rig.Pack("a", inv.Field.Target, second.Target));
        inv.Renderer.Value = second.Value; // later OnEnable selected the other source
        Assert.Equal(SkinApplyStatus.Unchanged, inv.Rig.Session.Refresh().Status);
        Assert.Equal(SkinApplyStatus.Applied, inv.Rig.Session.TryApply(inv.Pack("b")).Status);
        Assert.Same(second.Original, inv.Renderer.Value); Assert.Same(second.Original, second.Value);
        var gameValue = new object(); inv.Renderer.Value = gameValue;
        inv.Rig.Session.TryRestore(); Assert.Same(gameValue, inv.Renderer.Value);
        Assert.Equal(0, inv.Rig.Session.AccountedBytes);
    }

    [Fact]
    public void Inventory_unskinned_game_selection_is_not_overwritten_on_Off()
    {
        var inv = new InventoryRig(); inv.Rig.Session.TryApply(inv.Pack("a")); inv.Consume();
        var unskinned = new object(); inv.Renderer.Value = unskinned;
        inv.Rig.Session.TryRestore(); Assert.Same(unskinned, inv.Renderer.Value);
        Assert.Same(inv.Field.Original, inv.Field.Value); Assert.Equal(0, inv.Rig.Session.AccountedBytes);
    }

    [Fact]
    public void Inventory_consumer_outlives_source_and_still_restores_before_release()
    {
        var inv = new InventoryRig(); inv.Rig.Session.TryApply(inv.Pack("a")); inv.Consume();
        inv.Field.Alive = false;
        Assert.Equal(SkinApplyStatus.Restored, inv.Rig.Session.TryRestore().Status);
        Assert.Same(inv.Field.Original, inv.Renderer.Value); Assert.Equal(0, inv.Rig.Session.AccountedBytes);
    }

    [Fact]
    public void Inventory_ambiguous_shared_sprite_fails_before_mutation_and_retains_current()
    {
        var inv = new InventoryRig(); var second = new Slot("Inventory/Nail_2.png", new object(), null);
        inv.BindField(second, "level2"); var a = inv.Rig.Pack("a", inv.Field.Target, second.Target);
        var shared = new SkinPack("shared", a.Root, new Dictionary<string, string> { [inv.Field.Target] = "0.png", [second.Target] = "0.png" });
        inv.Rig.Session.TryApply(shared); inv.Consume(); var before = inv.Renderer.Value;
        Assert.Equal(SkinApplyStatus.Failed, inv.Rig.Session.TryApply(inv.Rig.Pack("b", inv.Field.Target, second.Target)).Status);
        Assert.Same(before, inv.Renderer.Value); Assert.Same(before, inv.Field.Value);
        Assert.False(inv.Rig.Loader.Created[0].Released); Assert.Equal(1, inv.Rig.Session.SkinStamp);
        Assert.Equal(SkinApplyStatus.RestoreFailed, inv.Rig.Session.TryRestore().Status);
        Assert.False(inv.Rig.Loader.Created[0].Released); // original selection is genuinely ambiguous, never guessed
        inv.Renderer.Value = second.Original; // a later ordinary game selection establishes an unskinned value
        Assert.Equal(SkinApplyStatus.Restored, inv.Rig.Session.TryRestore().Status);
        Assert.Same(second.Original, inv.Renderer.Value); Assert.Equal(0, inv.Rig.Session.AccountedBytes);
    }

    sealed class InventoryRig
    {
        public readonly Rig Rig = new Rig();
        public readonly Slot Field, Renderer;
        public readonly SkinSlot Consumer;
        public int FailWrites; public bool FailAfterWrite;
        readonly Dictionary<SkinTexture, object> sprites = new Dictionary<SkinTexture, object>();
        public InventoryRig(string target = "Inventory/Nail_1.png", string member = "level1")
        {
            Field = new Slot(target, new object(), null);
            Renderer = new Slot(target, Field.Original, null);
            Consumer = new SkinSlot(Renderer, "sprite", Field.Target, () => Renderer.Alive, () => Renderer.Value, value => {
                bool fail = FailWrites > 0; if (fail) FailWrites--;
                if (fail && !FailAfterWrite) throw new IOException("consumer failed before write");
                Renderer.Value = value;
                if (fail) throw new IOException("consumer failed after write");
            }, (skin, original) => throw new InvalidOperationException("Consumer has no independent original-capture authority."));
            BindField(Field, member);
            Rig.Loader.OnRelease = texture => Assert.DoesNotContain(sprites,
                x => ReferenceEquals(x.Key.Value, texture) && ReferenceEquals(x.Value, Renderer.Value));
        }
        public void BindField(Slot field, string member)
        {
            Rig.ExtraSlots.Add(new SkinSlot(field, member, field.Target, () => field.Alive, () => field.Value,
                value => field.Value = value, (skin, original) => {
                    Assert.Same(field.Original, original);
                    if (!sprites.TryGetValue(skin, out var sprite)) { sprite = new object(); skin.Own(sprite); sprites.Add(skin, sprite); }
                    return sprite;
                }, inventoryConsumer: Consumer));
        }
        public SkinPack Pack(string id) => Rig.Pack(id, Field.Target);
        // Exact managed InvNailSprite/InvItemDisplay OnEnable consumption, without game state calls.
        public void Consume() { Renderer.Value = Field.Value; }
    }

    [Fact]
    public void Mode_policy_filters_material_and_atlas_and_rebinds_without_environment()
    {
        var rig = new Rig(); var knight = rig.Add("Knight.png"); var geo = rig.Add("Geo.png");
        var ui = rig.Add("Inventory/Geo.png"); var quirrel = rig.Add("Quirrel.png"); var birthplace = new Atlas();
        rig.ExtraSlots.Add(new SkinAtlasSlot(birthplace, "Birthplace.png", rig.Session.AllocateAuxiliary).Binding());
        var pack = rig.Pack("a", "Knight.png", "Geo.png", "Inventory/Geo.png", "Quirrel.png", "Birthplace.png");
        var request = PolicyRequest(pack); var controller = PolicyController(rig, request);
        controller.Tick(); Assert.NotSame(geo.Original, geo.Value); Assert.NotSame(birthplace.Vanilla, birthplace.Visual);
        request.Mode = "ROTATE"; controller.Tick();
        Assert.Same(geo.Original, geo.Value); Assert.Same(birthplace.Vanilla, birthplace.Visual);
        Assert.NotSame(knight.Original, knight.Value); Assert.NotSame(ui.Original, ui.Value); Assert.NotSame(quirrel.Original, quirrel.Value);
        var fresh = rig.Add("Geo.png"); var freshKnight = rig.Add("Knight.png"); rig.Session.Refresh();
        Assert.Same(fresh.Original, fresh.Value); Assert.NotSame(freshKnight.Original, freshKnight.Value);
        request.Mode = "ON"; controller.Tick(); Assert.NotSame(geo.Original, geo.Value);
        request.Mode = "OFF"; controller.Tick(); Assert.Same(geo.Original, geo.Value); Assert.Same(knight.Original, knight.Value);
    }

    [Fact]
    public void Filter_empty_transition_restores_before_waiting_and_never_reapplies_environment()
    {
        var rig = new Rig(); var geo = rig.Add("Geo.png"); var request = PolicyRequest(rig.Pack("a", "Geo.png"));
        SkinLibraryObservation seen = null; var controller = PolicyController(rig, request, x => seen = x);
        controller.Tick(); Assert.NotSame(geo.Original, geo.Value);
        request.Mode = "ROTATE"; controller.Tick(); Assert.Same(geo.Original, geo.Value);
        Assert.Equal("AwaitingTargets", seen.Status); Assert.Equal(2, rig.Session.SkinStamp);
        rig.Session.Refresh(); controller.Tick(); Assert.Same(geo.Original, geo.Value); Assert.Equal(2, rig.Session.SkinStamp);
        request.Mode = "ON"; controller.Tick(); Assert.NotSame(geo.Original, geo.Value);
    }

    [Fact]
    public void Filter_empty_restore_failure_is_visible_and_retryable_without_false_stamp()
    {
        var rig = new Rig(); var geo = rig.Add("Geo.png"); var request = PolicyRequest(rig.Pack("a", "Geo.png"));
        SkinLibraryObservation seen = null; var controller = PolicyController(rig, request, x => seen = x);
        controller.Tick(); var previous = geo.Value; geo.FailNextWrite = true;
        request.Mode = "ROTATE"; controller.Tick(); Assert.Equal("Failed", seen.Status);
        Assert.Same(previous, geo.Value); Assert.Equal(1, rig.Session.SkinStamp);
        controller.Tick(); Assert.Same(geo.Original, geo.Value); Assert.Equal("AwaitingTargets", seen.Status); Assert.Equal(2, rig.Session.SkinStamp);
    }

    [Fact]
    public void Two_confirmed_deaths_apply_only_at_respawn_and_keep_rebound_environment_vanilla()
    {
        var rig=new Rig();var knight=rig.Add("Knight.png");var geo=rig.Add("Geo.png");
        var a=rig.Pack("a","Knight.png","Geo.png");var b=rig.Pack("b","Knight.png","Geo.png");
        var request=PolicyRequest(a);request.Mode="ROTATE";request.RotationRun="run";
        var frame=new SkinDeathFrame {Hero=new object(),Manager=new object(),Hud=new object(),SaveId=1,MapZone="CROSSROADS",
            Gameplay=true,Playing=true,InPosition=true,WaitingToTransition=true,AcceptingInput=true,TargetsAvailable=true};
        var death=new HollowKnightSkinDeathAdapter(()=>frame);string selected="a";int confirmations=0;
        using var library=new HollowKnightSkinLibrary(()=>request,pack=>rig.Session.TryApply(pack),rig.Session.TryRestore,observation=>{
            if(observation.PendingOccurrence>0&&(observation.Status=="Applied"||observation.Status=="Unchanged")) {
                selected=observation.ActivePackId;request.PendingOccurrence=0;
            }
            return true;
        },()=>rig.Session.Refresh(),death,(run,occurrence)=>{
            confirmations++;var next=selected=="a"?b:a;request.PackId=next.Id;request.Root=next.Root;
            request.Textures=next.Textures.ToDictionary(x=>x.Key,x=>x.Value);request.LastDeath=occurrence;request.PendingOccurrence=occurrence;
            return true;
        },_=>true);
        float now=0;void Poll(){frame.Frame++;library.Tick(now+=1);}
        Poll();Assert.Equal("a",rig.Session.CurrentPack.Id);
        for(int occurrence=1;occurrence<=2;occurrence++) {
            var before=knight.Value;int stamp=rig.Session.SkinStamp;
            death.OnDeath(frame.Hero,frame.Manager);frame.Dead=true;Poll();
            Assert.Equal(occurrence,confirmations);Assert.Same(before,knight.Value);Assert.Equal(stamp,rig.Session.SkinStamp);
            frame.Dead=false;death.HeroInPosition(frame.Hero,frame.Manager);death.SceneCompleted(frame.Hero,frame.Manager);
            frame.Frame++;library.Tick(now+.1f);frame.Frame++;library.Tick(now+.2f);Poll();
            Assert.NotSame(before,knight.Value);Assert.Equal(stamp+1,rig.Session.SkinStamp);Assert.Same(geo.Original,geo.Value);
            Poll();Assert.Equal(0,death.Occurrence);Assert.Equal(selected,rig.Session.CurrentPack.Id);
            var sceneGeo=rig.Add("Geo.png");var sceneKnight=rig.Add("Knight.png");rig.Session.Refresh();
            Assert.Same(sceneGeo.Original,sceneGeo.Value);Assert.NotSame(sceneKnight.Original,sceneKnight.Value);
        }
        rig.Session.TryRestore();Assert.Same(knight.Original,knight.Value);Assert.Same(geo.Original,geo.Value);
    }
    [Theory]
    [InlineData("owners")]
    [InlineData("scene")]
    public void Cached_pending_success_waits_for_current_scheduled_refresh_and_same_pack_recovery(string invalidation)
    {
        using var r=new ScheduledRotationRig();r.FirstRespawn();
        Assert.Equal("a",r.Selected);Assert.Equal(1,r.Request.PendingOccurrence);Assert.Equal(2,r.Applied.Count);
        int stamp=r.Rendering.Session.SkinStamp;
        r.Knight.Alive=false;r.Knight=r.Rendering.Add("Knight.png");
        if(invalidation=="owners"){r.Frame.Hero=new object();r.Frame.Hud=new object();}
        else {r.Frame.Transitioning=true;r.Schedule.Invalidate();}
        r.Tick(2.01f);int caches=r.TargetCaches;
        r.Frame.Transitioning=false;r.Respawn(2.1f,2.2f);r.Tick(3);
        Assert.Equal("a",r.Selected); // old cached Applied must not acknowledge vanilla replacement targets
        Assert.Equal(1,r.Request.PendingOccurrence);Assert.Same(r.Knight.Original,r.Knight.Value);
        Assert.Equal(SkinApplyStatus.AwaitingTargets,r.Schedule.LastResult.Status);
        Assert.Equal(caches,r.TargetCaches);Assert.Equal(stamp,r.Rendering.Session.SkinStamp);
        r.Knight.AlwaysFail=true;r.Tick(4.02f);
        Assert.Equal(SkinApplyStatus.RestoreFailed,r.Schedule.LastResult.Status);
        Assert.Equal("a",r.Selected);Assert.Equal(1,r.Request.PendingOccurrence);Assert.Equal(stamp,r.Rendering.Session.SkinStamp);
        var failure=r.Schedule.LastResult;r.Schedule.Invalidate();
        Assert.Same(failure,r.Schedule.LastResult); // invalidation cannot hide a current restore-required failure
        r.Knight.AlwaysFail=false;r.Tick(5.03f); // bounded restoration recovery, still the same frozen B
        Assert.Equal("a",r.Selected);Assert.Equal(1,r.Request.PendingOccurrence);
        r.Tick(6.04f);
        Assert.Equal("b",r.Selected);Assert.Equal(0,r.Request.PendingOccurrence);Assert.NotSame(r.Knight.Original,r.Knight.Value);
        Assert.Same(r.Applied[1],r.Applied[2]);Assert.Equal(1,r.Confirms);Assert.Equal(stamp+1,r.Rendering.Session.SkinStamp);
        Assert.Same(r.Geo.Original,r.Geo.Value);
        r.Request.Mode="OFF";r.Request.RotationRun=null;r.Tick(7.05f);
        Assert.Same(r.Knight.Original,r.Knight.Value);Assert.Same(r.Geo.Original,r.Geo.Value);
    }
    [Fact]
    public void Unchanged_owners_retry_cached_report_without_extra_scan_or_apply()
    {
        using var r=new ScheduledRotationRig();r.FirstRespawn();
        var result=r.Schedule.LastResult;int refreshes=r.Refreshes,caches=r.TargetCaches,stamp=r.Rendering.Session.SkinStamp;
        r.Tick(2.01f);r.Tick(2.1f);r.Tick(2.2f);r.Tick(3);
        Assert.Equal("b",r.Selected);Assert.Equal(0,r.Request.PendingOccurrence);
        Assert.NotSame(r.Knight.Original,r.Knight.Value);Assert.Same(result,r.Schedule.LastResult);
        Assert.Equal(refreshes,r.Refreshes);Assert.Equal(caches,r.TargetCaches);Assert.Equal(2,r.Applied.Count);
        Assert.Equal(stamp,r.Rendering.Session.SkinStamp);
    }
    sealed class ScheduledRotationRig : IDisposable
    {
        public readonly Rig Rendering=new Rig();
        public Slot Knight;public readonly Slot Geo;
        public readonly SkinDeathFrame Frame=new SkinDeathFrame {Hero=new object(),Manager=new object(),Hud=new object(),SaveId=1,MapZone="CROSSROADS",
            Gameplay=true,Playing=true,InPosition=true,WaitingToTransition=true,AcceptingInput=true,TargetsAvailable=true};
        public readonly SkinLibraryRequest Request;
        public readonly HollowKnightSkinDeathAdapter Death;
        public readonly HollowKnightSkinLibrary Library;
        public readonly SkinRuntimeRefreshSchedule Schedule;
        public readonly List<SkinPack> Applied=new List<SkinPack>();
        public string Selected="a";public int Confirms,Refreshes,TargetCaches;
        int reports;
        public ScheduledRotationRig()
        {
            Knight=Rendering.Add("Knight.png");Geo=Rendering.Add("Geo.png");
            var a=Rendering.Pack("a","Knight.png","Geo.png");var b=Rendering.Pack("b","Knight.png","Geo.png");
            Request=PolicyRequest(a);Request.Mode="ROTATE";Request.RotationRun="run";
            Death=new HollowKnightSkinDeathAdapter(()=>Frame);
            Schedule=new SkinRuntimeRefreshSchedule(()=>TargetCaches++,()=>Library.CanRefresh,
                ()=>{Refreshes++;Schedule.Publish(Rendering.Session.Refresh());});
            Library=new HollowKnightSkinLibrary(()=>Request,pack=>{Applied.Add(pack);return Schedule.Publish(Rendering.Session.TryApply(pack));},
                ()=>Schedule.Publish(Rendering.Session.TryRestore()),observation=>{
                    if(observation.PendingOccurrence==0 || (observation.Status!="Applied"&&observation.Status!="Unchanged"))return true;
                    if(++reports==1)return false;
                    Selected=observation.ActivePackId;Request.PendingOccurrence=0;return true;
                },()=>Schedule.LastResult,Death,(run,occurrence)=>{
                    Confirms++;Request.LastDeath=occurrence;Request.PendingOccurrence=occurrence;
                    Request.PackId=b.Id;Request.Root=b.Root;Request.Textures=b.Textures.ToDictionary(x=>x.Key,x=>x.Value);return true;
                },_=>true);
        }
        public void Tick(float time)
        {
            Frame.Frame++;
            Schedule.Tick(time,Frame.Hero,Frame.Hud); // actual production scheduler, before library's frame sampler/poll
            Library.Tick(time); // observe above is cached LastResult, never an unconditional refresh
        }
        public void Respawn(float first,float second){Frame.Dead=false;Death.HeroInPosition(Frame.Hero,Frame.Manager);Death.SceneCompleted(Frame.Hero,Frame.Manager);Tick(first);Tick(second);}
        public void FirstRespawn(){Tick(0);Death.OnDeath(Frame.Hero,Frame.Manager);Frame.Dead=true;Tick(.1f);Tick(1);Respawn(1.1f,1.2f);Tick(2);}
        public void Dispose(){Library.Dispose();Rendering.Session.Dispose();}
    }
    static SkinLibraryRequest PolicyRequest(SkinPack pack) => new SkinLibraryRequest {
        ProfileId = "hollow-knight", ConfigSha256 = new string('a', 64), Mode = "ON", PackId = pack.Id,
        TreeSha256 = new string('b', 64), Root = pack.Root, Textures = pack.Textures.ToDictionary(x => x.Key, x => x.Value)
    };
    static SkinLibraryRuntimeController PolicyController(Rig rig, SkinLibraryRequest request, Action<SkinLibraryObservation> report = null) =>
        new SkinLibraryRuntimeController(HollowKnightSkinPolicy.RuntimeRules, () => request, pack => rig.Session.TryApply(pack), () => rig.Session.TryRestore(), report ?? (_ => { }), () => rig.Session.Refresh());

    sealed class Rig
    {
        public readonly List<Slot> Slots = new List<Slot>();
        public readonly List<SkinSlot> ExtraSlots = new List<SkinSlot>();
        public readonly Loader Loader = new Loader();
        public readonly SkinRuntimeSession Session;
        public Rig(long budget = 512L * 1024 * 1024)
        {
            Session = new SkinRuntimeSession(Loader,
                () => Slots.Where(x => x.Alive).Select(x => x.Binding()).Concat(ExtraSlots).ToList(), budget,
                HollowKnightSkinPolicy.RuntimeRules);
            Loader.OnRelease = handle => Assert.DoesNotContain(Slots.Where(x => x.Alive), x => ReferenceEquals(x.Value, handle));
        }
        public Slot Add(string target, object original = null, Func<SkinTexture, object, object> prepare = null)
        {
            var slot = new Slot(target, original ?? new object(), prepare); Slots.Add(slot); return slot;
        }
        public SkinPack Pack(string id, params string[] targets)
        {
            var root = Path.Combine(AppContext.BaseDirectory, "runtime-fixtures", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); var files = new Dictionary<string, string>();
            for (int i = 0; i < targets.Length; i++)
            {
                var name = i + ".png";
                File.WriteAllBytes(Path.Combine(root, name), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg=="));
                files.Add(targets[i], name);
            }
            return new SkinPack(id, root, files);
        }
    }
    sealed class Slot
    {
        public readonly string Target; public readonly object Original; public object Value;
        public bool Alive = true, FailNextWrite, AlwaysFail; readonly Func<SkinTexture, object, object> prepare;
        public Slot(string target, object value, Func<SkinTexture, object, object> prepare) { Target = target; Original = Value = value; this.prepare = prepare; }
        public SkinSlot Binding() => new SkinSlot(this, "value", Target, () => Alive, () => Value, value =>
        {
            Value = value;
            if (AlwaysFail || FailNextWrite) { FailNextWrite = false; throw new IOException("injected setter failure"); }
        }, prepare ?? ((texture, _) => texture.Value));
    }
    sealed class Loaded
    {
        public object Value = new object(); public bool Released; public int ReleaseCount;
    }
    sealed class Loader : ISkinTextureDecoder
    {
        public readonly List<Loaded> Created = new List<Loaded>();
        public bool FailDecode, WrongDimensions, DelayedRelease, FailRelease; public Action AfterDecode; public Action<object> OnRelease;
        public SkinTexture Decode(byte[] bytes, int width, int height)
        {
            if (FailDecode) throw new IOException("injected decoder failure");
            var value = new Loaded(); Created.Add(value); AfterDecode?.Invoke();
            return new SkinTexture(value.Value, WrongDimensions ? width + 1 : width, height, () =>
            {
                if (FailRelease) throw new IOException("injected release failure");
                OnRelease?.Invoke(value.Value); value.ReleaseCount++;
                if (!DelayedRelease) value.Released = true;
            }, () => value.Released);
        }
    }
}
