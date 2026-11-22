#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.IO;
using DualSouls.Mods;
using UnityEngine;

namespace DualSouls.Mods.HollowKnight
{
    /// <summary>
    /// #58 Nexus top-100 review: noclip flight (#49), no dash cooldown (#50), no hazard
    /// damage (#52), disable vignette (#53), recall points (#55) and the extra-mode
    /// unlock (#57). Mods are behaviour references only. Choice effects are overlays
    /// released by <see cref="RestoreAll"/>; nothing here is written into the save.
    /// </summary>
    internal sealed class HollowKnightExtraCheats
    {
        const float FlightSpeed = 20f;

        // QA (#49): a held movement input for the flight, set by the `fly` command,
        // because the host cannot hold the game's stick.
        public static Vector2 QaFlightInput;

        bool _noclip;
        global::HeroController _flightHero;
        Vector3 _flightPosition;
        bool _flightSeeded;

        bool _noDashCooldown;
        global::HeroController _dashHero;
        float[] _dashBaseline;

        bool _noVignette;
        SpriteRenderer _vignette;

        bool _instantDialogue;
        readonly System.Collections.Generic.List<global::DialogueBox> _typewriters =
            new System.Collections.Generic.List<global::DialogueBox>();
        string _dialogueScene;
        float _nextDialogueScan;

        bool _noWorldRumble;
        PlayMakerFSM _shakeFsm;

        static global::HeroController _sparedHero;
        int _recallSlot = 1;
        string _arriveScene;
        string _arriveFrom;
        Vector3 _arrivePosition;
        float _arriveDeadline;

        internal static bool NoHazardDamage;

        public static bool IsChoice(string id) =>
            id == "noclip" || id == "no_dash_cooldown" || id == "no_hazard_damage" ||
            id == "disable_vignette" || id == "instant_dialogue" || id == "disable_world_rumble" ||
            id == "recall_slot";

        public static bool IsCommand(string id) =>
            id == "mark_recall" || id == "recall" || id == "unlock_extra_modes";

        public void SetChoice(string id, string value)
        {
            switch (id)
            {
                case "noclip":
                    _noclip = value == "on";
                    if (!_noclip) ReleaseFlight();
                    break;
                case "no_dash_cooldown":
                    _noDashCooldown = value == "on";
                    if (_noDashCooldown) MaintainDashCooldown(); else RestoreDashCooldown();
                    break;
                case "no_hazard_damage":
                    NoHazardDamage = value == "on";
                    HollowKnightGameplayHooks.SpareHazardHit = NoHazardDamage ? SpareHit : null;
                    break;
                case "disable_vignette":
                    _noVignette = value == "on";
                    if (!_noVignette) RestoreVignette();
                    break;
                case "instant_dialogue":
                    _instantDialogue = value == "on";
                    if (!_instantDialogue) RestoreTypewriters();
                    break;
                case "disable_world_rumble":
                    _noWorldRumble = value == "on";
                    if (!_noWorldRumble) _shakeFsm = null;
                    break;
                case "recall_slot":
                    int slot;
                    _recallSlot = int.TryParse(value, out slot) && slot >= 1 && slot <= 5 ? slot : 1;
                    break;
            }
        }

        public void Tick()
        {
            ReleaseSparedHit();
            MaintainArrival();
            MaintainFlight();
            MaintainDashCooldown();
            MaintainVignette();
            MaintainInstantDialogue();
            MaintainWorldRumble();
        }

        public void RestoreAll()
        {
            _noclip = false;
            ReleaseFlight();
            _noDashCooldown = false;
            RestoreDashCooldown();
            NoHazardDamage = false;
            HollowKnightGameplayHooks.SpareHazardHit = null;
            ReleaseSparedHit();
            _noVignette = false;
            RestoreVignette();
            _instantDialogue = false;
            RestoreTypewriters();
            _noWorldRumble = false;
            _shakeFsm = null;
            _recallSlot = 1;
            _arriveScene = null;
        }

