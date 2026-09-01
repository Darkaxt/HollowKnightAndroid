# Hollow Knight Mods managed API audit

## Assembly provenance

- Depot: local Hollow Knight `1.5.12620` managed set at `D:\Temp\dualsouls-hk-12620\Hollow Knight\hollow_knight_Data\Managed`
- Audited assembly: `Assembly-CSharp.dll`
- Assembly size: `3,597,312` bytes
- SHA-256: `5C84B8E59DD669C48DB1FC426541D0D96643F59D35A72534B21FEAD7C96A3086`
- Inspection tool: installed `ilspycmd 11.0.0.9375` / `ICSharpCode.Decompiler 11.0.0.9375`
- Bounded types inspected from that exact assembly: `PlayerData`, `HeroController`, `HealthManager`, `GameManager`, and `GameMap`
- Capability authority: [hollow-knight-mod-capabilities.md](hollow-knight-mod-capabilities.md)

## Observed public member signatures

The declarations below are signatures only. No decompiled method body is reproduced.

### `HeroController`

Damage and lifecycle:

- `public DamageMode damageMode;`
- `public bool takeNoDamage;`
- `public static HeroController instance { get; }`
- `public event TakeDamageEvent OnTakenDamage;`
- `public event HeroDeathEvent OnDeath;`
- `public void SetDamageMode(int invincibilityType);`
- `public void SetDamageModeFSM(int invincibilityType);`
- `public void SetDamageMode(DamageMode newDamageMode);`
- `public void TakeDamage(GameObject go, CollisionSide damageSide, int damageAmount, int hazardType);`

Movement fields relevant to a speed capability:

- `public float RUN_SPEED;`
- `public float RUN_SPEED_CH;`
- `public float RUN_SPEED_CH_COMBO;`
- `public float WALK_SPEED;`
- `public float UNDERWATER_SPEED;`
- `public float JUMP_SPEED;`
- `public float DASH_SPEED;`
- `public float BACK_DASH_SPEED;`
- `public float SHADOW_DASH_SPEED;`
- `public float SUPER_DASH_SPEED;`
- `public float MAX_FALL_VELOCITY;`
- `public float move_input;`
- `public Vector2 current_velocity;`

Soul, Geo, inventory, and respawn:

- `public void AddMPCharge(int amount);`
- `public void AddMPChargeSpa(int amount);`
- `public bool TryAddMPChargeSpa(int amount);`
- `public void SetMPCharge(int amount);`
- `public void TakeMP(int amount);`
- `public void TakeReserveMP(int amount);`
- `public void AddGeo(int amount);`
- `public void AddGeoQuietly(int amount);`
- `public void AddGeoToCounter(int amount);`
- `public void TakeGeo(int amount);`
- `public bool CanOpenInventory();`
- `public void SetBenchRespawn(string spawnMarker, string sceneName, int spawnType, bool facingRight);`

### `PlayerData`

Health:

- `public int health;`
- `public int maxHealth;`
- `public int maxHealthBase;`
- `public int healthBlue;`
- `public int joniHealthBlue;`
- `public int prevHealth;`
- `public int CurrentMaxHealth { get; }`
- `public void AddHealth(int amount);`
- `public void TakeHealth(int amount);`
- `public void MaxHealth();`
- `public void AddToMaxHealth(int amount);`
- `public bool WouldDie(int damage);`

Soul:

- `public int maxMP;`
- `public int MPCharge;`
- `public int MPReserve;`
- `public int MPReserveMax;`
- `public int MPReserveCap;`
- `public bool soulLimited;`
- `public int focusMP_amount;`
- `public bool AddMPCharge(int amount);`
- `public void TakeMP(int amount);`
- `public void TakeReserveMP(int amount);`
- `public void ClearMP();`
- `public void AddToMaxMPReserve(int amount);`
- `public void StartSoulLimiter();`
- `public void EndSoulLimiter();`

Geo and death pool:

- `public int geo;`
- `public int geoPool;`
- `public string shadeScene;`
- `public string shadeMapZone;`
- `public int shadeHealth;`
- `public int shadeMP;`
- `public void AddGeo(int amount);`
- `public void TakeGeo(int amount);`

Nail progression:

- `public int nailDamage;`
- `public int nailRange;`
- `public int beamDamage;`
- `public int nailSmithUpgrades;`

Charm and notch state:

- `public int charmSlots;`
- `public int charmSlotsFilled;`
- `public List<int> equippedCharms;`
- `public bool canOvercharm;`
- `public bool overcharmed;`
- `public int charmCost_1;` through `public int charmCost_40;`
- `public bool equippedCharm_1;` through `public bool equippedCharm_40;`
- `public void EquipCharm(int charmNum);`
- `public void UnequipCharm(int charmNum);`
- `public void CalculateNotchesUsed();`

Journal, map, bench, and boss state:

- `public bool hasJournal;`
- `public int lastJournalItem;`
- Per-entry `public bool killed...;`, `public int kills...;`, and `public bool newData...;` fields
- `public void IncrementInt(string intName);`
- `public void IntAdd(string intName, int amount);`
- `public void CountJournalEntries();`
- `public List<string> scenesVisited;`
- `public List<string> scenesMapped;`
- `public List<string> scenesEncounteredBench;`
- `public bool hasMap;`
- `public bool mapAllRooms;`
- Public area flags from `mapDirtmouth` through `mapAbyss`
- `public bool UpdateGameMap();`
- `public string respawnScene;`
- `public string respawnMarkerName;`
- `public int respawnType;`
- `public bool respawnFacingRight;`
- `public void SetBenchRespawn(RespawnMarker spawnMarker, string sceneName, int spawnType);`
- `public void SetBenchRespawn(string spawnMarker, string sceneName, bool facingRight);`
- `public void SetBenchRespawn(string spawnMarker, string sceneName, int spawnType, bool facingRight);`
- `public string bossReturnEntryGate;`
- `public List<string> unlockedBossScenes;`
- `public static PlayerData instance { get; set; }`
- `public void Reset();`

### `HealthManager`

- `public int hp;`
- `public bool isDead;`
- `public bool damageOverride;`
- `public bool IsInvincible { get; set; }`
- `public event DeathEvent OnDeath;`
- `public void Hit(HitInstance hitInstance);`
- `public void SubtractHealth(int damageDealt);`
- `public void ApplyExtraDamage(int damageAmount);`
- `public void Die(float? attackDirection, AttackTypes attackType, bool ignoreEvasion);`
- `public void SetIsDead(bool set);`
- `public void SetDamageOverride(bool set);`

### `GameManager`

Scene, death, and transition:

- `public class SceneLoadInfo`
- `public string sceneName;`
- `public string nextSceneName;`
- `public string entryGateName;`
- `public bool RespawningHero { get; set; }`
- `public bool IsInSceneTransition { get; private set; }`
- `public event SceneTransitionFinishEvent OnFinishedSceneTransition;`
- `public static event SceneTransitionBeganDelegate SceneTransitionBegan;`
- `public void BeginSceneTransition(SceneLoadInfo info);`
- `public void ChangeToScene(string targetScene, string entryGateName, float pauseBeforeEnter);`
- `public IEnumerator PlayerDead(float waitTime);`
- `public IEnumerator PlayerDeadFromHazard(float waitTime);`
- `public void ReadyForRespawn(bool isFirstLevelForPlayer);`

Save slots and map state:

- `public PlayerData playerData;`
- `public int profileID;`
- `public const int NoSaveSlotID = -1;`
- `public void SaveLevelState();`
- `public void SaveGame();`
- `public void SaveGame(Action<bool> callback);`
- `public void LoadGameFromUI(int saveSlot);`
- `public void LoadGame(int saveSlot, Action<bool> callback);`
- `public void ClearSaveFile(int saveSlot, Action<bool> callback);`
- `public void GetSaveStatsForSlot(int saveSlot, Action<SaveStats> callback);`
- `public bool UpdateGameMap();`
- `public void AddToScenesVisited(string scene);`
- `public void AddToBenchList();`

