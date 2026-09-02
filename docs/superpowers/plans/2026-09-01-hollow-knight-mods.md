# Hollow Knight Typed Mods Catalog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an independently authored Hollow Knight Mods catalog, persistent controller runtime, and existing-layout gear/modal with truthful typed capabilities and tracked deferred rows.

**Architecture:** Extend the shared descriptor/controller with explicit unavailable capabilities, add a pure shared menu model, then build a Hollow Knight adapter over a narrow typed API. A process-resident runtime owns persistence independently of display state, while a separate partial presenter renders through H2's existing context-box and gear seams. Only reversible presentation capabilities are initially available; every other catalog row remains disabled and ledgered until its final managed-rewrite gate.

**Tech Stack:** C#/.NET 8 xUnit, Unity 6000 IL2CPP managed patch injection, Hollow Knight `1.5.12620` managed APIs, Python source contracts, PowerShell exact-source compile checks, Git.

**Authority:** `docs/superpowers/specs/2026-09-01-hollow-knight-mods-design.md`

**Execution boundary:** Work only in `Darkaxt/HollowKnightAndroid`. Do not read from, contact, or modify `igawa6/dualsouls`; do not copy additional upstream modules. Do not run ADB, Android-device, emulator, signing, workflow-dispatch, or release commands. H3 and H4 will share one later explicitly authorized device candidate.

**Execution status:** Tasks 1–8 host work is complete through the current authority/state-core checkpoint `fix: unify Hollow Knight flash authority`, following process-owned policy checkpoint `ba32812`. This is host completion only: physical UI/effect acceptance remains unpassed and batched with H4, and every `HKMOD-001` through `HKMOD-018` capability remains deferred to Final managed-rewrite remediation.

---

## File structure and ownership

### Shared Mods contracts

- Modify `tools/shared-patches/src/Mods/TweakContracts.cs`: availability metadata and deferred descriptor factory.
- Modify `tools/shared-patches/src/Mods/TweakController.cs`: enforce unavailable-row behavior.
- Create `tools/shared-patches/src/Mods/TweakMenuModel.cs`: game-neutral group/selection/window/action state.
- Create `tools/shared-patches/src/Mods/PlayerPrefsTweakStore.cs`: shared Unity persistence adapter.
- Modify `tools/shared-patches-tests/TweakControllerTests.cs`.
- Create `tools/shared-patches-tests/TweakMenuModelTests.cs`.

### Hollow Knight typed implementation

- Create `tools/hollow-knight-patches/src/mods/HollowKnightTweakAdapter.cs`: fork-owned full catalog and typed mapping.
- Create `tools/hollow-knight-patches/src/mods/HollowKnightModsSession.cs`: pure readiness/controller/menu lifetime.
- Create `tools/hollow-knight-patches/src/mods/HollowKnightGameTweakApi.cs`: stock managed presentation seam.
- Create `tools/hollow-knight-patches/src/mods/HollowKnightModsRuntime.cs`: process-resident Unity owner for the session and lifeblood renderer policy.
- Create `tools/hollow-knight-patches/src/mods/HollowKnightFlashPolicyCore.cs`: immutable decisions and Unity-free per-renderer transition state.
- Create `tools/hollow-knight-patches/src/mods/HollowKnightLifebloodFlashPolicy.cs`: process-owned Unity binding over the pure state core.
- Create `tools/hollow-knight-patches/src/dualsouls/HollowKnightModsPresenter.cs`: gear and context-box rendering only.
- Modify `tools/hollow-knight-patches/src/dualsouls/HkStageHooks.cs`: delegate to runtime/presenter and keep Mods and nullable legacy H2 flash signals separate.
- Modify `tools/hollow-knight-patches/src/dualsouls/HkDirectDisplayAdapter.cs`: start Mods runtime before the display-enabled gate.
- Modify `tools/hollow-knight-patches/src/dualsouls/HKDualScreen.cs`: publish live H2 flash config only; no renderer-policy ownership.
- Modify `tools/shared-patches-tests/SharedPatches.Tests.csproj`: compile pure Hollow Knight adapter/session/state-core sources.
- Create `tools/shared-patches-tests/HollowKnightFlashPolicyCoreTests.cs`.
- Create `tools/shared-patches-tests/HollowKnightTweakAdapterTests.cs`.
- Create `tools/shared-patches-tests/HollowKnightModsSessionTests.cs`.

### Existing Silksong consumer

- Modify `tools/silksong-patches/src/dualscreen/DsModsScreen.cs`: use the shared PlayerPrefs store.
- Delete `tools/silksong-patches/src/mods/UnityTweakStore.cs` only after both exact compiles pass.

### Contracts and evidence

- Extend `tools/ci/tests/test_hollow_knight_reference_port.py`: H3 source/ownership/catalog/UI prohibitions.
- Create `docs/verification/hollow-knight-mod-capabilities.md`: authoritative `HKMOD-001` through `HKMOD-018` ledger.
- Modify `docs/superpowers/plans/2026-09-01-hollow-knight-first-dual-souls.md`: record H2 host completion and H3 host-batch status without adding device loops.
- Modify `README.md` only after the complete host batch passes.

## Non-negotiable behavior

- `GameId` is exactly `hollow-knight`.
- Master defaults off; successful explicit changes persist and flush.
- Display loss detaches UI only; it does not restore or disable enabled tweaks.
- Deferred rows render but cannot cycle, apply, or become active through stale preferences.
- `companion_backdrop` defaults to `dimmed`; `lifeblood_flash` defaults to existing H2 `soft` behavior.
- Master off and reset restore H2's current presentation behavior, not guessed vanilla constants.
- No state slot, save mutation, warp, injected field, native address, memory scan, or copied H3 module enters production.

