#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using DualSouls.Mods;
using GlobalEnums;
using UnityEngine;

namespace DualSouls.Mods.Silksong
{
    /// <summary>
    /// #58 Nexus top-100 review: noclip flight (#49), no dash cooldown (#50), no hazard
    /// damage (#52), disable vignette (#53), Needolin Silk regeneration (#54), recall
    /// points (#55) and the Steel Soul unlock (#57). Mods are behaviour references
    /// only. Choice effects are overlays released by <see cref="RestoreAll"/>; nothing
    /// here is written into the save.
    /// </summary>
    internal sealed class SilksongExtraCheats
    {
        const float FlightSpeed = 20f;
        const float NeedolinSilkInterval = 1f;

        bool _noclip;
        HeroController _flightHero;
        Vector3 _flightPosition;
        bool _flightSeeded;

        bool _noDashCooldown;
        HeroController _dashHero;
        float _dashBaseline;

        bool _noVignette;
        SpriteRenderer _vignette;

        bool _needolinSilk;
        float _needolinAt;
        static int _needolinGrants;

        static HeroController _sparedHero;
        static FieldInfo _takeNoDamage;
        int _recallSlot = 1;

        internal static bool NoHazardDamage;

        public static bool IsChoice(string id) =>
            id == "noclip" || id == "no_dash_cooldown" || id == "no_hazard_damage" ||
            id == "disable_vignette" || id == "needolin_silk_regen" || id == "recall_slot";

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
                    break;
                case "disable_vignette":
                    _noVignette = value == "on";
                    if (!_noVignette) RestoreVignette();
                    break;
                case "needolin_silk_regen":
                    _needolinSilk = value == "on";
                    _needolinAt = 0f;
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
            MaintainFlight();
            MaintainDashCooldown();
            MaintainVignette();
            MaintainNeedolinSilk();
        }

        public void RestoreAll()
        {
            _noclip = false;
            ReleaseFlight();
            _noDashCooldown = false;
            RestoreDashCooldown();
            NoHazardDamage = false;
            ReleaseSparedHit();
            _noVignette = false;
            RestoreVignette();
            _needolinSilk = false;
            _recallSlot = 1;
        }

        public void PrepareForStateTransfer()
        {
            ReleaseFlight();
            RestoreDashCooldown();
        }

        // #49: DebugMod-style flight. The hero's position is held and moved with the
        // game's own movement input; damage is cancelled in BeforeHeroDamage. During
        // scene transitions and deaths the game owns the hero, so the hold re-seeds.
        void MaintainFlight()
        {
            HeroController hero = HeroController.UnsafeInstance;
            if (!ReferenceEquals(hero, _flightHero)) { ReleaseFlight(); _flightHero = hero; }
            SilksongGameplayHooks.NoclipActive = false;
            if (!_noclip || hero == null) return;
            if (hero.cState.dead || hero.cState.hazardDeath ||
                hero.transitionState != HeroTransitionState.WAITING_TO_TRANSITION)
            {
                _flightSeeded = false;
                return;
            }
            SilksongGameplayHooks.NoclipActive = true;
            if (!_flightSeeded) { _flightPosition = hero.transform.position; _flightSeeded = true; }
            InputHandler input = InputHandler.UnsafeInstance;
            if (hero.acceptingInput && input != null && input.inputActions != null)
            {
                Vector2 move = input.inputActions.MoveVector.Vector;
                _flightPosition += new Vector3(move.x, move.y, 0f) * (FlightSpeed * Time.deltaTime);
            }
            hero.AffectedByGravity(false);
            hero.ResetVelocity();
            hero.transform.position = _flightPosition;
        }

        void ReleaseFlight()
        {
            if (_flightSeeded && _flightHero != null && !_flightHero.cState.dead) _flightHero.AffectedByGravity(true);
            _flightSeeded = false;
            SilksongGameplayHooks.NoclipActive = false;
        }

        // #50: the game's own dash timer, as QUICK ATTACKS does for the Needle.
        void MaintainDashCooldown()
        {
            if (!_noDashCooldown) return;
            HeroController hero = HeroController.UnsafeInstance;
            if (hero == null || ReferenceEquals(_dashHero, hero)) return;
            RestoreDashCooldown();
            _dashHero = hero;
            _dashBaseline = hero.DASH_COOLDOWN;
            hero.DASH_COOLDOWN = 0f;
        }

        void RestoreDashCooldown()
        {
            if (_dashHero != null) _dashHero.DASH_COOLDOWN = _dashBaseline;
            _dashHero = null;
        }

