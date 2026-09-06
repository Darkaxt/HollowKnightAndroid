# H4 catalog descriptor evidence gap — 2026-09-05

## Status and gate

`H4-CATALOG-DESCRIPTORS-003` is **OPEN**. The checked-in catalog fixes 205 target paths, order, category boundaries and SHA-256 `258a7fa2b3a1a94d114eb73c39259dfa6853139017afced53ca3afa668a1372a`. It does not supply the Task 3 per-row `strategy`, typed `bindingKey`, or `maximumTargets` table. Do not substitute category-wide defaults, filename-only binding keys, or guessed cardinalities to make the catalog tests pass.

**Named target:** H4 Batch 3 descriptor-contract completion, before declaring the deterministic-core batch complete; exact runtime resolution remains the separate Task 4 adapter gate.

**Acceptance:** a reviewed 205-row source-evidence matrix assigns each pinned ordinal/path an explicit supported recipe, source anchors, key/alias semantics and justified finite limits. Define what a target count measures, separate source cardinality from defensive traversal/live-instance caps, and handle multi-slot sprites, composite hooks and renderer property blocks explicitly. Kotlin/C#/independent golden validators must agree on the complete matrix. Unsupported recipes remain explicitly blocked rather than silently omitted or mapped to another family's implementation. Exact-build default capture, all-target postconditions and reverse rollback require separate proof; this matrix alone never enables production writes.

**Why pure-core work can continue:** lifecycle, transaction, rotation and accounting reducers operate on values without resolving Unity targets. No authoritative runtime catalog is constructed by this research, and production remains `ADAPTER-BLOCKED`. Storage retention/GC prerequisites are unchanged.

## Pinned source findings

