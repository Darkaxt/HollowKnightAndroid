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
| `HOST-COMPLETE` | One checkpoint passes every synthetic host gate, exact read-only corpus replay, full host reconciliation, and both exact compiles. |
| `ADAPTER-BLOCKED` | Exact `1.5.12620` typed bindings, whole-catalog apply, or synchronous rollback remain unproved; non-vanilla apply is disarmed. |
| `LIFECYCLE-BLOCKED` | Confirmed-death or stable-respawn authority remains unproved; `ROTATE` may stay configured but cannot rotate live. |
| `DEVICE-PENDING` | Host work/compiles may pass, but the approved combined H3/H4 physical gate has not. |
| `DEVICE-ACCEPTED` | Every `H34-DEVICE-001` scenario passed on an approved exact-product candidate. |
| `FAIL-CLOSED` | A safety interlock blocks live writes; this is status, never a fourth mode. |

The expected first checkpoint is
`HOST-COMPLETE / ADAPTER-BLOCKED / LIFECYCLE-BLOCKED / DEVICE-PENDING`.

H4 provides, only for the registered `hollow-knight` profile:

- bounded local ZIP normalization into a strict private manifest/object library;
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

Profile state keeps four independent concepts: selected pack ID, one `rotationEligible` Boolean per installed pack, mode, and last durably active visual receipt: `VANILLA` or exact `PACK` ID/tree/content/import-receipt digest. Selection or eligibility edits do not claim a visual change. Active `PACK` carries its import receipt for provenance/retention only; visual equality and `SkinStamp` decisions use ID/tree/content, never archiveName or importReceipt digest alone.

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
updates selected and active; failure preserves/restores prior selection and active
visual receipt. Leaving `ROTATE` cancels the pending epoch/candidate, making later
respawn tokens stale.

