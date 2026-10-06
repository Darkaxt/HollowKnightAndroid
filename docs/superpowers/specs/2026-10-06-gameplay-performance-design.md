# Gameplay performance simplification specification

## Authority and scope

Status: **already_authorized** by the user's go-ahead following the performance QA review and request to use Specification-Gated Implementation.

The authoritative requirements are this specification, not completion checkboxes in its staged plan. This is a coherent performance correction batch on the current fork checkpoint, before Phase2. No product direction or rendering contract is being changed. The findings PERF148-01 through PERF148-06 supply the source/cadence diagnoses; they are not native timing or FPS measurements.

Scope: Hollow Knight dialogue and bounded HUD observation/discovery; current Silksong shell, action UI and native HUD inventories; targeted regression fixtures and contracts. Prefer a few owner-local retained values and direct guarded calls. No generic cache/registry/event/invalidation service, cross-game rewrite, unrelated Mods or skin feature work.

Host-only. No device/ADB, Gradle, signing, release/workflow dispatch, provisioning, master integration, upstream-visible action, native addresses/IL2CPP offsets/process scanning, injected PlayerData fields or save changes. Preserve existing protected inputs/evidence and cleanup staging. No subagents or schedules. Existing fork commit/push authorization applies after verified completion, never to private paths/logs/game binaries. Phase2 remains NOT_STARTED. Historical allocation variability/full-suite timeout remain separately unproven and unwaived.

## Requirements and acceptance

### GP-01 — settled dialogue clone work (PERF148-01)

A successfully settled hidden clone must not request text mesh generation or hierarchy/clip discovery on every gameplay frame. Repeating the same visible speaker content must not regenerate unchanged text. Content, actual clone/component replacement and dirty native text properties must rearm necessary generation. Cache state commits only after successful application; failures remain retryable.

Preserve immediate hide, inter-line cached-name grace, all three speaker parts, missing parts, owner fake-null/replacement handling, animated parent geometry and complete clipping sanitation after generation including subsequently born fallback submeshes. Reuse clipping scratch rather than allocating a property block on every sanitation call. Do not touch shared font materials or reenable disabled native clip/FSM drivers.

Acceptance: actual extracted production-body tests demonstrate flat generation/discovery counters across repeated hidden and repeated healthy same-text visible frames, changed text/dirty properties/owner replacement rearm, failed generation retries, and late fallback renderer sanitation. Existing clipping/layout/restore contracts remain passing.

### GP-02 — native speaker source authority (PERF148-02)

Source TMP/FSM bindings belong to the actual Title Small owner. Correct the discovery scope so a retained valid card does not invalidate caches every open-dialogue frame. Replacement/dead component recovery must not keep stale TMP/FSM references. Preserve native title-state sampling, fade suppression and dialogue framing.

Acceptance: actual caller/body tests prove one successful discovery for stable repeated open dialogue, rebinding on owner/component retirement, and correct hide/restore behavior. No broad source rewrite.

### GP-03 — SS native HUD inventory reuse (PERF148-03)

Retain known Renderer, Canvas, Graphic, mask-FSM, blue-health and tool-icon inventories in the current DsHudView owner. Avoid repeated subtree discovery in a structurally healthy frame and redundant capture/primary-hide discovery. Sample enabled/active/layer/equipment/layout values live. Component/hierarchy addition, removal, owner replacement and presentation recovery must rearm inventories; a bounded local fallback covers changes lacking structural notifications. Newly generated art must be admitted without retaining a stale capture-layer value as source authority.

Preserve uGUI-before-batching ordering, render-scope restoration, capture/primary ownership, pause/loss/failure recovery, animated geometry, mask/lifeblood/tool changes and original overlay suppression scope. Do not throttle live geometry or claim the content-page graph belongs to OverlayCaptureRoot.

Acceptance: actual production inventory/callback bodies prove settled discovery counters stay flat between bounded fallback admissions, additions/removals/retirement and pause/recovery rearm correctly, live visibility changes work without rediscovery, and scopes restore on success/failure. Native latency remains unmeasured.

### GP-04 — SS shell and actions unchanged work (PERF148-04)