        public void PrepareForStateTransfer()
        {
            ReleaseFlight();
            RestoreDashCooldown();
        }

        // #49: DebugMod-style flight. The hero's position is held and moved with the
        // game's own movement input; damage is cancelled in BeforeTakeDamage. During
        // scene transitions and deaths the game owns the hero, so the hold re-seeds.
        void MaintainFlight()
        {
            global::HeroController hero = global::HeroController.UnsafeInstance;
            if (!ReferenceEquals(hero, _flightHero)) { ReleaseFlight(); _flightHero = hero; }
            HollowKnightGameplayHooks.NoclipActive = false;
            if (!_noclip || hero == null) return;
            if (hero.cState.dead || hero.cState.hazardDeath ||
                hero.transitionState != GlobalEnums.HeroTransitionState.WAITING_TO_TRANSITION)
            {
                _flightSeeded = false;
                return;
            }
            HollowKnightGameplayHooks.NoclipActive = true;
            Vector3 current = hero.transform.position;
            if (!_flightSeeded) { _flightPosition = current; _flightSeeded = true; }
            global::InputHandler input = global::InputHandler.Instance;
            if (hero.acceptingInput && input != null && input.inputActions != null)
            {
                Vector2 move = input.inputActions.moveVector.Vector + QaFlightInput;
                _flightPosition += new Vector3(move.x, move.y, 0f) * (FlightSpeed * Time.deltaTime);
            }
            hero.AffectedByGravity(false);
            Rigidbody2D body = hero.GetComponent<Rigidbody2D>();
            if (body != null) body.velocity = Vector2.zero;
            hero.transform.position = _flightPosition;
        }

        void ReleaseFlight()
        {
            if (_flightSeeded && _flightHero != null && !_flightHero.cState.dead) _flightHero.AffectedByGravity(true);
            _flightSeeded = false;
            QaFlightInput = Vector2.zero;
            HollowKnightGameplayHooks.NoclipActive = false;
        }

        // #50: the game's own dash timers, as QUICK ATTACKS does for the nail.
        void MaintainDashCooldown()
        {
            if (!_noDashCooldown) return;
            global::HeroController hero = global::HeroController.UnsafeInstance;
            if (hero == null || ReferenceEquals(_dashHero, hero)) return;
            RestoreDashCooldown();
            _dashHero = hero;
            _dashBaseline = new[] { hero.DASH_COOLDOWN, hero.DASH_COOLDOWN_CH, hero.SHADOW_DASH_COOLDOWN };
            hero.DASH_COOLDOWN = 0f;
            hero.DASH_COOLDOWN_CH = 0f;
            hero.SHADOW_DASH_COOLDOWN = 0f;
        }

        void RestoreDashCooldown()
        {
            if (_dashHero != null && _dashBaseline != null)
            {
                _dashHero.DASH_COOLDOWN = _dashBaseline[0];
                _dashHero.DASH_COOLDOWN_CH = _dashBaseline[1];
                _dashHero.SHADOW_DASH_COOLDOWN = _dashBaseline[2];
            }
            _dashHero = null;
            _dashBaseline = null;
        }

        // #52: called from the woven TakeDamage head for hazard hits (spikes, acid,
        // pits). The game's own takeNoDamage flag skips the mask loss while the rest of
        // the hit, including the hazard respawn, runs normally. Released next tick.
        static readonly Action SpareHit = SpareHazardHit;

        internal static void SpareHazardHit()
        {
            global::HeroController hero = global::HeroController.UnsafeInstance;
            if (hero == null || hero.takeNoDamage) return;
            hero.takeNoDamage = true;
            _sparedHero = hero;
        }

        static void ReleaseSparedHit()
        {
            if (_sparedHero != null) _sparedHero.takeNoDamage = false;
            _sparedHero = null;
        }

