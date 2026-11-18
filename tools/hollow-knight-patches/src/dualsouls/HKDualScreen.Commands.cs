using System;
using System.Reflection;
using System.Text;
using DualSouls.Mods.HollowKnight;
using UnityEngine;

// [Q] QA COMMANDS — drives what device QA cannot reach through adb input (HK's
// InControl ignores injected key events): pause, the Mods menu, lower tabs,
// save, and reads back the real game state. See DsCommandFile for the channel.
public partial class HKDualScreen
{
    const string CommandHelp =
        "help | report | state | pause | mods | skins | skin <list|row N|left N|right N> | tab <inventory|charms|guide|journal|map> | companion <0|1|config> | " +
        "mods-list | mod <id> <value> | pd <field>... | pdset <bool field> <true|false> | charm give <id>... | geo <amount> | die | hit <damage> [hp] [near] | hurt <n> | dash | travel <scene> <gate> | save | load <slot> | quit-menu | fonts | hudfit | sprites <text> | assists | collect [checklist] | mapdump\n";

    // QA: one hero nail hit on the nearest live enemy through HealthManager.Hit, as a slash
    // would land. The optional hp first sets the target's health, e.g. above the boss threshold.
    string CommandHit(string[] a, GameManager gm)
    {
        int damage, hp = 0;
        bool near = a.Length > 2 && a[a.Length - 1] == "near";
        int numbers = near ? a.Length - 1 : a.Length;
        if (numbers < 2 || !int.TryParse(a[1], out damage) || damage <= 0 || (numbers > 2 && !int.TryParse(a[2], out hp)))
            return "error hit <damage> [hp] [near]";
        string guard = SaveSlotGuard(gm);
        if (guard != null) return guard;
        var hero = HeroController.UnsafeInstance;
        if (hero == null) return "error no HeroController";
        HealthManager target = null;
        float best = float.MaxValue;
        foreach (HealthManager enemy in UnityEngine.Object.FindObjectsOfType<HealthManager>())
        {
            if (enemy.isDead || enemy.hp <= 0 || !enemy.gameObject.activeInHierarchy) continue;
            float distance = (enemy.transform.position - hero.transform.position).sqrMagnitude;
            if (distance < best) { best = distance; target = enemy; }
        }
        if (target == null) return "error no live enemy in scene";
        if (hp > 0) target.hp = hp;
        // near: bring the target beside the hero so its bar and damage number are on screen.
        if (near) target.transform.position = hero.transform.position + new Vector3(3f, 1f, 0f);
        int before = target.hp, hooks = DualSouls.Mods.CombatOverlay.HookCalls, heroHits = DualSouls.Mods.CombatOverlay.HeroHits;
        target.Hit(new HitInstance
        {
            Source = hero.gameObject, AttackType = AttackTypes.Nail, DamageDealt = damage,
            Multiplier = 1f, MagnitudeMultiplier = 1f, IgnoreInvulnerable = true,
        });
        return "ok target=" + target.gameObject.name + " hp=" + before + "->" + target.hp + " distance=" + Mathf.Sqrt(best).ToString("0.0")
            + " hooks=+" + (DualSouls.Mods.CombatOverlay.HookCalls - hooks) + " heroHits=+" + (DualSouls.Mods.CombatOverlay.HeroHits - heroHits);
    }

    void PollCommands() { DsCommandFile.Poll(RunCommand); }

