# Hollow Knight Skin Corpus Evidence

**Status:** Read-only observation record; not runtime authority or implementation proof.

This document records the bounded evidence used to adapt H4 to real, non-normalized CustomKnight archives. Observations and design consequences are deliberately separated. The source corpus stays read-only, and this repository contains no third-party skin assets, previews, or link lists.

## 1. Observed public sources

### `hkskins.art`

- The site is Gatsby `5.16.1`; its public source is [Tadeas-Jun/hkskins](https://github.com/Tadeas-Jun/hkskins) at commit `12d0b519c9c6225f5a91d21cd9440ebe82f1ee12`.
- The repository has no license, backend, stable supported API, stable public IDs, or published archive hashes, sizes, signatures, or licenses.
- Undocumented page data contained 609 records: 547 Hollow Knight and 62 Silksong. Its observed schema was `subDir`, `imagePath`, and `metadata{name,source,author,game,type,desc,dateAdded}`.
- `source` values point to arbitrary third-party locations including Google Drive, Discord, and Nexus Mods. The page data is discovery evidence, not an authenticated package index.

### Offline list export

- Local read-only reference: `G:\Modding\Downloads\Hollow Knight\ColetteMSLP's Hollow Knight Skin List.md`.
- Exact size: 24,787,165 bytes.
- SHA-256: `291c30d1173127c031e6386ba6041d4a36453999a65766e18042543134bd092e`.
- The export contains 806 Markdown links and 655 unique link targets, but no stable IDs, archive hashes, licenses, or machine-stable schema. It is mutable, unpublished prose derived from a Google Doc.

Neither source is parsed at runtime. H4 does not use either as package authority and does not redistribute their metadata or previews.

## 2. Observed local archive corpus

Read-only root: `G:\Modding\Downloads\Hollow Knight`.

The corpus contains 8 ZIP and 2 RAR archives. None contains `skin.json`, and no standalone license was observed. No inspected archive contained traversal, absolute paths, symlink entries, NFKC collisions, or case-fold collisions. Observed maxima were depth 9, 439 entries, expansion ratio 17.8:1, and approximately 200 MiB uncompressed. These are observations, not acceptance limits.

Candidate coverage is measured against the fixed 205-path catalog. Ignored extras are not counted as mappings.

| Archive | SHA-256 | Observed normalization result |
| --- | --- | --- |
| `Daughter of Hallownest 1.2.zip` | `aace82ede0f928e6fa86de2875e11b3c26a1e491289483356d62ca9e8f12c243` | 1 candidate, 46/205; exact full-install salvage path under `hollow_knight_Data/Managed/Mods/CustomKnight`; unrelated DLL, game, HP-bar, and nested-archive extras. |
| `Grimm_Knight by 复印纸-20260829T012747Z-1-001.zip` | `11f88e1d946f070cbb43b15df6192fcd64711b5098686fd45102eca1cf499a17` | 1 candidate, 9/205; wrapper, case alias, and extras. |
| `Hylian Knight-20260829T014209Z-1-001.zip` | `53fd262462b4b4444827c3674d4248a44df2df33a15b810118f9dd61bfa67dc5` | 1 candidate, 43/205; `Swap`, cinematics, and other extras. |
| `IllyiaKnight-20260829T014227Z-1-001.zip` | `ff5dc79a687483473507b06964987f46c45c52e2b68f631d4a4b396dc8e5585a` | 1 candidate, 24/205; case variants and cinematics. |
| `Isaac Knight Skin-20260829T014234Z-1-001.zip` | `8c2f319ee09a0527ea0984b62a07ce5bcefb00b34110dc691e244dca312cbcca` | 1 core candidate, 138/205; double wrapper, file extensions, HP-bar extras, and the vetted `Inventory/ElegantKey.png` alias to `Inventory/ElegentKey.png`. |
| `Little Radiance (by HBKit)-20260829T012546Z-1-001.zip` | `e2ffe325a1d9d54ae187165921d9ab73eda4a4287a580fb9569b923aaef3998b` | 1 candidate, 65/205; `Swap`, HP-bar, and other extras. |
| `The Hollow Knight Skin Pack-20260829T021826Z-1-001.zip` | `931dace39b1dfcc801fbbfdcbb5a96bb164ca2859bbe08912cd56b6b31404b20` | 3 independent candidates mapping 65/205, 74/205, and 78/205; wrapper and alternates. |
| `死亡细胞.zip` | `06497cffa9ac156a12e78ae7ee9fe5a1cf733b243079498b98c021223ead1e07` | 1 candidate, 14/205; unflagged GBK names, wrapper, readme, and icon. |
| `Biboo V0.6.rar` | `d4024d6b0e0be9122f5eac976340c4b395efc1c8414b961a8e5adee3618cbe10` | RAR4, 19/205; unsupported by the H4 core. |
| `The novelist (1).rar` | `bee28d2193757b444c0673c6ce530413ac939a61abc95defa6f54b874899744f` | RAR5, 113/205; unsupported by the H4 core. |

The eight ZIPs therefore expose ten independent candidates. Their mapped counts are `46, 9, 43, 24, 138, 65, 65, 74, 78, 14`.

## 3. Designed behavior derived from the evidence

These are requirements, not claims about current implementation:

- Local ZIP normalization is the H4 core path. One selected file or every bounded immediate regular file in a selected folder is copied once and magic-classified regardless of extension/MIME; RAR returns an explicit unsupported result until its deferred ledger item closes.
- Finite layout recognition and explicit aliases replace fuzzy recursive discovery. Unknown files and unsupported feature trees are warned about but never extracted.
- Deterministic candidateKey IDs, canonical `assets/<digestBase32>` payloads, strict internal manifest/object bytes, and immutable import receipts replace archive naming conventions. New packs are rotation-ineligible and cannot alter mode, selection, active visual state, or stamp.
- `hkskins.art` and the offline list remain human reference material. Direct cross-domain downloading is outside core H4.
- Host fixtures reproduce these layouts and edge cases with tiny generated PNGs; third-party assets are never committed.
- After synthetic host gates pass, exact host corpus replay copies and verifies these archives without modifying or extracting into the read-only source tree. `HOST-COMPLETE` additionally requires full reconciliation and exact compiles from that checkpoint.
- Only after `HOST-COMPLETE`, future Android validation stages digest-verified copies in an isolated test document provider and completes SAF import before game startup. One approved automated gate then uses an isolated disposable profile, first level, and disabled input, with no movement, combat, progression, existing-user-save use/change, or Silksong delta.

The normative security, mapping, state, and verification contracts are in [`../superpowers/specs/2026-09-02-hollow-knight-skins-design.md`](../superpowers/specs/2026-09-02-hollow-knight-skins-design.md).