        // #52: called from the woven TakeDamage head for hazard hits. The game's own
        // takeNoDamage flag skips the mask loss while the rest of the hit, including
        // the hazard respawn, runs normally. Released on the next tick.
        internal static void SpareHazardHit()
        {
            HeroController hero = HeroController.UnsafeInstance;
            if (hero == null) return;
            if (_takeNoDamage == null)
                _takeNoDamage = typeof(HeroController).GetField("takeNoDamage", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_takeNoDamage == null || (bool)_takeNoDamage.GetValue(hero)) return;
            _takeNoDamage.SetValue(hero, true);
            _sparedHero = hero;
        }

        static void ReleaseSparedHit()
        {
            if (_sparedHero != null && _takeNoDamage != null) _takeNoDamage.SetValue(_sparedHero, false);
            _sparedHero = null;
        }

        // #53: the vignette sprite the game draws around the hero.
        void MaintainVignette()
        {
            if (!_noVignette) return;
            HeroController hero = HeroController.UnsafeInstance;
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

        // #54: Silk through the game's own path while the Needolin is played.
        // Scaled time, so pause stops the clock.
        void MaintainNeedolinSilk()
        {
            if (!_needolinSilk) return;
            HeroController hero = HeroController.UnsafeInstance;
            if (hero == null || hero.cState.dead || !HeroPerformanceRegion.IsPerforming)
            {
                _needolinAt = 0f;
                return;
            }
            if (_needolinAt <= 0f) { _needolinAt = Time.time + NeedolinSilkInterval; return; }
            if (Time.time < _needolinAt) return;
            hero.AddSilk(1, false);
            _needolinGrants++;
            _needolinAt = Time.time + NeedolinSilkInterval;
        }

        internal static string Report()
        {
            HeroController hero = HeroController.UnsafeInstance;
            PlayerData data = PlayerData.HasInstance ? PlayerData.instance : null;
            return "noclip=" + SilksongGameplayHooks.NoclipActive + " hazardSpare=" + NoHazardDamage +
                " performing=" + HeroPerformanceRegion.IsPerforming + " needolinGrants=" + _needolinGrants +
                (data == null ? "" : " silk=" + data.silk) +
                (hero == null ? "" : " dashCd=" + hero.DASH_COOLDOWN +
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
            GameManager game = GameManager.instance;
            HeroController hero = HeroController.UnsafeInstance;
            if (game == null || hero == null || game.playerData == null)
                return TweakActionResult.Fail("Load a save before using " + id + ".");
            string path = RecallPath(game.playerData.profileID, _recallSlot);
            bool standing = !hero.cState.dead && hero.transitionState == HeroTransitionState.WAITING_TO_TRANSITION;
            if (id == "mark_recall")
            {
                if (!standing) return TweakActionResult.Fail("Mark a recall point while standing in a room.");
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
            if (!standing) return TweakActionResult.Fail("Recall needs Hornet standing in a room.");
            var position = new Vector3(target.x, target.y, target.z);
            if (target.scene == game.sceneName)
            {
                Arrive(position);
                return Text("recalled to slot " + _recallSlot + " in this room");
            }
            SilksongGameplayFeatures.Transport(target.scene, position, scene => Arrive(position));
            return Text("recalling to slot " + _recallSlot + " in " + target.scene);
        }

        void Arrive(Vector3 position)
        {
            HeroController hero = HeroController.UnsafeInstance;
            if (hero == null) return;
            hero.transform.position = position;
            hero.ResetVelocity();
            _flightSeeded = false;
            // Hazards after a recall respawn Hornet here, not in the previous room.
            PlayerData data = PlayerData.HasInstance ? PlayerData.instance : null;
            if (data != null) data.SetHazardRespawn(position, hero.cState.facingRight);
        }

        static string RecallPath(int profile, int slot) =>
            Path.Combine(Application.persistentDataPath, "dualsouls-recall-points", "silksong", "profile-" + profile, "slot-" + slot + ".json");

        // #57: the game's own Steel Soul record, as its PermadeathUnlock sets it.
        static TweakActionResult UnlockExtraModes()
        {
            if (GameManager.instance == null) return TweakActionResult.Fail("The game is not ready yet.");
            PermadeathUnlock.Unlock();
            return Text("Steel Soul unlocked");
        }

        static TweakActionResult Text(string value) => TweakActionResult.Ok(TweakReadback.Text(value));
    }
}
#endif