    string RunCommand(string line)
    {
        string[] a = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        var gm = GameManager.instance;
        var ui = UIManager.instance;
        switch (a[0].ToLowerInvariant())
        {
            case "help": return CommandHelp;
            case "report": return CommandReport(gm, ui);
            case "collect":
                // #37: what the checklist shows and how many pins are on the map; "checklist" toggles the panel.
                if (a.Length > 1 && a[1] == "checklist") HollowKnightPatches.Lower.HkMapScreen.RequestChecklistToggle = true;
                return "collect " + HollowKnightPatches.Lower.HkMapScreen.ChecklistReport() +
                       " pins=" + HollowKnightPatches.Lower.HkMapView.LastPinsShown;
            case "state": return CommandState(gm, ui);
            case "mapdump":
                return (HollowKnightPatches.Lower.HkMapView.Last != null ? HollowKnightPatches.Lower.HkMapView.Last.Dump() : "error no map view") +
                       "\n" + (HollowKnightPatches.Lower.HkMapScreen.Last != null ? HollowKnightPatches.Lower.HkMapScreen.Last.RawDump() : "no map page");
            case "pause":
                if (gm == null) return "error no GameManager";
                bool was = gm.IsGamePaused();
                gm.StartCoroutine(gm.PauseGameToggle());
                return "ok was-paused=" + was;
            case "mods": return HollowKnightNativeModsMenu.OpenForCommand(0);
            case "skins": return HollowKnightNativeModsMenu.OpenForCommand(1);
            case "skin":
                if (a.Length < 2) return "error skin <list|row N|left N|right N>";
                if (a[1] != "list" && (a.Length < 3 || (a[1] != "row" && a[1] != "left" && a[1] != "right")))
                    return "error skin <list|row N|left N|right N>";
                return HollowKnightNativeModsMenu.SkinCommand(a[1], a.Length > 2 ? a[2] : "");
            case "tab":
                if (a.Length < 2) return "error tab <id>";
                if (shell == null) return "error no shell";
                return shell.ShowId(a[1]) ? "ok active=" + shell.ActiveId : "error unknown tab";
            case "companion":
                if (a.Length < 2) return "error companion <0|1|config>";
                compOn = a[1] == "config" ? -1 : (a[1] == "1" ? 1 : 0);
                return "ok compOn=" + compOn;
            case "mods-list": return CommandModsList();
            case "assists": return HollowKnightGameplayFeatures.DeathReport() + "\n" + HollowKnightCheats.AssistReport();
            case "mod":
            {
                if (a.Length < 3) return "error mod <id> <value>";
                var c = ModsController();
                if (c == null) return "error Mods session not ready";
                var r = c.Set(a[1], a[2]);
                return (r.Success ? "ok" : "error " + r.Error) + (r.Pending ? " pending" : "") + " value=" + c.Value(a[1])
                    + (r.Readback.Kind == DualSouls.Mods.TweakReadbackKind.Text ? " result=" + r.Readback.Value : "");
            }
            case "pd": return CommandPlayerData(a);
            case "pdset":
                if (a.Length != 3) return "error pdset <bool field> <true|false>";
                return SaveSlotGuard(gm) ?? DsQaReport.SetBool(PlayerData.instance, a[1], a[2]);
            case "charm": return SaveSlotGuard(gm) ?? CommandCharm(a);
            case "geo":
            {
                string guard = SaveSlotGuard(gm);
                if (guard != null) return guard;
                int amount;
                if (a.Length < 2 || !int.TryParse(a[1], out amount) || amount <= 0) return "error geo <amount>";
                var hero = HeroController.instance;
                if (hero == null) return "error no HeroController";
                hero.AddGeo(amount);
                return "ok geo=" + (PlayerData.instance != null ? PlayerData.instance.geo : -1);
            }
            case "hit": return CommandHit(a, gm);
            case "hurt": return CommandHurt(a, gm);
            case "dash": return CommandDash(gm);
            case "travel": return CommandTravel(a, gm);
            case "die":
            {
                // QA: one lethal hit through the game's own damage path, so death rotation runs for real.
                var hero = HeroController.instance;
                var pd = PlayerData.instance;
                if (hero == null || pd == null) return "error no HeroController";
                if (gm == null || gm.gameState != GlobalEnums.GameState.PLAYING) return "error not playing";
                string guard = SaveSlotGuard(gm);
                if (guard != null) return guard;
                pd.healthBlue = 0;
                pd.health = 1;
                hero.TakeDamage(hero.gameObject, GlobalEnums.CollisionSide.top, 1, 1);
                return "ok health=" + pd.health;
            }
            case "save":
                if (gm == null) return "error no GameManager";
                {
                    string guard = SaveSlotGuard(gm);
                    if (guard != null) return guard;
                }
                gm.SaveGame();
                return "ok slot=" + gm.profileID;
            case "load":
            {
                // QA: load a save through the game's own profile-menu path; injected gamepad
                // presses lose their key-up on this device and leave the menu scrolling.
                int slot;
                if (a.Length < 2 || !int.TryParse(a[1], out slot) || slot < 1 || slot > 4) return "error load <1-4>";
                if (gm == null || gm.gameState != GlobalEnums.GameState.MAIN_MENU) return "error not on the main menu";
                gm.LoadGameFromUI(slot);
                return "ok loading slot " + slot;
            }
            case "quit-menu":
                if (ui == null) return "error no UIManager";
                ui.StartCoroutine(ui.ReturnToMainMenu());
                return "ok";
            case "fonts": return DsTheme.Report();
            case "hudfit": return CommandHudFit();
            case "sprites": return a.Length < 2 ? "error sprites <text>" : HollowKnightPatches.Lower.HkSprites.List(a[1], 60);
            default: return "error unknown command; " + CommandHelp;
        }
    }