### `GameMap`

- `public GameObject currentScene;`
- `public Vector3 currentScenePos;`
- `public bool displayNextArea;`
- `public void LevelReady();`
- `public void SetupMap(bool pinsOnly = false);`
- `public void WorldMap();`
- Public `QuickMap...()` methods for the stock map areas
- `public void PositionDreamGateMarker();`
- `public void PositionCompass(bool posShade);`
- `public void SetupMapMarkers();`

## Cross-cutting semantic finding

Public symbol presence proves only that the exact assembly exposes a callable member with the observed name and type. It does not prove:

- **Baseline ownership:** progression, charms, scripts, boss bindings, or scene initialization may legitimately recompute the same value after capture.
- **Legal-state behavior:** a public field or mutator may bypass inventory FSM actions, overcharm rules, transition guards, UI events, or other invariants.
- **Scene replacement:** a public member on one `HeroController`, `HealthManager`, or map object does not prove reacquisition and exact restoration after object replacement or pooling.
- **Event interception:** a public consumer such as `Hit`, `TakeDamage`, `AddGeo`, or `SetMPCharge` is not proof of a typed interception point covering every producer and attack/reward source.
- **Semantic parity:** stock no-damage modes are not evidence of PreventDeath behavior, and resource setters are not evidence of an unlimited-resource policy.
- **Save rollback:** public `PlayerData` fields and asynchronous save/load entry points do not provide transactional snapshots, failure rollback, or proof that master-off restores every persisted consequence.

These gaps prevent promotion even where names look directly relevant.

## Per-capability semantic findings and promotion decisions

