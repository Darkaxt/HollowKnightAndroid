# Second gameplay performance QA

Scope: the specification-gated gameplay simplification, after implementation and the first paired actual production compiles. Local SPEC review preceded this QUALITY/performance review. This is host/source QA, not a native timing, GPU, FPS or Android lifecycle certificate.

## Review result

One additional fault-path gap was reproduced and fixed (`GP04-S4-B02`). No unresolved in-scope finding remains after remediation and re-review.

The active SS shell publishes actions before collecting a replacement tab strip. If strip collection then throws, the page is disabled but its already-published actions remained callable; a previously active strip also kept the ordinary tabs hidden. An actual-shell regression first failed with the stale callback still invocable. The shared failure boundary now clears actions only when the failed entry is active, preserving other pages' authority. Strip collection failure returns through the existing broken-owner withdrawal path and restores the ordinary tabs. The expanded regression verifies a resident strip, current hit rectangle, no invocation and strip withdrawal. All four tick/action/pane/strip fault cases pass; 190 affected SS tests pass without skips. This correction adds no healthy-frame validation loop or allocation.

The actual native API compile also caught a host-boundary mistake (`GP04-S4-B01`): shipped TMProOld has no `wordSpacing` member. The nonexistent measurement key/model member were removed. The original compiler failure and both strip regression failures remain retained privately, not relabeled.

## Performance assessment

| Area | Removed redundant work | Retained correctness work / limitations |
|---|---|---|
| HK dialogue | Settled hidden/same-text clones stop repeated mesh generation and clipping discovery; native speaker source/FSM bindings remain attached to the actual card; unchanged joined text is retained. | Small part/lifetime/child-count checks remain. Generation, fallback clipping and failure retry are event/recovery work. Animated dialogue geometry remains live. |
| SS HUD | Retained Renderer/Canvas/Graphic/mask/lifeblood/tool inventories replace healthy-frame subtree queries. Structural watches replace the first candidate's repeated whole-node parent/child validation. The actual render-callback test performs zero inventory child-count reads between fallback admissions. | Enabled/active/layer/equipment/layout and retained-component lifetime checks remain live. Component-only recovery rescans every30 frames; this scan and watch binding are not free. No native CPU improvement magnitude is claimed. |
| SS shell/actions | Direct tick/collection guards, retained map callbacks, raw-title normalization, keyed text measurement and changed-output assignment remove settled churn. The modeled active-shell120-frame allocation gate reports zero bytes. | Availability, current callbacks, hit rectangles, alpha, typography keys and header geometry remain sampled. Modeled allocation results do not imply zero native allocations or constant native cost. |
| HK Animator | One state observation record per actual Animator per capture pass replaces per-renderer state arrays/queries. Two shared renderers observe each layer once and both receive real advance/transition invalidation. | Current/next normalized-time and transition-phase invalidation stay conservative. Real animation can still rearm bounds/fitting each pass. Separate same-frame capture passes deliberately observe again. |
| Discovery | Known tutorial root/name inventories remain retained across unchanged30-frame polls. Missing SS camera pairs back off for30 frames and recover on actual owner/scene/map edges. Healthy camera pairs never search. | Tutorial recovery still walks scenes at a120-frame fallback. Known routing remains owned/live; source-camera texture recovery is unthrottled. |
| Heal effects | No speculative replacement authority was introduced. | Six-frame global-name discovery and per-frame pinning are intentionally preserved: equivalent typed local spawn coverage/timing has not been demonstrated. This is the specification's safe disposition, not a required-work deferral. |

No generic registry, event service, cache manager or cross-game observer was added. Watches have structural/lifetime message handlers, not per-node Update methods. Inventories and Animator records retire through their existing owners. The dormant DsPortRuntime path was not used to attribute current shell costs.

## Verification and remaining limits

Fresh final verification comprises20 extracted HK dialogue/tutorial/heal cases,372 affected HK/SS tests and6 SS source-contract tests, with no skips or weakened assertions. Both actual production profiles compile against authenticated game/Android player inputs after the QA correction; the HK mandatory weave succeeds. Exact source identities and historical RED/failure evidence are retained privately. Publication and preservation are reconciled in the accompanying implementation plan.

Remaining native work is explicit, not hidden behind an allocation-only claim: retained-object validation, live geometry/TMP/FSM sampling, bounded discovery bursts and capture-camera/render-texture GPU work. No native timing evidence establishes their relative significance. Cold pane cloning/skin decode hitches and optional Mods discovery are outside this finite correction review; they are not certified by these tests. Historical idle-allocation variability and the full-suite timeout remain separately unproven and unwaived. Phase2 remains NOT_STARTED; no device test or new live-test prerequisite was introduced.