        // #53: the vignette sprite the game draws around the hero.
        void MaintainVignette()
        {
            if (!_noVignette) return;
            global::HeroController hero = global::HeroController.UnsafeInstance;
            SpriteRenderer vignette = hero == null ? null : hero.vignette;
            if (!ReferenceEquals(vignette, _vignette)) RestoreVignette();
            if (vignette == null || !vignette.enabled) return;
            vignette.enabled = false;
            _vignette = vignette;
        }

        void RestoreVignette()
        {
            if (_vignette != null) _vignette.enabled = true;
            _vignette = null;
        }

        // INSTANT DIALOGUE (Silksong parity): the game's dialogue boxes print a whole page at
        // once when their typewriter is off. They live with the HUD, so look again only when
        // the scene changes, or every two seconds while none has been found.
        void MaintainInstantDialogue()
        {
            if (!_instantDialogue) return;
            global::GameManager game = global::GameManager.UnsafeInstance;
            string scene = game != null ? game.sceneName : null;
            bool due = _typewriters.Count == 0 ? UnityEngine.Time.unscaledTime >= _nextDialogueScan
                                               : !string.Equals(scene, _dialogueScene, StringComparison.Ordinal);
            if (!due) return;
            _dialogueScene = scene;
            _nextDialogueScan = UnityEngine.Time.unscaledTime + 2f;
            _typewriters.RemoveAll(box => box == null);
            foreach (global::DialogueBox box in UnityEngine.Object.FindObjectsOfType<global::DialogueBox>())
            {
                if (box == null || !box.useTypeWriter) continue;
                box.useTypeWriter = false;
                _typewriters.Add(box);
            }
        }

        // DISABLE WORLD RUMBLE (Silksong parity): ambient rumbles are the camera-shake FSM's
        // Rumbling Small/Med/Big/Huge states, each left by its own StopRumble event. The
        // hero's focus and fall rumbles are player feedback and stay. Nothing is captured,
        // so turning it off only stops cancelling new rumbles.
        void MaintainWorldRumble()
        {
            if (!_noWorldRumble) return;
            if (_shakeFsm == null)
            {
                global::GameCameras cameras = global::GameManager.UnsafeInstance != null ? global::GameCameras.instance : null;
                _shakeFsm = cameras != null ? cameras.cameraShakeFSM : null;
                if (_shakeFsm == null) return;
            }
            string state = _shakeFsm.ActiveStateName;
            if (state == "RumblingSmall" || state == "RumblingMed" || state == "RumblingBig" || state == "RumblingHuge")
                _shakeFsm.SendEvent("StopRumble");
        }

        void RestoreTypewriters()
        {
            foreach (global::DialogueBox box in _typewriters)
                if (box != null) box.useTypeWriter = true;
            _typewriters.Clear();
            _dialogueScene = null;
            _nextDialogueScan = 0f;
        }

        internal static string Report()
        {
            global::HeroController hero = global::HeroController.UnsafeInstance;
            return "noclip=" + HollowKnightGameplayHooks.NoclipActive + " hazardSpare=" + NoHazardDamage +
                (hero == null ? "" : " dashCd=" + hero.DASH_COOLDOWN + "/" + hero.SHADOW_DASH_COOLDOWN +
                " vignette=" + (hero.vignette != null && hero.vignette.enabled) +
                " pos=" + hero.transform.position.x.ToString("0.0") + "," + hero.transform.position.y.ToString("0.0"));
        }

        [Serializable]
        sealed class RecallPoint
        {
            public string scene;
            public float x;
            public float y;
            public float z;
        }

