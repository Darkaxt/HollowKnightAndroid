# Hollow Knight skins: reuse-first implementation decision

Date: 2026-09-07
Status: user-approved direction ("go ahead, simplify"); implementation and runtime acceptance are not yet complete.

## Decision and supersession

Reuse and adapt the working skin runtime from `igawa6/dualsouls` rather than independently rebuilding it behind a generalized transaction system. The user explicitly permits reading, analysis, copying, adaptation and reuse of reference code. Upstream pushes, pull requests, issues, comments and maintainer contact remain prohibited.

This decision supersedes the blanket independent-implementation/no-copying interpretation and the requirement to complete the remaining generalized H4 protocol/golden gates before implementing usable skins. In particular, the uncompleted Task90 main gate, Task91 token matrix and parent transaction-matrix expansion are not prerequisites for this route. Their source, tests and evidence are retained without claiming those unfinished gates passed. The broader 2026-09-03 skin plan remains historical design/evidence, not the active dependency chain for the vertical slice below.

Source reference: `https://github.com/igawa6/dualsouls/tree/5c22451435b772acde0c7e6456f9019bc1baef73`. Its LICENSE explicitly labels repository source as MIT-licensed. Preserve provenance and applicable notices when adding adapted source; do not import game assets or unrelated gameplay modifications.

## Required user-visible behavior

- One active skin per game profile.
- Mode cycle `OFF → ON → ROTATE → OFF`.
- Import ordinary, non-normalized CustomKnight ZIPs using bounded, path-contained ingestion.
- `ON` uses the selected skin's supported replacements, including furniture/environment; `OFF` restores all originals.
- `ROTATE` advances once for a confirmed death and applies the pending successor at stable respawn, not on every scene load, hazard, frame or duplicate callback.
- `ROTATE` changes character/HUD and associated character visuals only; furniture/environment stay at vanilla/default assets. Entering `ROTATE` from `ON` restores already-applied environment overrides without waiting for the next death. Returning to `ON` permits the selected full pack again. Scene rebinds obey the current mode's policy.
- Failed changes retain or restore the last working visual state. Failure is reported, not silently treated as success.
- Companion visuals refresh after successful skin changes.
- No game save modification.

## Architecture

### Reused Hollow Knight runtime

Adapt the reference `Assets/HKMods.cs` texture application and restoration machinery: tk2d collection aliases and Knight/Sprint pages; named HUD, hero effects, charms and inventory targets; supported PlayMaker sprite targets; atlas/original-resource restoration. Remove dependencies on the reference app's hub, picker, tweak settings and multi-enabled registry. Integrate through the fork's managed build/runtime conventions and existing companion stamp consumer.

This is source reuse, not a claim that the AssetRipper project is a drop-in match for the fork. Compile against the actual managed game APIs. Keep Hollow Knight bindings separate from a future Silksong adapter.

### One small configuration authority

Use one authority for selected pack, mode and rotation eligibility/order. Do not run the reference `mods.json` and the existing H4 visual registry as competing sources of truth. Reuse bounded importer and profile isolation code when doing so does not force the retired transaction/lease architecture back into the active path.

Keep imported files immutable while a game uses them. A simple prohibition on in-use replacement/deletion is acceptable for this slice; do not require a full retention/pin graph or garbage collector merely to permit skin selection. Use atomic configuration publication and fail safely on malformed input.

### Focused lifecycle controller

Use a small death-confirmation/pending-successor/stable-respawn controller. Reuse useful existing pure selection/lifecycle behavior without requiring exhaustive three-language equivalence. Define duplicate-event handling, normal versus dream/terminal deaths, OFF/profile-switch cancellation, zero/one eligible pack behavior and failed application explicitly. An actual managed event adapter is required; a caller-fed reducer alone is not integration.