---

### Task 1: Add truthful deferred capabilities to the shared controller

**Files:**
- Modify: `tools/shared-patches/src/Mods/TweakContracts.cs`
- Modify: `tools/shared-patches/src/Mods/TweakController.cs`
- Modify: `tools/shared-patches-tests/TweakControllerTests.cs`

- [x] **Step 1: Write failing descriptor invariant tests**

Add these tests to `TweakControllerTests.cs`:

```csharp
[Fact]
public void DeferredDescriptorRequiresReasonAndTrackingId()
{
    Assert.Throws<ArgumentException>(() => TweakDescriptor.Deferred(
        "bench_teleport", "WORLD", "BENCH TELEPORT", "Travel to a bench.", "", "missing seam"));
    Assert.Throws<ArgumentException>(() => TweakDescriptor.Deferred(
        "bench_teleport", "WORLD", "BENCH TELEPORT", "Travel to a bench.", "HKMOD-017", ""));
}

[Fact]
public void DeferredDescriptorHasImmutableOffStorageDefault()
{
    var row = TweakDescriptor.Deferred(
        "bench_teleport", "WORLD", "BENCH TELEPORT", "Travel to a bench.",
        "HKMOD-017", "No safe scene-transition seam is enabled.");

    Assert.False(row.IsAvailable);
    Assert.Equal("off", row.DefaultValue);
    Assert.Equal(new[] { "off" }, row.Values);
    Assert.Equal("HKMOD-017", row.TrackingId);
}
```

- [x] **Step 2: Write failing controller enforcement tests**

```csharp
[Fact]
public void DeferredRowCannotCycleApplyOrEnableFromStaleStorage()
{
    var adapter = new DeferredRecordingAdapter();
    var store = new MemoryStore
    {
        ["dualsouls.mods.hollow-knight.master"] = "1",
        ["dualsouls.mods.hollow-knight.value.bench_teleport"] = "on"
    };
    var controller = new TweakController(adapter, store);

    Assert.True(controller.Initialize().Success);
    Assert.True(controller.MasterEnabled);
    Assert.Equal("off", controller.Value("bench_teleport"));
    Assert.Empty(adapter.Applied);

    var cycle = controller.Cycle("bench_teleport");
    Assert.False(cycle.Success);
    Assert.Contains("HKMOD-017", cycle.Error);
    Assert.Empty(adapter.Applied);
}
```

Define `DeferredRecordingAdapter` beside the existing test adapter with one `TweakDescriptor.Deferred(...)` row and an `Applied` list.

- [x] **Step 3: Run RED tests**

Run:

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release --filter "FullyQualifiedName~TweakControllerTests"
```

Expected: compilation fails because `Deferred`, `IsAvailable`, `UnavailableReason`, and `TrackingId` do not exist.

- [x] **Step 4: Implement descriptor availability**

Extend `TweakDescriptor` with a private canonical constructor, preserve the existing constructor as available, and add this factory:

```csharp
public static TweakDescriptor Deferred(
    string id,
    string group,
    string title,
    string description,
    string trackingId,
    string unavailableReason)
{
    if (string.IsNullOrWhiteSpace(trackingId))
        throw new ArgumentException("A deferred tweak tracking id is required.", nameof(trackingId));
    if (string.IsNullOrWhiteSpace(unavailableReason))
        throw new ArgumentException("A deferred tweak reason is required.", nameof(unavailableReason));
    return new TweakDescriptor(
        id, group, title, description, "off", new[] { "off" },
        false, trackingId, unavailableReason);
}

public bool IsAvailable { get; }
public string TrackingId { get; }
public string UnavailableReason { get; }
```

The existing public constructor delegates with `isAvailable: true`, empty tracking ID, and empty reason.

- [x] **Step 5: Enforce availability in `TweakController`**

In `Cycle`, reject deferred rows before the master check:

```csharp
if (!descriptor.IsAvailable)
    return TweakActionResult.Fail(
        descriptor.Title + " is deferred (" + descriptor.TrackingId + "): " +
        descriptor.UnavailableReason);
```

In `ApplySelectedSet`, skip unavailable descriptors before reading/applying values:

```csharp
if (!descriptor.IsAvailable) continue;
```

Initialization continues validating a deferred row against its sole `off` value so stale `on` values are corrected and flushed.

- [x] **Step 6: Run GREEN shared tests**

Run:

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release --filter "FullyQualifiedName~TweakControllerTests"
```

Expected: all `TweakControllerTests` pass with zero failures/skips.

- [x] **Step 7: Commit and push the contract checkpoint**

```powershell
git add tools/shared-patches/src/Mods/TweakContracts.cs `
        tools/shared-patches/src/Mods/TweakController.cs `
        tools/shared-patches-tests/TweakControllerTests.cs
git commit -m "feat: track unavailable tweak capabilities"
git push
```

### Task 2: Add the shared menu model and persistence implementation

**Files:**
- Create: `tools/shared-patches/src/Mods/TweakMenuModel.cs`
- Create: `tools/shared-patches/src/Mods/PlayerPrefsTweakStore.cs`
- Create: `tools/shared-patches-tests/TweakMenuModelTests.cs`
- Modify: `tools/silksong-patches/src/dualscreen/DsModsScreen.cs`
- Delete after verification: `tools/silksong-patches/src/mods/UnityTweakStore.cs`

- [x] **Step 1: Write failing menu-model tests**

Create `TweakMenuModelTests.cs` covering stable groups, row selection, viewport movement, disabled rejection, master/reset forwarding, errors, and close state:

```csharp
[Fact]
public void SelectionScrollsWithinCurrentGroupAndDeferredRowCannotRun()
{
    var fixture = MenuFixture.Create(visibleRows: 2);
    fixture.Model.Open();

    fixture.Model.MoveRow(1);
    fixture.Model.MoveRow(1);

    Assert.Equal(2, fixture.Model.SelectedRowIndex);
    Assert.Equal(1, fixture.Model.WindowStart);
    Assert.False(fixture.Model.CycleSelected().Success);
    Assert.Contains("HKMOD-017", fixture.Model.Message);
    Assert.True(fixture.Model.MessageIsError);
    Assert.Empty(fixture.Adapter.Applied);
}

[Fact]
public void ChangingGroupsResetsRowAndPreservesOpenState()
{
    var fixture = MenuFixture.Create(visibleRows: 3);
    fixture.Model.Open();
    fixture.Model.MoveRow(1);
    fixture.Model.MoveGroup(1);

    Assert.True(fixture.Model.IsOpen);
    Assert.Equal(1, fixture.Model.SelectedGroupIndex);
    Assert.Equal(0, fixture.Model.SelectedRowIndex);
    Assert.Equal(0, fixture.Model.WindowStart);
}
```

The fixture uses the existing memory store and a recording adapter with at least two groups and one deferred row.

- [x] **Step 2: Run the RED menu-model tests**

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release --filter "FullyQualifiedName~TweakMenuModelTests"
```

Expected: compilation fails because `TweakMenuModel` does not exist.

- [x] **Step 3: Implement `TweakMenuModel`**

Create a pure C# type with this public contract:

```csharp
public sealed class TweakMenuModel
{
    public TweakMenuModel(TweakController controller, int visibleRows);
    public bool IsOpen { get; }
    public int SelectedGroupIndex { get; }
    public int SelectedRowIndex { get; }
    public int WindowStart { get; }
    public int VisibleRows { get; }
    public string Message { get; }
    public bool MessageIsError { get; }
    public IReadOnlyList<string> Groups { get; }
    public IReadOnlyList<TweakDescriptor> CurrentRows { get; }
    public TweakDescriptor Selected { get; }
    public void Open();
    public void Close();
    public void MoveGroup(int delta);
    public void MoveRow(int delta);
    public TweakActionResult ToggleMaster();
    public TweakActionResult CycleSelected();
    public TweakActionResult Reset();
}
```

Use first-seen descriptor group order, wrap group/row movement, keep the selected row inside `[WindowStart, WindowStart + VisibleRows)`, and set `Message` from each action result. Do not call an adapter directly.

- [x] **Step 4: Run GREEN model tests**

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release --filter "FullyQualifiedName~TweakMenuModelTests"
```

Expected: all menu-model tests pass.

- [x] **Step 5: Create the shared Unity store**

Create `PlayerPrefsTweakStore.cs`:

```csharp
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

namespace DualSouls.Mods
{
    public sealed class PlayerPrefsTweakStore : ITweakStore
    {
        public string Read(string key) =>
            PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;

        public void Write(string key, string value) =>
            PlayerPrefs.SetString(key, value);

        public void Flush() => PlayerPrefs.Save();
    }
}
#endif
```

Change the Silksong consumer to construct `new PlayerPrefsTweakStore()`.

- [x] **Step 6: Run both exact compiles before deleting the old store**

```powershell
pwsh -NoProfile -File tools/silksong-patches/check.ps1 `
  -Depot 'D:\Temp\hkandroid-task11-silksong-managed' `
  -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
pwsh -NoProfile -File tools/hollow-knight-patches/check.ps1 `
  -Depot 'D:\Temp\dualsouls-hk-12620\Hollow Knight\hollow_knight_Data\Managed' `
  -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
```

Expected: both compile with zero errors.

- [x] **Step 7: Delete the superseded store and rerun Silksong compile**

Delete `tools/silksong-patches/src/mods/UnityTweakStore.cs`, then rerun the Silksong command above. Expected: zero errors and no reference to `UnityTweakStore` remains.

- [x] **Step 8: Commit and push the shared-menu checkpoint**

```powershell
git add tools/shared-patches/src/Mods `
        tools/shared-patches-tests/TweakMenuModelTests.cs `
        tools/silksong-patches/src/dualscreen/DsModsScreen.cs `
        tools/silksong-patches/src/mods/UnityTweakStore.cs