    string CommandState(GameManager gm, UIManager ui)
    {
        var sb = new StringBuilder();
        sb.Append("scene=").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        if (gm != null) sb.Append(" game=").Append(gm.gameState).Append(" paused=").Append(gm.IsGamePaused()).Append(" slot=").Append(gm.profileID);
        if (ui != null) sb.Append(" ui=").Append(ui.uiState).Append(" menu=").Append(ui.menuState);
        sb.Append("\nshell=").Append(shell != null ? shell.ActiveId : "-").Append(" shown=").Append(shellShown)
          .Append(" inGame=").Append(shellInGame).Append(" shellPaused=").Append(shell != null && shell.Paused)
          .Append(" companion=").Append(compOn).Append(" credit=").Append(creditNow).Append(" popupBlack=").Append(popupBlack)
          .Append(" hudWanted=").Append(shellHudWanted).Append(" hudFit=").Append(nativeHudFitReady);
        return sb.ToString();
    }

    // One call for device QA: everything the per-candidate checklist reads, so no UI navigation is needed.
    string CommandReport(GameManager gm, UIManager ui)
    {
        var sb = new StringBuilder();
        sb.Append("report: game=Hollow Knight version=").Append(Application.version)
          .Append(" time=").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        sb.Append(CommandState(gm, ui)).Append('\n');
        sb.Append(shell != null ? shell.Report() : "panes: no shell").Append('\n');
        sb.Append("hudfit: ").Append(CommandHudFit()).Append('\n');
        sb.Append("player: ").Append(DsQaReport.Fields(PlayerData.instance,
            "health", "maxHealth", "healthBlue", "MPCharge", "MPReserve", "geo", "charmSlots", "charmSlotsFilled",
            "equippedCharms", "overcharmed")).Append('\n');
        var mods = ModsController();
        sb.Append("mods: ").Append(mods != null ? "\n" + CommandModsList() : "not ready").Append('\n');
        var skins = DualSouls.Skins.HollowKnight.Runtime.HollowKnightSkinRuntime.Current;
        sb.Append("skins: stamp=").Append(DualSouls.Skins.HollowKnight.Runtime.HollowKnightSkinRuntime.SkinStamp)
          .Append(' ').Append(skins != null ? DsQaReport.Fields(skins.LastResult, "Status", "Detail") : "no runtime")
          .Append(" pack: ").Append(skins != null ? DsQaReport.Fields(skins.CurrentPack, "Id", "Mode", "SpriteScope") : "none").Append('\n');
        sb.Append(DualSouls.Mods.HeroTint.Diagnostics).Append('\n');
        sb.Append(DualSouls.Mods.CombatOverlay.Describe()).Append('\n');
        sb.Append(DsQaReport.Frames()).Append('\n');
        sb.Append(DsQaReport.Errors());
        return sb.ToString();
    }

    static DualSouls.Mods.TweakController ModsController()
    {
        var runtime = HollowKnightModsRuntime.Current;
        var session = runtime != null ? runtime.Session : null;
        return session != null && session.IsReady ? session.Controller : null;
    }

    static string CommandModsList()
    {
        var c = ModsController();
        if (c == null) return "error Mods session not ready";
        var sb = new StringBuilder();
        foreach (var d in c.Descriptors)
        {
            sb.Append(d.Group).Append('/').Append(d.Id).Append('=').Append(c.Value(d.Id));
            if (!d.IsAvailable) sb.Append(" (unavailable: ").Append(d.UnavailableReason).Append(')');
            sb.Append(" [").Append(string.Join(",", d.Values)).Append("]\n");
        }
        return sb.ToString().TrimEnd('\n');
    }

    static string CommandPlayerData(string[] a)
    {
        if (a.Length < 2) return "error pd <field>...";
        var names = new string[a.Length - 1];
        Array.Copy(a, 1, names, 0, names.Length);
        return PlayerData.instance != null ? DsQaReport.Fields(PlayerData.instance, names) : "error no PlayerData";
    }

    // QA hooks for what injected input cannot reach. hurt: non-lethal damage
    // through the game's own TakeHealth, so the HUD follows.
    static string CommandHurt(string[] a, GameManager gm)
    {
        int amount;
        if (a.Length < 2 || !int.TryParse(a[1], out amount) || amount <= 0) return "error hurt <amount>";
        string guard = SaveSlotGuard(gm);
        if (guard != null) return guard;
        var pd = PlayerData.instance;
        if (pd == null) return "error no PlayerData";
        pd.TakeHealth(Mathf.Min(amount, pd.health - 1));
        return "ok " + DsQaReport.Fields(pd, "health", "maxHealth");
    }