        public TweakActionResult Run(string id)
        {
            if (id == "unlock_extra_modes") return UnlockExtraModes();
            global::GameManager game = global::GameManager.instance;
            global::HeroController hero = global::HeroController.UnsafeInstance;
            if (game == null || hero == null || game.playerData == null)
                return TweakActionResult.Fail("Load a save before using " + id + ".");
            // The picked slot: a new game's PlayerData.profileID stays 0 until its first save.
            string path = RecallPath(game.profileID, _recallSlot);
            if (id == "mark_recall")
            {
                if (hero.cState.dead || hero.transitionState != GlobalEnums.HeroTransitionState.WAITING_TO_TRANSITION)
                    return TweakActionResult.Fail("Mark a recall point while standing in a room.");
                Vector3 at = hero.transform.position;
                var point = new RecallPoint { scene = game.sceneName, x = at.x, y = at.y, z = at.z };
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(point));
                return Text("recall " + _recallSlot + " marked in " + point.scene);
            }
            if (!File.Exists(path)) return TweakActionResult.Fail("Recall slot " + _recallSlot + " is empty.");
            RecallPoint target = JsonUtility.FromJson<RecallPoint>(File.ReadAllText(path));
            if (target == null || string.IsNullOrEmpty(target.scene))
                return TweakActionResult.Fail("Recall slot " + _recallSlot + " is unreadable.");
            if (hero.cState.dead || hero.transitionState != GlobalEnums.HeroTransitionState.WAITING_TO_TRANSITION)
                return TweakActionResult.Fail("Recall needs the Knight standing in a room.");
            var position = new Vector3(target.x, target.y, target.z);
            if (target.scene == game.sceneName)
            {
                Arrive(position);
                return Text("recalled to slot " + _recallSlot + " in this room");
            }
            _arriveScene = target.scene;
            _arriveFrom = game.sceneName;
            _arrivePosition = position;
            _arriveDeadline = Time.realtimeSinceStartup + 30f;
            HollowKnightGameplayFeatures.Transport(target.scene, position, null);
            return Text("recalling to slot " + _recallSlot + " in " + target.scene);
        }

        // #55: a recall into another room is applied again once the hero has finished
        // entering it, so nothing the room does on entry can leave her elsewhere. A
        // transition that never reaches the room drops the pending recall.
        void MaintainArrival()
        {
            if (_arriveScene == null) return;
            if (Time.realtimeSinceStartup > _arriveDeadline) { _arriveScene = null; return; }
            global::GameManager game = global::GameManager.UnsafeInstance;
            global::HeroController hero = global::HeroController.UnsafeInstance;
            if (game == null || hero == null || game.IsInSceneTransition || hero.cState.dead ||
                hero.transitionState != GlobalEnums.HeroTransitionState.WAITING_TO_TRANSITION) return;
            if (game.sceneName == _arriveFrom) return;
            if (game.sceneName == _arriveScene) Arrive(_arrivePosition);
            _arriveScene = null;
        }

        void Arrive(Vector3 position)
        {
            global::HeroController hero = global::HeroController.UnsafeInstance;
            if (hero == null) return;
            hero.transform.position = position;
            Rigidbody2D body = hero.GetComponent<Rigidbody2D>();
            if (body != null) body.velocity = Vector2.zero;
            _flightSeeded = false;
            // Spikes after a recall respawn the hero here, not in the previous room.
            global::PlayerData data = global::PlayerData.instance;
            if (data != null) data.SetHazardRespawn(position, hero.cState.facingRight);
        }

        static string RecallPath(int profile, int slot) =>
            Path.Combine(Application.persistentDataPath, "dualsouls-recall-points", "hollow-knight", "profile-" + profile, "slot-" + slot + ".json");

        // #57: the game's own status records, as its PermadeathUnlock and Godhome
        // completion set them.
        static TweakActionResult UnlockExtraModes()
        {
            global::GameManager game = global::GameManager.instance;
            if (game == null) return TweakActionResult.Fail("The game is not ready yet.");
            game.SetStatusRecordInt("RecPermadeathMode", 1);
            game.SetStatusRecordInt("RecBossRushMode", 1);
            game.SaveStatusRecords();
            return Text("Steel Soul and Godseeker unlocked");
        }

        static TweakActionResult Text(string value) => TweakActionResult.Ok(TweakReadback.Text(value));
    }
}
#endif