git commit -m "feat: share Mods menu state and storage"
git push
```

### Task 3: Define the Hollow Knight catalog, typed adapter, and ledger

**Files:**
- Create: `tools/hollow-knight-patches/src/mods/HollowKnightTweakAdapter.cs`
- Create: `tools/shared-patches-tests/HollowKnightTweakAdapterTests.cs`
- Modify: `tools/shared-patches-tests/SharedPatches.Tests.csproj`
- Create: `docs/verification/hollow-knight-mod-managed-api-audit.md`
- Create: `docs/verification/hollow-knight-mod-capabilities.md`

- [x] **Step 1: Audit the exact managed API without staging decompiled game code**

Record the exact `Assembly-CSharp.dll` SHA-256, then use the installed `ilspycmd` only against the user's local `1.5.12620` depot:

```powershell
Get-FileHash 'D:\Temp\dualsouls-hk-12620\Hollow Knight\hollow_knight_Data\Managed\Assembly-CSharp.dll' -Algorithm SHA256
$root = 'D:\Temp\hk-h3-api-audit'
New-Item -ItemType Directory -Force $root | Out-Null
foreach ($type in 'PlayerData','HeroController','HealthManager','GameManager','GameMap') {
  ilspycmd --disable-updatecheck `
    -r 'D:\Temp\dualsouls-hk-12620\Hollow Knight\hollow_knight_Data\Managed' `
    -t $type `
    'D:\Temp\dualsouls-hk-12620\Hollow Knight\hollow_knight_Data\Managed\Assembly-CSharp.dll' `
    | Set-Content -Encoding UTF8 "$root\$type.cs"
}
```

Create `hollow-knight-mod-managed-api-audit.md` containing only assembly provenance, public member signatures, semantic findings, and promotion decisions—never decompiled method bodies. Inspect every catalog row. A public field or method alone is not sufficient: if baseline ownership, legal-state behavior, scene replacement, or save rollback remains unproved, record the row as deferred with the missing proof. The bounded audit must explicitly record the stock `HeroController.SetDamageMode`/`damageMode`, movement fields, `PlayerData` resource/charm/Geo methods and fields, and why none is promoted merely from symbol presence.

Delete `D:\Temp\hk-h3-api-audit` after the audit document is complete.

- [x] **Step 2: Write the RED adapter catalog tests**

Add `HollowKnightTweakAdapter.cs` to the test project explicitly:

```xml
<Compile Include="..\hollow-knight-patches\src\mods\HollowKnightTweakAdapter.cs"
         Link="HollowKnight\HollowKnightTweakAdapter.cs"
         Condition="Exists('..\hollow-knight-patches\src\mods\HollowKnightTweakAdapter.cs')" />
```

Create tests asserting:

```csharp
[Fact]
public void CatalogIsCompleteStableAndProfileQualified()
{
    var api = new FakeHollowKnightTweakApi();
    var adapter = new HollowKnightTweakAdapter(api);

    Assert.Equal("hollow-knight", adapter.GameId);
    Assert.Equal(new[]
    {
        "companion_backdrop", "lifeblood_flash", "damage_received", "nail_damage",
        "one_hit_kills", "run_speed", "unlimited_soul", "charm_costs",
        "unlimited_notches", "equip_anywhere", "geo_multiplier", "keep_geo_on_death",
        "journal_one_kill", "auto_map", "health_bars", "damage_numbers",
        "boss_retry", "secret_radar", "bench_teleport", "state_slots"
    }, adapter.Descriptors.Select(row => row.Id));
    Assert.All(adapter.Descriptors.Skip(2), row =>
    {
        Assert.False(row.IsAvailable);
        Assert.StartsWith("HKMOD-", row.TrackingId);
    });
}

[Fact]
public void AvailablePresentationRowsMapAndRestoreExactly()
{
    var api = new FakeHollowKnightTweakApi();
    var adapter = new HollowKnightTweakAdapter(api);
    adapter.CaptureBaseline();

    Assert.True(adapter.Apply("companion_backdrop", "black").Success);
    Assert.True(adapter.Apply("lifeblood_flash", "off").Success);
    adapter.RestoreBaseline();

    Assert.Equal(new[] { "capture", "backdrop:black", "flash:off", "restore" }, api.Calls);
}
```

Also test unknown IDs/values, API exceptions, deferred direct calls, and `Tick` not invoking deferred maintenance.

- [x] **Step 3: Run RED adapter tests**

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release --filter "FullyQualifiedName~HollowKnightTweakAdapterTests"
```

Expected: compilation fails because the adapter and API interface do not exist.

- [x] **Step 4: Implement the fork-owned adapter**

Define:

```csharp
public enum HollowKnightFlashMode { Soft, Vanilla, Off }

public interface IHollowKnightTweakApi
{
    bool IsReady { get; }
    void CaptureBaseline();
    void RestoreBaseline();
    void SetCompanionBackdropBlack(bool black);
    void SetLifebloodFlash(HollowKnightFlashMode mode);
}
```

Create the first two available descriptors:

```csharp
new TweakDescriptor(
    "companion_backdrop", "PRESENTATION", "COMPANION BACKDROP",
    "Choose the accepted dimmed scenery wash or a black lower-screen backdrop.",
    "dimmed", new[] { "dimmed", "black" }),
new TweakDescriptor(
    "lifeblood_flash", "PRESENTATION", "LIFEBLOOD FLASH",
    "Use the accepted softened flash, the original flash, or no flash.",
    "soft", new[] { "soft", "vanilla", "off" }),
```

Add the remaining IDs in the exact tested order using `TweakDescriptor.Deferred`, with tracking IDs `HKMOD-001` through `HKMOD-018` matching the design specification. `Apply` validates descriptor availability and values before dispatch. Applying an available default restores that capability through `SetCompanionBackdropBlack(false)` or `SetLifebloodFlash(Soft)`.

