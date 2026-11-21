#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace DualSouls.Mods.HollowKnight
{
    static class HollowKnightGameplayFeatures
    {
        const int MaximumStateBytes = 4 * 1024 * 1024;
        const float FastTransitionScale = 2.5f;
        static bool fastTimeOwned;
        static float fastTimeBaseline = 1f;
        static PlayerData owner;
        static bool mapCaptured;
        static bool mapBaseline;
        static bool quillBaseline;
        static List<string> mapScenesBaseline;
        static MapRegionBaseline mapRegionBaseline;
        static int mapUpdateDepth;
        static List<string> mapScenesLive;
        static MapRegionBaseline mapRegionsLive;
        static int[] charmCostBaseline;
        static bool charmCostsApplied;
        static int notchBaseline;
        static bool notchCaptured;
        static bool compassOwned;
        static int compassCostBaseline;
        static bool magnetOwned;
        static int magnetCostBaseline;
        static bool benchOwned;
        static bool benchBaseline;
        // Read by the lower Charms pane: equip-anywhere stands in for a bench there too.
        internal static bool EquipAnywhereActive;
        static Texture2D pixelTexture;
        static Sprite pixelSprite;
        static readonly List<Transform> radarMarks = new List<Transform>();
        static string radarScene;
        static float radarPhase;
        static string bossScene;
        static string inspectedBossScene;
        static bool bossDeathArmed;
        static float nextBossInspection;
        static Action stateTransferPreparation;
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

        sealed class MapRegionBaseline
        {
            public bool Dirtmouth;
            public bool Crossroads;
            public bool Greenpath;
            public bool FogCanyon;
            public bool FungalWastes;
            public bool RoyalGardens;
            public bool City;
            public bool Waterways;
            public bool Mines;
            public bool Deepnest;
            public bool Cliffs;
            public bool Outskirts;
            public bool RestingGrounds;
            public bool Abyss;
        }

        [Serializable]
        sealed class StateEnvelope
        {
            public int version = 2;
            public int profileId;
            public string scene;
            public float x;
            public float y;
            public float z;
            public SaveGameData save;
        }

        // An empty entry gate only finishes the GameManager side of the entry, which
        // leaves the hero in EXITING_SCENE without input or HUD. The game's own
        // "dreamGate" entry finishes the hero where NotifyFinished placed it.
        const string HeroEntry = "dreamGate";

        sealed class StateSceneLoadInfo : GameManager.SceneLoadInfo
        {
            public Vector3 Position;
            public Action<string> Completed;
            public override void NotifyFinished()
            {
                base.NotifyFinished();
                HeroController hero = HeroController.UnsafeInstance;
                if (hero != null) hero.transform.position = Position;
                Completed?.Invoke(SceneName);
            }
        }

        public static void Tick(
            bool fastTransitions, bool autoMap, bool innateCompass, bool secretRadar,
            bool healthBars, bool damageNumbers, bool bossRetry, bool equipAnywhere,
            bool charmCostsFree, bool unlimitedNotches, bool geoMagnet,
            bool keepGeoOnDeath, bool journalOneKill)
        {
            GameManager game = GameManager.UnsafeInstance;
            PlayerData player = game != null ? game.playerData : null;
            if (player == null) return;
            if (!ReferenceEquals(owner, player))
            {
                RestorePlayerOwned();
                owner = player;
            }

            MaintainFastTransitions(game, fastTransitions);
            MaintainAutoMap(game, player, autoMap);
            MaintainCharmCosts(player, charmCostsFree);
            MaintainInnateCharm(player, 2, innateCompass, ref compassOwned, ref compassCostBaseline);
            MaintainInnateCharm(player, 1, geoMagnet, ref magnetOwned, ref magnetCostBaseline);
            MaintainNotches(player, unlimitedNotches);
            MaintainEquipAnywhere(player, equipAnywhere);
            MaintainKeepGeo(player, keepGeoOnDeath);
            MaintainCombatOverlays(healthBars, damageNumbers);
            MaintainSecretRadar(game, secretRadar);
            MaintainBossRetry(game, player, bossRetry);
            MaintainBossProbe(game.sceneName, player.health <= 0);
            MaintainDeathTally(game, player);
            MaintainRebirth(game, player, bossRetry);
        }

        // Same boss test as BOSS RETRY, sampled until the room is known to hold one.
        static bool SceneHasBoss()
        {
            foreach (HealthManager enemy in UnityEngine.Object.FindObjectsByType<HealthManager>(FindObjectsSortMode.None))
                if (enemy != null && !enemy.isDead && enemy.hp >= 200 && enemy.hp < 5000) return true;
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
                deathTally = new DeathTally(Path.Combine(Application.persistentDataPath, "dualsouls-death-counter"), "hollow-knight");
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

        // An entrance the Knight can be put back through: an active gate without a scripted fade.
        static string UsableGate(string name)
        {
            if (string.IsNullOrEmpty(name) || TransitionPoint.TransitionPoints == null) return null;
            foreach (TransitionPoint gate in TransitionPoint.TransitionPoints)
            {
                if (gate == null || gate.name != name) continue;
                if (gate.customFade || gate.customFadeFSM != null || !gate.gameObject.activeInHierarchy) return null;
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
            // Dreams and Godhome have their own way back.
            bool special = scene == null || scene.StartsWith("Dream_", StringComparison.Ordinal) || scene.StartsWith("GG_", StringComparison.Ordinal);
            if (dead && !rebirthWasDead && rebirthArmedScene == null && rebirthGate != null && scene == rebirthRoom &&
                !special && scene != player.respawnScene && !(bossRetry && BossInRoom()))
            {
                rebirthArmedScene = scene;
                rebirthArmedGate = rebirthGate;
            }
            rebirthWasDead = dead;
            if (rebirthArmedScene == null || dead || game.IsInSceneTransition) return;
            if (scene == rebirthArmedScene) return;  // still in the room, the respawn has not started
            HeroController hero = HeroController.UnsafeInstance;
            // After the respawn has finished and the Knight is standing: the bench's own
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

        public static void SetStateTransferPreparation(Action prepare)
        {
            stateTransferPreparation = prepare;
        }

        public static void PrepareForSave()
        {
            stateTransferPreparation?.Invoke();
        }

        public static void RestorePlayerOwnedForStateTransfer()
        {
            RestorePlayerOwned();
        }

        public static void RestoreAll()
        {
            RestoreFastTransitions();
            RestorePlayerOwned();
            RemoveRadar();
            RemoveCombatOverlays();
            ReleaseVisualResources();
            bossScene = null;
            inspectedBossScene = null;
            bossDeathArmed = false;
        }

        public static void SaveState(int slot)
        {
            ValidateSlot(slot);
            GameManager game = RequireGame();
            HeroController hero = HeroController.UnsafeInstance;
            if (hero == null) throw new InvalidOperationException("The Knight is not ready for a save state.");
            string json = CaptureStateJson(game, hero);
            WriteStateSidecar(StatePath(slot, game.profileID), json);
        }

        public static void LoadState(int slot, Action<string> completed = null)
        {
            ValidateSlot(slot);
            GameManager game = RequireGame();
            LoadStateFile(StatePath(slot, game.profileID), completed);
        }

        static void LoadStateFile(string path, Action<string> completed = null)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("The selected save-state slot is empty.", path);
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaximumStateBytes)
                throw new InvalidDataException("The selected save-state sidecar is invalid.");
            StateEnvelope envelope = JsonUtility.FromJson<StateEnvelope>(File.ReadAllText(path));
            if (envelope == null || envelope.version != 2 || envelope.save == null ||
                envelope.save.playerData == null || envelope.save.sceneData == null ||
                string.IsNullOrEmpty(envelope.scene))
                throw new InvalidDataException("The selected save-state sidecar is incomplete.");

            GameManager game = RequireGame();
            // The slot is the GameManager's: a new game's PlayerData.profileID stays 0
            // until its first save, and passing that 0 back would re-bind the run to
            // no slot at all.
            int currentProfileId = game.profileID;
            int savedProfileId = envelope.save.playerData.profileID;
            if (envelope.profileId != currentProfileId ||
                (savedProfileId != currentProfileId && savedProfileId != 0))
                throw new InvalidDataException("The selected save-state belongs to another profile.");
            MethodInfo load = typeof(GameManager).GetMethod(
                "SetLoadedGameData", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(SaveGameData), typeof(int) }, null);
            if (load == null) throw new MissingMethodException("GameManager.SetLoadedGameData(SaveGameData,int)");
            stateTransferPreparation?.Invoke();
            load.Invoke(game, new object[] { envelope.save, currentProfileId });
            // The hero reads PlayerData.instance only in Start and Respawn, so without
            // this its health and silk would keep going to the replaced object.
            HeroController hero = HeroController.UnsafeInstance;
            if (hero != null) hero.playerData = game.playerData;
            owner = null;
            game.BeginSceneTransition(new StateSceneLoadInfo
            {
                SceneName = envelope.scene,
                EntryGateName = HeroEntry,
                PreventCameraFadeOut = true,
                WaitForSceneTransitionCameraFade = false,
                AlwaysUnloadUnusedAssets = true,
                Visualization = GameManager.SceneLoadVisualizations.ContinueFromSave,
                Position = new Vector3(envelope.x, envelope.y, envelope.z),
                Completed = completed,
            });
        }

        // #55 recall points: move the hero only. Unlike a save state no save data is
        // loaded, so health, items and progress stay as they are.
        public static void Transport(string scene, Vector3 position, Action<string> completed)
        {
            GameManager game = RequireGame();
            game.BeginSceneTransition(new StateSceneLoadInfo
            {
                SceneName = scene,
                EntryGateName = HeroEntry,
                PreventCameraFadeOut = true,
                WaitForSceneTransitionCameraFade = false,
                Visualization = GameManager.SceneLoadVisualizations.ContinueFromSave,
                Position = position,
                Completed = completed,
            });
        }

        public static void DeleteState(int slot)
        {
            ValidateSlot(slot);
            GameManager game = RequireGame();
            string path = StatePath(slot, game.profileID);
            if (File.Exists(path)) File.Delete(path);
        }

        public static bool StateExists(int slot)
        {
            ValidateSlot(slot);
            GameManager game = RequireGame();
            return File.Exists(StatePath(slot, game.profileID));
        }

        static string StateDirectory => Path.Combine(Application.persistentDataPath, "dualsouls-save-states");
        static string ProfileStateDirectory(int profileId) =>
            Path.Combine(StateDirectory, "profile-" + profileId);
        static string StatePath(int slot, int profileId) =>
            Path.Combine(ProfileStateDirectory(profileId), "slot-" + slot + ".json");
        static void ValidateSlot(int slot)
        {
            if (slot < 1 || slot > 5) throw new ArgumentOutOfRangeException(nameof(slot));
        }
        static GameManager RequireGame()
        {
            GameManager game = GameManager.UnsafeInstance;
            if (game == null || game.playerData == null || game.sceneData == null)
                throw new InvalidOperationException("Hollow Knight save data is not ready.");
            return game;
        }

        static string CaptureStateJson(GameManager game, HeroController hero)
        {
            stateTransferPreparation?.Invoke();
            game.SaveLevelState();
            Vector3 position = hero.transform.position;
            string json = JsonUtility.ToJson(new StateEnvelope
            {
                profileId = game.profileID,
                scene = game.sceneName,
                x = position.x,
                y = position.y,
                z = position.z,
                save = new SaveGameData(game.playerData, game.sceneData),
            });
            if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumStateBytes)
                throw new InvalidDataException("The save-state sidecar exceeds the 4 MiB limit.");
            return json;
        }

        static void WriteStateSidecar(string path, string json)
        {
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException("The save-state sidecar directory is invalid.");
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
            if (fastTimeOwned && Mathf.Abs(Time.timeScale - FastTransitionScale) < 0.01f)
                Time.timeScale = fastTimeBaseline;
            fastTimeOwned = false;
        }

        static void MaintainAutoMap(GameManager game, PlayerData player, bool enabled)
        {
            if (!enabled)
            {
                if (mapCaptured) RestoreMapBaseline(player);
                return;
            }
            if (!mapCaptured) CaptureMapBaseline(player);
            player.hasMap = true;
            player.hasQuill = true;
            if (!string.IsNullOrEmpty(game.sceneName) && !player.scenesMapped.Contains(game.sceneName))
                player.scenesMapped.Add(game.sceneName);
            SetCurrentZoneMap(game.GetCurrentMapZone(), player);
        }

        static void CaptureMapBaseline(PlayerData player)
        {
            mapBaseline = player.hasMap;
            quillBaseline = player.hasQuill;
            mapScenesBaseline = new List<string>(player.scenesMapped);
            mapRegionBaseline = CaptureMapRegions(player);
            mapCaptured = true;
        }

        static MapRegionBaseline CaptureMapRegions(PlayerData player)
        {
            return new MapRegionBaseline
            {
                Dirtmouth = player.mapDirtmouth,
                Crossroads = player.mapCrossroads,
                Greenpath = player.mapGreenpath,
                FogCanyon = player.mapFogCanyon,
                FungalWastes = player.mapFungalWastes,
                RoyalGardens = player.mapRoyalGardens,
                City = player.mapCity,
                Waterways = player.mapWaterways,
                Mines = player.mapMines,
                Deepnest = player.mapDeepnest,
                Cliffs = player.mapCliffs,
                Outskirts = player.mapOutskirts,
                RestingGrounds = player.mapRestingGrounds,
                Abyss = player.mapAbyss,
            };
        }

        static void RestoreMapBaseline(PlayerData player)
        {
            ApplyMapBaseline(player);
            mapScenesBaseline = null;
            mapRegionBaseline = null;
            mapScenesLive = null;
            mapRegionsLive = null;
            mapUpdateDepth = 0;
            mapCaptured = false;
        }

        static void ApplyMapBaseline(PlayerData player)
        {
            player.hasMap = mapBaseline;
            player.hasQuill = quillBaseline;
            player.scenesMapped.Clear();
            if (mapScenesBaseline != null) player.scenesMapped.AddRange(mapScenesBaseline);
            if (mapRegionBaseline != null)
            {
                player.mapDirtmouth = mapRegionBaseline.Dirtmouth;
                player.mapCrossroads = mapRegionBaseline.Crossroads;
                player.mapGreenpath = mapRegionBaseline.Greenpath;
                player.mapFogCanyon = mapRegionBaseline.FogCanyon;
                player.mapFungalWastes = mapRegionBaseline.FungalWastes;
                player.mapRoyalGardens = mapRegionBaseline.RoyalGardens;
                player.mapCity = mapRegionBaseline.City;
                player.mapWaterways = mapRegionBaseline.Waterways;
                player.mapMines = mapRegionBaseline.Mines;
                player.mapDeepnest = mapRegionBaseline.Deepnest;
                player.mapCliffs = mapRegionBaseline.Cliffs;
                player.mapOutskirts = mapRegionBaseline.Outskirts;
                player.mapRestingGrounds = mapRegionBaseline.RestingGrounds;
                player.mapAbyss = mapRegionBaseline.Abyss;
            }
        }

        // Only exact native SetBool writes to these map-owned fields are rebased.
        // Direct mod overlays never pass through this consumer.
        internal static void BeforeAuthoritativeMapBoolSet(PlayerData player, string field, bool value)
        {
            if (!mapCaptured || !ReferenceEquals(owner, player)) return;
            switch (field)
            {
                case "hasMap": mapBaseline = value; break;
                case "hasQuill": quillBaseline = value; break;
                case "mapDirtmouth": mapRegionBaseline.Dirtmouth = value; break;
                case "mapCrossroads": mapRegionBaseline.Crossroads = value; break;
                case "mapGreenpath": mapRegionBaseline.Greenpath = value; break;
                case "mapFogCanyon": mapRegionBaseline.FogCanyon = value; break;
                case "mapFungalWastes": mapRegionBaseline.FungalWastes = value; break;
                case "mapRoyalGardens": mapRegionBaseline.RoyalGardens = value; break;
                case "mapCity": mapRegionBaseline.City = value; break;
                case "mapWaterways": mapRegionBaseline.Waterways = value; break;
                case "mapMines": mapRegionBaseline.Mines = value; break;
                case "mapDeepnest": mapRegionBaseline.Deepnest = value; break;
                case "mapCliffs": mapRegionBaseline.Cliffs = value; break;
                case "mapOutskirts": mapRegionBaseline.Outskirts = value; break;
                case "mapRestingGrounds": mapRegionBaseline.RestingGrounds = value; break;
                case "mapAbyss": mapRegionBaseline.Abyss = value; break;
            }
        }

        internal static void BeginAuthoritativeMapUpdate(PlayerData player)
        {
            if (!mapCaptured || !ReferenceEquals(owner, player)) return;
            if (mapUpdateDepth == 0)
            {
                mapScenesLive = new List<string>(player.scenesMapped);
                mapRegionsLive = CaptureMapRegions(player);
                ApplyMapBaseline(player);
            }
            mapUpdateDepth++;
        }

        internal static void EndAuthoritativeMapUpdate(PlayerData player)
        {
            if (!mapCaptured || !ReferenceEquals(owner, player) || mapUpdateDepth == 0) return;
            if (--mapUpdateDepth != 0) return;
            // The native consumer ran with real quill/region ownership and no borrowed
            // mapped rooms. Its additions (including already overlaid rooms) are genuine.
            CaptureMapBaseline(player);
            foreach (string scene in mapScenesLive)
                if (!player.scenesMapped.Contains(scene)) player.scenesMapped.Add(scene);
            player.hasMap = player.hasQuill = true;
            player.mapDirtmouth |= mapRegionsLive.Dirtmouth;
            player.mapCrossroads |= mapRegionsLive.Crossroads;
            player.mapGreenpath |= mapRegionsLive.Greenpath;
            player.mapFogCanyon |= mapRegionsLive.FogCanyon;
            player.mapFungalWastes |= mapRegionsLive.FungalWastes;
            player.mapRoyalGardens |= mapRegionsLive.RoyalGardens;
            player.mapCity |= mapRegionsLive.City;
            player.mapWaterways |= mapRegionsLive.Waterways;
            player.mapMines |= mapRegionsLive.Mines;
            player.mapDeepnest |= mapRegionsLive.Deepnest;
            player.mapCliffs |= mapRegionsLive.Cliffs;
            player.mapOutskirts |= mapRegionsLive.Outskirts;
            player.mapRestingGrounds |= mapRegionsLive.RestingGrounds;
            player.mapAbyss |= mapRegionsLive.Abyss;
            mapScenesLive = null;
            mapRegionsLive = null;
        }

        static void SetCurrentZoneMap(string zone, PlayerData player)
        {
            switch (zone)
            {
                case "TOWN": case "KINGS_PASS": player.mapDirtmouth = true; break;
                case "CROSSROADS": case "SHAMAN_TEMPLE": player.mapCrossroads = true; break;
                case "GREEN_PATH": player.mapGreenpath = true; break;
                case "FOG_CANYON": case "MONOMON_ARCHIVE": player.mapFogCanyon = true; break;
                case "WASTES": case "QUEENS_STATION": player.mapFungalWastes = true; break;
                case "ROYAL_GARDENS": player.mapRoyalGardens = true; break;
                case "CITY": case "KINGS_STATION": case "SOUL_SOCIETY": case "LURIENS_TOWER": player.mapCity = true; break;
                case "WATERWAYS": case "GODSEEKER_WASTE": player.mapWaterways = true; break;
                case "MINES": player.mapMines = true; break;
                case "DEEPNEST": case "BEASTS_DEN": player.mapDeepnest = true; break;
                case "CLIFFS": player.mapCliffs = true; break;
                case "OUTSKIRTS": case "HIVE": case "COLOSSEUM": player.mapOutskirts = true; break;
                case "RESTING_GROUNDS": player.mapRestingGrounds = true; break;
                case "ABYSS": player.mapAbyss = true; break;
            }
        }

        static void MaintainInnateCharm(PlayerData player, int charm, bool enabled, ref bool owned, ref int baselineCost)
        {
            bool equipped = player.equippedCharms != null && player.equippedCharms.Contains(charm);
            if (enabled)
            {
                if (equipped && !owned) return;
                if (!owned)
                {
                    baselineCost = player.GetInt("charmCost_" + charm);
                    owned = true;
                }
                player.SetInt("charmCost_" + charm, 0);
                if (!equipped)
                {
                    player.EquipCharm(charm);
                    player.SetBool("equippedCharm_" + charm, true);
                    player.CalculateNotchesUsed();
                    HeroController hero = HeroController.UnsafeInstance;
                    if (hero != null) hero.CharmUpdate();
                }
            }
            else if (owned)
            {
                player.UnequipCharm(charm);
                player.SetBool("equippedCharm_" + charm, false);
                player.SetInt("charmCost_" + charm, charmCostsApplied ? 0 : baselineCost);
                player.CalculateNotchesUsed();
                HeroController hero = HeroController.UnsafeInstance;
                if (hero != null) hero.CharmUpdate();
                owned = false;
            }
        }

        static void MaintainCharmCosts(PlayerData player, bool enabled)
        {
            if (enabled && !charmCostsApplied)
            {
                charmCostBaseline = new int[40];
                for (int i = 1; i <= 40; i++)
                {
                    charmCostBaseline[i - 1] = player.GetInt("charmCost_" + i);
                    player.SetInt("charmCost_" + i, 0);
                }
                player.CalculateNotchesUsed();
                charmCostsApplied = true;
            }
            else if (!enabled && charmCostsApplied)
            {
                for (int i = 1; i <= 40; i++)
                    player.SetInt("charmCost_" + i, charmCostBaseline[i - 1]);
                player.CalculateNotchesUsed();
                charmCostBaseline = null;
                charmCostsApplied = false;
            }
        }

        static void MaintainNotches(PlayerData player, bool enabled)
        {
            if (enabled && !notchCaptured)
            {
                notchBaseline = player.charmSlots;
                notchCaptured = true;
            }
            if (enabled)
            {
                if (player.charmSlots > 99) notchBaseline += player.charmSlots - 99;
                player.charmSlots = 99;
                player.overcharmed = false;
            }
            else if (notchCaptured)
            {
                player.charmSlots = notchBaseline;
                player.CalculateNotchesUsed();
                notchCaptured = false;
            }
        }

        static void MaintainEquipAnywhere(PlayerData player, bool enabled)
        {
            EquipAnywhereActive = enabled;
            bool shouldOwn = enabled && global::HKDualScreen.GameInventoryOpen;
            if (shouldOwn && !benchOwned)
            {
                benchBaseline = player.atBench;
                player.atBench = true;
                benchOwned = true;
            }
            else if (!shouldOwn && benchOwned)
            {
                player.atBench = benchBaseline;
                benchOwned = false;
            }
        }

        static void MaintainKeepGeo(PlayerData player, bool enabled)
        {
            int pool = player.geoPool;
            if (!enabled)
            {
                HollowKnightGameplayHooks.CompleteDeathHandling();
                return;
            }
            if (HollowKnightGameplayHooks.DeathObserved && pool > 0)
            {
                player.AddGeo(pool);
                HeroController hero = HeroController.UnsafeInstance;
                if (hero != null) hero.AddGeoToCounter(pool);
                player.geoPool = 0;
                HollowKnightGameplayHooks.CompleteDeathHandling();
            }
        }

        static void RestorePlayerOwned()
        {
            PlayerData player = owner;
            if (player != null)
            {
                if (mapCaptured) RestoreMapBaseline(player);
                MaintainInnateCharm(player, 2, false, ref compassOwned, ref compassCostBaseline);
                MaintainInnateCharm(player, 1, false, ref magnetOwned, ref magnetCostBaseline);
                MaintainCharmCosts(player, false);
                MaintainNotches(player, false);
                MaintainEquipAnywhere(player, false);
            }
            owner = null;
            mapCaptured = false;
            mapScenesBaseline = null;
            mapRegionBaseline = null;
            charmCostBaseline = null;
            charmCostsApplied = false;
            notchCaptured = false;
            compassOwned = false;
            magnetOwned = false;
            benchOwned = false;
        }

        static Sprite PixelSprite()
        {
            if (pixelSprite != null) return pixelSprite;
            pixelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            pixelTexture.SetPixel(0, 0, Color.white);
            pixelTexture.Apply(false);
            pixelSprite = Sprite.Create(pixelTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return pixelSprite;
        }

        static void MaintainCombatOverlays(bool barsEnabled, bool numbersEnabled)
        {
            if (CombatOverlay.NameResolver == null) CombatOverlay.NameResolver = BossName;
            CombatOverlay.Tick(barsEnabled, numbersEnabled, HeroController.UnsafeInstance);
        }
        static void RemoveCombatOverlays() { CombatOverlay.Clear(); }
        // Hollow Knight has no hero-damage flag: acid, splatter and water hits are the environment.
        public static void NoteHitHook() { CombatOverlay.HookCalls++; }

        public static void NoteEnemyHit(HealthManager target, HitInstance hit)
        {
            AttackTypes type = hit.AttackType;
            if (type == AttackTypes.Nail || type == AttackTypes.Spell || type == AttackTypes.SharpShadow
                || type == AttackTypes.NailBeam || type == AttackTypes.Generic)
                CombatOverlay.NoteHeroHit(target);
        }
        // Hollow Knight keeps journal names in conversation sheets; the object name is clear enough.
        static string BossName(HealthManager enemy) =>
            System.Text.RegularExpressions.Regex.Replace(enemy.gameObject.name.Replace("(Clone)", string.Empty), @"( New| \(\d+\)|\d+)$", string.Empty).Replace('_', ' ').Trim();
        static void ReleaseVisualResources()
        {
            if (pixelSprite != null) UnityEngine.Object.Destroy(pixelSprite);
            if (pixelTexture != null) UnityEngine.Object.Destroy(pixelTexture);
            pixelSprite = null;
            pixelTexture = null;
            CombatOverlay.ReleaseResources();
        }

        static void MaintainSecretRadar(GameManager game, bool enabled)
        {
            if (!enabled) { RemoveRadar(); return; }
            string scene = game.sceneName ?? "";
            if (scene != radarScene)
            {
                RemoveRadar();
                radarScene = scene;
                var seen = new HashSet<GameObject>();
                foreach (PlayMakerFSM fsm in UnityEngine.Object.FindObjectsByType<PlayMakerFSM>(FindObjectsSortMode.None))
                {
                    if (fsm == null || !RadarMatch(fsm.FsmName, fsm.gameObject.name) || !seen.Add(fsm.gameObject)) continue;
                    var marker = new GameObject("DualSouls Secret Radar");
                    marker.transform.position = fsm.transform.position + Vector3.up * 0.3f;
                    var renderer = marker.AddComponent<SpriteRenderer>();
                    renderer.sprite = PixelSprite();
                    renderer.color = new Color(0.5f, 0.9f, 1f, 0.8f);
                    renderer.sortingOrder = 3195;
                    radarMarks.Add(marker.transform);
                }
            }
            radarPhase += Time.unscaledDeltaTime * 3.2f;
            float scale = 0.28f + 0.10f * Mathf.Abs(Mathf.Sin(radarPhase));
            for (int i = radarMarks.Count - 1; i >= 0; i--)
            {
                if (radarMarks[i] == null) radarMarks.RemoveAt(i);
                else radarMarks[i].localScale = new Vector3(scale, scale, 1f);
            }
        }

        static bool RadarMatch(string fsmName, string objectName)
        {
            string fsm = (fsmName ?? "").ToLowerInvariant();
            string name = (objectName ?? "").ToLowerInvariant();
            if (fsm.Contains("chain") || name.Contains("chain") || name.Contains("dust") ||
                name.Contains("rubble") || name.Contains("particle") || name.Contains("puff") ||
                name.Contains("corpse")) return false;
            return fsm.Contains("break") || fsm.Contains("collaps") || fsm.Contains("quake") ||
                   name.Contains("breakable") || name.Contains("break wall") ||
                   name.Contains("break floor") || name.StartsWith("collapser", StringComparison.Ordinal) ||
                   name.Contains("secret") || name.Contains("hidden");
        }

        static void RemoveRadar()
        {
            foreach (Transform marker in radarMarks) if (marker != null) UnityEngine.Object.Destroy(marker.gameObject);
            radarMarks.Clear();
            radarScene = null;
        }

        static void MaintainBossRetry(GameManager game, PlayerData player, bool enabled)
        {
            if (!enabled) { bossScene = null; inspectedBossScene = null; bossDeathArmed = false; return; }
            string scene = game.sceneName;
            if (scene != inspectedBossScene && Time.unscaledTime >= nextBossInspection)
            {
                nextBossInspection = Time.unscaledTime + 1.5f;
                bool hasBoss = false;
                foreach (HealthManager enemy in UnityEngine.Object.FindObjectsByType<HealthManager>(FindObjectsSortMode.None))
                    if (enemy != null && enemy.hp >= 200 && enemy.hp < 5000) { hasBoss = true; break; }
                inspectedBossScene = hasBoss ? scene : null;
                if (hasBoss && player.health > 0 && bossScene != scene)
                {
                    bossScene = scene;
                    SaveBossState();
                }
            }
            if (bossScene == null) return;
            if (!bossDeathArmed && player.health <= 0 && scene == bossScene) bossDeathArmed = true;
            else if (bossDeathArmed && player.health > 0 && !game.IsInSceneTransition)
            {
                bossDeathArmed = false;
                LoadBossState();
            }
        }

        static string BossStatePath(int profileId) =>
            Path.Combine(ProfileStateDirectory(profileId), "boss-retry.json");
        static void SaveBossState()
        {
            GameManager game = RequireGame();
            HeroController hero = HeroController.UnsafeInstance;
            if (hero == null) return;
            string json = CaptureStateJson(game, hero);
            WriteStateSidecar(BossStatePath(game.profileID), json);
        }
        static void LoadBossState()
        {
            GameManager game = RequireGame();
            string path = BossStatePath(game.profileID);
            if (File.Exists(path)) LoadStateFile(path);
        }
    }
}
#endif