## 3. Public evidence and authority boundary
| Source | Pinned public provenance | Evidence | Limitation |
| --- | --- | --- | --- |
| [HollowKnight.CustomKnight](https://github.com/PrashantMohta/HollowKnight.CustomKnight) | `v3.5.0`, commit `46cf73415dd29baa2fed98a9939f58f0c488bf12`, MIT | Finite 162 `Skinable` + 9 `SaveHud` + 34 `AreaBackgrounds` catalog and strategy families | No exact `1.5.12620` declaration; cannot close adapter proof. |
| [hk-modding/api](https://github.com/hk-modding/api) | tag `1.5.78.11833-77`, commit `3c47ab577dfcac71bcfae73415618c79310b8f52`, MIT | Before/after-death and HeroUpdate hook ordering | No stable-ready callback or exact-port proof. |
| [Archipelago.HollowKnight](https://github.com/ArchipelagoMW-HollowKnight/Archipelago.HollowKnight) | commit `caecfdde39d1636191791402e55e48c7e2b2fb25`, MIT | Stable-playable predicate inspiration | Inspiration only, not exact-port or all-death authority. |
| [`hkskins.art`](https://hkskins.art/) / [source](https://github.com/Tadeas-Jun/hkskins) | Gatsby `5.16.1`, commit `12d0b519c9c6225f5a91d21cd9440ebe82f1ee12`; no repository license | Undocumented page data has 609 records (547 Hollow Knight, 62 Silksong) with `subDir,imagePath,metadata{name,source,author,game,type,desc,dateAdded}`; source links span arbitrary hosts | No backend, supported API, stable public IDs, or archive hashes/sizes/signatures/licenses; discovery reference only. |

The fork implementation is independent. Public source evidence never substitutes for typed exact-build runtime proof. H4 bundles no upstream/game images, user packs, generated game assemblies, site metadata/previews, or other source assets. Copied source requires a separate file/license audit; private imports are never uploaded or released. The mutable unpublished Google Doc/Markdown export is offline reference only and never parsed at runtime. Exact site, export, and read-only archive observations are in [`hollow-knight-skin-corpus.md`](../../verification/hollow-knight-skin-corpus.md).

Core H4 has no cross-domain download manager. Optional later browser support is an isolated in-app Android browser rendering only same-origin `https://hkskins.art/`, with no JavaScript bridge or native/file authority. Arbitrary external hosts and all downloads hand off to Android system handling; files can return only through normal SAF import. This browser and RAR support remain deferred, nonblocking ledger items and are not claimed implemented here.

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

## 5. ZIP normalization and strict import boundary
One selected SAF file, or every bounded immediate regular file in one selected SAF folder regardless of extension/MIME, is an independent archive input. Copy it exactly once into private quarantine while streaming SHA-256 and byte count; inspect only that copy. Magic then classifies ZIP, unsupported RAR, or invalid input—filename and MIME are hints only. No network source, page data, Google Doc, or Markdown is consulted.

### 5.1 Bounded ZIP reader
Accept only single-disk, non-ZIP64 ZIP Store/Deflate; both methods allow data-descriptor bit 3 clear or set. Local/central filename bytes, flags, and methods agree. With bit 3 clear, local CRC-32 and 32-bit compressed/uncompressed sizes equal central values and a descriptor is forbidden. With bit 3 set, local `{crc32,compressedSize,uncompressedSize}` is either all zero or all exactly equal the central-directory tuple; reject partial-zero, partial-present, mismatched, sentinel, or any other tuple. Exactly one mandatory 12-byte unsigned or 16-byte signature-prefixed descriptor begins directly after data and is authoritative against matching central and streamed CRC/sizes. Reject absent, unexpected, duplicate, or ambiguous descriptors, encryption, unsupported methods, ZIP64/multi-disk, malformed/overlapping central/local ranges, inconsistent headers/sizes, truncation, and ambiguous trailing structure; CRC-verify content.

Central/local filename byte strings are identical and 1–512 bytes. Literal `0x2f` is the sole separator. Reject NUL, backslash, leading slash, empty interior component, `.`/`..`, and drive/device forms. One trailing slash marks an explicit directory; a regular file may not have it. A candidate prefix is its component byte strings joined by one `0x2f` without trailing slash; root is zero bytes. Derive implicit directories from regular-file components, so explicit directory entries never affect candidateKey or layout. Every path is exact-raw collision checked.

With UTF-8 bit set, strictly decode each component and additionally collision-check NFKC plus ASCII-fold. Unflagged paths use exact raw bytes plus ASCII fold only for bytes `0x41..0x5a`; non-ASCII is allowed only in wrapper/candidate names or ignored extras, never catalog texture mapping. ASCII paths collide across flag domains. Parse every extra field structurally within bounds; reject ZIP64 and path-authoritative Unicode Path `0x7075`, while other non-path metadata cannot affect path/mapping/key and may emit `IGNORED_EXTRA_METADATA`. Raw central bytes are sole path authority. ZIP slip, symlink/device aliases, duplicate normalized paths, and nested-archive execution are forbidden; RAR magic returns `UNSUPPORTED_RAR` without extraction.

| Archive limit | Maximum |
| --- | ---: |
| quarantined compressed bytes | 256 MiB |
| entries / directories | 4,096 / 512 |
| raw source path / depth | 512 bytes / 16 segments |
| declared and streamed uncompressed bytes | 512 MiB |
| declared and streamed per-entry/archive expansion ratio | 100:1 |
| accepted candidates | 128 |

Charge central declarations before extraction and streamed bytes during it. Enforce 100:1 independently for each entry and the archive aggregate, comparing declared and actual uncompressed `U` against that entry's/aggregate central compressed payload bytes `C` (headers excluded). Checked integer rule: `U=0` passes including `0/0`; `U>0,C=0` rejects; otherwise reject iff `U > 100*C`; overflow rejects. Either declared or streamed violation rejects the archive. The 512 MiB aggregate and every other bound still apply. Reserve quarantine/staging against profile quota before copy/decode and remove only by contained, synced cleanup.

### 5.2 Deterministic candidate discovery and mapping
A recognized candidate is a canonical raw prefix with at least one catalog asset mapped by exact, case-fold, or finite-alias rules below. Candidate order is unsigned lexicographic over canonical raw prefixes.

1. Find exact `hollow_knight_Data/Managed/Mods/CustomKnight/<pack>` full-install suffix candidates first. If any exist and any recognized root/wrapper candidate lies outside those subtrees, return `AMBIGUOUS_LAYOUT`; otherwise choose all full-install candidates in raw-prefix order and ignore/warn unrelated entries.
2. Without full-install candidates, if root has mapped assets, choose root; any recognized wrapper candidate also makes this `AMBIGUOUS_LAYOUT`.
3. Otherwise identify the unique first-level raw wrapper containing recognized mappings: zero returns `NO_CANDIDATE`, more than one returns `AMBIGUOUS_LAYOUT`. If assets map directly under it, choose one wrapper candidate and treat deeper trees as unsupported extras/warnings. Otherwise choose every immediate child with mappings as one or many multi-pack candidates in raw-prefix order. Never recursively inspect deeper folders as candidates.

For each recognized candidate, map PNG sources in this order:

1. exact case-sensitive 205-path target;
2. one unique ASCII-case-fold match; then
3. only these finite aliases: root `Charm_*` to `Charms/Charm_*`; `HUD` to `Hud`; `DreamNail` to `Dreamnail`; `Voidspells` to `VoidSpells`; `DeathPt` to `Deathpt`; `Inventory/Godfinder_*` to `Inventory/GodFinder_*`; and `Inventory/ElegantKey` to `Inventory/ElegentKey`.

There is no fuzzy name/basename mapping, flattening, or last-wins behavior. Two sources resolving to one target reject that candidate. Root `orbicon` never maps to `SaveHud/soulOrbIcon`. Warn about but never extract `Swap`, `Cinematics`, `ReplaceAudio`, HP bars, arbitrary config/text, nested archives, alternates, or unknown files.

Optional explicit round-tripping GB18030 may render unflagged wrapper/candidate raw bytes in import-receipt/UI views only; it never changes canonical document bytes, manifest, ID, ordering, any hash, mapping, or path authority.

```text
candidateKey = SHA256(ASCII("HKS-CANDIDATE-V1\0") || archiveSha256Bytes || U32BE(rawCandidatePath.length) || rawCandidatePathBytes || U8(layoutCode))
```

`rawCandidatePathBytes` is the canonical raw candidate prefix (empty for root); `layoutCode` is fixed as `0=root`, `1=wrapper`, `2=multi-pack`, `3=full-install`. Normalize payload and compute candidateKey before choosing manifest ID. Under both locks, require unique `registry.packs[].candidateKey`; duplicate ownership is `REGISTRY_CORRUPT` and read-only, and resolve its owner first. If an owner exists, use its ID to generate canonical manifest/tree: exact owner candidateKey + normalizerVersion + generated tree returns that owner and its existing import receipt idempotently, without mutating it for a provider/archive display rename; different version/tree is `REIMPORT_CHANGED` and requires explicit **Replace** on that owner. If no owner exists, derive `local-` plus the first 58 lowercase candidateKey hex characters (64 total), return `ID_COLLISION` if that ordinary new-install ID already belongs to another candidateKey, otherwise install. Display name/path never implies identity or replacement.

Validate every mapped PNG, hash its bytes, and stage it at `assets/<digestBase32>`, where `digestBase32` is lowercase RFC 4648 base32 of the full SHA-256 without padding: exactly 52 characters and no extension. `assetRoot` is exactly `assets`; `textures[target]` is the 52-character filename. Equal destinations may be shared only for byte-identical content; mismatch rejects. Catalog-target mapping is deterministic and canonical JSON governs key order.

Manifest `name` comes only from the candidate leaf: a strict UTF-8 flagged leaf normalized to NFKC when it passes existing name rules, or safe printable ASCII unflagged raw leaf when it passes them. Root/invalid leaves use `Imported skin <first12 candidateKey hex>`. Author and attribution are exactly `Unknown` absent separately verified explicit metadata. Default output omits `license`, `source`, `homepage`, and `preview`; only a future explicit metadata operation may add separately verified, validated optional fields, never values inferred from filename/site/transport.

Each accepted candidate becomes a generated strict pack with canonical internal `skin.json` and `object.json`, all payload hashes, and `rotationEligible=false`; import changes no mode, selection, active visual receipt, or `SkinStamp`. Before registry publication, publish an immutable canonical import receipt containing normalizer version, candidateKey, archive SHA-256/name, raw candidate path and layout decision, aliases, warnings, optional advisory source/homepage, and signature status. V1 records only `UNVERIFIED_SOURCE`. Same input must produce byte-identical canonical manifest/object/import-receipt documents and hashes; shared goldens enforce this.

### 5.3 Generated documents v1
Each normalized candidate has exactly one generated root `skin.json`: UTF-8 without BOM, at most 65,536 bytes, one JSON object, no duplicate or unknown keys.

| Field | Required contract |
| --- | --- |
| `schemaVersion` | JSON integer `1` only. |
| `id` | New: `local-` + first 58 lowercase candidateKey hex characters; explicit replacement retains selected installed ID. All IDs match `[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?` in 1–64 ASCII. |
| `name`, `author` | 1–80 Unicode scalars, already NFKC; no control/bidi-control/surrogate, slash, backslash, or edge whitespace. |
| `contentSha256` | Exactly 64 lowercase hex digits; canonical payload identity below. |
| `games` | Exactly key `hollow-knight`. |
| game object | Exactly `gameVersion`, `catalogId`, `assetRoot`, `textures`; `assetRoot` is literal `assets`. |
| `gameVersion` / `catalogId` | Exactly `1.5.12620` / `hk-custom-knight-v3.5.0-205`. |
| `textures` | 1–205 exact case-sensitive catalog keys; each value is its 52-character digestBase32 filename. |

Only optional top-level fields are `license` (1–80 display characters), `source` and `homepage` (absolute HTTPS, at most 2,048 characters each), `attribution` (1–512), and `preview` (validated root-relative PNG). Metadata is advisory and does not assert identity, authenticity, or redistribution rights.

Canonical `import-receipt.json` is at most 8 MiB, sufficient for the defined worst case of 4,096 entries with 512-byte raw paths, 4,106 warnings, 205 aliases, and field overhead; its bounded maximum shape must fit. Never truncate: if canonical serialization somehow exceeds 8 MiB, reject before publication as `LIMIT_EXCEEDED`. Null/unknown fields reject.

| Import-receipt field | Exact v1 contract |
| --- | --- |
| `schemaVersion`; `normalizerVersion` | JSON integer `1`; literal `hkzip-v1`. |
| `candidateKey`; `archiveSha256` | Each exactly 64 lowercase hex. |
| `archiveName` | SAF display name after NFKC; valid only at 1–128 Unicode scalars with no control, bidi-control, surrogate, slash, backslash, NUL, or leading/trailing whitespace; otherwise exact fallback `archive-<first12 archiveSha256 hex>`. |
| `candidateRawPathHex`; `layoutCode`; `signatureStatus` | Lowercase even hex 0–1,024 chars; JSON integer 0–3; only `UNVERIFIED_SOURCE`. |
| `source?`; `homepage?` | Absolute HTTPS, at most 2,048 characters; absent by default. |
| `aliases` | At most 205 in central-entry order; object exactly `{sourceRawPathHex,target,rule}`, source lowercase even hex at most 1,024 chars, target exact catalog path, rule one of `ASCII_CASE_FOLD,ROOT_CHARM,HUD,DREAM_NAIL,VOID_SPELLS,DEATH_PT,GOD_FINDER,ELEGENT_KEY`. |
| `warnings` | At most 4,106 (4,096 one-per-entry rows plus at most 10 archive-level one-per-code rows); object exactly `{code,sourceRawPathHex}`, source lowercase even hex at most 1,024 chars and empty only for archive-level; closed code priority is `IGNORED_NESTED_ARCHIVE,IGNORED_SWAP,IGNORED_CINEMATICS,IGNORED_REPLACE_AUDIO,IGNORED_HP_BAR,IGNORED_CONFIG_OR_TEXT,IGNORED_ALTERNATE,IGNORED_PATH_ENCODING,IGNORED_EXTRA_METADATA,IGNORED_UNKNOWN`. |

Emit at most one warning per central-directory entry; if several apply, the first code in the fixed priority above wins, with no duplicate entry/code. Entry warnings are in central-directory order. Across a multi-candidate archive, assign each ignored entry to the longest chosen candidate prefix containing it; entries outside every chosen prefix attach only to the first unsigned-raw-prefix candidate, so each ignored entry occurs in exactly one import receipt. Archive-level warnings have empty source, follow all entry rows in fixed code order, occur at most once per code, and attach only to that first candidate.

`archiveName` participates in canonical import-receipt bytes/hash/provenance but never affects candidateKey, derived ID, payload/content, manifest, or object/tree identity. The same byte input plus the same canonical archiveName produces identical receipt bytes/hash. An ordinary renamed-provider reimport resolves the existing candidate owner and retains its prior import receipt without metadata mutation.

### 5.4 Normalized paths, tree, PNG, and bounds
All normalized relative paths use `/`, at most four segments and 240 UTF-8 bytes. Each 1–64-character ASCII segment matches `[A-Za-z0-9][A-Za-z0-9._-]*`. Reject empty, `.`/`..`, absolute, drive/UNC, backslash, percent-escaped, NUL, trailing-dot/space, or case-insensitive Windows-device segments. Texture payload path is exactly `assetRoot + "/" + value`; preview remains root-relative.

Reject escapes/traversal, symlinks, junctions, mounts, reparse points, hard-link aliases, sockets/FIFOs/devices, virtual or unknown nodes, unstable identity/size, and ASCII-case-fold collisions. Preview may not equal/case-fold-collide with a texture; no payload may collide with `skin.json`. Multiple targets may share one identical canonical texture path and are deduplicated as one file.

After normalization, traversal visits only exact prefixes of generated payloads. Every row is charged before classification; an extra or undeclared staged node is corruption and rejects the candidate. Filesystem opens use no-follow plus containment and pre/post identity checks; provider input is never reopened after the one quarantine copy. The staged private tree is rechecked.

| Import/tree limit | Maximum |
| --- | ---: |
| candidates per import / installed packs | 128 / 64 |
| texture mappings / regular files including manifest+preview | 205 / 207 |
| observed nodes / directories per candidate | 512 / 64 |
| provider rows across selected-folder listing | 1,024 |
| texture / preview / total declared payload bytes | 16 MiB / 4 MiB / 256 MiB |
| PNG width or height / decoded pixels | 8,192 / 33,554,432 |

Crossing a candidate bound returns `LIMIT_EXCEEDED`; valid siblings remain processable unless an archive-wide structural/security failure invalidates the container. A changed archive/repack is a new pack by default. **Replace** is available only when explicitly invoked on one selected installed target ID and confirmed. If incoming candidateKey is owned by another ID, reject `CANDIDATE_ALREADY_INSTALLED` and name that owner; if unowned or target-owned, this confirmed operation is the sole same-ID/new-candidateKey path. Under both locks it CASes expected generation + target ID + current tree/import-receipt digest and writes the generated manifest with that ID. It performs no live write and preserves mode, selection, eligibility, active visual object/import receipt, and `SkinStamp`; the changed-away old candidateKey becomes unowned. If the replaced object was active, its old object/import receipt remains retained and descriptor-pinned until a later successful visual transaction establishes another object. Stale CAS returns `REGISTRY_CONFLICT`.

Each PNG requires valid signature, first IHDR, bounded chunk lengths and CRCs, one IEND, dimensions in bounds, and no APNG chunks. Full decode occurs in prepare before live mutation.

### 5.5 Canonical identities
Store the generated manifest as RFC 8785 canonical JSON. To avoid a hash cycle, `contentSha256` excludes `skin.json`; sort unique canonical texture payload paths plus optional root-relative preview by unsigned UTF-8 bytes and hash:

```text
ASCII("HKS-PAYLOAD-V1\0")
for each path: U32BE(pathUtf8.length) || pathUtf8 ||
               U64BE(file.length) || SHA256(fileBytes)
```

`treeSha256` uses `HKS-TREE-V1\0` and identical framing over the completed canonical `skin.json` plus the same payload set. Recompute from quarantined/staged bytes; never trust archive metadata. Formatting-only manifest changes preserve identity; semantic or payload changes do not.

## 6. Private immutable storage and schemas
`ProfilePaths` contains all data under
`<files>/profiles/hollow-knight/skins/`; no build staging or SharedPreferences is
skin authority.

```text
objects/sha256/<2hex>/<treeSha256>/{object.json,pack/{skin.json,<declared-payloads>},.complete}
import-receipts/sha256/<2hex>/<importReceiptSha256>/{import-receipt.json,.complete}
staging/{import-<uuid>/{quarantine.bin,normalized/},registry-<uuid>/}
registry/{generations/rg-<20digit-sequence>-<uuid>/{registry.json,registry.sha256,
  .complete},current,previous,next}
sessions/{active,sequence,<descriptorId>/{descriptor.json,descriptor.sha256,.complete,
  lease/{states/ls-<20digit-transition>-<uuid>/{lease.json,lease.sha256,.complete},
  current,previous,next}}}
locks/{session.lock,registry.lock}
```

Canonical JSON rejects duplicate/unknown fields. Counts/lengths/sequences/stamps
are canonical unsigned-decimal strings; digests are lowercase hex; internal
operation/generation/descriptor/lease IDs are canonical lowercase UUIDs. Completed
objects, import receipts, and descriptors are immutable and have no production
mutation path.

| Canonical document | Exact logical fields |
| --- | --- |
| `object.json` | `schemaVersion, treeSha256, contentSha256, manifestSha256, fileCount, payloadBytes, files[]` where each file is `path,length,sha256`. |
| `import-receipt.json` | Exact v1 fields, bounds, ordering, and closed enums from section 5.3; `importReceiptSha256` hashes canonical bytes. |
| `registry.json` | `schemaVersion, generationId, sequence, parentGenerationId, operationId, writer, profileId, gameVersion, catalogId, catalogSha256, packs[], activation`. |
| registry pack | `id,name,author,candidateKey,treeSha256,contentSha256,importReceiptSha256,rotationEligible`; records sorted by ASCII ID. |
| activation | `mode,selectedPackId,active,skinStamp,rotationInterlock`; active is `VANILLA` or exact `PACK` ID/tree/content/import-receipt digest. |
| descriptor | `schemaVersion, descriptorId, sessionSequence, leaseId, leaseToken, profileId, gameVersion, catalogId, catalogSha256, registryGenerationId, registrySha256, mode, selectedPackId, active, skinStamp, rotationInterlock, packs[]`. |
| descriptor pack/object | Pack: `id,name,orderKeyHex,rotationEligible,currentObject,retainedActiveObject?`; object: `treeSha256,contentSha256,importReceiptSha256,objectRelativePath,textures[]`; texture: `target,source,sourceSha256`. |
| lease state | `schemaVersion, leaseId, leaseTokenSha256, profileId, sessionSequence, transitionSequence, transitionId, parentTransitionId, state, descriptorId, descriptorSha256, registryGenerationId, registrySha256, launcherOwner, gameOwner, closeReason`. |

`registry.packs[].candidateKey` is unique; duplicate ownership is registry corruption and forces read-only recovery. `object.json.files` lists canonical `skin.json` plus each unique payload once in
unsigned UTF-8 path order; `fileCount` includes the manifest, `payloadBytes`
excludes it, and `manifestSha256` hashes canonical manifest bytes. Import-receipt
arrays are in source-entry order and each alias records exact source bytes and
target. Readers fully reparse/re-hash all bytes, manifest, import receipt, rows,
counts, payload/tree framing, shard, and no-alias tree; `.complete` alone is never
acceptance.

## 7. Durable publication and registry recovery
One primitive publishes object, import receipt, generation, descriptor, and lease-state directories:

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

Recovery validates bounded `current/next/previous`, unique candidateKey ownership,
catalog/digest/profile/interlock, and referenced objects/import receipts; it never chooses mtime. The normal history window is the
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

Separately pin every generation/object/import receipt named by `current/previous/next`, each
active or retained session descriptor, an active visual receipt, or a non-clear interlock,
even outside the normal window. Explicit pins are non-transitive across parent
history: they retain each named generation and its referenced objects/import receipts, never an
ancestor solely because of a parent edge. Cleanup runs
only without an active/unknown lease, revalidates containment/no-alias, and syncs
parents; it never deletes a pin or its object/import-receipt dependencies. Unpinned complete
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
include mode, activation/rotation state, plans, active visual receipts, and commit results,
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
6. only then publish the active visual receipt/stamp in memory, trigger companion clone
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
active visual receipt/stamp, and prohibits further writes until next-process recovery.

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
  binding; fallback is recovered baseline or durable prior active visual receipt.
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
**Import file**/**Import folder** SAF guidance, archive/candidate normalization
results and warnings, installed name/author/ID/hash/source status, independent
**Select**, **Include in rotation**, and selected-ID **Replace** actions, mode/active/
eligible order, interlock/error, and explicit replacement confirmation. Persistence
is immediate.
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

- one-copy quarantine/digest and magic over hints; Store and Deflate fixtures each
  cover bit 3 clear/descriptor absent and bit 3 set/12-/16-byte descriptor present,
  both all-zero/full-central local tuples, and partial-zero/partial-present/mismatch/sentinel rejection;
  local/central/stream agreement and every descriptor/ZIP64/multi-disk edge;
- raw central-path grammar/authority, exact and flag-domain collisions, implicit
  directories, bounded extra parsing/`0x7075`, UTF-8/raw-ASCII mapping, and
  display-only GB18030; bounded immediate files regardless extension/MIME, archive
  bounds, and checked per-entry/aggregate ratio cases;
- full-install precedence, root/wrapper ambiguity, unique-wrapper and one/many
  multi-pack classification, `NO_CANDIDATE`, layout codes/raw-prefix order, exact/case/finite
  aliases, source-target collision, extras/root-`orbicon`, and no recursive/fuzzy/
  flatten/last-wins discovery;
- payload-before-ID candidateKey, unique owner/corruption, owner-ID reimport and
  renamed-provider idempotence, `REIMPORT_CHANGED`, ordinary `ID_COLLISION`,
  `CANDIDATE_ALREADY_INSTALLED`, and sole CAS-qualified explicit replacement;
- deterministic name/`assets/<digestBase32>` and byte-identical manifest/object/
  import-receipt plus canonical/fallback archiveName, renamed-reimport, and 4,106-warning maximum-shape goldens; exact receipt scalar/array/size/order/
  closed-enum bounds, one-warning precedence, cross-candidate assignment, and no
  duplicate/truncation; `UNVERIFIED_SOURCE`, inert activation defaults,
  path/tree/PNG/catalog hashes and dedup;
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

Archive fixtures reproduce corpus layouts/security edges with tiny generated PNGs; no third-party asset is committed. After synthetic gates pass, exact host corpus replay copies the ten digest-verified archives from read-only `G:\Modding\Downloads\Hollow Knight` through normal quarantine, verifies ten ZIP candidate mappings and two unsupported RAR results, and never modifies/extracts into the source tree. `HOST-COMPLETE` is assigned only when every synthetic gate, this exact replay, full host reconciliation, and both exact compiles pass from one checkpoint.

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

Only after `HOST-COMPLETE` and explicit approval, one future automated combined
`H34-DEVICE-001` gate uses one exact-product candidate and an isolated disposable
test profile; it never uses or alters an existing user save. Before
`GameProcessStartup` lease acquisition, digest-verified copies of the corpus are
staged from read-only `G:` into an isolated test document provider and complete
normal SAF/quarantine import/replay with synthetic invalid archives. Then the gate
launches only the first level, disables player input, and permits no movement,
combat, or progression. It proves import receipts/mappings, ID idempotence/
collision/explicit replacement, exact `OFF -> ON -> ROTATE -> OFF`, vanilla
retention, 0/1/2/3 rotation, and selection/eligibility. Harness-driven normal/
dream/Steel Soul and scene/bench death signals cover debounce, three-update
stability, cancel/rebinding/stale tokens without gameplay. The same gate covers all
205 families/default reset, memory/dedup/retirement telemetry, stamp clone refresh,
forced apply/commit/rollback failures and persistent interlock, lease claim/second
launch/crash/close, relaunch/display loss, H2/H3 regression, and no existing-save
or Silksong delta. No Android/ADB/emulator/signing/release or micro live-check loop
belongs to host implementation.

## 15. Closure ledger
| ID | Closure condition | Current truth |
| --- | --- | --- |
| `HKSKIN-HOST-001` | One checkpoint passes all synthetic quarantine/normalizer/manifest/object/import-receipt/registry/session/UI/runtime goldens, exact digest-verified host corpus replay, full host reconciliation, and both exact compiles. | `SPECIFIED / NOT IMPLEMENTED` |
| `HKSKIN-CATALOG-001` | Companion file remains 205 unique paths in 162+9+34 order with exact digest; every row has one finite typed descriptor; Kotlin/C#/Python agree. | `PUBLIC EVIDENCE + FILE WRITTEN / TYPED DESCRIPTORS NOT IMPLEMENTED` |
| `HKSKIN-ADAPTER-001` | Exact compile plus approved runtime gate prove all 205 `1.5.12620` bindings, missing-row vanilla reset, rebinding, postcondition, reverse rollback, and failed-rollback interlock. | `ADAPTER-BLOCKED` |
| `HKSKIN-LIFECYCLE-001` | Exact compile plus approved runtime gate prove normal/dream/Steel Soul/scene/bench death, debounce, three-update stability, cancel/stale/rebind behavior. | `LIFECYCLE-BLOCKED` |
| `HKSKIN-STAMP-001` | Host transactions and exact compile prove `long HkStageHooks.SkinStamp` only after durable visual commit; approved gate proves companion clones refresh after success and retain prior content after failure. | `SPECIFIED / NOT IMPLEMENTED` |
| `HKSKIN-RAR-001` | A separately approved bounded RAR4/RAR5 parser path passes equivalent quarantine, traversal/link/collision/bomb/malformed fixtures and the two corpus expectations without weakening ZIP/object rules. | `DEFERRED / NONBLOCKING; CORE RETURNS UNSUPPORTED` |
| `HKSKIN-BROWSER-001` | A separately approved isolated Android browser proves same-origin `https://hkskins.art/` rendering, no JS bridge/native/file authority, and system handoff for every external host/download back through SAF import; it adds no cross-domain manager. | `DEFERRED / NONBLOCKING; NOT IMPLEMENTED` |
| `H34-DEVICE-001` | After `HOST-COMPLETE`, the one combined automated disposable-profile first-level/input-disabled scenario in section 14 passes on an approved exact-product candidate, including pre-startup isolated-provider corpus replay, with no movement/combat/progression or existing-save/Silksong delta. | `DEVICE-PENDING / NOT AUTHORIZED` |

Documentation/public evidence closes none of these runtime rows.

## 16. Host staging
Use five coherent host batches, each with its complete host checks—not live micro-loops:

1. quarantine/ZIP parser, finite normalization, canonical manifest/object/import
   receipt, immutable publication/recovery;
2. registry activation, durable lease/descriptor, launcher UI, isolation/quota;
3. fixed catalog plus Unity-free transaction/rotation/lifecycle cores and goldens;
4. typed adapter/runtime/lifecycle/presenter/stamp integration and both compiles;
5. all synthetic/fault/policy gates, exact read-only corpus replay, then one full
   host reconciliation and truthful ledger from that checkpoint.

RAR and browser work are outside these batches until their ledger rows are
separately approved. A combined H3/H4 device batch starts only after
`HOST-COMPLETE` and explicit approval. That status keeps adapter, lifecycle, and
device blockers open until their stronger closure conditions pass.
