#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GlobalEnums;
using UnityEngine;

namespace DualSouls.Mods.Silksong
{
    static class SilksongGameplayFeatures
    {
        const int MaximumStateBytes = 4 * 1024 * 1024;
        const float FastTransitionScale = 2.5f;
        const int MaximumBenches = 256;

        static bool fastTimeOwned;
        static float fastTimeBaseline = 1f;
        static PlayerData owner;
        static readonly HashSet<string> mappedScenesOwned = new HashSet<string>(StringComparer.Ordinal);
        static readonly List<string> mappedScenesUnderAuthoritativeUpdate = new List<string>();
        static readonly Dictionary<string, bool> mapFlagsOwned = new Dictionary<string, bool>(StringComparer.Ordinal);
        static readonly Dictionary<string, CrestBaseline> crestBaselines = new Dictionary<string, CrestBaseline>(StringComparer.Ordinal);
        static bool extraSlotsCaptured;
        static bool extraBlueBaseline;
        static bool extraYellowBaseline;
        static readonly Dictionary<HealthManager, EnemyVisual> enemies = new Dictionary<HealthManager, EnemyVisual>();
        static readonly List<HealthManager> deadEnemies = new List<HealthManager>();
        static readonly List<FloatingNumber> numbers = new List<FloatingNumber>();
        static readonly List<Transform> radarMarks = new List<Transform>();
        static Texture2D pixelTexture;
        static Sprite pixelSprite;
        static Font numberFont;
        static float nextEnemyScan;
        static float nextRosaryScan;
        static string radarScene;
        static float radarPhase;
        static string bossScene;
        static string inspectedBossScene;
        static bool bossDeathArmed;
        static float nextBossInspection;
        static int bossProfile = -1;
        static Action stateTransferPreparation;
        static bool lastAtBench;
        static int benchProfile = -1;
        static readonly Dictionary<string, BenchRecord> benches = new Dictionary<string, BenchRecord>(StringComparer.Ordinal);
        static bool benchRouteRequested;

        [Serializable]
        sealed class StateEnvelope
        {
            public int version = 1;
            public int profileId;
            public string scene;
            public float x;
            public float y;
            public float z;
            public string saveJson;
        }

        sealed class StateSceneLoadInfo : GameManager.SceneLoadInfo
        {
            public Vector3 Position;
            public override void NotifyFinished()
            {
                base.NotifyFinished();
                HeroController hero = HeroController.instance;
                if (hero != null) hero.transform.position = Position;
            }
        }

        sealed class CrestBaseline
        {
            public int Count;
            public bool[] Unlocked;
        }

        sealed class EnemyVisual
        {
            public int Maximum;
            public int Last;
            public Transform Root;
            public Transform Fill;
        }

        sealed class FloatingNumber
        {
            public Transform Transform;
            public TextMesh Text;
            public float Until;
        }

        [Serializable]
        public sealed class BenchRecord
        {
            public string scene;
            public string marker;
            public int type;
        }

        [Serializable]
        sealed class BenchEnvelope
        {
            public int profileId;
            public List<BenchRecord> records = new List<BenchRecord>();
        }

        public static void SetStateTransferPreparation(Action prepare) { stateTransferPreparation = prepare; }
        public static void RestorePlayerOwnedForStateTransfer() { RestorePlayerOwned(false); }

        public static void Tick(bool fastTransitions, bool autoMap, bool innateCompass,
            bool secretRadar, bool healthBars, bool damageNumbers, bool bossRetry,
            bool unlimitedToolSlots, bool rosaryMagnet, bool keepRosariesOnDeath)
        {
            GameManager game = GameManager.SilentInstance;
            PlayerData player = game != null ? game.playerData : null;
            if (player == null) return;
            if (!ReferenceEquals(owner, player))
            {
                RestorePlayerOwned();
                ResetBossRuntime();
                owner = player;
                EnsureBenches(player.profileID);
            }

            MaintainFastTransitions(game, fastTransitions);
            MaintainAutoMap(game, player, autoMap);
            SilksongGameplayHooks.InnateCompassEnabled = innateCompass;
            MaintainToolSlots(player, unlimitedToolSlots);
            MaintainRosaryMagnet(rosaryMagnet);
            MaintainKeepRosaries(player, keepRosariesOnDeath);
            MaintainCombatOverlays(healthBars, damageNumbers);
            MaintainSecretRadar(game, secretRadar);
            MaintainBossRetry(game, player, bossRetry);
            RecordBench(player);
        }

        public static void RestoreAll()
        {
            RestoreFastTransitions();
            RestorePlayerOwned();
            RemoveRadar();
            RemoveCombatOverlays();
            ReleaseVisualResources();
            ResetBossRuntime();
            benchRouteRequested = false;
            SilksongGameplayHooks.Reset();
        }

        public static void SaveState(int slot)
        {
            ValidateSlot(slot);
            GameManager game = RequireGame();
            HeroController hero = HeroController.instance;
            if (hero == null) throw new InvalidOperationException("Hornet is not ready for a save state.");
            WriteStateSidecar(StatePath(slot, game.playerData.profileID), CaptureStateJson(game, hero));
        }