- [x] **Step 5: Run GREEN adapter tests**

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release --filter "FullyQualifiedName~HollowKnightTweakAdapterTests"
```

Expected: all adapter tests pass.

- [x] **Step 6: Create the authoritative capability ledger**

Create `docs/verification/hollow-knight-mod-capabilities.md` with this complete initial ledger; replace `Pending host evidence` only when the corresponding promotion gate actually passes:

```markdown
| ID | Capability | State | Missing seam/proof | Target | Acceptance condition | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| HKMOD-001 | Damage received | DEFERRED | Stock damage-mode symbols exist, but PreventDeath semantics and scene-safe baseline ownership are unproved | Final managed-rewrite remediation | Exact compile; fake mapping; death/respawn/scene/save rollback; integrated candidate | Pending host evidence |
| HKMOD-002 | Nail damage | DEFERRED | No progression-safe authority owns smith-upgrade recomputation while active | Final managed-rewrite remediation | Exact compile; upgrade-aware recomputation; master-off/reset/save rollback; integrated candidate | Pending host evidence |
| HKMOD-003 | One-hit kills | DEFERRED | No typed hit interception preserves non-enemy and scripted damage paths | Final managed-rewrite remediation | Exact compile; enemy-only hit contract; boss/script controls; rollback; integrated candidate | Pending host evidence |
| HKMOD-004 | Run speed | DEFERRED | Public movement fields exist, but hero replacement and scene-transition maintenance are unproved | Final managed-rewrite remediation | Exact compile; hero reacquisition; transition/death baseline restore; integrated candidate | Pending host evidence |
| HKMOD-005 | Unlimited Soul | DEFERRED | Managed refill methods exist, but live resource ownership and stale-baseline restoration are unproved | Final managed-rewrite remediation | Exact compile; focus/spell/death/scene/save rollback; integrated candidate | Pending host evidence |
| HKMOD-006 | Charm costs | DEFERRED | Public cost fields exist, but equipped-state and save reversion across all charms are unproved | Final managed-rewrite remediation | Exact compile; all-cost snapshot; equip/reload/reset rollback; integrated candidate | Pending host evidence |
| HKMOD-007 | Unlimited notches | DEFERRED | Overcharm/notch invariants and legal unequip behavior are unproved | Final managed-rewrite remediation | Exact compile; overcharm/equip/save lifecycle; exact restore; integrated candidate | Pending host evidence |
| HKMOD-008 | Equip anywhere | DEFERRED | No safe managed inventory/bench legality bridge is proven | Final managed-rewrite remediation | Exact compile; legal inventory FSM actions; bench/scene/save rollback; integrated candidate | Pending host evidence |
| HKMOD-009 | Geo multiplier | DEFERRED | No award-source interception distinguishes new Geo from existing balance | Final managed-rewrite remediation | Exact compile; pickup/reward-source tests; save rollback; integrated candidate | Pending host evidence |
| HKMOD-010 | Keep Geo on death | DEFERRED | Shade/death-pool ownership and duplicate-award prevention are unproved | Final managed-rewrite remediation | Exact compile; death/shade/respawn/save rollback; integrated candidate | Pending host evidence |
| HKMOD-011 | Journal one kill | DEFERRED | Journal progression writes and completion-event idempotence are unproved | Final managed-rewrite remediation | Exact compile; synthetic journal progression; reload/rollback; integrated candidate | Pending host evidence |
| HKMOD-012 | Auto map | DEFERRED | Map reveal mutates progression and no bounded current-area seam is proven | Final managed-rewrite remediation | Exact compile; area-only reveal contract; scene/save rollback; integrated candidate | Pending host evidence |
| HKMOD-013 | Health bars | DEFERRED | No managed renderer/event ownership is proven for spawned and pooled enemies | Final managed-rewrite remediation | Exact compile; spawn/despawn/boss renderer lifecycle; integrated candidate | Pending host evidence |
| HKMOD-014 | Damage numbers | DEFERRED | No typed authoritative dealt-damage event covers all attack sources | Final managed-rewrite remediation | Exact compile; attack-source matrix; pooled UI teardown; integrated candidate | Pending host evidence |
| HKMOD-015 | Boss retry | DEFERRED | Boss-scene reset and save checkpoint semantics are unproved | Final managed-rewrite remediation | Exact compile; synthetic boss transition/death/save rollback; integrated candidate | Pending host evidence |
| HKMOD-016 | Secret radar | DEFERRED | Secret identity, range, and non-progression presentation authority are unproved | Final managed-rewrite remediation | Exact compile; multi-scene discovery contract; no save delta; integrated candidate | Pending host evidence |
| HKMOD-017 | Bench teleport | DEFERRED | Recorded-bench validation and safe transition rollback are unproved | Final managed-rewrite remediation | Exact compile; recorded-only destination; transition failure/save rollback; integrated candidate | Pending host evidence |
| HKMOD-018 | State slots | DEFERRED | Versioned synthetic snapshot/restore format and transactional rollback are absent | Final managed-rewrite remediation | Exact compile; checksummed disposable fixtures; atomic restore/failure rollback; integrated candidate | Pending host evidence |
```

Use `DEFERRED`, never an empty target. Every row remains visible in the runtime catalog.

- [x] **Step 7: Commit and push the catalog checkpoint**

```powershell
git add tools/hollow-knight-patches/src/mods/HollowKnightTweakAdapter.cs `
        tools/shared-patches-tests/HollowKnightTweakAdapterTests.cs `
        tools/shared-patches-tests/SharedPatches.Tests.csproj `
        docs/verification/hollow-knight-mod-managed-api-audit.md `
        docs/verification/hollow-knight-mod-capabilities.md
