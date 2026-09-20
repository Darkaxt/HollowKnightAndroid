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
        static readonly Dictionary<HealthManager, EnemyVisual> enemies = new Dictionary<HealthManager, EnemyVisual>();
        static readonly List<HealthManager> deadEnemies = new List<HealthManager>();
        static readonly List<FloatingNumber> numbers = new List<FloatingNumber>();
        static Texture2D pixelTexture;
        static Sprite pixelSprite;
        static Font numberFont;
        static float nextEnemyScan;
        static readonly List<Transform> radarMarks = new List<Transform>();
        static string radarScene;
        static float radarPhase;
        static string bossScene;
        static string inspectedBossScene;
        static bool bossDeathArmed;
        static float nextBossInspection;
        static Action stateTransferPreparation;

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

        sealed class StateSceneLoadInfo : GameManager.SceneLoadInfo
        {
            public Vector3 Position;
            public override void NotifyFinished()
            {
                base.NotifyFinished();
                HeroController hero = HeroController.UnsafeInstance;
                if (hero != null) hero.transform.position = Position;
            }
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
        }

        public static void SetStateTransferPreparation(Action prepare)
        {
            stateTransferPreparation = prepare;
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
            WriteStateSidecar(StatePath(slot, game.playerData.profileID), json);
        }

        public static void LoadState(int slot)
        {
            ValidateSlot(slot);
            GameManager game = RequireGame();
            LoadStateFile(StatePath(slot, game.playerData.profileID));
        }

        static void LoadStateFile(string path)
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
            int currentProfileId = game.playerData.profileID;
            if (envelope.profileId != currentProfileId ||
                envelope.save.playerData.profileID != currentProfileId)
                throw new InvalidDataException("The selected save-state belongs to another profile.");
            MethodInfo load = typeof(GameManager).GetMethod(
                "SetLoadedGameData", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(SaveGameData), typeof(int) }, null);
            if (load == null) throw new MissingMethodException("GameManager.SetLoadedGameData(SaveGameData,int)");
            stateTransferPreparation?.Invoke();
            load.Invoke(game, new object[] { envelope.save, currentProfileId });
            owner = null;
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

        public static void DeleteState(int slot)
        {
            ValidateSlot(slot);
            GameManager game = RequireGame();
            string path = StatePath(slot, game.playerData.profileID);
            if (File.Exists(path)) File.Delete(path);
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
                profileId = game.playerData.profileID,
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
            mapRegionBaseline = new MapRegionBaseline
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
            mapCaptured = true;
        }

        static void RestoreMapBaseline(PlayerData player)
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
            mapScenesBaseline = null;
            mapRegionBaseline = null;
            mapCaptured = false;
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
            if (!barsEnabled && !numbersEnabled) { RemoveCombatOverlays(); return; }
            if (Time.unscaledTime >= nextEnemyScan)
            {
                nextEnemyScan = Time.unscaledTime + 0.5f;
                foreach (HealthManager enemy in UnityEngine.Object.FindObjectsByType<HealthManager>(FindObjectsSortMode.None))
                    if (enemy != null && enemy.hp > 0 && enemy.hp < 5000 && !enemies.ContainsKey(enemy))
                        enemies.Add(enemy, new EnemyVisual { Maximum = enemy.hp, Last = enemy.hp });
            }
            deadEnemies.Clear();
            foreach (KeyValuePair<HealthManager, EnemyVisual> pair in enemies)
            {
                HealthManager enemy = pair.Key;
                EnemyVisual visual = pair.Value;
                if (enemy == null || enemy.gameObject == null || !enemy.gameObject.activeInHierarchy || enemy.hp <= 0)
                {
                    if (visual.Root != null) UnityEngine.Object.Destroy(visual.Root.gameObject);
                    deadEnemies.Add(enemy);
                    continue;
                }
                if (enemy.hp < visual.Last && numbersEnabled) SpawnNumber(enemy.transform.position, visual.Last - enemy.hp);
                visual.Last = enemy.hp;
                if (enemy.hp > visual.Maximum) visual.Maximum = enemy.hp;
                if (barsEnabled) UpdateBar(enemy, visual);
                else if (visual.Root != null) visual.Root.gameObject.SetActive(false);
            }
            foreach (HealthManager enemy in deadEnemies) enemies.Remove(enemy);
            for (int i = numbers.Count - 1; i >= 0; i--)
            {
                FloatingNumber number = numbers[i];
                if (number.Transform == null || Time.unscaledTime >= number.Until)
                {
                    if (number.Transform != null) UnityEngine.Object.Destroy(number.Transform.gameObject);
                    numbers.RemoveAt(i);
                    continue;
                }
                number.Transform.position += Vector3.up * (Time.unscaledDeltaTime * 1.6f);
                Color color = number.Text.color;
                color.a = Mathf.Clamp01((number.Until - Time.unscaledTime) / 0.5f);
                number.Text.color = color;
            }
        }

        static void UpdateBar(HealthManager enemy, EnemyVisual visual)
        {
            if (visual.Root == null)
            {
                var root = new GameObject("DualSouls Enemy Health");
                root.transform.SetParent(enemy.transform, false);
                root.transform.localPosition = new Vector3(0f, 1.35f, 0f);
                visual.Root = root.transform;
                visual.Fill = BarPart(root.transform, "Fill", new Color(0.92f, 0.25f, 0.2f, 0.92f), 3201, new Vector3(1.44f, 0.10f, 1f));
                BarPart(root.transform, "Background", new Color(0f, 0f, 0f, 0.62f), 3200, new Vector3(1.5f, 0.16f, 1f)).SetSiblingIndex(0);
            }
            float fill = Mathf.Clamp01(visual.Maximum > 0 ? (float)enemy.hp / visual.Maximum : 0f);
            visual.Fill.localScale = new Vector3(1.44f * fill, 0.10f, 1f);
            visual.Fill.localPosition = new Vector3(-0.72f * (1f - fill), 0f, 0f);
            Vector3 scale = enemy.transform.lossyScale;
            visual.Root.localScale = new Vector3(scale.x == 0f ? 1f : 1f / scale.x, scale.y == 0f ? 1f : 1f / scale.y, 1f);
            visual.Root.gameObject.SetActive(true);
        }

        static Transform BarPart(Transform parent, string name, Color color, int order, Vector3 scale)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            var renderer = part.AddComponent<SpriteRenderer>();
            renderer.sprite = PixelSprite();
            renderer.color = color;
            renderer.sortingOrder = order;
            part.transform.localScale = scale;
            return part.transform;
        }

        static void SpawnNumber(Vector3 position, int damage)
        {
            if (damage <= 0) return;
            if (numberFont == null) numberFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            var go = new GameObject("DualSouls Damage Number");
            go.transform.position = position + Vector3.up * 1.6f;
            var text = go.AddComponent<TextMesh>();
            text.text = damage.ToString(System.Globalization.CultureInfo.InvariantCulture);
            text.font = numberFont;
            text.fontSize = 44;
            text.characterSize = 0.14f;
            text.anchor = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 0.92f, 0.5f, 1f);
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && numberFont != null) { renderer.material = numberFont.material; renderer.sortingOrder = 3210; }
            numbers.Add(new FloatingNumber { Transform = go.transform, Text = text, Until = Time.unscaledTime + 1.1f });
        }

        static void RemoveCombatOverlays()
        {
            foreach (EnemyVisual visual in enemies.Values)
                if (visual.Root != null) UnityEngine.Object.Destroy(visual.Root.gameObject);
            enemies.Clear();
            foreach (FloatingNumber number in numbers)
                if (number.Transform != null) UnityEngine.Object.Destroy(number.Transform.gameObject);
            numbers.Clear();
        }

        static void ReleaseVisualResources()
        {
            if (pixelSprite != null) UnityEngine.Object.Destroy(pixelSprite);
            if (pixelTexture != null) UnityEngine.Object.Destroy(pixelTexture);
            pixelSprite = null;
            pixelTexture = null;
            numberFont = null;
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
            WriteStateSidecar(BossStatePath(game.playerData.profileID), json);
        }
        static void LoadBossState()
        {
            GameManager game = RequireGame();
            string path = BossStatePath(game.playerData.profileID);
            if (File.Exists(path)) LoadStateFile(path);
        }
    }
}
#endif