Replace healthy per-frame capturing guard lambdas with direct locally guarded tick/collection calls preserving identical fault isolation. Retain instance action callbacks where currently recreated each frame. Normalize title only when its raw value changes. Cache action text measurement by relevant label/font/size/style/spacing inputs, and skip unchanged layout/output assignments while still sampling action availability, callback authority and fade alpha each frame.

Preserve outgoing map ticks during slide, active-page failure isolation, current action invocation/hit rectangles, font/language/header geometry updates, marker mode and fading/reset behavior. A changed callback must be current even if the label/count is unchanged. Do not replace pull-based action availability with an event framework.

Acceptance: actual active shell/action bodies prove no repeated guard allocation, uppercase normalization or static text measurement on stable frames; content/font/geometry/state changes update promptly; callback changes invoke the new owner; page throws still isolate and clear stale actions. Paused settled contracts remain passing.

### GP-05 — shared HK Animator sampling (PERF148-05)

Observe a shared Animator once per owned capture pass, not once per included Renderer. Keep the observer within the bounded native HUD and retire it with the owner inventory. Reduce duplicate per-renderer state arrays if possible without adding a global observer. Each renderer must receive the same changed result for that pass.

Preserve conservative time/transition invalidation, live current geometry checks, particles/text/mesh/sprite changes, skin replacement, frame retry semantics and unsupported-layer fail-closed behavior. Removing animation-time invalidation is not part of this batch; it is not proven safe by source review.

Acceptance: actual production-body tests with two renderers sharing an Animator show one per-layer state observation per pass, both owners remain dirty on real advance/transition, separate Animators remain distinct, repeated capture passes and owner replacement rearm, and existing native HUD framing tests pass.

### GP-06 — bounded discovery and legacy routing (PERF148-06)

Stop rebuilding known HK tutorial/credit root/name inventories on every thirty-frame poll. Retain loaded-scene/persistent-root authority and discover on relevant scene/structural/lifetime edges with a bounded local recovery fallback for late-born objects. Preserve routing and prompt/credit visibility.

Bound unavailable SS source-camera retries, resetting retry admission on map/scene/owner recovery while a healthy pair never scans. Keep render-texture context recovery intact.

For heal clones, use an existing typed local spawn/hierarchy authority only if equivalent clone coverage and prompt timing can be demonstrated. Otherwise keep the current six-frame discovery fallback and per-frame pinning; do not guess that every effect implies a PlayerData health delta or add a game hook/framework solely for this optimization. The required disposition is an evidence-backed safe implementation choice, not silent omission or a promise that all global searches vanish.

Acceptance: actual-body/source-cadence tests demonstrate retained tutorial inventories with correct late-owner/scene recovery and bounded SS unavailable-camera scans with successful camera-pair recovery. Heal routing, rename/multi-clone behavior and pinning remain equivalent, with its implemented local path or preserved fallback explicitly reconciled.

### GP-07 — scope, simplicity and preservation

Only the named production owners and their focused tests/docs may change. Preserve private inputs, historical failure records, cleanup staging and unrelated source. Prefer existing helpers and small local state over new orchestration abstractions. Keep generated test outputs outside project tmp/source. Reconcile every stage against this specification and record every blocker/deferral; neither may remain at final closure.

Acceptance: frozen before/after hashes, reviewed finite changed-path inventory, empty project tmp, explicit local SPEC then QUALITY checks, zero unresolved blockers/required deferrals. No forbidden operation or false runtime/FPS claim.

### GP-08 — fresh integrated proof and publication

Fresh focused RED/GREEN production-body tests and affected existing correctness contracts must cover GP-01..06. Compile both actual HK and SS production patch profiles against their already authenticated inputs; generated host stand-ins alone are insufficient. Run the focused integrated host gate against the final working bytes. Private receipts/counter evidence retain exact source identities and original failures; do not upload game inputs/binaries or relabel old failures.

Acceptance: all applicable focused/affected tests pass without skips or weakened assertions, paired actual compiles succeed, final full-spec reconciliation records blockers=0 and tracked_deferrals=0. Then commit and push the coherent batch to the authorized fork checkpoint and verify the remote ref/bytes. No Android/FPS/live or full Phase2 acceptance is implied.