| ID | Observed public signatures/symbols | Semantic finding | Promotion decision |
| --- | --- | --- | --- |
| HKMOD-001 | `HeroController.damageMode`; three `SetDamageMode...` signatures; `HeroController.TakeDamage(...)`; `PlayerData.health`, `TakeHealth(...)`, `WouldDie(...)`; hero damage/death events | The damage-mode overloads do not establish one shared baseline contract, and symbol presence does not supply PreventDeath semantics across ordinary damage, hazards, death, respawn, scene replacement, and saving | DEFERRED per ledger |
| HKMOD-002 | `PlayerData.nailDamage`, `nailRange`, `beamDamage`, `nailSmithUpgrades` | Mutable progression fields exist, but no audited public member owns upgrade-aware nail recomputation or proves exact reset/save rollback | DEFERRED per ledger |
| HKMOD-003 | `HealthManager.Hit(HitInstance)`, `SubtractHealth(int)`, `ApplyExtraDamage(int)`, `Die(...)`, `hp`, `isDead` | Callable damage consumers are not an enemy-only interception contract and do not prove exclusions for bosses, scripted health, special deaths, or non-enemy targets | DEFERRED per ledger |
| HKMOD-004 | Public run/walk/dash fields; `HeroController.instance`, `move_input`, `current_velocity` | Instance tuning fields expose values but not owner-safe baseline capture, hero reacquisition, scene maintenance, or transition/death restoration | DEFERRED per ledger |
| HKMOD-005 | `PlayerData.MPCharge`, reserve/cap fields and Soul methods; `HeroController.AddMPCharge(...)`, `SetMPCharge(int)`, and drain methods | Add/set/drain APIs do not define live ownership across focus, spells, reserve spill, boss bindings, death, scene replacement, and save rollback | DEFERRED per ledger |
| HKMOD-006 | `charmCost_1` through `_40`; equipped fields/list; `EquipCharm`, `UnequipCharm`, `CalculateNotchesUsed` | Public costs are broad mutable save state; no complete baseline snapshot or equip/reload/reset lifecycle contract is proven | DEFERRED per ledger |
| HKMOD-007 | `charmSlots`, `charmSlotsFilled`, `canOvercharm`, `overcharmed`, equipped fields/list, notch calculation | These members do not prove legal overcharm transitions, safe unequip, save lifecycle behavior, or exact restoration of every related invariant | DEFERRED per ledger |
| HKMOD-008 | `HeroController.CanOpenInventory()`; `PlayerData.atBench`; public charm equip/list members | Inventory openness and list mutation are not a safe managed equip action and do not prove bench legality, FSM parity, scene behavior, or save rollback | DEFERRED per ledger |
| HKMOD-009 | `PlayerData.AddGeo(int)`, `TakeGeo(int)`, `geo`; HeroController Geo methods | Balance methods do not intercept or classify every pickup and reward producer, so multiplying only newly awarded Geo without double counting is unproved | DEFERRED per ledger |
| HKMOD-010 | `geoPool`, Shade fields, Geo methods; `GameManager.PlayerDead(float)` and `ReadyForRespawn(bool)` | Death exposes several persisted and scene-transition states, but no seam owns Shade creation, death-pool transfer, duplicate-award prevention, and rollback as one transaction | DEFERRED per ledger |
| HKMOD-011 | Journal killed/kills/new-data fields; `IncrementInt`, `IntAdd`, `CountJournalEntries` | Generic field writes do not prove the per-entry completion event sequence, idempotence, reload behavior, or rollback | DEFERRED per ledger |
| HKMOD-012 | `scenesVisited`, `scenesMapped`, map flags, `UpdateGameMap`; `GameMap.SetupMap(bool)` and area display methods | The exposed map state includes persisted progression; no audited member provides a bounded current-area-only reveal with scene/save rollback | DEFERRED per ledger |
| HKMOD-013 | `HealthManager.hp`, `isDead`, `OnDeath`; no audited public spawned/pooled-enemy lifecycle contract | Per-instance health and death access does not establish discovery, pooling reset, renderer ownership, boss policy, or teardown | DEFERRED per ledger |
| HKMOD-014 | `HealthManager.Hit(HitInstance)`, `SubtractHealth(int)`, `ApplyExtraDamage(int)`; only a public death event | Multiple public damage entry points exist, but no authoritative typed dealt-damage event covers all attack sources or supplies pooled UI teardown | DEFERRED per ledger |
| HKMOD-015 | `GameManager.BeginSceneTransition`, death/respawn methods, save methods; boss-return fields | General transitions and saves do not define a boss-retry checkpoint, reset semantics, or failure-safe save rollback | DEFERRED per ledger |
| HKMOD-016 | GameMap marker/display members; no relevant public secret identity or range member in the five audited types | Map presentation members do not identify secrets or prove bounded discovery with zero progression/save delta | DEFERRED per ledger |
| HKMOD-017 | Bench respawn overloads, respawn fields, `scenesEncounteredBench`, `BeginSceneTransition(SceneLoadInfo)` | Recorded scene names and general transitions do not validate an exact legal destination or guarantee transition-failure and save rollback | DEFERRED per ledger |
| HKMOD-018 | Public save/load/clear-slot methods, `profileID`, `PlayerData.instance`, and `Reset()` | Stock slot APIs do not provide a separate versioned, checksummed, transactional state format or atomic restore/failure rollback | DEFERRED per ledger |

## H3 promotion decision

Only the two fork-owned presentation capabilities are promoted in this H3 slice:

| Capability | Typed public boundary | Decision |
| --- | --- | --- |
| Companion backdrop | `void IHollowKnightTweakApi.SetCompanionBackdropBlack(bool black);` | AVAILABLE through the fork-owned presentation adapter |
| Lifeblood flash | `void IHollowKnightTweakApi.SetLifebloodFlash(HollowKnightFlashMode mode);` | AVAILABLE through the fork-owned presentation adapter |

Their promotion is based on the fork-owned reversible presentation boundary, not on stock gameplay symbol presence. All `HKMOD-001` through `HKMOD-018` gameplay, progression, world, economy, and state rows remain DEFERRED despite the observed symbols and are governed by the capability ledger's final managed-rewrite acceptance conditions.