    // dash: the hero's own dash start, as the dash button would trigger it.
    static string CommandDash(GameManager gm)
    {
        string guard = SaveSlotGuard(gm);
        if (guard != null) return guard;
        var hero = HeroController.instance;
        if (hero == null) return "error no HeroController";
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        MethodInfo canDash = typeof(HeroController).GetMethod("CanDash", Private, null, Type.EmptyTypes, null);
        MethodInfo dash = typeof(HeroController).GetMethod("HeroDash", Private, null, Type.EmptyTypes, null);
        if (canDash == null || dash == null) return "error no HeroController.CanDash/HeroDash";
        if (!(bool)canDash.Invoke(hero, null)) return "error hero cannot dash now";
        dash.Invoke(hero, null);
        return "ok " + DsQaReport.Fields(hero.cState, "dashing");
    }

    // travel: a normal scene transition through a named entrance.
    static string CommandTravel(string[] a, GameManager gm)
    {
        if (a.Length < 3) return "error travel <scene> <gate>";
        string guard = SaveSlotGuard(gm);
        if (guard != null) return guard;
        if (gm.gameState != GlobalEnums.GameState.PLAYING || gm.IsInSceneTransition) return "error not playing";
        gm.BeginSceneTransition(new GameManager.SceneLoadInfo
        {
            SceneName = a[1],
            EntryGateName = a[2],
            PreventCameraFadeOut = false,
            WaitForSceneTransitionCameraFade = true,
            Visualization = GameManager.SceneLoadVisualizations.Default,
        });
        return "ok travelling to " + a[1] + " via " + a[2];
    }

    // Every slot on the test device is QA data (owner, 2026-11-18), so no slot is refused.
    static string SaveSlotGuard(GameManager gm)
    {
        if (gm == null) return "error no GameManager";
        return null;
    }

    // QA: own charms on a disposable save so the Charms pane can be exercised without playing to them.
    static string CommandCharm(string[] a)
    {
        var pd = PlayerData.instance;
        if (pd == null) return "error no PlayerData";
        if (a.Length < 3 || a[1] != "give") return "error charm give <id>...";
        var sb = new StringBuilder("ok");
        for (int i = 2; i < a.Length; i++)
        {
            int id;
            FieldInfo f = int.TryParse(a[i], out id)
                ? typeof(PlayerData).GetField("gotCharm_" + id, BindingFlags.Public | BindingFlags.Instance) : null;
            if (f == null || f.FieldType != typeof(bool)) { sb.Append(' ').Append(a[i]).Append("=?"); continue; }
            f.SetValue(pd, true);
            sb.Append(' ').Append(id).Append("=owned");
        }
        return sb.ToString();
    }

    string CommandHudFit()
    {
        var sb = new StringBuilder();
        var d = nativeHudDestination;
        sb.Append("fitReady=").Append(nativeHudFitReady).Append(" bound=").Append(nativeHudBound)
          .Append(" owners=").Append(nativeHudOwnerCount).Append(" dest=").Append(d.x.ToString("F0")).Append(',').Append(d.y.ToString("F0"))
          .Append(' ').Append(d.width.ToString("F0")).Append('x').Append(d.height.ToString("F0"))
          .Append(" view=").Append(nativeHudViewMin).Append("..").Append(nativeHudViewMax);
        var src = hudCameraStateSource;
        if (src != null) sb.Append(" srcOrtho=").Append(src.orthographicSize.ToString("F2")).Append(" srcAspect=").Append(src.aspect.ToString("F2"));
        for (int i = 0; i < nativeHudOwnerCount; i++)
        {
            var o = nativeHudOwners[i];
            if (o == null || !o.Included) continue;
            sb.Append('\n').Append(NativeHudOwnerName(i)).Append(o.FitExempt ? " (fit-exempt)" : "").Append(" ready=").Append(o.Ready)
              .Append(" c=").Append(o.Bounds.center).Append(" s=").Append(o.Bounds.size);
            if (o.Text != null)
                sb.Append(" text='").Append(o.Text()).Append("' a=").Append(o.TextColor != null ? o.TextColor().a.ToString("F2") : "?");
        }
        return sb.ToString();
    }
}