        public static void LoadState(int slot)
        {
            ValidateSlot(slot);
            GameManager game = RequireGame();
            LoadStateFile(StatePath(slot, game.playerData.profileID));
        }

        public static void DeleteState(int slot)
        {
            ValidateSlot(slot);
            GameManager game = RequireGame();
            string path = StatePath(slot, game.playerData.profileID);
            if (File.Exists(path)) File.Delete(path);
        }

        static string CaptureStateJson(GameManager game, HeroController hero)
        {
            stateTransferPreparation?.Invoke();
            game.SaveLevelState();
            Vector3 position = hero.transform.position;
            string saveJson = SaveDataUtility.SerializeSaveData(new SaveGameData(game.playerData, game.sceneData));
            var envelope = new StateEnvelope
            {
                profileId = game.playerData.profileID,
                scene = game.sceneName,
                x = position.x,
                y = position.y,
                z = position.z,
                saveJson = saveJson,
            };
            string json = JsonUtility.ToJson(envelope);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumStateBytes)
                throw new InvalidDataException("The save-state sidecar exceeds the 4 MiB limit.");
            return json;
        }

        static void LoadStateFile(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("The selected save-state slot is empty.", path);
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaximumStateBytes)
                throw new InvalidDataException("The selected save-state sidecar is invalid.");
            var envelope = JsonUtility.FromJson<StateEnvelope>(File.ReadAllText(path));
            if (envelope == null || envelope.version != 1 || string.IsNullOrEmpty(envelope.scene) || string.IsNullOrEmpty(envelope.saveJson))
                throw new InvalidDataException("The selected save-state sidecar is incomplete.");
            GameManager game = RequireGame();
            int profile = game.playerData.profileID;
            if (envelope.profileId != profile)
                throw new InvalidDataException("The selected save-state belongs to another profile.");
            SaveGameData save = SaveDataUtility.DeserializeSaveData<SaveGameData>(envelope.saveJson);
            if (save == null || save.playerData == null || save.sceneData == null || save.playerData.profileID != profile)
                throw new InvalidDataException("The selected save-state payload is incomplete or belongs to another profile.");
            MethodInfo load = typeof(GameManager).GetMethod("SetLoadedGameData", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(SaveGameData), typeof(int) }, null);
            if (load == null) throw new MissingMethodException("GameManager.SetLoadedGameData(SaveGameData,int)");
            stateTransferPreparation?.Invoke();
            load.Invoke(game, new object[] { save, profile });
            owner = null;
            ResetBossRuntime();
            game.BeginSceneTransition(new StateSceneLoadInfo
            {
                SceneName = envelope.scene,
                EntryGateName = "",
                PreventCameraFadeOut = true,
                WaitForSceneTransitionCameraFade = false,
                AlwaysUnloadUnusedAssets = true,
                Visualization = GameManager.SceneLoadVisualizations.ContinueFromSave,
                Position = new Vector3(envelope.x, envelope.y, envelope.z),
            });
        }

        static string StateDirectory { get { return Path.Combine(Application.persistentDataPath, "dualsouls-save-states", "silksong"); } }
        static string ProfileStateDirectory(int profile) { return Path.Combine(StateDirectory, "profile-" + profile); }
        static string StatePath(int slot, int profile) { return Path.Combine(ProfileStateDirectory(profile), "slot-" + slot + ".json"); }
        static string BossStatePath(int profile) { return Path.Combine(ProfileStateDirectory(profile), "boss-retry.json"); }
        static void ValidateSlot(int slot) { if (slot < 1 || slot > 5) throw new ArgumentOutOfRangeException(nameof(slot)); }
        static GameManager RequireGame()
        {
            GameManager game = GameManager.SilentInstance;
            if (game == null || game.playerData == null || game.sceneData == null)
                throw new InvalidOperationException("Silksong save data is not ready.");
            return game;
        }
        static void WriteStateSidecar(string path, string json)
        {
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("The save-state directory is invalid.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, json);
        }

        static void MaintainFastTransitions(GameManager game, bool enabled)
        {
            bool transitioning = enabled && game != null && game.IsInSceneTransition;
            if (transitioning && !fastTimeOwned && Mathf.Abs(Time.timeScale - 1f) < 0.01f)
            {
                fastTimeBaseline = Time.timeScale;
                Time.timeScale = FastTransitionScale;
                fastTimeOwned = true;
            }
            else if (!transitioning) RestoreFastTransitions();
        }
        static void RestoreFastTransitions()
        {
            if (fastTimeOwned && Mathf.Abs(Time.timeScale - FastTransitionScale) < 0.01f) Time.timeScale = fastTimeBaseline;
            fastTimeOwned = false;
        }

        static void MaintainAutoMap(GameManager game, PlayerData player, bool enabled)
        {
            if (!enabled)
            {
                RestoreMap(player);
                return;
            }
            string scene = game.sceneName;
            if (!string.IsNullOrEmpty(scene) && !player.scenesMapped.Contains(scene))
            {
                player.scenesMapped.Add(scene);
                mappedScenesOwned.Add(scene);
            }
            SetMapFlag(player, game.GetCurrentMapZoneEnum().ToString(), true);
            player.mapUpdateQueued = true;
        }

        static void RestoreMap(PlayerData player)
        {
            if (player == null) { ClearMapOwnership(); return; }
            foreach (string scene in mappedScenesOwned) player.scenesMapped.Remove(scene);
            foreach (KeyValuePair<string, bool> flag in mapFlagsOwned) SetMapFlagValue(player, flag.Key, flag.Value);
            ClearMapOwnership();
        }
        static void ClearMapOwnership()
        {
            mappedScenesOwned.Clear();
            mappedScenesUnderAuthoritativeUpdate.Clear();
            mapFlagsOwned.Clear();
        }

        static void SetMapFlag(PlayerData p, string zone, bool value, bool capture = true)
        {
            string key;
            switch (zone)
            {
                case "MOSS_CAVE": key = "HasMossGrottoMap"; break;
                case "WILDS": key = "HasWildsMap"; break;
                case "PATH_OF_BONE": case "BONETOWN": case "BONECHURCH": case "MOSSTOWN": key = "HasBoneforestMap"; break;
                case "DOCKS": case "PHARLOOM_BAY": key = "HasDocksMap"; break;
                case "GREYMOOR": case "WISP": key = "HasGreymoorMap"; break;
                case "BELLTOWN": key = "HasBellhartMap"; break;
                case "SHELLWOOD_THICKET": key = "HasShellwoodMap"; break;
                case "CRAWLSPACE": key = "HasCrawlMap"; break;
                case "HUNTERS_NEST": key = "HasHuntersNestMap"; break;
                case "JUDGE_STEPS": case "FRONT_GATE": key = "HasJudgeStepsMap"; break;
                case "DUSTPENS": case "DUST_MAZE": key = "HasDustpensMap"; break;
                case "THE_SLAB": key = "HasSlabMap"; break;
                case "PEAK": case "SURFACE": key = "HasPeakMap"; break;
                case "UNDERSTORE": key = "HasCitadelUnderstoreMap"; break;
                case "RED_CORAL_GORGE": case "CORAL_CAVERNS": key = "HasCoralMap"; break;
                case "SWAMP": case "GLOOM": key = "HasSwampMap"; break;
                case "CLOVER": key = "HasCloverMap"; break;
                case "ABYSS": key = "HasAbyssMap"; break;
                case "HANG": key = "HasHangMap"; break;
                case "CITY_OF_SONG": key = "HasSongGateMap"; break;
                case "WARD": key = "HasWardMap"; break;
                case "COG_CORE": key = "HasCogMap"; break;
                case "LIBRARY": key = "HasLibraryMap"; break;
                case "CRADLE": key = "HasCradleMap"; break;
                case "ARBORIUM": key = "HasArboriumMap"; break;
                case "AQUEDUCT": key = "HasAqueductMap"; break;
                case "WEAVER_SHRINE": key = "HasWeavehomeMap"; break;
                default: return;
            }
            bool old = GetMapFlag(p, key);
            if (capture && !mapFlagsOwned.ContainsKey(key)) mapFlagsOwned.Add(key, old);
            SetMapFlagValue(p, key, value);
        }
        static bool GetMapFlag(PlayerData p, string key)
        {
            switch (key)
            {
                case "HasMossGrottoMap": return p.HasMossGrottoMap; case "HasWildsMap": return p.HasWildsMap;
                case "HasBoneforestMap": return p.HasBoneforestMap; case "HasDocksMap": return p.HasDocksMap;
                case "HasGreymoorMap": return p.HasGreymoorMap; case "HasBellhartMap": return p.HasBellhartMap;
                case "HasShellwoodMap": return p.HasShellwoodMap; case "HasCrawlMap": return p.HasCrawlMap;
                case "HasHuntersNestMap": return p.HasHuntersNestMap; case "HasJudgeStepsMap": return p.HasJudgeStepsMap;
                case "HasDustpensMap": return p.HasDustpensMap; case "HasSlabMap": return p.HasSlabMap;
                case "HasPeakMap": return p.HasPeakMap; case "HasCitadelUnderstoreMap": return p.HasCitadelUnderstoreMap;
                case "HasCoralMap": return p.HasCoralMap; case "HasSwampMap": return p.HasSwampMap;
                case "HasCloverMap": return p.HasCloverMap; case "HasAbyssMap": return p.HasAbyssMap;
                case "HasHangMap": return p.HasHangMap; case "HasSongGateMap": return p.HasSongGateMap;
                case "HasWardMap": return p.HasWardMap; case "HasCogMap": return p.HasCogMap;
                case "HasLibraryMap": return p.HasLibraryMap; case "HasCradleMap": return p.HasCradleMap;
                case "HasArboriumMap": return p.HasArboriumMap; case "HasAqueductMap": return p.HasAqueductMap;
                case "HasWeavehomeMap": return p.HasWeavehomeMap; default: return false;
            }
        }
        static void SetMapFlagValue(PlayerData p, string key, bool v)
        {
            switch (key)
            {
                case "HasMossGrottoMap": p.HasMossGrottoMap = v; break; case "HasWildsMap": p.HasWildsMap = v; break;
                case "HasBoneforestMap": p.HasBoneforestMap = v; break; case "HasDocksMap": p.HasDocksMap = v; break;
                case "HasGreymoorMap": p.HasGreymoorMap = v; break; case "HasBellhartMap": p.HasBellhartMap = v; break;
                case "HasShellwoodMap": p.HasShellwoodMap = v; break; case "HasCrawlMap": p.HasCrawlMap = v; break;
                case "HasHuntersNestMap": p.HasHuntersNestMap = v; break; case "HasJudgeStepsMap": p.HasJudgeStepsMap = v; break;
                case "HasDustpensMap": p.HasDustpensMap = v; break; case "HasSlabMap": p.HasSlabMap = v; break;
                case "HasPeakMap": p.HasPeakMap = v; break; case "HasCitadelUnderstoreMap": p.HasCitadelUnderstoreMap = v; break;
                case "HasCoralMap": p.HasCoralMap = v; break; case "HasSwampMap": p.HasSwampMap = v; break;
                case "HasCloverMap": p.HasCloverMap = v; break; case "HasAbyssMap": p.HasAbyssMap = v; break;
                case "HasHangMap": p.HasHangMap = v; break; case "HasSongGateMap": p.HasSongGateMap = v; break;
                case "HasWardMap": p.HasWardMap = v; break; case "HasCogMap": p.HasCogMap = v; break;
                case "HasLibraryMap": p.HasLibraryMap = v; break; case "HasCradleMap": p.HasCradleMap = v; break;
                case "HasArboriumMap": p.HasArboriumMap = v; break; case "HasAqueductMap": p.HasAqueductMap = v; break;
                case "HasWeavehomeMap": p.HasWeavehomeMap = v; break;
            }
        }

        public static void BeforeAuthoritativeBoolSet(PlayerData player, string boolName, bool value)
        {
            if (!value || !ReferenceEquals(owner, player) || string.IsNullOrEmpty(boolName)) return;
            mapFlagsOwned.Remove(boolName);
            if (!extraSlotsCaptured) return;
            if (boolName == "UnlockedExtraBlueSlot") extraBlueBaseline = true;
            else if (boolName == "UnlockedExtraYellowSlot") extraYellowBaseline = true;
        }

        public static void BeginAuthoritativeMapUpdate()
        {
            if (mappedScenesUnderAuthoritativeUpdate.Count != 0) EndAuthoritativeMapUpdate();
            PlayerData player = owner;
            if (player == null || !PlayerData.HasInstance ||
                !ReferenceEquals(player, PlayerData.instance) || player.scenesMapped == null) return;
            foreach (string scene in mappedScenesOwned)
            {
                if (!player.scenesMapped.Remove(scene)) continue;
                mappedScenesUnderAuthoritativeUpdate.Add(scene);
            }
        }

        public static void EndAuthoritativeMapUpdate()
        {
            PlayerData player = owner;
            if (player == null || player.scenesMapped == null)
            {
                mappedScenesUnderAuthoritativeUpdate.Clear();
                return;
            }
            for (int i = 0; i < mappedScenesUnderAuthoritativeUpdate.Count; i++)
            {
                string scene = mappedScenesUnderAuthoritativeUpdate[i];
                if (player.scenesMapped.Contains(scene)) mappedScenesOwned.Remove(scene);
                else player.scenesMapped.Add(scene);
            }
            mappedScenesUnderAuthoritativeUpdate.Clear();
        }

        public static void BeforeAuthoritativeCrestSlotSet(
            InventoryToolCrestSlot slot, ToolCrestsData.SlotData value)
        {
            if (!PlayerData.HasInstance || !ReferenceEquals(owner, PlayerData.instance)) return;
            if (!value.IsUnlocked || slot == null || slot.Crest == null || slot.Crest.CrestData == null) return;
            CrestBaseline baseline;
            if (!crestBaselines.TryGetValue(slot.Crest.CrestData.name, out baseline)) return;
            int required = slot.SlotIndex + 1;
            if (required <= 0) return;
            if (baseline.Unlocked.Length < required) Array.Resize(ref baseline.Unlocked, required);
            if (baseline.Count < required) baseline.Count = required;
            baseline.Unlocked[slot.SlotIndex] = true;
        }

        static void MaintainToolSlots(PlayerData player, bool enabled)
        {
            if (!enabled) { RestoreToolSlots(player); return; }
            bool changed = false;
            if (!extraSlotsCaptured)
            {
                extraBlueBaseline = player.UnlockedExtraBlueSlot;
                extraYellowBaseline = player.UnlockedExtraYellowSlot;
                extraSlotsCaptured = true;
            }
            if (!player.UnlockedExtraBlueSlot) { player.UnlockedExtraBlueSlot = true; changed = true; }
            if (!player.UnlockedExtraYellowSlot) { player.UnlockedExtraYellowSlot = true; changed = true; }
            foreach (ToolCrest crest in ToolItemManager.GetAllCrests())
            {
                if (crest == null || !crest.IsUnlocked) continue;
                ToolCrestsData.Data data = player.ToolEquips.GetData(crest.name);
                if (data.Slots == null) data.Slots = new List<ToolCrestsData.SlotData>();
                CrestBaseline baseline;
                if (!crestBaselines.TryGetValue(crest.name, out baseline))
                {
                    baseline = new CrestBaseline { Count = data.Slots.Count, Unlocked = new bool[data.Slots.Count] };
                    for (int i = 0; i < data.Slots.Count; i++) baseline.Unlocked[i] = data.Slots[i].IsUnlocked;
                    crestBaselines.Add(crest.name, baseline);
                }
                bool crestChanged = false;
                while (data.Slots.Count < crest.Slots.Length)
                {
                    data.Slots.Add(new ToolCrestsData.SlotData());
                    crestChanged = true;
                }
                for (int i = 0; i < data.Slots.Count; i++)
                {
                    ToolCrestsData.SlotData slot = data.Slots[i];
                    if (slot.IsUnlocked) continue;
                    slot.IsUnlocked = true;
                    data.Slots[i] = slot;
                    crestChanged = true;
                }
                if (crestChanged) { player.ToolEquips.SetData(crest.name, data); changed = true; }
            }
            if (changed && PlayerData.HasInstance && ReferenceEquals(PlayerData.instance, player))
                ToolItemManager.SendEquippedChangedEvent(true);
        }
        static void RestoreToolSlots(PlayerData player)
        {
            if (player == null) { crestBaselines.Clear(); extraSlotsCaptured = false; return; }
            bool changed = crestBaselines.Count != 0 || extraSlotsCaptured;
            foreach (KeyValuePair<string, CrestBaseline> pair in crestBaselines)
            {
                ToolCrest crest = ToolItemManager.GetCrestByName(pair.Key);
                if (crest == null) continue;
                ToolCrestsData.Data data = player.ToolEquips.GetData(crest.name);
                if (data.Slots == null) continue;
                CrestBaseline baseline = pair.Value;
                while (data.Slots.Count > baseline.Count) data.Slots.RemoveAt(data.Slots.Count - 1);
                for (int i = 0; i < data.Slots.Count && i < baseline.Unlocked.Length; i++)
                {
                    ToolCrestsData.SlotData slot = data.Slots[i];
                    slot.IsUnlocked = baseline.Unlocked[i];
                    data.Slots[i] = slot;
                }
                player.ToolEquips.SetData(crest.name, data);
            }
            crestBaselines.Clear();
            if (extraSlotsCaptured)
            {
                player.UnlockedExtraBlueSlot = extraBlueBaseline;
                player.UnlockedExtraYellowSlot = extraYellowBaseline;
            }
            extraSlotsCaptured = false;
            if (changed && PlayerData.HasInstance && ReferenceEquals(PlayerData.instance, player))
                ToolItemManager.SendEquippedChangedEvent(true);
        }

        static void MaintainRosaryMagnet(bool enabled)
        {
            if (!enabled || Time.unscaledTime < nextRosaryScan) return;
            nextRosaryScan = Time.unscaledTime + 0.12f;
            HeroController hero = HeroController.instance;
            if (hero == null) return;
            Vector3 target = hero.transform.position;
            foreach (GeoControl rosary in UnityEngine.Object.FindObjectsByType<GeoControl>(FindObjectsSortMode.None))
            {
                if (rosary == null || !rosary.gameObject.activeInHierarchy) continue;
                Vector3 delta = target - rosary.transform.position;
                if (delta.sqrMagnitude > 225f) continue;
                rosary.transform.position = Vector3.MoveTowards(rosary.transform.position, target, 32f * Time.unscaledDeltaTime);
            }
        }
        static void MaintainKeepRosaries(PlayerData player, bool enabled)
        {
            if (!enabled) { SilksongGameplayHooks.CompleteDeathHandling(); return; }
            if (SilksongGameplayHooks.DeathObserved && player.HeroCorpseMoneyPool > 0)
            {
                int amount = player.HeroCorpseMoneyPool;
                player.AddGeo(amount);
                CurrencyManager.AddGeoToCounter(amount);
                player.HeroCorpseMoneyPool = 0;
                SilksongGameplayHooks.CompleteDeathHandling();
            }
        }

        static Sprite PixelSprite()
        {
            if (pixelSprite != null) return pixelSprite;
            pixelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            pixelTexture.SetPixel(0, 0, Color.white); pixelTexture.Apply(false);
            pixelSprite = Sprite.Create(pixelTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return pixelSprite;
        }
        static void MaintainCombatOverlays(bool barsEnabled, bool numbersEnabled)
        {
            if (!barsEnabled && !numbersEnabled) { RemoveCombatOverlays(); return; }
            if (Time.unscaledTime >= nextEnemyScan)
            {
                nextEnemyScan = Time.unscaledTime + 0.5f;
                foreach (HealthManager enemy in HealthManager.EnumerateActiveEnemies())
                    if (enemy != null && enemy.hp > 0 && enemy.hp < 50000 && !enemies.ContainsKey(enemy))
                        enemies.Add(enemy, new EnemyVisual { Maximum = enemy.hp, Last = enemy.hp });
            }
            deadEnemies.Clear();
            foreach (KeyValuePair<HealthManager, EnemyVisual> pair in enemies)
            {
                HealthManager enemy = pair.Key; EnemyVisual visual = pair.Value;
                if (enemy == null || enemy.gameObject == null || !enemy.gameObject.activeInHierarchy || enemy.hp <= 0)
                {
                    if (visual.Root != null) UnityEngine.Object.Destroy(visual.Root.gameObject);
                    deadEnemies.Add(enemy); continue;
                }
                if (enemy.hp < visual.Last && numbersEnabled) SpawnNumber(enemy.transform.position, visual.Last - enemy.hp);
                visual.Last = enemy.hp; if (enemy.hp > visual.Maximum) visual.Maximum = enemy.hp;
                if (barsEnabled) UpdateBar(enemy, visual); else if (visual.Root != null) visual.Root.gameObject.SetActive(false);
            }
            foreach (HealthManager enemy in deadEnemies) enemies.Remove(enemy);
            for (int i = numbers.Count - 1; i >= 0; i--)
            {
                FloatingNumber number = numbers[i];
                if (number.Transform == null || Time.unscaledTime >= number.Until)
                {
                    if (number.Transform != null) UnityEngine.Object.Destroy(number.Transform.gameObject);
                    numbers.RemoveAt(i); continue;
                }
                number.Transform.position += Vector3.up * (Time.unscaledDeltaTime * 1.6f);
                Color color = number.Text.color; color.a = Mathf.Clamp01((number.Until - Time.unscaledTime) / 0.5f); number.Text.color = color;
            }
        }
        static void UpdateBar(HealthManager enemy, EnemyVisual visual)
        {
            if (visual.Root == null)
            {
                var root = new GameObject("DualSouls Enemy Health"); root.transform.SetParent(enemy.transform, false); root.transform.localPosition = new Vector3(0f, 1.35f, 0f); visual.Root = root.transform;
                visual.Fill = BarPart(root.transform, "Fill", new Color(0.92f, 0.25f, 0.2f, 0.92f), 3201, new Vector3(1.44f, 0.10f, 1f));
                BarPart(root.transform, "Background", new Color(0f, 0f, 0f, 0.62f), 3200, new Vector3(1.5f, 0.16f, 1f)).SetSiblingIndex(0);
            }
            float fill = Mathf.Clamp01(visual.Maximum > 0 ? (float)enemy.hp / visual.Maximum : 0f);
            visual.Fill.localScale = new Vector3(1.44f * fill, 0.10f, 1f); visual.Fill.localPosition = new Vector3(-0.72f * (1f - fill), 0f, 0f);
            Vector3 scale = enemy.transform.lossyScale; visual.Root.localScale = new Vector3(scale.x == 0f ? 1f : 1f / scale.x, scale.y == 0f ? 1f : 1f / scale.y, 1f); visual.Root.gameObject.SetActive(true);
        }
        static Transform BarPart(Transform parent, string name, Color color, int order, Vector3 scale)
        {
            var part = new GameObject(name); part.transform.SetParent(parent, false); var renderer = part.AddComponent<SpriteRenderer>(); renderer.sprite = PixelSprite(); renderer.color = color; renderer.sortingOrder = order; part.transform.localScale = scale; return part.transform;
        }
        static void SpawnNumber(Vector3 position, int damage)
        {
            if (damage <= 0) return; if (numberFont == null) numberFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            var go = new GameObject("DualSouls Damage Number"); go.transform.position = position + Vector3.up * 1.6f; var text = go.AddComponent<TextMesh>(); text.text = damage.ToString(System.Globalization.CultureInfo.InvariantCulture); text.font = numberFont; text.fontSize = 44; text.characterSize = 0.14f; text.anchor = TextAnchor.MiddleCenter; text.color = new Color(1f, 0.92f, 0.5f, 1f);
            MeshRenderer renderer = go.GetComponent<MeshRenderer>(); if (renderer != null && numberFont != null) { renderer.material = numberFont.material; renderer.sortingOrder = 3210; }
            numbers.Add(new FloatingNumber { Transform = go.transform, Text = text, Until = Time.unscaledTime + 1.1f });
        }
        static void RemoveCombatOverlays()
        {
            foreach (EnemyVisual visual in enemies.Values) if (visual.Root != null) UnityEngine.Object.Destroy(visual.Root.gameObject); enemies.Clear();
            foreach (FloatingNumber number in numbers) if (number.Transform != null) UnityEngine.Object.Destroy(number.Transform.gameObject); numbers.Clear();
        }
        static void ReleaseVisualResources() { if (pixelSprite != null) UnityEngine.Object.Destroy(pixelSprite); if (pixelTexture != null) UnityEngine.Object.Destroy(pixelTexture); pixelSprite = null; pixelTexture = null; numberFont = null; }

        static void MaintainSecretRadar(GameManager game, bool enabled)
        {
            if (!enabled) { RemoveRadar(); return; }
            string scene = game.sceneName ?? "";
            if (scene != radarScene)
            {
                RemoveRadar(); radarScene = scene;
                foreach (Breakable item in UnityEngine.Object.FindObjectsByType<Breakable>(FindObjectsSortMode.None))
                {
                    if (item == null || !RadarMatch(item.gameObject.name)) continue;
                    var marker = new GameObject("DualSouls Secret Radar"); marker.transform.position = item.transform.position + Vector3.up * 0.3f; var renderer = marker.AddComponent<SpriteRenderer>(); renderer.sprite = PixelSprite(); renderer.color = new Color(0.5f, 0.9f, 1f, 0.8f); renderer.sortingOrder = 3195; radarMarks.Add(marker.transform);
                }
            }
            radarPhase += Time.unscaledDeltaTime * 3.2f; float scale = 0.28f + 0.10f * Mathf.Abs(Mathf.Sin(radarPhase));
            for (int i = radarMarks.Count - 1; i >= 0; i--) { if (radarMarks[i] == null) radarMarks.RemoveAt(i); else radarMarks[i].localScale = new Vector3(scale, scale, 1f); }
        }
        static bool RadarMatch(string objectName)
        {
            string name = (objectName ?? "").ToLowerInvariant();
            if (name.Contains("pot") || name.Contains("bone") || name.Contains("debris") || name.Contains("particle") || name.Contains("corpse")) return false;
            return name.Contains("secret") || name.Contains("hidden") || name.Contains("break wall") || name.Contains("break floor") || name.Contains("false wall");
        }
        static void RemoveRadar() { foreach (Transform marker in radarMarks) if (marker != null) UnityEngine.Object.Destroy(marker.gameObject); radarMarks.Clear(); radarScene = null; }

        static void MaintainBossRetry(GameManager game, PlayerData player, bool enabled)
        {
            if (!enabled) { ResetBossRuntime(); return; }
            if (bossProfile != player.profileID)
            {
                ResetBossRuntime();
                bossProfile = player.profileID;
            }
            string scene = game.sceneName;
            if (scene != inspectedBossScene && Time.unscaledTime >= nextBossInspection)
            {
                nextBossInspection = Time.unscaledTime + 1.5f; bool hasBoss = false;
                foreach (HealthManager enemy in HealthManager.EnumerateActiveEnemies()) if (enemy != null && enemy.hp >= 200 && enemy.hp < 50000) { hasBoss = true; break; }
                inspectedBossScene = hasBoss ? scene : null;
                HeroController hero = HeroController.instance;
                if (hasBoss && hero != null && player.health > 0 && bossScene != scene)
                {
                    bossScene = scene;
                    WriteStateSidecar(BossStatePath(player.profileID), CaptureStateJson(game, hero));
                }
            }
            if (bossScene == null) return;
            if (!bossDeathArmed && player.health <= 0 && scene == bossScene) bossDeathArmed = true;
            else if (bossDeathArmed && player.health > 0 && !game.IsInSceneTransition)
            {
                bossDeathArmed = false; string path = BossStatePath(player.profileID); if (File.Exists(path)) LoadStateFile(path);
            }
        }

        public static void ResetBossRuntime()
        {
            bossScene = null;
            inspectedBossScene = null;
            bossDeathArmed = false;
            nextBossInspection = 0f;
            bossProfile = -1;
        }

        static void RestorePlayerOwned(bool releaseOwner = true)
        {
            PlayerData player = owner;
            if (player != null) { RestoreMap(player); RestoreToolSlots(player); }
            else { ClearMapOwnership(); crestBaselines.Clear(); extraSlotsCaptured = false; }
            if (releaseOwner) owner = null;
        }

        static string BenchPath(int profile) { return Path.Combine(Application.persistentDataPath, "dualsouls-recorded-benches", "silksong-profile-" + profile + ".json"); }
        static void EnsureBenches(int profile)
        {
            if (benchProfile == profile) return; benches.Clear(); benchProfile = profile; lastAtBench = false;
            string path = BenchPath(profile); if (!File.Exists(path)) return;
            var info = new FileInfo(path); if (info.Length <= 0 || info.Length > 512 * 1024) throw new InvalidDataException("The recorded-bench sidecar is invalid.");
            BenchEnvelope envelope = JsonUtility.FromJson<BenchEnvelope>(File.ReadAllText(path));
            if (envelope == null || envelope.profileId != profile || envelope.records == null) throw new InvalidDataException("The recorded-bench sidecar belongs to another profile.");
            int count = Math.Min(envelope.records.Count, MaximumBenches);
            for (int i = 0; i < count; i++) { BenchRecord record = envelope.records[i]; if (record != null && !string.IsNullOrEmpty(record.scene) && !string.IsNullOrEmpty(record.marker)) benches[record.scene] = record; }
        }
        static void RecordBench(PlayerData player)
        {
            bool atBench = player.atBench;
            if (!atBench || lastAtBench) { lastAtBench = atBench; return; }
            lastAtBench = true; if (string.IsNullOrEmpty(player.respawnScene) || string.IsNullOrEmpty(player.respawnMarkerName)) return;
            benches[player.respawnScene] = new BenchRecord { scene = player.respawnScene, marker = player.respawnMarkerName, type = player.respawnType };
            var envelope = new BenchEnvelope { profileId = player.profileID };
            foreach (BenchRecord record in benches.Values) { if (envelope.records.Count == MaximumBenches) break; envelope.records.Add(record); }
            string path = BenchPath(player.profileID); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(envelope));
        }
        public static void RequestBenchRoute() { benchRouteRequested = true; }
        public static bool ConsumeBenchRouteRequest() { bool requested = benchRouteRequested; benchRouteRequested = false; return requested; }
        public static List<BenchRecord> BenchDestinations()
        {
            var result = new List<BenchRecord>(benches.Values); result.Sort((a, b) => string.Compare(a.scene, b.scene, StringComparison.Ordinal)); return result;
        }
        public static void WarpToBench(string scene)
        {
            GameManager game = RequireGame(); EnsureBenches(game.playerData.profileID); BenchRecord record;
            if (string.IsNullOrEmpty(scene) || !benches.TryGetValue(scene, out record)) throw new InvalidOperationException("That bench has not been recorded for this profile.");
            game.playerData.SetBenchRespawn(record.marker, record.scene, record.type, true); game.ReadyForRespawn(false);
        }
    }

    public static class SilksongGameplayHooks
    {
        public static int NeedleMultiplier { get; set; } = 1;
        public static bool DamageCapEnabled { get; set; }
        public static bool InnateCompassEnabled { get; set; }
        public static bool JournalOneKillEnabled { get; set; }
        public static bool ToolCostsFreeEnabled { get; set; }
        public static bool KeepRosariesEnabled { get; set; }
        public static int RosaryMultiplier { get; set; } = 1;
        static bool deathObserved;

        public static void BeforeEnemyHit(HealthManager target, ref HitInstance hit)
        {
            if (target == null || target.isDead || NeedleMultiplier <= 1 || hit.DamageDealt <= 0 || !hit.IsNailDamage) return;
            long scaled = (long)hit.DamageDealt * NeedleMultiplier; hit.DamageDealt = scaled > int.MaxValue ? int.MaxValue : (int)scaled;
        }
        public static void BeforeHeroDamage(ref int damageAmount) { if (DamageCapEnabled && damageAmount > 1) damageAmount = 1; }
        public static void BeforeCurrencyChange(ref int amount, CurrencyType type)
        {
            if (type != CurrencyType.Money || amount <= 0 || RosaryMultiplier <= 1) return;
            long scaled = (long)amount * RosaryMultiplier; amount = scaled > int.MaxValue ? int.MaxValue : (int)scaled;
        }
        public static void BeforeAddGeoQuietly(ref int amount)
        {
            if (amount <= 0 || RosaryMultiplier <= 1) return;
            long scaled = (long)amount * RosaryMultiplier; amount = scaled > int.MaxValue ? int.MaxValue : (int)scaled;
        }
        public static void BeforeJournalKill(EnemyJournalRecord record)
        {
            if (!JournalOneKillEnabled || record == null || record.KillsRequired <= 0 || !PlayerData.HasInstance) return;
            EnemyJournalKillData.KillData data = PlayerData.instance.EnemyJournalKillData.GetKillData(record.name);
            if (data.Kills < record.KillsRequired) { data.Kills = record.KillsRequired - 1; PlayerData.instance.EnemyJournalKillData.RecordKillData(record.name, data); }
        }
        public static void BeforeHeroDeath(bool nonLethal) { if (KeepRosariesEnabled && !nonLethal) deathObserved = true; }
        public static bool DeathObserved { get { return deathObserved; } }
        public static void CompleteDeathHandling() { deathObserved = false; }
        public static void BeforeToolReplenishCost(ref float cost)
        {
            if (ToolCostsFreeEnabled && cost > 0f) cost = 0f;
        }
        public static bool ShouldForceToolEquipped(ToolItem tool)
        {
            return InnateCompassEnabled && tool != null && tool == GlobalSettings.Gameplay.CompassTool;
        }
        public static void BeforeAuthoritativeBoolSet(PlayerData player, string boolName, bool value)
        {
            SilksongGameplayFeatures.BeforeAuthoritativeBoolSet(player, boolName, value);
        }
        public static void BeginAuthoritativeSilkGrant(PlayerData player)
        {
            SilksongGameTweakApi.BeginAuthoritativeSilkGrant(player);
        }
        public static void EndAuthoritativeSilkGrant(PlayerData player)
        {
            SilksongGameTweakApi.EndAuthoritativeSilkGrant(player);
        }
        public static void BeginAuthoritativeSilkPartsGrant(PlayerData player)
        {
            SilksongGameTweakApi.BeginAuthoritativeSilkPartsGrant(player);
        }
        public static void EndAuthoritativeSilkPartsGrant(PlayerData player)
        {
            SilksongGameTweakApi.EndAuthoritativeSilkPartsGrant(player);
        }
        public static void BeginAuthoritativeMapUpdate()
        {
            SilksongGameplayFeatures.BeginAuthoritativeMapUpdate();
        }
        public static void EndAuthoritativeMapUpdate()
        {
            SilksongGameplayFeatures.EndAuthoritativeMapUpdate();
        }
        public static void BeforeAuthoritativeCrestSlotSet(
            InventoryToolCrestSlot slot, ToolCrestsData.SlotData value)
        {
            SilksongGameplayFeatures.BeforeAuthoritativeCrestSlotSet(slot, value);
        }
        public static void Reset()
        {
            NeedleMultiplier = 1; DamageCapEnabled = false; InnateCompassEnabled = false; JournalOneKillEnabled = false; ToolCostsFreeEnabled = false; KeepRosariesEnabled = false; RosaryMultiplier = 1; deathObserved = false;
        }
    }
}
#endif
