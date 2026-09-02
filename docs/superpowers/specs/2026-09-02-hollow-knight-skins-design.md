# Hollow Knight H4 Skin Library and Death Rotation Design

**Date:** 2026-09-02
**Status:** `WRITTEN-DESIGN / IMPLEMENTATION-PENDING`
**Product target:** Hollow Knight `1.5.12620` in the unified Android fork
**Stage:** H4, host first
## 1. Status and scope
This is a written contract, not implementation or device evidence.

| Status | Meaning |
| --- | --- |
| `WRITTEN-DESIGN / IMPLEMENTATION-PENDING` | The contract exists; production code/tests do not yet satisfy it. |
| `HOST-COMPLETE` | All Kotlin/JVM, Robolectric, C#, Python/golden, and exact HK/Silksong host gates pass from one checkpoint. |
| `ADAPTER-BLOCKED` | Exact `1.5.12620` typed bindings, whole-catalog apply, or synchronous rollback remain unproved; non-vanilla apply is disarmed. |
| `LIFECYCLE-BLOCKED` | Confirmed-death or stable-respawn authority remains unproved; `ROTATE` may stay configured but cannot rotate live. |
| `DEVICE-PENDING` | Host work/compiles may pass, but the approved combined H3/H4 physical gate has not. |
| `DEVICE-ACCEPTED` | Every `H34-DEVICE-001` scenario passed on an approved exact-product candidate. |
| `FAIL-CLOSED` | A safety interlock blocks live writes; this is status, never a fourth mode. |

The expected first checkpoint is
`HOST-COMPLETE / ADAPTER-BLOCKED / LIFECYCLE-BLOCKED / DEVICE-PENDING`.

H4 provides, only for the registered `hollow-knight` profile:

- strict immediate-child pack import into a private content-addressed library;
- transactional registry generations, activation state, and durable session ownership;
- one immutable launch descriptor consumed by a process-owned C# runtime;
- a finite typed adapter, Unity-free transaction/rotation core, and lifecycle adapter;
- launcher library management and the existing in-game Skin control.

The `silksong` profile and saves are never read or changed by H4.

## 2. User-visible behavior
The in-game setting has exactly this cycle:

```text
OFF -> ON -> ROTATE -> OFF
```

There is no separate rotation toggle and no shuffle/random mode.

| Mode/transition | Contract |
| --- | --- |
| `OFF` | Keep or restore verified vanilla visuals. Retain selected pack and eligibility. Death signals do nothing. |
| `OFF -> ON` | With a selection, transactionally apply that exact current object; failure leaves `OFF`, selection, and verified vanilla. Without one, remain `OFF` with `NO_SELECTED_SKIN`. |
| `ON` | Keep the selected skin fixed across deaths, regardless of its rotation eligibility. |
| `ON -> ROTATE` | Commit mode only; keep current visuals, selection, eligibility, and stamp. Choose nothing. |
| `ROTATE` | Rotate only among separately user-enabled eligible packs. |
| `ROTATE -> OFF` | Cancel pending rotation first, then restore/keep vanilla transactionally; retain selection and eligibility. Failure keeps pending canceled and `ROTATE` disarmed until verified closure. |

`ROTATE -> OFF` is mode-only with unchanged stamp when verified live state is
already vanilla; otherwise it uses the write-ahead transaction.

Profile state keeps four independent concepts: selected pack ID, one
`rotationEligible` Boolean per installed pack, mode, and last durably active
receipt (`VANILLA` or exact pack ID/tree digest). Selection or eligibility edits
do not claim a visual change.

Eligible ordering is deterministic:

1. manifest `name`, already NFKC, compared as unsigned UTF-8 bytes, case-sensitive;
2. immutable ASCII pack ID as unsigned bytes.

Locale, filesystem/registry order, installation time, and UUIDs never order packs.
The anchor is active eligible pack, else selected eligible pack, else before the
first entry. Zero or one eligible pack makes each death a diagnosed no-op, even
if the sole pack differs from active.

In `ROTATE`, matching before/after-death signals confirm one monotonic death
epoch. The core chooses one successor from the immutable descriptor ring at
confirmation and records it pending; it does not write then. Duplicate signals for the epoch cannot advance again.
Only a matching stable-respawn token may apply the pending choice. Success then
updates selected and active; failure preserves/restores prior selection and
receipt. Leaving `ROTATE` cancels the pending epoch/candidate, making later
respawn tokens stale.