git commit -m "feat: define Hollow Knight Mods capabilities"
git push
```

### Task 4: Add the display-independent Hollow Knight Mods session and runtime

**Files:**
- Create: `tools/hollow-knight-patches/src/mods/HollowKnightModsSession.cs`
- Create: `tools/hollow-knight-patches/src/mods/HollowKnightGameTweakApi.cs`
- Create: `tools/hollow-knight-patches/src/mods/HollowKnightModsRuntime.cs`
- Create: `tools/shared-patches-tests/HollowKnightModsSessionTests.cs`
- Modify: `tools/shared-patches-tests/SharedPatches.Tests.csproj`
- Modify: `tools/hollow-knight-patches/src/dualsouls/HkDirectDisplayAdapter.cs`
- Modify: `tools/hollow-knight-patches/src/dualsouls/HkStageHooks.cs`

- [x] **Step 1: Write RED pure-session tests**

Add `HollowKnightModsSession.cs` to the test project. Create tests for readiness delay, one-time initialization, persisted master application, display-independent ticks, presenter attach/detach neutrality, and disposal restoration:

```csharp
[Fact]
public void SessionWaitsForApiReadinessAndDisplayDetachDoesNotRestore()
{
    var api = new FakeHollowKnightTweakApi { IsReady = false };
    var store = new MemoryStore
    {
        ["dualsouls.mods.hollow-knight.master"] = "1",
        ["dualsouls.mods.hollow-knight.value.companion_backdrop"] = "black"
    };
    using var session = new HollowKnightModsSession(api, store, visibleRows: 5);

    session.Tick();
    Assert.False(session.IsReady);
    Assert.Empty(api.Calls);

    api.IsReady = true;
    session.Tick();
    Assert.True(session.IsReady);
    Assert.Contains("backdrop:black", api.Calls);

    session.SetPresenterAttached(false);
    session.Tick();
    Assert.DoesNotContain("restore", api.Calls);
}
```

- [x] **Step 2: Run RED session tests**

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release --filter "FullyQualifiedName~HollowKnightModsSessionTests"
```

Expected: compilation fails because `HollowKnightModsSession` does not exist.

- [x] **Step 3: Implement the pure session**

The session owns adapter, controller, and menu model:

```csharp
public sealed class HollowKnightModsSession : IDisposable
{
    public HollowKnightModsSession(IHollowKnightTweakApi api, ITweakStore store, int visibleRows);
    public bool IsReady { get; }
    public string LastError { get; }
    public TweakController Controller { get; }
    public TweakMenuModel Menu { get; }
    public void Tick();
    public void SetPresenterAttached(bool attached);
    public void Dispose();
}
```

`Tick` waits for `api.IsReady`, initializes once, records initialization failure, and thereafter calls `Controller.Tick()` regardless of presenter attachment. `Dispose` calls adapter baseline restoration without changing persisted master or selections.

- [x] **Step 4: Run GREEN session tests**

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release --filter "FullyQualifiedName~HollowKnightModsSessionTests"
```

Expected: all session tests pass.

- [x] **Step 5: Implement production API and runtime**

`HollowKnightGameTweakApi` reads/writes only fork-owned `HkStageHooks` presentation overrides:

```csharp
public bool IsReady => true;
public void CaptureBaseline() { _captured = true; }
public void SetCompanionBackdropBlack(bool black) => HkStageHooks.SetBackdropOverride(black);
public void SetLifebloodFlash(HollowKnightFlashMode mode) => HkStageHooks.SetFlashOverride(mode);
public void RestoreBaseline()
{
    if (!_captured) return;
    HkStageHooks.ClearPresentationOverrides();
}
```

`HollowKnightModsRuntime` is a `DontDestroyOnLoad` MonoBehaviour with `EnsureStarted`, a static `Current`, one session using `PlayerPrefsTweakStore`, `Update => Session.Tick()`, and ordered disposal.

- [x] **Step 6: Start Mods before the display gate**

Change `HkDirectDisplayAdapter.Bootstrap` so the first statement is:

```csharp
HollowKnightModsRuntime.EnsureStarted();
```

Keep the existing `IsProductionEnabled` check only around direct-display adapter creation. This guarantees persisted gameplay capabilities later remain process-owned even when display transport is disabled or absent.

- [x] **Step 7: Make `HkStageHooks` a delegation boundary**

Remove the inert H2 partial `HKDualScreen` boundary from `HkStageHooks.cs`. Replace constant availability with runtime-backed properties and add explicit presentation override methods:

```csharp
internal static bool TweaksAvailable =>
    HollowKnightModsRuntime.Current != null && HollowKnightModsRuntime.Current.Session.IsReady;
internal static bool TweaksMenuVisible =>
    TweaksAvailable && HollowKnightModsRuntime.Current.Session.Menu.IsOpen;
internal static bool BlackBackground => _backdropBlackOverride == true;
internal static HollowKnightFlashMode? FlashOverride => _flashOverride;

internal static void SetBackdropOverride(bool black) => _backdropBlackOverride = black;
internal static void SetFlashOverride(HollowKnightFlashMode mode) =>
    _flashOverride = mode == HollowKnightFlashMode.Soft ? null : mode;
internal static void ClearPresentationOverrides()
{
    _backdropBlackOverride = null;
    _flashOverride = null;
}
```

Preserve the existing joystick-slot logic unchanged.

- [x] **Step 8: Run session tests and exact Hollow Knight compile**

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release --filter "FullyQualifiedName~HollowKnightModsSessionTests|FullyQualifiedName~HollowKnightTweakAdapterTests"
pwsh -NoProfile -File tools/hollow-knight-patches/check.ps1 `
  -Depot 'D:\Temp\dualsouls-hk-12620\Hollow Knight\hollow_knight_Data\Managed' `
  -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
```

Expected: all focused tests pass and exact compile has zero errors.

- [x] **Step 9: Commit and push the runtime checkpoint**

```powershell
git add tools/hollow-knight-patches/src/mods `
        tools/hollow-knight-patches/src/dualsouls/HkDirectDisplayAdapter.cs `
        tools/hollow-knight-patches/src/dualsouls/HkStageHooks.cs `
        tools/shared-patches-tests/HollowKnightModsSessionTests.cs `
        tools/shared-patches-tests/SharedPatches.Tests.csproj
git commit -m "feat: run Hollow Knight Mods independently of display"
git push
```

### Task 5: Make the two presentation capabilities exactly reversible

**Files:**
- Modify: `tools/hollow-knight-patches/src/dualsouls/HKDualScreen.cs`
- Extend: `tools/ci/tests/test_hollow_knight_reference_port.py`