Classify supported replacement targets using reference source, actual binding consumers and the pinned catalog, not filename guesses. Use the existing apply/restore and original-resource ownership machinery for mode-specific target selection, not another active pack, registry or generalized protocol. A failed furniture/environment restoration remains visible and retryable; it must not be reported as successful vanilla restoration.

Keep this policy small: NPC character art is not automatically scenery, so the mixed Knight/Quirrel character sheet does not require a compositor. World `Geo.png` stays vanilla in `ROTATE`; `Inventory/Geo.png` remains UI. The `Birthplace.png` policy leaves the whole sheet vanilla in `ROTATE` because the inspected candidate atlas combines character frames with grouped grave-shell scenery; its cutscene-character frames are a documented narrow exception. This does not reject the pack or change full-pack `ON` policy. Seven inspected preload candidate collections establish offline content classification, not original CustomKnight selector equivalence or live binding: their names lack current aliases and their textures are named `atlas0`. Task95 does not add speculative aliases or expand claimed support to resolve that gap.

### Targeted safety

Retain path containment, bounded ZIP/PNG processing, decoded-image dimension and aggregate-memory limits, deterministic resource disposal, original-resource identity and scene-rebinding handling. Do not retain multi-generation visual receipts, durable lease-transition histories or exhaustive lexical matrices solely to justify past work. A fresh process need not reconstruct a previous process's GPU state: it can begin with vanilla resources and apply validated configuration afresh.

## Implementation slices and acceptance

1. **Reference runtime adapter (Task93):** actual host-compilable texture apply/restore code, source provenance, bounded loading, resource ownership and companion stamp. Focused tests cover the boundary and failure/restoration behavior. No new generic transaction framework.
2. **Library controls (Task94):** connect bounded imports, explicit selection, eligibility and mode controls to the single authority. Production source must no longer offer only unavailable/read-only services for the completed feature. In-use mutation restrictions must be visible and recoverable.
3. **Death rotation (Task95):** wire authoritative managed death/respawn observations to the controller and runtime, with focused lifecycle and cancellation tests. Ordinary transitions and duplicate signals must not advance the ring. Cover full-pack `ON → ROTATE` environment restoration, repeated rotation with default environment, `ROTATE → ON`, `OFF`, scene rebinds and failed-restoration retry.
4. **Host integration gate (Task96):** exact relevant C#/Kotlin builds and focused import, manual switching, OFF restoration, memory, companion invalidation, death-rotation and mode-specific environment regressions. Use representative skin families; retain failures and actual reports.

Each slice gets a fresh bounded implementer, an independent specification review, a separate quality review and fresh relevant main verification. Reviews target concrete defects and meaningful behavior, not exhaustive generalized state-space coverage. Do not make every prior golden suite a prerequisite for unrelated runtime edits; retain applicable regression tests.

## Coverage and non-claims

The reference documents approximately 110 of approximately 160 sheet names and explicitly excludes preload sheets, `AreaBackgrounds/`, free-form `Swap/` and `Cinematics/`. Its denominator is not interchangeable with the fork's 205-path catalog. Preserve or extend supported behavior with explicit evidence; do not claim 205 validated bindings or complete CustomKnight compatibility from source reuse alone. Missing families remain documented follow-up work rather than blocking basic runtime parity.

Host tests do not prove a texture renders correctly on the device. The final device acceptance must cover representative packs, switching/OFF restoration, repeated deaths, recreated scene targets, companion visuals and memory behavior at one explicitly approved integration checkpoint.

## Operational boundaries

Work only in `D:/Temp/HollowKnightAndroid-h1`. Preserve previous source and evidence; no cleanup or commits under the current restriction. Read-only reference access is allowed. No upstream-visible action, nested agents, native addresses, IL2CPP offsets, game-process scanning, injected PlayerData fields or save edits. No ADB, physical-device, emulator, signing, release workflow or live Android testing without explicit approval. Host source implementation and compilation do not authorize packaging/source-image activation or deployment.