## 3. Public evidence and authority boundary
| Source | Pinned public provenance | Evidence | Limitation |
| --- | --- | --- | --- |
| [HollowKnight.CustomKnight](https://github.com/PrashantMohta/HollowKnight.CustomKnight) | `v3.5.0`, commit `46cf73415dd29baa2fed98a9939f58f0c488bf12`, MIT | Finite 162 `Skinable` + 9 `SaveHud` + 34 `AreaBackgrounds` catalog and strategy families | No exact `1.5.12620` declaration; cannot close adapter proof. |
| [hk-modding/api](https://github.com/hk-modding/api) | tag `1.5.78.11833-77`, commit `3c47ab577dfcac71bcfae73415618c79310b8f52`, MIT | Before/after-death and HeroUpdate hook ordering | No stable-ready callback or exact-port proof. |
| [Archipelago.HollowKnight](https://github.com/ArchipelagoMW-HollowKnight/Archipelago.HollowKnight) | commit `caecfdde39d1636191791402e55e48c7e2b2fb25`, MIT | Stable-playable predicate inspiration | Inspiration only, not exact-port or all-death authority. |

The fork implementation is independent. Public source evidence never substitutes
for typed exact-build runtime proof. H4 bundles no upstream/game images, user
packs, generated game assemblies, or other source assets. Copied source would
require a separate file/license audit; private imports are never uploaded or
released.

## 4. Fixed target catalog
The normative catalog is
[`data/hollow-knight-skin-catalog-v1.txt`](data/hollow-knight-skin-catalog-v1.txt):
exactly 205 unique LF-terminated `.png` paths in pinned source order—162
`Skinable`, then 9 `SaveHud`, then 34 ordinal-UTF-8 `AreaBackgrounds` paths.
SHA-256 of its exact UTF-8 bytes is
`258a7fa2b3a1a94d114eb73c39259dfa6853139017afced53ca3afa668a1372a`.
Its provenance is the CustomKnight pin above.

Catalog ID is `hk-custom-knight-v3.5.0-205`. Each row has one fork-owned finite
typed strategy descriptor (exact sprite, one/multiple tk2d material, one/multiple
bounded material, hook, or no-cache/rebind). Catalog order governs descriptor
texture rows and forward writes; rollback uses reverse order. Runtime target
discovery is forbidden.

## 5. Pack manifest and import boundary
Each selected folder's immediate child directory is one independent candidate.
Root files are ignored; alias/reparse children are rejected. Each candidate has
exactly one root `skin.json`: UTF-8 without BOM, at most 65,536 bytes, one JSON
object, no duplicate or unknown keys.

### 5.1 Manifest v1 fields
| Field | Required contract |
| --- | --- |
| `schemaVersion` | JSON integer `1` only. |
| `id` | 1–64 ASCII; `[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?`. |
| `name`, `author` | 1–80 Unicode scalars, already NFKC; no control/bidi-control/surrogate, slash, backslash, or edge whitespace. |
| `contentSha256` | Exactly 64 lowercase hex digits; canonical payload identity below. |
| `games` | Exactly key `hollow-knight`. |
| game object | Exactly `gameVersion`, `catalogId`, `assetRoot`, `textures`. |
| `gameVersion` / `catalogId` | Exactly `1.5.12620` / `hk-custom-knight-v3.5.0-205`. |
| `textures` | 1–205 entries; each key is an exact case-sensitive catalog path; each value is relative to `assetRoot`. |

Only optional top-level fields are `license` (1–80 display characters),
`homepage` (absolute HTTPS, at most 2,048 characters), `attribution` (1–512), and
`preview` (validated root-relative PNG). Metadata does not assert redistribution
rights.

### 5.2 Paths, tree, PNG, and bounds
All relative paths use `/`, at most four segments and 240 UTF-8 bytes. Each
1–64-character ASCII segment matches `[A-Za-z0-9][A-Za-z0-9._-]*`. Reject empty,
`.`/`..`, absolute, drive/UNC, backslash, percent-escaped, NUL, trailing-dot/space,
or case-insensitive Windows-device segments. Texture payload path is exactly
`assetRoot + "/" + value`; preview remains root-relative.

Reject escapes/traversal, symlinks, junctions, mounts, reparse points, hard-link
aliases, sockets/FIFOs/devices, virtual or unknown nodes, unstable identity/size,
and ASCII-case-fold collisions. Preview may not equal/case-fold-collide with a
texture; no payload may collide with `skin.json`. Multiple targets may share one
identical canonical texture path and are deduplicated as one file.

After one bounded root listing, traversal visits only exact prefixes of declared
payloads. Every row is charged before classification. Extra/empty directories
or undeclared files reject the candidate, including `Swap/`, `Cinematics/`,
`names.json`, text/config replacement, runtime/provided-skin material, or any
other dynamic target input. Filesystem opens use no-follow plus containment and
pre/post identity checks; document providers may expose only classified regular
streams/directories. The staged private tree is rechecked.

| Import/tree limit | Maximum |
| --- | ---: |
| candidates per import / installed packs | 128 / 64 |
| texture mappings / regular files including manifest+preview | 205 / 207 |
| observed nodes / directories per candidate | 512 / 64 |
| provider rows across candidate listings | 1,024 |
| texture / preview / total declared payload bytes | 16 MiB / 4 MiB / 256 MiB |
| PNG width or height / decoded pixels | 8,192 / 33,554,432 |

Crossing a bound aborts that candidate with `LIMIT_EXCEEDED`; valid siblings
remain processable. Same ID/tree is idempotent; same ID/different tree requires
explicit replacement confirmation; different IDs never conflict by name or
basename. Replacement publishes a new immutable object, preserves selection and
eligibility by ID, and retains old reachable objects.

Each PNG requires valid signature, first IHDR, chunk lengths
and CRCs, one IEND, dimensions in bounds, and no APNG chunks. Full decode occurs
in prepare before live mutation.

### 5.3 Canonical identities
Store the validated manifest as RFC 8785 canonical JSON. For `contentSha256`,
exclude `skin.json`; sort unique canonical texture payload paths plus optional
root-relative preview by unsigned UTF-8 bytes and hash:

```text
ASCII("HKS-PAYLOAD-V1\0")
for each path: U32BE(pathUtf8.length) || pathUtf8 ||
               U64BE(file.length) || SHA256(fileBytes)
```

`treeSha256` uses `HKS-TREE-V1\0` and identical framing over canonical
`skin.json` plus the same payload set. Recompute from staged bytes; never trust
source metadata. Formatting-only manifest changes preserve identity; semantic or
payload changes do not.

## 6. Private immutable storage and schemas
`ProfilePaths` contains all data under
`<files>/profiles/hollow-knight/skins/`; no build staging or SharedPreferences is
skin authority.

```text
objects/sha256/<2hex>/<treeSha256>/{object.json,pack/{skin.json,<declared-payloads>},.complete}
staging/{import-<uuid>/,registry-<uuid>/}
registry/{generations/rg-<20digit-sequence>-<uuid>/{registry.json,registry.sha256,
  .complete},current,previous,next}
sessions/{active,sequence,<descriptorId>/{descriptor.json,descriptor.sha256,.complete,
  lease/{states/ls-<20digit-transition>-<uuid>/{lease.json,lease.sha256,.complete},
  current,previous,next}}}
locks/{session.lock,registry.lock}
```

Canonical JSON rejects duplicate/unknown fields. Counts/lengths/sequences/stamps
are canonical unsigned-decimal strings; digests are lowercase hex; IDs are
canonical lowercase UUIDs. Completed objects and descriptors are immutable and
have no production mutation path.

| Canonical document | Exact logical fields |
| --- | --- |
| `object.json` | `schemaVersion, treeSha256, contentSha256, manifestSha256, fileCount, payloadBytes, files[]` where each file is `path,length,sha256`. |
| `registry.json` | `schemaVersion, generationId, sequence, parentGenerationId, operationId, writer, profileId, gameVersion, catalogId, catalogSha256, packs[], activation`. |
| registry pack | `id,name,author,treeSha256,contentSha256,rotationEligible`; records sorted by ASCII ID. |
| activation | `mode,selectedPackId,active,skinStamp,rotationInterlock`; active is `VANILLA` or exact `PACK` ID/digest. |
| descriptor | `schemaVersion, descriptorId, sessionSequence, leaseId, leaseToken, profileId, gameVersion, catalogId, catalogSha256, registryGenerationId, registrySha256, mode, selectedPackId, active, skinStamp, rotationInterlock, packs[]`. |
| descriptor pack/object | Pack: `id,name,orderKeyHex,rotationEligible,currentObject,retainedActiveObject?`; object: `treeSha256,contentSha256,objectRelativePath,textures[]`; texture: `target,source,sourceSha256`. |
| lease state | `schemaVersion, leaseId, leaseTokenSha256, profileId, sessionSequence, transitionSequence, transitionId, parentTransitionId, state, descriptorId, descriptorSha256, registryGenerationId, registrySha256, launcherOwner, gameOwner, closeReason`. |

`object.json.files` lists canonical `skin.json` plus each unique payload once in
unsigned UTF-8 path order; `fileCount` includes the manifest, `payloadBytes`
excludes it, and `manifestSha256` hashes canonical manifest bytes. Readers fully reparse/re-hash all bytes, manifest,
rows, counts, payload/tree framing, shard, and no-alias tree; `.complete` alone is
never acceptance.

## 7. Durable publication and registry recovery
One primitive publishes object, generation, descriptor, and lease-state
directories:

1. create same-filesystem ancestors one component at a time and sync each new
   directory and naming parent;
2. write/sync all files and contained directories; write/sync zero-byte
   `.complete` last;
3. atomically rename to final name;
4. sync the destination parent and each newly created ancestor leaf-to-root
   through the first pre-existing profile ancestor, including the profile-root
   parent if `skins` was new.

No reference, pointer, or durable-success response precedes step 4. Idempotent
existing destinations are fully revalidated and receive the same parent barrier.
Unavailable guarantees return `DURABILITY_UNAVAILABLE`. Fault tests cover rename
before destination-parent sync and every sync/reference boundary.

A virgin profile deterministically publishes one sequence-0 genesis: empty packs,
`OFF`, null selection, vanilla active, stamp `0`, clear interlock, null parent.
Only after its destination barrier does it publish/sync `next`, establish/sync
`current`, remove/sync `next`; genesis has no `previous`. If no current/previous
but committed evidence exists, recovery reconstructs genesis only when exactly
one fully valid sequence-0/null-parent generation exists, no other committed
entry exists, and `next` is absent or names it exactly. Any ambiguity is
`REGISTRY_GENESIS_CORRUPT`; never synthesize over non-virgin evidence.

Normal commits hold locks in `session.lock -> registry.lock` order, CAS the exact
current generation/digest, publish sequence `current+1` with immediate parent,
then sync `next`, old-current `previous`, new `current`, and removal of redundant
`next` in that order. Operation IDs are idempotent only for byte-identical
requests. Stale writers get `REGISTRY_CONFLICT`; failed publication consumes no
sequence.

Recovery validates bounded `current/next/previous`, catalog/digest/profile/interlock,
and referenced objects; it never chooses mtime. The normal history window is the
accepted head plus at most seven immediate parents. Sequence/parent adjacency is
required only for child-parent pairs inside that last-eight window; the oldest
retained generation is the history floor, and its older parent edge is neither
resolved nor a reachability root. A valid current wins; otherwise valid `next`
must be `previous+1` and its child, else valid previous. Ambiguity is read-only
`REGISTRY_RECOVERY_AMBIGUOUS`; no valid non-virgin candidate is
`REGISTRY_UNRECOVERABLE`. Incomplete contained staging may be deleted; corrupt
objects/generations are reported, never automatically deleted.

## 8. Immutable launch descriptor and session lease
Descriptor `packs` is the complete union by immutable pack ID of selection,
eligibility, and active/rollback references. Each envelope carries current object
and, only when active uses an older digest, nested `retainedActiveObject`.
Envelopes use normalized-name+ID order. Each object's mapped `textures` is filtered
into catalog-file order, never manifest/filesystem/lexical path order. Kotlin and
C# recompute name ordering keys, catalog identity, object hashes, paths, and order.
The descriptor retains the structured interlock; it is never flattened.

`GameProcessStartup` captures one canonical descriptor/digest under both locks.
It is immutable for the Unity process; C# never rereads `registry/current` or
rescans storage. Descriptor/profile/build/catalog/object/lease mismatch blocks
adapter startup and leaves natural vanilla. `SkinSessionBridge` exposes only
descriptor verification, lease claim/close, and idempotent lease-qualified
registry CAS—no scanner or arbitrary writer.

A profile has at most one durable lease:

```text
LAUNCH_PENDING -> GAME_OWNED -> CLOSED
LAUNCH_PENDING -> CLOSED  (definitive launch failure/recovery only)
```

States are immutable linked transitions; `CLOSED` is terminal. Under
`session.lock`, every lease-head advance first publishes the complete child state
through the destination-parent barrier, then publishes/syncs `next`, replaces and
syncs `previous` with old `current`, replaces and syncs `current` with the child,
and removes/syncs `next`. Transition zero omits `previous`.

Acquisition reserves durable monotonic `sessionSequence`, publishes descriptor
and a `LAUNCH_PENDING` head, then CAS-publishes/syncs `sessions/active` from
absent to that exact head before `startActivity`. Claim validates lease ID,
unhashed 256-bit token, descriptor digest, exact `{uid,pid,processStartToken}`,
and active `LAUNCH_PENDING`; it durably advances the descriptor head to
`GAME_OWNED`, then CAS-replaces/syncs `sessions/active` from that exact pending
head to the owned head. Claim is confirmed only after the active-pointer barrier.

Graceful, definitive-failure, or recovery close durably advances the head to
`CLOSED`, then CAS-removes and syncs the exact same-lease active pointer expected
from its parent head. Close is confirmed only after that barrier. Every game
commit revalidates token hash, owner, lease/head, descriptor, and registry parent.
All launcher imports, replacement, selection, eligibility, launch, cleanup, and
game commits take `session.lock` before `registry.lock`; a non-closed or
liveness-unknown lease excludes launcher mutation and a second launch.

Recovery under `session.lock` discovers at most 64 complete descriptors and three
complete lease states per descriptor. It validates descriptor/state bytes,
immediate parent/sequence, and resolves `current/previous/next` only as follows:

- without `next`, valid `current` is the head only when `previous` is absent for
  transition zero or its exact parent; valid `previous` alone is a fallback only
  when both other pointers are absent;
- valid `next` is selected only when it is transition zero with both other
  pointers absent, an immediate child of valid `current` with `previous` either
  that current or its exact parent, or the same state as `current` with
  `previous` absent for transition zero or naming its exact parent;
- the accepted tuple is canonicalized to `current=head`, `previous=exact parent`
  when present, and no `next`, with each pointer change synced.

After resolving every head, absent or stale same-lease `sessions/active` is
CAS-rewritten and synced to the unique non-`CLOSED` head. If that lease's resolved
head is `CLOSED`, its exact same-lease active pointer is CAS-removed and synced,
even when it still names the parent head. Multiple non-`CLOSED` candidates,
malformed descriptor/state/pointer tuples, cross-lease active state, or either
bound exceeded is `SESSION_RECOVERY_AMBIGUOUS`; mutation and launch stay
read-only, never guessed. A claim/close crash after its head barrier but before
its active-pointer barrier is therefore reconciled by these same rules.

When every resolved head is `CLOSED`, no active pointer is reconstructed.
CLOSED-only orphans follow eight-highest-session retention; only retained
descriptors pin dependencies. A descriptor without one valid terminal head is
malformed, not cleanup-eligible. No lease expires by age. Recovery may close
`LAUNCH_PENDING` only when launcher is definitively dead and target game absent,
and `GAME_OWNED` only when exact owner/start token is definitively dead;
unknown/denied/contradictory liveness remains blocking.

Storage bounds: full profile skin tree 1 GiB; sessions 96 MiB; descriptor 8 MiB;
lease state 64 KiB. Account allocated bytes when available, otherwise logical
length rounded to 4 KiB per file; reserve staged import size first. Retain one
active lease, the eight highest closed sessions, and the last-eight normal
registry window ending at its history floor.

Separately pin every generation/object named by `current/previous/next`, each
active or retained session descriptor, an active receipt, or a non-clear interlock,
even outside the normal window. Explicit pins are non-transitive across parent
history: they retain each named generation and its referenced objects, never an
ancestor solely because of a parent edge. Cleanup runs
only without an active/unknown lease, revalidates containment/no-alias, and syncs
parents; it never deletes a pin or its object dependencies. Unpinned complete
history below the floor and CLOSED-only orphans beyond retention are collectible;
corrupt state is not traversed/deleted. If bounded retention plus pins cannot fit,
fail new publication with `PROFILE_QUOTA_EXCEEDED`; never evict pins or retain
unbounded parent history.

## 9. Process runtime and typed boundaries
`HollowKnightSkinRuntime : MonoBehaviour` is one process-owned
`DontDestroyOnLoad` instance, independent of display 1. It owns session, adapters,
stamp, errors, and resource lifetime. Presenter/display detach affects only view;
it cannot change mode, cancel a death, dispose the runtime, or create a session.

`tools/hollow-knight-patches/src/dualsouls/HollowKnightSkinPresenter.cs` extends
`public partial class HKDualScreen` and borrows
`HollowKnightSkinRuntime.Current`. It creates no `HollowKnightSkinPresenter` type,
runtime, registry, store, or adapter authority. `HkStageHooks.SkinStamp` remains
delegated to the process runtime; the existing `lastSkinStamp` comparison and
`InvalidateCompanionClones()` remain the sole clone-invalidation seam.

The C# transaction/rotation core has no `UnityEngine` reference. Typed values
include mode, activation/rotation state, plans/receipts/commit results,
`DeathEpoch`, `HeroBindingToken`, `SkinBindingToken`, and `StableRespawnToken`.
The core emits explicit prepare/apply/rollback/commit/cancel commands. Kotlin and
C# must agree on shared golden logs.

`IHollowKnightSkinApi` is a finite fakeable API for exact binding readiness,
default capture, prepare/decode, catalog-row apply, reverse restore, complete
postcondition, and disposal. Its only production implementation binds exact
managed `1.5.12620` types using typed singletons/hook payloads or exact bounded
descendants. Forbidden: native addresses/offsets, process memory or scans,
injected native bridge, arbitrary reflection/name similarity, global renderer or
object scans (`FindObjectsOfType`, `Resources.FindObjectsOfTypeAll`), injected
`PlayerData`, save edits, and undeclared/dynamic Swap/text/cinematic/runtime
skin discovery.

Whole-catalog apply writes prepared PNG for each mapped row and captured vanilla
default for every unmapped row, preventing residue. Prepare resolves all binders,
decodes all mapped files, and creates exact prior rollback plans before mutation.

## 10. Resource bounds and lifetime
Use checked 64-bit `width * height * 4` RGBA32 accounting, no mipmaps, and release
readable CPU upload/scratch before the next decode.

| Runtime aggregate | Maximum |
| --- | ---: |
| one unique decoded texture and sequential scratch | 32 MiB |
| resident textures in one prepared/committed plan | 96 MiB |
| unique allocations in one plan | 205 |
| all H4 resident bytes, including active/candidate/retired/scratch | 224 MiB |
| all H4 allocations in process | 410 |
| displaced committed generations awaiting retirement | 1 |

Reserve before decode; overflow/allocation/limit failure occurs before ARMED.
Rows sharing a verified canonical source decode once; other byte sharing requires
equal length, SHA-256, dimensions, and format. Every target/material/rollback
reference is ref-counted. Preview is never game-decoded.

After durable completion and companion clone invalidation, displaced resources
enter the single retirement set. Disposal waits for zero target/rollback refs and
acknowledgement that `InvalidateCompanionClones()` destroyed all old-stamp clones
(or authoritative registered clone count is zero). An undrained set blocks the
next apply; growth is not queued. Game/default textures are borrowed, never
disposed.

**Disposal is write-free:** rollback is an explicit transaction action, never a
disposer/finalizer/teardown side effect. Teardown only cancels tokens, detaches
hooks, disposes zero-reference H4 resources, and may close its lease. It performs
no target restoration, activation commit, or interlock clear.

## 11. Write-ahead activation and `SkinStamp`
`rotationInterlock` has exactly:

- `CLEAR`: all transaction fields null;
- `ARMED`: transaction ID, operation kind (`STARTUP_APPLY`, `MODE_ON`,
  `MODE_OFF`, `DEATH_ROTATION`, `REBIND_APPLY`), base generation, complete prior
  and target activation snapshots, exact binding token, and
  `priorEstablishedOnBinding`; failure null;
- `ROLLBACK_FAILED`: all ARMED data plus stable original/rollback error codes.

Every registry generation and launch descriptor carries this full structure.
Metadata mutation requires `CLEAR`. Before any live write:

1. verify `GAME_OWNED`, clear base, authoritative adapters, and drained retirement;
2. reserve/decode/deduplicate all resources and prepare desired/prior plans without
   mutation;
3. CAS and validate a durable child whose outer activation remains prior and whose
   interlock is `ARMED`;
4. revalidate the recorded `SkinBindingToken` immediately before every catalog
   write and before and after complete postcondition;
5. after verified apply, CAS and validate the immediate completion child with
   target activation, `skinStamp + 1`, and `CLEAR`;
6. only then publish the receipt/stamp in memory, trigger companion clone
   invalidation, and retire displaced resources after acknowledgement.

Thus `SkinStamp` exists only with a durable visual-state commit; companion
invalidation is triggered only after that commit. It is signed-64 range persisted
as unsigned decimal. It advances for vanilla→pack, pack/digest→other, pack→vanilla,
and successful establishment on a new binding. It does not advance for metadata,
mode-only/write-free changes, pending/no-op rotation, failed prepare, or verified
same-binding rollback/fresh-binding vanilla fallback. Durable prior-pack
re-establishment on a fresh binding is a real change and advances once.

A pre-ARM failure writes nothing. Apply/postcondition or definitively rejected
completion rolls back changed rows in reverse order on the same still-authoritative
token and verifies prior. Same-binding prior or fresh-binding vanilla closure
keeps prior stamp; fresh-binding prior-pack closure uses prior stamp+1. Only a
durably confirmed closure clears ARMED. Rollback/postcondition failure persists
`ROLLBACK_FAILED` where possible and prohibits later process writes. An
indeterminate completion/closure performs no speculative rollback, publishes no
in-memory receipt/stamp, and prohibits further writes until next-process recovery.

The sole non-clear recovery runs at most once per process/binding and always
chooses recorded `prior`, never guesses target. On a fresh authoritative token it
prepares prior; vanilla is verified without write, while prior pack is applied in
catalog order. A CAS child clears to exact prior (same stamp for vanilla,
prior+1 for newly established pack). Failure or indeterminate result leaves the
interlock and blocks all other writes; stale CAS never clears it. Failed ARMED
recovery may append `ROLLBACK_FAILED` with `INTERRUPTED_TRANSACTION` and the
recovery code; an existing `ROLLBACK_FAILED` record remains unchanged.

## 12. Startup, rebinding, and lifecycle
Startup begins on natural vanilla and first performs sole interlock recovery.
Then:

- `OFF`: no write/commit/stamp; selection remains.
- `ON`: establish exact selected current object unless already verified on this
  binding; fallback is recovered baseline or durable prior receipt.
- `ROTATE`: never choose a successor; re-establish exact durable active
  current/retained object, or keep vanilla. Eligibility is consulted only after
  confirmed death.

The adapter emits a monotonic logical `SkinBindingToken` only after the complete
typed target set/default capture validates. On a new authoritative binding,
`OFF` captures vanilla only; `ON` applies selected current; `ROTATE` reapplies
exact active current/retained without advancing rotation. Pack work is
`REBIND_APPLY` and follows ARMED/stamp/resource rules.

If rebinding occurs while a confirmed death awaits respawn, retain its one
pending candidate, invalidate old hero/skin/respawn tokens, and wait for stable
respawn carrying the new tokens; do not run competing rebind. A zero/one no-op or
canceled pending candidate falls back to the mode rule. Leaving `ROTATE` cancels
first. Rebinding alone never advances the ring.

Plans/ARMED bind one token. Revalidate before each write and before/after
postcondition. Stale before first write closes prior/CLEAR without stamp and may
schedule the new token. After any write, rollback may touch only the original
still-authoritative binding; if detached, touch neither old nor new targets,
leave/persist `ROLLBACK_FAILED`, and prohibit further writes.

`HollowKnightSkinLifecycleAdapter` emits no Unity writes and is ready only after
exact-port typed proof. It arms at most one before-death candidate, confirms one
unsigned-64 epoch on matching after-death, reacquires hero identity, and issues a
new `HeroBindingToken` on change. It emits one stable-respawn token only after
three consecutive HeroUpdate observations on the same hero and current skin token
satisfy: accepting input, full damage mode, health > 0, `CanTakeDamage`, playable,
and not paused/in cutscene/scene transition. False resets the window; stale,
duplicate, mismatched, or pre-confirmation signals are ignored and diagnosed.
Public hooks/predicates are evidence only; exact-port authority remains a ledger
gate.

## 13. UI and accessibility
Launcher `SettingsActivity` adds profile-aware **Skins** beside Mods/logs.
`SkinsActivity` follows existing true-dark card/action idioms and shows target,
**Import folder** guidance, per-candidate results, installed name/author/ID/hash,
independent **Select** and **Include in rotation**, mode/active/eligible order,
interlock/error, and explicit replacement confirmation. Persistence is immediate.
When a lease is active or liveness unknown, mutation controls are disabled with
reason; status/error copy remain available.

The in-game modal row shows full `SKIN: OFF|ON|ROTATE`, selected name, eligible
count/no-op reason, pending state, and error. It is independent of H3 Mods master;
display detach does not change runtime state.

All actions have labels/content descriptions, 48 dp targets, visual-order focus,
font scaling, wrapping/scrolling, non-color state cues, and non-disruptive status
announcements. Controller navigation is not required; controller input remains
the game's.

## 14. Focused verification gates
Host completion requires one checkpoint passing:

- manifest/path/tree/PNG limits, alias/traversal/collision/undeclared rejection,
  duplicate decode/path dedup, canonical payload/tree and catalog digests;
- candidate isolation; publication fault injection at rename, destination-parent/
  ancestor sync, marker, pointer, deterministic genesis, CAS, and recovery edges;
- last-eight history-floor adjacency, ignored older parent reachability, explicit
  out-of-window pins/dependencies, cleanup, and fail-closed quota exhaustion;
- profile isolation; lease race/PID-reuse/unknown-liveness/crash recovery, with
  fault injection after every claim/close head and active-pointer durability
  barrier; absent/stale same-lease reconciliation, exact CAS, explicit pointer
  tuples, multiple/malformed/cross-lease/bound ambiguity, and CLOSED-only cleanup;
- all three modes, deterministic order, zero/one no-op, death debounce/cancel,
  startup/rebind tokens, stable-respawn window, stale signals, and rollback;
- ARMED-before-write, whole-catalog/reverse rollback, indeterminate outcomes,
  sole recovery, write-free disposal, exact stamp and clone acknowledgement;
- 32/96/224 MiB, 205/410 allocation, overflow, dedup/refcounts, and one retirement
  set;
- descriptor complete-union/nested-retained/catalog order and Kotlin/C#/Python
  golden agreement; launcher accessibility and active-lease disabling;
- policy scans proving the exact presenter partial/borrowed-runtime/single-authority
  contract and no random/shuffle, dynamic discovery, native/process/global scans,
  injected `PlayerData`, save writes, or Silksong coupling.

Host commands include `make test` and:

```text
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release
```

Exact compiles from the same checkpoint are:

```powershell
pwsh -NoProfile -File tools/hollow-knight-patches/check.ps1 `
  -Depot 'D:\Temp\dualsouls-hk-12620\Hollow Knight\hollow_knight_Data\Managed' `
  -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
pwsh -NoProfile -File tools/silksong-patches/check.ps1 `
  -Depot 'D:\Temp\hkandroid-task11-silksong-managed' `
  -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
```

HK compile proves symbols, not live bindings/lifecycle. Silksong compile is only
a shared-source regression gate; it does not enable H4 there.

After explicit approval only, one future combined `H34-DEVICE-001` candidate
covers two valid/one invalid pack, duplicate IDs, selection/eligibility, complete
mode cycle/vanilla retention, 0/1/2/3 rotation, normal/dream/Steel Soul and
scene/bench deaths, debounce/stability/cancel/rebinding/stale-token injection,
all 205 families and default reset, memory/dedup/retirement telemetry, stamp clone
refresh, forced apply/commit/rollback failures and persistent interlock, lease
claim/second-launch/crash/close, relaunch/display loss, H2/H3 regression, and no
save or Silksong delta. No Android/ADB/emulator/signing/release or micro live-check
loop belongs to host implementation.

## 15. Closure ledger
| ID | Closure condition | Current truth |
| --- | --- | --- |
| `HKSKIN-HOST-001` | Private importer/object/registry/session/descriptor/UI and pure C# runtime satisfy the complete host matrix and cross-language goldens together. | `SPECIFIED / NOT IMPLEMENTED` |
| `HKSKIN-CATALOG-001` | Companion file remains 205 unique paths in 162+9+34 order with exact digest; every row has one finite typed descriptor; Kotlin/C#/Python agree. | `PUBLIC EVIDENCE + FILE WRITTEN / TYPED DESCRIPTORS NOT IMPLEMENTED` |
| `HKSKIN-ADAPTER-001` | Exact compile plus approved runtime gate prove all 205 `1.5.12620` bindings, missing-row vanilla reset, rebinding, postcondition, reverse rollback, and failed-rollback interlock. | `ADAPTER-BLOCKED` |
| `HKSKIN-LIFECYCLE-001` | Exact compile plus approved runtime gate prove normal/dream/Steel Soul/scene/bench death, debounce, three-update stability, cancel/stale/rebind behavior. | `LIFECYCLE-BLOCKED` |
| `HKSKIN-STAMP-001` | Host transactions and exact compile prove `long HkStageHooks.SkinStamp` only after durable visual commit; approved gate proves companion clones refresh after success and retain prior content after failure. | `SPECIFIED / NOT IMPLEMENTED` |
| `H34-DEVICE-001` | Every combined scenario in section 14 passes on one explicitly approved exact-product candidate with retained evidence and no save/Silksong delta. | `DEVICE-PENDING / NOT AUTHORIZED` |

Documentation/public evidence closes none of these runtime rows.

## 16. Host staging
Use five coherent host batches, each with its complete host checks—not live
micro-loops:

1. secure import, canonical identities, immutable objects, publication/recovery;
2. registry activation, durable lease/descriptor, launcher UI, isolation/quota;
3. fixed catalog plus Unity-free transaction/rotation/lifecycle cores and goldens;
4. typed adapter/runtime/lifecycle/presenter/stamp integration and both compiles;
5. one full host reconciliation, fault matrix, policy audit, and truthful ledger.

A combined H3/H4 device batch is separate and starts only after explicit approval.
`HOST-COMPLETE` keeps adapter, lifecycle, and device blockers open until their
stronger closure conditions pass.