- [x] **Step 1: Write RED source/lifecycle contracts**

Add focused Python tests requiring:

- `HkStageHooks.BlackBackground` remains the sole Mods backdrop decision;
- lifeblood flash baseline records renderer enabled/color before modification;
- `Soft`, `Vanilla`, and `Off` are all handled;
- baseline entries for destroyed flashes are pruned;
- teardown restores surviving renderers and clears the baseline map.

Use method-body extraction rather than whole-file token matching so unrelated comments cannot satisfy the contract.

- [x] **Step 2: Run RED source contracts**

```powershell
python -m unittest tools.ci.tests.test_hollow_knight_reference_port
```

Expected: new lifeblood baseline/restoration assertions fail.

- [x] **Step 3: Replace one-way flash softening with a reversible policy**

In `HKDualScreen.cs`, add:

```csharp
struct FlashBaseline
{
    public bool Enabled;
    public Color Color;
}
readonly Dictionary<SpriteRenderer, FlashBaseline> flashBaselines =
    new Dictionary<SpriteRenderer, FlashBaseline>();
```

For each live `Screen Flash` renderer, capture once before mutation. Resolve effective mode as:

```csharp
HollowKnightFlashMode mode = HkStageHooks.FlashOverride ??
    (cfg.killBlueFlash == 1 ? HollowKnightFlashMode.Soft : HollowKnightFlashMode.Vanilla);
```

Apply:

- `Vanilla`: restore captured enabled/color;
- `Soft`: restore enabled/color first, then clamp alpha to `cfg.flashAlpha`;
- `Off`: set `enabled = false` without changing stored baseline.

Prune destroyed keys. Add `RestoreLifebloodFlashBaselines()` and call it during companion/reference teardown before clearing the dictionary.

- [x] **Step 4: Run GREEN contracts and exact compile**

```powershell
python -m unittest tools.ci.tests.test_hollow_knight_reference_port
pwsh -NoProfile -File tools/hollow-knight-patches/check.ps1 `
  -Depot 'D:\Temp\dualsouls-hk-12620\Hollow Knight\hollow_knight_Data\Managed' `
  -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
```

Expected: all focused contracts pass and compile has zero errors.

- [x] **Step 5: Commit and push the reversible-effects checkpoint**

```powershell
git add tools/hollow-knight-patches/src/dualsouls/HKDualScreen.cs `
        tools/ci/tests/test_hollow_knight_reference_port.py
git commit -m "feat: make Hollow Knight presentation tweaks reversible"
git push
```

### Task 6: Implement the existing-layout Mods gear and modal

**Files:**
- Create: `tools/hollow-knight-patches/src/dualsouls/HollowKnightModsPresenter.cs`
- Modify: `tools/ci/tests/test_hollow_knight_reference_port.py`
- Existing callers retained: `HKDualScreen.Bottom.Frame.cs`, `HKDualScreen.Bottom.Hud.cs`, `HKDualScreen.Bottom.Select.cs`

- [x] **Step 1: Write RED presenter ownership contracts**

Extend the Python suite to require:

- `HollowKnightModsPresenter.cs` supplies `TweaksPaneTick`, `PositionGear`, `GearTapN`, `ToggleTweaksPane`, and `CloseTweaksPane`;
- `HkStageHooks.cs` contains no partial `HKDualScreen` class;
- the presenter consumes `TweakMenuModel` and never constructs a controller/store/adapter;
- no fourth tab or change to `TAB_TO_COL`;
- deferred rows render `DEFERRED`, `TrackingId`, and `UnavailableReason`;
- gear/modal teardown clears objects and tells the session the presenter detached;
- no copied class names `HKTweaks`, `HKModsMenu`, or `HKDualScreen.Bottom.Tweaks` appear.

- [x] **Step 2: Run RED presenter contracts**

```powershell
python -m unittest tools.ci.tests.test_hollow_knight_reference_port
```

Expected: presenter file/method assertions fail.

- [x] **Step 3: Implement gear construction and placement**

In the new partial class, move the H2 boundary fields from `HkStageHooks.cs`. Build one procedural gear `SpriteRenderer` under `frameRoot`, set it to `ATTR_LAYER`, sorting order above frame chrome, and position it from the existing `hudGearAnchor`, `hudGearH`, `hudFpsB`, camera geometry, scale, aspect, and tab Y values. `PositionGear` hides the gear unless the runtime session is ready.

`GearTapN` converts normalized lower-display coordinates to the measured gear renderer bounds and returns true only for a clean contained tap.

- [x] **Step 4: Implement modal/model interaction**

`ToggleTweaksPane` opens/closes `HollowKnightModsRuntime.Current.Session.Menu`. `CloseTweaksPane` closes it and clears `tweaksOpen` without changing `tab.tap` or `cfg.compTab`.

`TweaksPaneTick`:

1. creates `tweaksRoot` once under `compRoot`;
2. clones and sanitizes existing native frame/TMP donors already owned by H2 rather than adding a synthetic canvas;
3. displays one selected group and the model's visible row window;
4. shows master, current available value, `DEFERRED`, descriptions, errors, reasons, and ledger IDs;
5. stows `mapClone`, `invCloneCache`, and `charmCloneCache` while open;
6. positions the existing attribute camera exactly as the normal context box;
7. forwards semantic touch actions to `TweakMenuModel` and repaints only when state changes.

Use reflection helpers already present in `HKDualScreen.Util.cs` to set cloned native TMP text and force mesh updates. Disable every non-TMP `MonoBehaviour` on detached text clones using the accepted H2 clip-safe sanitation path.

- [x] **Step 5: Implement modal touch boundaries**

Keep `PollTouch` as the only lower-touch collector. `GearTapN` consumes the gear tap first. While open, the presenter maps clean taps to master, group previous/next, visible rows, reset, and close. A second tap on the selected available row cycles it; a deferred row selects and explains but cannot cycle. Existing tab taps call `CloseTweaksPane` and then continue normal tab selection.

- [x] **Step 6: Implement deterministic teardown**

Destroy `tweaksRoot` and gear objects through the existing companion teardown. Clear row references, close the menu model, call `Session.SetPresenterAttached(false)`, and leave the process runtime/controller active. Reattachment rebuilds view objects from the existing model state.

- [x] **Step 7: Run presenter contracts and exact compile**

```powershell
python -m unittest tools.ci.tests.test_hollow_knight_reference_port
pwsh -NoProfile -File tools/hollow-knight-patches/check.ps1 `
  -Depot 'D:\Temp\dualsouls-hk-12620\Hollow Knight\hollow_knight_Data\Managed' `
  -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
