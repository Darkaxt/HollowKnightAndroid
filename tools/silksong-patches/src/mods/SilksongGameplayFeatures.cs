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
        // #43: crest slot arrays as shipped, while UNLIMITED TOOL SLOTS adds its extra row.
        static readonly Dictionary<ToolCrest, ToolCrest.SlotInfo[]> crestSlotOriginals = new Dictionary<ToolCrest, ToolCrest.SlotInfo[]>();
        static FieldInfo crestSlotsField;
        static bool toolSlotsTidy;
        const int ExtraSlotsPerColour = 3;
        const string FloatingMarker = "@floating";
        static readonly List<Transform> radarMarks = new List<Transform>();
        static Texture2D pixelTexture;
        static Sprite pixelSprite;
        static float nextRosaryScan;
        static string radarScene;
        static float radarPhase;
        static string bossScene;
        static string inspectedBossScene;
        static bool bossDeathArmed;
        static float nextBossInspection;
        static int bossProfile = -1;
        static Action stateTransferPreparation;
        static Func<PlayerData, bool> saveOwnership;
        static Action<PlayerData, PlayerData> saveCleaner;
        static MethodInfo memberwiseClone;
        static bool droppedEquipsRestored;
        static string droppedRestoreReport = "-";
        static bool lastAtBench;
        static int benchProfile = -1;
        static readonly Dictionary<string, BenchRecord> benches = new Dictionary<string, BenchRecord>(StringComparer.Ordinal);
        // #38 death counter and the per-room boss probe it shares with #40.
        static DeathTally deathTally;
        static string bossProbeScene;
        static bool bossProbeFound;
        static float nextBossProbe;
        static readonly Func<bool> BossInRoom = () => bossProbeFound || (bossProbeFound = SceneHasBoss());
        // #40 rebirth in scene: the current room's usable entrance, and the room to return to.
        internal static bool RebirthEnabled;
        static string rebirthRoom;
        static string rebirthGate;
        static bool rebirthWasDead;
        static string rebirthArmedScene;
        static string rebirthArmedGate;
        internal static int RebirthCount;

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
            public Action<string> Completed;
            public override void NotifyFinished()
            {
                base.NotifyFinished();
                HeroController hero = HeroController.instance;
                if (hero != null) hero.transform.position = Position;
                Completed?.Invoke(SceneName);
            }
        }

        sealed class CrestBaseline
        {
            public int Count;
            public bool[] Unlocked;
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
        public static void SetSaveCleaner(Func<PlayerData, bool> owns, Action<PlayerData, PlayerData> clean)
        {
            saveOwnership = owns;
            saveCleaner = clean;
        }

        // #48: every Silksong save builds SaveGameData(playerData, sceneData) and serializes it
        // later on a worker thread, so the live PlayerData cannot be reverted around the save.
        // When a row owns PlayerData values, the save gets a shallow copy holding the baselines
        // instead; the live object is never touched. Nothing owned: the save is unchanged.
        internal static void PrepareSaveData(ref PlayerData playerData)
        {
            PlayerData live = playerData;
            if (live == null) return;
            try
            {
                bool mine = ReferenceEquals(owner, live);
                bool owned = mine && (mappedScenesOwned.Count != 0 || mapFlagsOwned.Count != 0 || extraSlotsCaptured
                    || crestBaselines.Count != 0 || crestSlotOriginals.Count != 0);
                Func<PlayerData, bool> owns = saveOwnership;
                bool others = owns != null && owns(live);
                List<string> dropped = null;
                if (owned || others)
                {
                    if (memberwiseClone == null)
                        memberwiseClone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
                    var copy = (PlayerData)memberwiseClone.Invoke(live, null);
                    if (owned) dropped = CleanOwned(live, copy);
                    if (others) saveCleaner?.Invoke(live, copy);
                    playerData = copy;
                }
                if (mine) WriteDroppedEquips(live.profileID, dropped);
            }
            catch (Exception error)
            {
                playerData = live;
                Debug.LogWarning("[DualSouls] Silksong save cleanup skipped: " + error);
            }
        }

        static List<string> CleanOwned(PlayerData live, PlayerData copy)
        {
            if (mappedScenesOwned.Count != 0 && live.scenesMapped != null)
            {
                var scenes = new HashSet<string>(live.scenesMapped, live.scenesMapped.Comparer);
                foreach (string scene in mappedScenesOwned) scenes.Remove(scene);
                copy.scenesMapped = scenes;
            }
            foreach (KeyValuePair<string, bool> flag in mapFlagsOwned) SetMapFlagValue(copy, flag.Key, flag.Value);
            // Tools taken out of slots the row added or unlocked are kept in a sidecar file and put
            // back when the save is loaded with the row on.
            var dropped = new List<string>();
            if (extraSlotsCaptured)
            {
                copy.UnlockedExtraBlueSlot = extraBlueBaseline;
                copy.UnlockedExtraYellowSlot = extraYellowBaseline;
                // IsToolEquipped reads every floating slot entry whatever the flags say.
                if ((!extraBlueBaseline || !extraYellowBaseline) && live.ExtraToolEquips != null)
                {
                    var floating = new FloatingCrestSlotsData();
                    foreach (string id in live.ExtraToolEquips.GetValidNames())
                    {
                        ToolCrestsData.SlotData slot = live.ExtraToolEquips.GetData(id);
                        ToolItem tool = string.IsNullOrEmpty(slot.EquippedTool) ? null : ToolItemManager.GetToolByName(slot.EquippedTool);
                        if (tool != null && ((tool.Type == ToolItemType.Blue && !extraBlueBaseline)
                            || (tool.Type == ToolItemType.Yellow && !extraYellowBaseline)))
                        {
                            dropped.Add(FloatingMarker + "\t" + id + "\t" + slot.EquippedTool);
                            continue;
                        }
                        floating.SetData(id, slot);
                    }
                    copy.ExtraToolEquips = floating;
                }
            }
            if ((crestBaselines.Count == 0 && crestSlotOriginals.Count == 0) || live.ToolEquips == null) return dropped;
            // Crest entries as they were before the row: slots it added are dropped and slots it
            // unlocked are locked again.
            var equips = new ToolCrestsData();
            foreach (KeyValuePair<string, ToolCrestsData.Data> pair in live.ToolEquips.Enumerate())
            {
                ToolCrestsData.Data data = pair.Value;
                if (data.Slots != null)
                {
                    var slots = new List<ToolCrestsData.SlotData>(data.Slots);
                    CrestBaseline baseline;
                    bool hasBaseline = crestBaselines.TryGetValue(pair.Key, out baseline);
                    ToolCrest crest = hasBaseline ? null : ToolItemManager.GetCrestByName(pair.Key);
                    int keep = hasBaseline ? baseline.Count : crest != null ? OriginalSlotCount(crest) : slots.Count;
                    for (int i = 0; i < slots.Count; i++)
                    {
                        ToolCrestsData.SlotData slot = slots[i];
                        bool locked = i < keep && hasBaseline && i < baseline.Unlocked.Length && !baseline.Unlocked[i];
                        if (i < keep && !locked) continue;
                        if (!string.IsNullOrEmpty(slot.EquippedTool))
                            dropped.Add(pair.Key + "\t" + i + "\t" + slot.EquippedTool);
                        slot.EquippedTool = null;
                        slot.IsUnlocked = false;
                        slots[i] = slot;
                    }
                    if (slots.Count > keep) slots.RemoveRange(keep, slots.Count - keep);
                    data.Slots = slots;
                }
                equips.SetData(pair.Key, data);
            }
            copy.ToolEquips = equips;
            return dropped;
        }

        static string DroppedEquipsPath(int profile) { return Path.Combine(Application.persistentDataPath, "dualsouls-extra-tool-slots", "silksong-profile-" + profile + ".txt"); }

        static void WriteDroppedEquips(int profile, List<string> dropped)
        {
            string path = DroppedEquipsPath(profile);
            if (dropped == null || dropped.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, string.Join("\n", dropped.ToArray()));
        }

        // With the row on, put back the tools a clean save took out of the crests (#48).
        static bool RestoreDroppedEquips(PlayerData player)
        {
            string path = DroppedEquipsPath(player.profileID);
            droppedRestoreReport = "profile=" + player.profileID + " scene=" + (GameManager.SilentInstance != null ? GameManager.SilentInstance.sceneName : "-");
            if (!File.Exists(path)) { droppedRestoreReport += " no-file"; return false; }
            bool changed = false;
            foreach (string line in File.ReadAllLines(path))
            {
                string[] parts = line.Split('\t');
                int index;
                if (parts.Length != 3) continue;
                if (parts[0] == FloatingMarker)
                {
                    ToolItem floatingTool = ToolItemManager.GetToolByName(parts[2]);
                    string floatingSkip = floatingTool == null ? "no-tool" : !floatingTool.IsUnlocked ? "locked-tool"
                        : ToolItemManager.IsToolEquipped(floatingTool.name) ? "already-equipped"
                        : !string.IsNullOrEmpty(player.ExtraToolEquips.GetData(parts[1]).EquippedTool) ? "slot-taken" : null;
                    droppedRestoreReport += " " + parts[2] + "@" + parts[1] + "=" + (floatingSkip ?? "restored");
                    if (floatingSkip != null) continue;
                    ToolItemManager.SetExtraEquippedTool(parts[1], floatingTool.name);
                    changed = true;
                    continue;
                }
                if (!int.TryParse(parts[1], out index) || index < 0) continue;
                ToolCrest crest = ToolItemManager.GetCrestByName(parts[0]);
                ToolItem tool = ToolItemManager.GetToolByName(parts[2]);
                ToolCrestsData.Data data = crest != null ? player.ToolEquips.GetData(crest.name) : default(ToolCrestsData.Data);
                string skip = crest == null || crest.Slots == null ? "no-crest" : index >= crest.Slots.Length ? "no-slot"
                    : tool == null ? "no-tool" : !tool.IsUnlocked ? "locked-tool" : tool.Type != crest.Slots[index].Type ? "wrong-type"
                    : ToolItemManager.IsToolEquipped(tool.name) ? "already-equipped"
                    : data.Slots == null || index >= data.Slots.Count ? "no-entry"
                    : !string.IsNullOrEmpty(data.Slots[index].EquippedTool) ? "slot-taken" : null;
                droppedRestoreReport += " " + parts[2] + "@" + parts[0] + "/" + index + "=" + (skip ?? "restored");
                if (skip != null) continue;
                ToolCrestsData.SlotData slot = data.Slots[index];
                slot.EquippedTool = tool.name;
                data.Slots[index] = slot;
                player.ToolEquips.SetData(crest.name, data);
                changed = true;
            }
            return changed;
        }

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
                toolSlotsTidy = false;
                droppedEquipsRestored = false;
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
            MaintainBossProbe(game.sceneName, player.health <= 0);
            MaintainDeathTally(game, player);
            MaintainRebirth(game, player, bossRetry);
        }

        // Same boss test as BOSS RETRY, sampled until the room is known to hold one.
        static bool SceneHasBoss()
        {
            foreach (HealthManager enemy in HealthManager.EnumerateActiveEnemies())
                if (enemy != null && !enemy.isDead && enemy.hp >= 200 && enemy.hp < 50000) return true;
            return false;
        }

        static void MaintainBossProbe(string scene, bool dead)
        {
            if (scene != bossProbeScene) { bossProbeScene = scene; bossProbeFound = false; nextBossProbe = 0f; }
            if (bossProbeFound || dead || Time.unscaledTime < nextBossProbe) return;
            nextBossProbe = Time.unscaledTime + 2f;
            bossProbeFound = SceneHasBoss();
        }

        static void MaintainDeathTally(GameManager game, PlayerData player)
        {
            if (deathTally == null)
                deathTally = new DeathTally(Path.Combine(Application.persistentDataPath, "dualsouls-death-counter"), "silksong");
            // The slot the player picked: a new game's PlayerData.profileID stays 0 until its
            // first save, which is the first death, so that death would never count.
            int slot = game.IsGameplayScene() ? game.profileID : 0;
            deathTally.Tick(player, slot, player.playTime, game.sceneName, player.health <= 0, BossInRoom);
            DsFrame.HeaderStatus = deathTally.Label;
        }

        internal static string DeathReport()
        {
            if (deathTally == null) return "deaths=unbound";
            return "deaths=" + deathTally.Total + " boss=" + deathTally.BossDeaths + " bossScene=" + (deathTally.BossScene ?? "-") +
                " label=" + (deathTally.Label ?? "-") + " error=" + (deathTally.LastError ?? "-") +
                " rebirth=" + RebirthEnabled + " room=" + (rebirthRoom ?? "-") + " gate=" + (rebirthGate ?? "-") +
                " armed=" + (rebirthArmedScene ?? "-") + " rebirths=" + RebirthCount;
        }

        // An entrance the hero can be put back through: a live gate without a scripted fade.
        static string UsableGate(string name)
        {
            if (string.IsNullOrEmpty(name) || TransitionPoint.TransitionPoints == null) return null;
            foreach (TransitionPoint gate in TransitionPoint.TransitionPoints)
            {
                if (gate == null || gate.name != name) continue;
                if (gate.isInactive || gate.customFade || gate.customFadeFSM != null || !gate.gameObject.activeInHierarchy) return null;
                return name;
            }
            return null;
        }

        static void ResetRebirth()
        {
            rebirthRoom = null; rebirthGate = null; rebirthWasDead = false;
            rebirthArmedScene = null; rebirthArmedGate = null;
        }

        static void MaintainRebirth(GameManager game, PlayerData player, bool bossRetry)
        {
            if (!RebirthEnabled) { ResetRebirth(); return; }
            string scene = game.sceneName;
            bool dead = player.health <= 0;
            if (rebirthArmedScene == null && !dead && !game.IsInSceneTransition && scene != rebirthRoom)
            {
                rebirthRoom = scene;
                rebirthGate = UsableGate(game.entryGateName);
            }
            if (dead && !rebirthWasDead && rebirthArmedScene == null && rebirthGate != null && scene == rebirthRoom &&
                scene != player.respawnScene && scene != player.tempRespawnScene && !(bossRetry && BossInRoom()))
            {
                rebirthArmedScene = scene;
                rebirthArmedGate = rebirthGate;
            }
            rebirthWasDead = dead;
            if (rebirthArmedScene == null || dead || game.IsInSceneTransition) return;
            if (scene == rebirthArmedScene) return;  // still in the room, the respawn has not started
            HeroController hero = HeroController.instance;
            // After the respawn has finished and the hero is standing: the bench's own
            // sitting state is left by the player, never broken by a scene change.
            if (hero == null || !hero.acceptingInput || hero.controlReqlinquished || hero.cState.dead) return;
            string target = rebirthArmedScene, gate = rebirthArmedGate;
            rebirthArmedScene = null;
            rebirthArmedGate = null;
            RebirthCount++;
            game.BeginSceneTransition(new GameManager.SceneLoadInfo
            {
                SceneName = target,
                EntryGateName = gate,
                PreventCameraFadeOut = false,
                WaitForSceneTransitionCameraFade = true,
                Visualization = GameManager.SceneLoadVisualizations.Default,
            });
        }

        public static void RestoreAll()
        {
            RestoreFastTransitions();
            RestorePlayerOwned();
            RemoveRadar();
            RemoveCombatOverlays();
            ReleaseVisualResources();
            ResetBossRuntime();
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

        public static void LoadState(int slot, Action<string> completed = null)
        {
            ValidateSlot(slot);
            GameManager game = RequireGame();
            LoadStateFile(StatePath(slot, game.playerData.profileID), completed);
        }

        public static void DeleteState(int slot)
        {
            ValidateSlot(slot);
            GameManager game = RequireGame();
            string path = StatePath(slot, game.playerData.profileID);
            if (File.Exists(path)) File.Delete(path);
        }

        public static bool StateExists(int slot)
        {
            ValidateSlot(slot);
            GameManager game = RequireGame();
            return File.Exists(StatePath(slot, game.playerData.profileID));
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

        static void LoadStateFile(string path, Action<string> completed = null)
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
                Completed = completed,
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
            if (slot.SlotIndex >= OriginalSlotCount(slot.Crest.CrestData)) return;
            int required = slot.SlotIndex + 1;
            if (required <= 0) return;
            if (baseline.Unlocked.Length < required) Array.Resize(ref baseline.Unlocked, required);
            if (baseline.Count < required) baseline.Count = required;
            baseline.Unlocked[slot.SlotIndex] = true;
        }

        static void MaintainToolSlots(PlayerData player, bool enabled)
        {
            if (!enabled) { RestoreToolSlots(player); return; }
            toolSlotsTidy = false;
            ExtendCrestSlots();
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
                    // A save written with the row on already holds the extra entries; they are never baseline.
                    int count = Math.Min(data.Slots.Count, OriginalSlotCount(crest));
                    baseline = new CrestBaseline { Count = count, Unlocked = new bool[count] };
                    for (int i = 0; i < count; i++) baseline.Unlocked[i] = data.Slots[i].IsUnlocked;
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
            if (!droppedEquipsRestored && ReferenceEquals(owner, player) && ToolItemManager.GetAllCrests().Count != 0)
            {
                droppedEquipsRestored = true;
                if (RestoreDroppedEquips(player)) changed = true;
            }
            if (changed && PlayerData.HasInstance && ReferenceEquals(PlayerData.instance, player))
                ToolItemManager.SendEquippedChangedEvent(true);
        }
        static void RestoreToolSlots(PlayerData player)
        {
            bool arraysRestored = RestoreCrestSlots();
            if (player == null) { crestBaselines.Clear(); extraSlotsCaptured = false; return; }
            bool changed = arraysRestored || crestBaselines.Count != 0 || extraSlotsCaptured;
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
            // Once per save with the row off: entries past a crest's shipped slots (a save written
            // with the row on) are dropped, because IsToolEquipped reads every entry and would keep
            // those tools acting.
            List<ToolCrest> crests;
            if (!toolSlotsTidy && ReferenceEquals(owner, player) && (crests = ToolItemManager.GetAllCrests()).Count != 0)
            {
                foreach (ToolCrest crest in crests)
                {
                    if (crest == null || crest.Slots == null) continue;
                    ToolCrestsData.Data data = player.ToolEquips.GetData(crest.name);
                    if (data.Slots == null || data.Slots.Count <= crest.Slots.Length) continue;
                    data.Slots.RemoveRange(crest.Slots.Length, data.Slots.Count - crest.Slots.Length);
                    player.ToolEquips.SetData(crest.name, data);
                    changed = true;
                }
                toolSlotsTidy = true;
            }
            if (changed && PlayerData.HasInstance && ReferenceEquals(PlayerData.instance, player))
                ToolItemManager.SendEquippedChangedEvent(true);
        }

        static int OriginalSlotCount(ToolCrest crest)
        {
            ToolCrest.SlotInfo[] original;
            if (crestSlotOriginals.TryGetValue(crest, out original)) return original.Length;
            return crest.Slots != null ? crest.Slots.Length : 0;
        }

        // #43: a row of blue and yellow slots under every crest. The crest assets change only in
        // memory; the shipped arrays come back when the row is turned off.
        static void ExtendCrestSlots()
        {
            if (crestSlotOriginals.Count != 0) return;
            if (crestSlotsField == null)
                crestSlotsField = typeof(ToolCrest).GetField("slots", BindingFlags.Instance | BindingFlags.NonPublic);
            if (crestSlotsField == null) return;
            foreach (ToolCrest crest in ToolItemManager.GetAllCrests())
            {
                ToolCrest.SlotInfo[] original = crest != null ? crest.Slots : null;
                if (original == null || original.Length == 0 || crestSlotOriginals.ContainsKey(crest)) continue;
                crestSlotOriginals.Add(crest, original);
                crestSlotsField.SetValue(crest, WithExtraSlots(original));
            }
        }

        static bool RestoreCrestSlots()
        {
            if (crestSlotOriginals.Count == 0) return false;
            foreach (KeyValuePair<ToolCrest, ToolCrest.SlotInfo[]> pair in crestSlotOriginals)
                if (pair.Key != null) crestSlotsField.SetValue(pair.Key, pair.Value);
            crestSlotOriginals.Clear();
            return true;
        }

        static ToolCrest.SlotInfo[] WithExtraSlots(ToolCrest.SlotInfo[] original)
        {
            int first = original.Length, extra = 2 * ExtraSlotsPerColour;
            var slots = new ToolCrest.SlotInfo[first + extra];
            Array.Copy(original, slots, first);
            float lowest = 0f;
            for (int i = 0; i < first; i++) lowest = Mathf.Min(lowest, original[i].Position.y);
            float y = Mathf.Min(-3.5f, lowest - 1.25f);
            for (int k = 0; k < extra; k++)
                slots[first + k] = new ToolCrest.SlotInfo
                {
                    Type = k < ExtraSlotsPerColour ? ToolItemType.Blue : ToolItemType.Yellow,
                    Position = new Vector2(1.5f * k - 0.75f * (extra - 1), y),
                    NavUpIndex = -1, NavDownIndex = -1, NavLeftIndex = -1, NavRightIndex = -1,
                    NavUpFallbackIndex = -1, NavDownFallbackIndex = -1, NavLeftFallbackIndex = -1, NavRightFallbackIndex = -1,
                };
            // Navigation: an extra slot links to its nearest slot in each direction; a shipped slot
            // only switches to an extra one that fits that direction better than its current link.
            for (int i = 0; i < slots.Length; i++)
                for (int dir = 0; dir < 4; dir++)
                {
                    int best = -1;
                    float bestDistance = float.MaxValue;
                    for (int j = i < first ? first : 0; j < slots.Length; j++)
                    {
                        if (j == i || NavDirection(slots[i].Position, slots[j].Position) != dir) continue;
                        float distance = Vector2.Distance(slots[i].Position, slots[j].Position);
                        if (distance < bestDistance) { bestDistance = distance; best = j; }
                    }
                    if (best < 0) continue;
                    int current = GetNav(slots[i], dir);
                    if (current >= 0 && current < slots.Length && current != i
                        && NavDirection(slots[i].Position, slots[current].Position) == dir
                        && Vector2.Distance(slots[i].Position, slots[current].Position) <= bestDistance) continue;
                    SetNav(ref slots[i], dir, best);
                }
            return slots;
        }

        // 0 up, 1 down, 2 left, 3 right (the order of InventoryToolCrestSlot.Selectables).
        static int NavDirection(Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            if (d.sqrMagnitude < 0.0001f) return -1;
            if (Mathf.Abs(d.x) >= Mathf.Abs(d.y)) return d.x > 0f ? 3 : 2;
            return d.y > 0f ? 0 : 1;
        }

        static int GetNav(ToolCrest.SlotInfo slot, int dir)
        {
            switch (dir)
            {
                case 0: return slot.NavUpIndex;
                case 1: return slot.NavDownIndex;
                case 2: return slot.NavLeftIndex;
                default: return slot.NavRightIndex;
            }
        }

        static void SetNav(ref ToolCrest.SlotInfo slot, int dir, int index)
        {
            switch (dir)
            {
                case 0: slot.NavUpIndex = index; break;
                case 1: slot.NavDownIndex = index; break;
                case 2: slot.NavLeftIndex = index; break;
                default: slot.NavRightIndex = index; break;
            }
        }

        // QA (#43): the current crest's slots and what each holds.
        public static string CrestReport()
        {
            PlayerData player = PlayerData.instance;
            ToolCrest crest = player != null ? ToolItemManager.GetCrestByName(player.CurrentCrestID) : null;
            if (crest == null || crest.Slots == null) return "error no current crest";
            ToolCrestsData.Data data = player.ToolEquips.GetData(crest.name);
            int saved = data.Slots != null ? data.Slots.Count : 0;
            var sb = new System.Text.StringBuilder();
            sb.Append("crest=").Append(crest.name).Append(" slots=").Append(crest.Slots.Length)
              .Append(" shipped=").Append(OriginalSlotCount(crest)).Append(" saved=").Append(saved)
              .Append(" restore=[").Append(droppedRestoreReport).Append(']');
            for (int i = 0; i < Math.Max(crest.Slots.Length, saved); i++)
            {
                string tool = i < saved ? data.Slots[i].EquippedTool : null;
                sb.Append("\n  ").Append(i).Append(' ').Append(i < crest.Slots.Length ? crest.Slots[i].Type.ToString() : "(no slot)")
                  .Append(i < saved && !data.Slots[i].IsUnlocked ? " locked" : "")
                  .Append(" tool=").Append(string.IsNullOrEmpty(tool) ? "-"
                      : tool + (ToolItemManager.IsToolEquipped(tool) ? "(equipped)" : "(not equipped)"));
            }
            return sb.ToString();
        }

        // QA (#43): put an unlocked tool in slot index of the current crest.
        public static string CrestEquip(int index, string toolName)
        {
            PlayerData player = PlayerData.instance;
            ToolCrest crest = player != null ? ToolItemManager.GetCrestByName(player.CurrentCrestID) : null;
            if (crest == null || crest.Slots == null) return "error no current crest";
            if (index < 0 || index >= crest.Slots.Length) return "error slot index 0.." + (crest.Slots.Length - 1);
            ToolItem tool = ToolItemManager.GetToolByName(toolName);
            if (tool == null) return "error no tool " + toolName;
            if (!tool.IsUnlocked) return "error " + toolName + " is not unlocked (crest unlock <tool name>)";
            if (tool.Type != crest.Slots[index].Type) return "error " + toolName + " is " + tool.Type + ", slot is " + crest.Slots[index].Type;
            ToolCrestsData.Data data = player.ToolEquips.GetData(crest.name);
            if (data.Slots == null) data.Slots = new List<ToolCrestsData.SlotData>();
            while (data.Slots.Count <= index) data.Slots.Add(new ToolCrestsData.SlotData { IsUnlocked = true });
            ToolCrestsData.SlotData slot = data.Slots[index];
            slot.EquippedTool = tool.name;
            data.Slots[index] = slot;
            player.ToolEquips.SetData(crest.name, data);
            ToolItemManager.SendEquippedChangedEvent(true);
            return "ok " + CrestReport();
        }

        // QA (#48): unlock a tool on a disposable test save so it can be equipped.
        public static string CrestUnlockTool(string toolName)
        {
            ToolItem tool = ToolItemManager.GetToolByName(toolName);
            if (tool == null) return "error no tool " + toolName;
            if (!tool.IsUnlocked) tool.Unlock(null, ToolItem.PopupFlags.None);
            return "ok " + tool.name + " unlocked=" + tool.IsUnlocked + " type=" + tool.Type;
        }

        // QA (#43): unlocked tools of one type, to pick a name for CrestEquip.
        public static string CrestTools(string type)
        {
            var sb = new System.Text.StringBuilder("ok");
            foreach (ToolItem tool in ToolItemManager.GetUnlockedTools())
                if (tool != null && string.Equals(tool.Type.ToString(), type, StringComparison.OrdinalIgnoreCase))
                    sb.Append(' ').Append(tool.name);
            return sb.ToString();
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
            if (CombatOverlay.NameResolver == null) CombatOverlay.NameResolver = JournalName;
            CombatOverlay.Tick(barsEnabled, numbersEnabled, HeroController.SilentInstance);
        }
        static void RemoveCombatOverlays() { CombatOverlay.Clear(); }
        static readonly System.Reflection.FieldInfo JournalRecordField =
            typeof(EnemyDeathEffects).GetField("journalRecord", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        // Boss bar title: the Hunter's Journal name, as mod 28 shows it.
        static string JournalName(HealthManager enemy)
        {
            EnemyDeathEffects effects = enemy.GetComponent<EnemyDeathEffects>() ?? enemy.GetComponentInChildren<EnemyDeathEffects>() ?? enemy.GetComponentInParent<EnemyDeathEffects>();
            EnemyJournalRecord record = effects != null && JournalRecordField != null ? JournalRecordField.GetValue(effects) as EnemyJournalRecord : null;
            return record != null ? record.DisplayName.ToString() : null;
        }
        static void ReleaseVisualResources() { if (pixelSprite != null) UnityEngine.Object.Destroy(pixelSprite); if (pixelTexture != null) UnityEngine.Object.Destroy(pixelTexture); pixelSprite = null; pixelTexture = null; CombatOverlay.ReleaseResources(); }

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
            else { ClearMapOwnership(); crestBaselines.Clear(); extraSlotsCaptured = false; RestoreCrestSlots(); }
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
        // #25: mod 19 scales every DamageEnemies hit, not only the Needle.
        public static int AllDamageMultiplier { get; set; } = 1;
        public static bool DamageCapEnabled { get; set; }
        public static bool InnateCompassEnabled { get; set; }
        public static bool JournalOneKillEnabled { get; set; }
        public static bool ToolCostsFreeEnabled { get; set; }
        public static bool KeepRosariesEnabled { get; set; }
        public static int RosaryMultiplier { get; set; } = 1;
        static bool deathObserved;

        public static void BeforeEnemyHit(HealthManager target, ref HitInstance hit)
        {
            CombatOverlay.HookCalls++;
            if (target == null || target.isDead || hit.DamageDealt <= 0) return;
            if (hit.IsHeroDamage) CombatOverlay.NoteHeroHit(target.SendDamageTo != null ? target.SendDamageTo : target);
            long factor = (hit.IsNailDamage ? Math.Max(NeedleMultiplier, 1) : 1) * (long)Math.Max(AllDamageMultiplier, 1);
            if (factor <= 1) return;
            long scaled = hit.DamageDealt * factor; hit.DamageDealt = scaled > int.MaxValue ? int.MaxValue : (int)scaled;
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
        public static void BeforeHeroDeath(bool nonLethal)
        {
            if (KeepRosariesEnabled && !nonLethal) deathObserved = true;
            // HeroController.Die calls DeliveryQuestItem.BreakAll first thing: a death still breaks deliveries.
            deliveryDeathPending = true;
        }

        // #39 Easy Deliveries (Nexus mod 53 behaviour): hits no longer chip delivery items, and the
        // scripted BreakAll/BreakAllNoEffects calls (Bellway and Ventrica arrival, flea caravan travel,
        // CriticalDamage, lava) leave them whole. Death and the courier timer stay vanilla.
        public static bool EasyDeliveriesEnabled { get; set; }
        static bool deliveryDeathPending;
        public static int DeliveryHitsKept { get; private set; }
        public static int DeliveryBreaksKept { get; private set; }
        public static void BeforeDeliveryHit(ref int hits)
        {
            if (!EasyDeliveriesEnabled || hits <= 0) return;
            hits = 0; DeliveryHitsKept++;
        }
        public static bool ShouldKeepDeliveries()
        {
            bool death = deliveryDeathPending; deliveryDeathPending = false;
            if (!EasyDeliveriesEnabled || death) return false;
            DeliveryBreaksKept++;
            return true;
        }
        // #41: percentage of each Flea Festival champion target (stock 30/42/65, Seth 55/68/90).
        // Both the champion getters and the game FSM's minimum (read through GetConstantsValue) follow it,
        // so a score above the lowered target is saved as the real highscore and wins.
        public static int FleaTargetPercent { get; set; } = 100;
        public static int FleaGameTarget(int stock) => FleaTargetPercent >= 100 ? stock : stock * FleaTargetPercent / 100;
        public static int FleaConstantsScaled { get; private set; }
        public static void FinishConstantsValue(GetConstantsValue action)
        {
            try
            {
                if (FleaTargetPercent < 100 && action.storeValue != null && !action.storeValue.IsNone &&
                    action.storeValue.Type == HutongGames.PlayMaker.VariableType.Int &&
                    action.variableName != null && !action.variableName.IsNone &&
                    IsFleaTargetConstant(action.variableName.Value))
                {
                    action.storeValue.SetValue(FleaGameTarget((int)action.storeValue.GetValue()));
                    FleaConstantsScaled++;
                }
            }
            catch (Exception e) { Debug.LogWarning("[DualSouls] Flea Festival target scaling skipped: " + e.Message); }
            action.Finish();
        }
        public static bool IsFleaTargetConstant(string name) => name != null &&
            (name.StartsWith("FLEA_FESTIVAL_CHAMP_", StringComparison.Ordinal) ||
             name.StartsWith("FLEA_FESTIVAL_SETH_", StringComparison.Ordinal));
        public static string DeliveryReport()
        {
            return "easyDeliveries=" + EasyDeliveriesEnabled + " hitsKept=" + DeliveryHitsKept + " breaksKept=" + DeliveryBreaksKept +
                " fleaTargetPercent=" + FleaTargetPercent + " fleaConstantsScaled=" + FleaConstantsScaled;
        }
        public static bool DeathObserved { get { return deathObserved; } }
        public static void CompleteDeathHandling() { deathObserved = false; }
        // #48: prefix of SaveGameData(PlayerData, SceneData), which every save path runs.
        public static void BeforeSaveGameData(ref PlayerData playerData) { SilksongGameplayFeatures.PrepareSaveData(ref playerData); }
        public static void BeforeToolReplenishCost(ref float cost)
        {
            if (ToolCostsFreeEnabled && cost > 0f) cost = 0f;
        }
        public static bool ShouldForceToolEquipped(ToolItem tool)
        {
            if (tool == null) return false;
            return (InnateCompassEnabled && tool == GlobalSettings.Gameplay.CompassTool) ||
                SilksongCheats.IsForcedTool(tool);
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
            NeedleMultiplier = 1; AllDamageMultiplier = 1; DamageCapEnabled = false; InnateCompassEnabled = false; JournalOneKillEnabled = false; ToolCostsFreeEnabled = false; KeepRosariesEnabled = false; RosaryMultiplier = 1; deathObserved = false; EasyDeliveriesEnabled = false; deliveryDeathPending = false; FleaTargetPercent = 100;
        }
    }
}
#endif