Read-only research examined [CustomKnight v3.5.0, commit `46cf73415dd29baa2fed98a9939f58f0c488bf12`](https://github.com/PrashantMohta/HollowKnight.CustomKnight/tree/46cf73415dd29baa2fed98a9939f58f0c488bf12). It reported dispatch evidence for 162 Skinable registrations and all 9 SaveHud / 34 AreaBackgrounds paths: **205/205 source-dispatch coverage, no complete H4 descriptor table authored, 0/205 exact Hollow Knight 1.5.12620 bindings validated**.

The registration bases are 117 `Skinable_Sprite`, 20 `Skinable_Tk2d`, 10 `Skinable_Single`, 7 `Skinable_Tk2ds`, 4 `Skinable_noCache`, 2 `Skinable_Multiple`, 1 `Skinable_Hook` (DeathNail), and 1 direct `Skinable` (Salubra). These total 162 across eight shapes, not a mechanical translation to H4's seven strategy names. Inheritance also differs from target semantics: the registered `Tk2ds` implementations obtain particle-renderer materials, while Grubberfly's `Multiple` recipe obtains eight tk2d materials.

Key pinned evidence (all paths below are within that commit):

- [`SkinManager.cs:25–197`](https://github.com/PrashantMohta/HollowKnight.CustomKnight/blob/46cf73415dd29baa2fed98a9939f58f0c488bf12/CustomKnight/Skin/Base/SkinManager.cs#L25-L197): registration order, concrete classes and constructor variants.
- [`Grubberfly.cs:7–20`](https://github.com/PrashantMohta/HollowKnight.CustomKnight/blob/46cf73415dd29baa2fed98a9939f58f0c488bf12/CustomKnight/Skin/Base/SkinableItems/Grubberfly.cs#L7-L20): eight explicit material expressions; not proof of eight distinct physical materials.
- [`SaveHud.cs:8–104`](https://github.com/PrashantMohta/HollowKnight.CustomKnight/blob/46cf73415dd29baa2fed98a9939f58f0c488bf12/CustomKnight/Skin/Morph/SaveHud.cs#L8-L104): slot fields/components and event-driven background dispatch. Ordinary area rows share a background destination selected by MapZone; four slot IDs do not prove at most four live UI instances. The game's MapZone declaration is not provided here.
- [`DeathNail.cs:8–29`](https://github.com/PrashantMohta/HollowKnight.CustomKnight/blob/46cf73415dd29baa2fed98a9939f58f0c488bf12/CustomKnight/Skin/Base/SkinableItems/Knight/DeathNail.cs#L8-L29): one sprite assignment per matching callback, without a live-instance/lifetime cap.
- [`Salubra.cs:6–39`](https://github.com/PrashantMohta/HollowKnight.CustomKnight/blob/46cf73415dd29baa2fed98a9939f58f0c488bf12/CustomKnight/Skin/Base/SkinableItems/Minions/Salubra.cs#L6-L39) and [`SpriteRendererMaterialPropertyBlock.cs:49–125`](https://github.com/PrashantMohta/HollowKnight.CustomKnight/blob/46cf73415dd29baa2fed98a9939f58f0c488bf12/CustomKnight/Skin/Swapper/SpriteRendererMaterialPropertyBlock.cs#L49-L125): direct-base renderer property-block recipe with render callback behavior; it is not silently equivalent to `Skinable_Hook` or a `Material.mainTexture` write. Dynamic Swap is outside H4 scope.
- [`FlameUI.cs:15–35`](https://github.com/PrashantMohta/HollowKnight.CustomKnight/blob/46cf73415dd29baa2fed98a9939f58f0c488bf12/CustomKnight/Skin/Base/SkinableItems/FlameUI.cs#L15-L35): six explicit FSM sprite slots for a single row. A sprite strategy does not imply cardinality one.
- [`OrbFull.cs:25–40`](https://github.com/PrashantMohta/HollowKnight.CustomKnight/blob/46cf73415dd29baa2fed98a9939f58f0c488bf12/CustomKnight/Skin/Base/SkinableItems/Knight/OrbFull.cs#L25-L40): descendant matching plus destruction of Pulse Sprite objects, not a texture-only transaction.
- [`DungRecharge.cs:11–77`](https://github.com/PrashantMohta/HollowKnight.CustomKnight/blob/46cf73415dd29baa2fed98a9939f58f0c488bf12/CustomKnight/Skin/Base/SkinableItems/Particles/DungRecharge.cs#L11-L77): two initially returned materials do not cover later prefab cloning, additional texture writes, particle-color changes and FSM-reference redirection. Counting only the returned pair would omit mutations.

## Contract constraints for descriptor authoring

1. Keep H4 texture-only: upstream object destruction, shader changes, prefab cloning, particle-color edits, dynamic Swap and FSM-reference redirection are not authorized by this evidence. A row depending on them requires a proven texture-only recipe or an explicit blocked outcome.
2. Binding identity must preserve typed root, selector chain, terminal field/action and variant/predicate. Shared resolver prefixes do not collapse distinct terminal fields. MapZone variants and charm variants need explicit discrimination.
3. Treat shared physical destinations and conditional exclusivity explicitly; never inherit registration-order last-writer-wins behavior. Texture decode identity is separate from binding identity/default capture.
4. Record literal expression/slot counts as source facts. Traversal budgets, duplicate-name handling, live hook-instance bounds and lifetime resource limits are separate defensive policy requiring justification and fail-closed enforcement.
5. Upstream default/reset behavior is not proof of per-target vanilla capture or synchronous reverse rollback. Exact-build adapter acceptance stays open even after the evidence matrix is complete.

## Local research provenance

Line-numbered HTTP response compilations were retained by the session harness outside the repository under `C:/Users/darka/.claude/projects/D--Temp-HollowKnightAndroid-h1/1174652c-74ee-48e5-8977-2ea3d63474df/tool-results/`:

- `bfhp93piz.txt`: base strategies and SaveHud.
- `bmot6yify.txt`: SkinManager and cache/loader helpers.
- `b0d3d9uxr.txt`: 71 additional concrete implementation files, including Salubra.

These are formatted research logs, not a pristine or published source snapshot. The pinned URLs above are the durable source references. No game assets were extracted, no source was vendored, and no runtime binding was exercised.