```

Expected: contracts pass, two entry points remain, and compile has zero errors.

- [x] **Step 8: Commit and push the complete H3 presentation batch**

```powershell
git add tools/hollow-knight-patches/src/dualsouls/HollowKnightModsPresenter.cs `
        tools/ci/tests/test_hollow_knight_reference_port.py `
        tools/hollow-knight-patches/src/dualsouls/HkStageHooks.cs
git commit -m "feat: add Hollow Knight Mods companion modal"
git push
```

### Task 7: Run the bounded H3 host gate

**Files:**
- Test only; no source changes unless a gate exposes a concrete defect.

- [x] **Step 1: Run focused .NET Mods tests**

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release `
  --filter "FullyQualifiedName~TweakControllerTests|FullyQualifiedName~TweakMenuModelTests|FullyQualifiedName~HollowKnightTweakAdapterTests|FullyQualifiedName~HollowKnightModsSessionTests"
```

Expected: zero failures/skips.

- [x] **Step 2: Run the complete shared suite once**

```powershell
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release
```

Expected: zero failures/skips.

- [x] **Step 3: Run focused Hollow Knight source contracts**

```powershell
python -m unittest tools.ci.tests.test_hollow_knight_reference_port
```

Expected: zero failures/skips.

- [x] **Step 4: Run both exact compiles once**

```powershell
pwsh -NoProfile -File tools/hollow-knight-patches/check.ps1 `
  -Depot 'D:\Temp\dualsouls-hk-12620\Hollow Knight\hollow_knight_Data\Managed' `
  -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
pwsh -NoProfile -File tools/silksong-patches/check.ps1 `
  -Depot 'D:\Temp\hkandroid-task11-silksong-managed' `
  -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
```

Expected: zero errors and unchanged entrypoint counts except for no new entrypoint—the existing Hollow Knight bootstrap owns Mods startup.

- [x] **Step 5: Run prohibited-content scan**

```powershell
rg -n "DllImport|ReadProcessMemory|WriteProcessMemory|HKTW_|class HKTweaks|class HKModsMenu|Bottom\.Tweaks" `
  tools/hollow-knight-patches/src tools/shared-patches/src
```

Expected: no production match introduced by H3. Existing explanatory contract strings outside production are allowed.

- [x] **Step 6: Verify repository state**

```powershell
git diff --check
git status --short
git log -6 --oneline
```

Expected: no unstaged/untracked implementation files and all H3 checkpoints present.

### Task 8: Reconcile H3 host status without creating a device loop

**Files:**
- Modify: `docs/superpowers/plans/2026-09-01-hollow-knight-first-dual-souls.md`
- Modify: `README.md`
- Modify: `docs/verification/hollow-knight-mod-capabilities.md`

- [x] **Step 1: Record the actual host result**

Update the parent plan to state:

```text
H2 source/host implementation is complete at 00627e3; its remaining physical observations are batched into the complete Hollow Knight candidate rather than a micro-candidate.
H3 is HOST-COMPLETE / TRACKED-DEFERRALS through implementation checkpoint 3cc321b.
The H3 documentation reconciliation is b0def8a; it advances HEAD without changing the implementation boundary.
H3 did not trigger a signed build. H4 follows on the same branch and the complete H3/H4 slice receives one future explicitly authorized device gate.
```

Do not mark any `HKMOD-*` row complete without its promotion evidence. Do not change device-only rows to passed.

- [x] **Step 2: Update README conservatively**

State that the fork now has a host-verified Hollow Knight Mods catalog/controller/process runtime/existing-layout modal with two reversible presentation capabilities and visible tracked deferrals. Do not claim device acceptance, gameplay-tweak parity, release readiness, or completed Mods effects.

- [x] **Step 3: Re-run the focused documentation/source contracts**

```powershell
python -m unittest tools.ci.tests.test_hollow_knight_reference_port `
                         tools.ci.tests.test_hollow_knight_first_plan
```

Expected: zero failures.

- [x] **Step 4: Commit and push H3 reconciliation**

```powershell
git add README.md `
        docs/superpowers/plans/2026-09-01-hollow-knight-first-dual-souls.md `
        docs/verification/hollow-knight-mod-capabilities.md
git commit -m "docs: record Hollow Knight Mods host gate"
git push
```

- [x] **Step 5: Stop before live testing and continue to H4 host work**

Do not build/sign/install or use ADB. H3's host checkpoint advances directly into H4 skin-library and death/respawn-rotation implementation under the user's standing go-ahead. The explicit user gate is required only when the complete H3/H4 candidate is ready for live Android testing.
