// [Q] QA COMMANDS for Silksong — the same app-owned channel as Hollow Knight's
// (see DsCommandFile): adb drops dualsouls-cmd.txt, replies land in
// dualsouls-cmd.out. `report` returns everything the per-candidate device
// checklist reads in one call. There is deliberately no `save` here: a
// reflection-chosen SaveGame overload could take a slot number.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Reflection;
using System.Text;
using DualSouls.Mods.Silksong;
using DualSouls.Skins.Silksong.Runtime;
using UnityEngine;

public partial class DualScreenV2
{
    const string CommandHelp =
        "help | report | state | pause | mods | skins | skin <list|row N|left N|right N|close> | tab <id> | mods-list | mod <id> <value> | pd <field>... | pdset <bool|int field> <value> | mapsetup | die, hit <damage> [hp] [near] | hurt <n>, dash, travel <scene> <gate> | delivery <list|give <name>|hit|break|state> | sprites | assists | collect [checklist]";

    static readonly string[] ReportPlayerFields =
        { "health", "maxHealth", "healthBlue", "silk", "silkMax", "geo", "ShellShards" };

    // QA: one hero nail hit on the nearest live enemy through HealthManager.Hit, as a slash
    // would land. The optional hp first sets the target's health, e.g. above the boss threshold.
    string CommandHit(string[] a, GameManager gm)
    {
        int damage, hp = 0;
        bool near = a.Length > 2 && a[a.Length - 1] == "near";
        int numbers = near ? a.Length - 1 : a.Length;
        if (numbers < 2 || !int.TryParse(a[1], out damage) || damage <= 0 || (numbers > 2 && !int.TryParse(a[2], out hp)))
            return "error hit <damage> [hp] [near]";
        if (gm == null) return "error no GameManager";
        var hero = HeroController.SilentInstance;
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
            Multiplier = 1f, MagnitudeMultiplier = 1f, IgnoreInvulnerable = true, IsHeroDamage = true,
        });
        return "ok target=" + target.gameObject.name + " hp=" + before + "->" + target.hp + " distance=" + Mathf.Sqrt(best).ToString("0.0")
            + " hooks=+" + (DualSouls.Mods.CombatOverlay.HookCalls - hooks) + " heroHits=+" + (DualSouls.Mods.CombatOverlay.HeroHits - heroHits);
    }

    void PollCommands() { DsCommandFile.Poll(RunCommand); }

    string RunCommand(string line)
    {
        string[] a = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        GameManager gm = _gameManager;
        switch (a[0].ToLowerInvariant())
        {
            case "help": return CommandHelp;
            case "report": return CommandReport(gm);
            case "collect":
                // #37: what the checklist shows and how many pins are on the map; "checklist" toggles the panel.
                if (a.Length > 1 && a[1] == "checklist") DsMapScreen.RequestChecklistToggle = true;
                return "collect " + DsMapScreen.ChecklistReport() + " pins=" + SsCollectibles.PinsShown;
            case "state": return CommandState(gm);
            case "pause":
                if (gm == null) return "error no GameManager";
                bool was = NativeGamePaused();
                return DsQaReport.Invoke(gm, gm, "PauseGameToggle", null) + " was-paused=" + was;
            case "mods": return SilksongNativeModsMenu.OpenForCommand(0);
            case "skins": return SilksongNativeModsMenu.OpenForCommand(1);
            case "skin":
                if (a.Length < 2 || (a[1] != "list" && a[1] != "close" &&
                    (a.Length < 3 || (a[1] != "row" && a[1] != "left" && a[1] != "right"))))
                    return "error skin <list|row N|left N|right N|close>";
                return SilksongNativeModsMenu.SkinCommand(a[1], a.Length > 2 ? a[2] : "");
            case "tab":
                if (a.Length < 2) return "error tab <id>";
                if (_shell == null) return "error no shell";
                return _shell.ShowId(a[1]) ? "ok active=" + _shell.ActiveId : "error unknown tab";
            case "mods-list": return CommandModsList();
            case "assists": return SilksongGameplayFeatures.DeathReport() + "\n" + SilksongCheats.AssistReport();
            case "mod":
            {
                if (a.Length < 3) return "error mod <id> <value>";
                var c = ModsController();
                if (c == null) return "error Mods session not ready";
                var r = c.Set(a[1], a[2]);
                return (r.Success ? "ok" : "error " + r.Error) + (r.Pending ? " pending" : "") + " value=" + c.Value(a[1])
                    + (r.Readback.Kind == DualSouls.Mods.TweakReadbackKind.Text ? " result=" + r.Readback.Value : "");
            }
            case "pd":
                if (a.Length < 2) return "error pd <field>...";
                var names = new string[a.Length - 1];
                Array.Copy(a, 1, names, 0, names.Length);
                return DsQaReport.Fields(PlayerData.instance, names);
            case "pdset":
                if (a.Length != 3) return "error pdset <bool|int field> <value>";
                return DsQaReport.SetBool(PlayerData.instance, a[1], a[2]);
            case "mapsetup":
                // The game's own room-state pass (what opening its map runs): mapped/visited rooms
                // pick up scenesMapped, mapAllRooms and the quill. QA after pdset on a disposable save.
                if (gm == null || gm.gameMap == null) return "error no GameMap";
                gm.gameMap.SetupMap(false);
                return "ok SetupMap " + DsQaReport.Fields(PlayerData.instance, "mapAllRooms", "hasQuill");
            case "die": return CommandDie(gm);
            case "hit": return CommandHit(a, gm);
            case "hurt": return CommandHurt(a, gm);
            case "dash": return CommandDash(gm);
            case "travel": return CommandTravel(a, gm);
            case "delivery": return CommandDelivery(a, gm);
            case "sprites":
                var skinRuntime = SilksongSkinRuntime.Current;
                return skinRuntime != null ? skinRuntime.DescribeCollections() : "error no skin runtime";
            default: return "error unknown command; " + CommandHelp;
        }
    }

    string CommandState(GameManager gm)
    {
        var sb = new StringBuilder();
        sb.Append("scene=").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        if (gm != null)
        {
            sb.Append(" game=").Append(gm.GameState).Append(" paused=").Append(NativeGamePaused())
              .Append(' ').Append(DsQaReport.Fields(gm, "profileID"));
            if (gm.ui != null) sb.Append(" ui=").Append(gm.ui.uiState);
        }
        else sb.Append(" game=no GameManager");
        sb.Append("\nshell=").Append(_shell != null ? _shell.ActiveId : "-")
          .Append(" shellPaused=").Append(_shell != null && _shell.Paused)
          .Append(" card=").Append(_card != null);
        return sb.ToString();
    }

    // One call for device QA: everything the per-candidate checklist reads, so no UI navigation is needed.
    string CommandReport(GameManager gm)
    {
        var sb = new StringBuilder();
        sb.Append("report: game=Silksong version=").Append(Application.version)
          .Append(" time=").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        sb.Append(CommandState(gm)).Append('\n');
        sb.Append(_shell != null ? _shell.Report() : "panes: no shell").Append('\n');
        sb.Append("player: ").Append(DsQaReport.Fields(PlayerData.instance, ReportPlayerFields)).Append('\n');
        sb.Append("mods: ").Append(ModsController() != null ? "\n" + CommandModsList() : "not ready").Append('\n');
        var skins = SilksongSkinRuntime.Current;
        sb.Append("skins: ").Append(skins != null
            ? DsQaReport.Fields(skins.LastResult, "Status", "Detail") + " lastError=" + skins.LastError
              + " pack: " + DsQaReport.Fields(skins.CurrentPack, "Id", "Mode", "SpriteScope")
            : "no runtime").Append('\n');
        var modsRuntime = SilksongModsRuntime.Current;
        var library = modsRuntime != null ? modsRuntime.SkinLibrary : null;
        sb.Append("rotation: ").Append(library != null ? library.Describe() : "no library").Append('\n');
        sb.Append(DualSouls.Mods.HeroTint.Diagnostics).Append('\n');
        sb.Append(DualSouls.Mods.CombatOverlay.Describe()).Append('\n');
        sb.Append(DsQaReport.Frames()).Append('\n');
        sb.Append(DsQaReport.Errors());
        return sb.ToString();
    }

    // #39 QA on a disposable save: start a courier delivery, hurt Hornet through the game's own
    // damage path, and make the static DeliveryQuestItem.BreakAll call that the Bellway and
    // Ventrica arrival FSMs make. Each reply ends with the active delivery items.
    string CommandDelivery(string[] a, GameManager gm)
    {
        if (a.Length < 2) return "error delivery <list|give <name>|hit|break|state>";
        if (gm == null) return "error no GameManager";
        var hero = HeroController.SilentInstance;
        string result;
        switch (a[1])
        {
            case "list":
            {
                var sb = new StringBuilder("ok");
                foreach (BasicQuestBase basic in QuestManager.GetAllQuests())
                {
                    var quest = basic as FullQuestBase;
                    if (quest == null) continue;
                    foreach (FullQuestBase.QuestTarget target in quest.Targets)
                        if (target.Counter is DeliveryQuestItem)
                            sb.Append(' ').Append(quest.name).Append(':').Append(target.Counter.name).Append('x').Append(target.Count)
                              .Append(quest.IsAccepted ? (quest.IsCompleted ? "(done)" : "(active)") : "");
                }
                foreach (DeliveryQuestItemStandalone item in Resources.FindObjectsOfTypeAll<DeliveryQuestItemStandalone>())
                    sb.Append(" standalone:").Append(item.name).Append('x').Append(item.TargetCount);
                return sb.ToString();
            }
            case "give":
            {
                if (a.Length < 3) return "error delivery give <quest or standalone item name>";
                string wanted = string.Join(" ", a, 2, a.Length - 2);
                result = null;
                foreach (BasicQuestBase basic in QuestManager.GetAllQuests())
                {
                    var quest = basic as FullQuestBase;
                    if (quest == null || quest.name != wanted) continue;
                    foreach (FullQuestBase.QuestTarget target in quest.Targets)
                    {
                        var item = target.Counter as DeliveryQuestItem;
                        if (item == null) continue;
                        quest.BeginQuest(null, false);
                        item.Collect(target.Count, false);
                        result = "ok began " + quest.name + " with " + item.name + "x" + target.Count;
                        break;
                    }
                    break;
                }
                if (result == null)
                    foreach (DeliveryQuestItemStandalone item in Resources.FindObjectsOfTypeAll<DeliveryQuestItemStandalone>())
                    {
                        if (item.name != wanted) continue;
                        item.Collect(item.TargetCount, false);
                        result = "ok collected " + item.name + "x" + item.TargetCount;
                        break;
                    }
                if (result == null) return "error no delivery quest or standalone item " + wanted;
                break;
            }
            case "hit":
            {
                if (hero == null) return "error no HeroController";
                var pd = PlayerData.instance;
                int before = pd.health;
                hero.TakeDamage(hero.gameObject, GlobalEnums.CollisionSide.left, 1, GlobalEnums.HazardType.ENEMY);
                result = "ok health=" + before + "->" + pd.health;
                break;
            }
            case "break":
                DeliveryQuestItem.BreakAll();
                result = "ok BreakAll";
                break;
            case "state":
                result = "ok";
                break;
            default: return "error delivery <list|give <name>|hit|break|state>";
        }
        var items = new StringBuilder();
        foreach (DeliveryQuestItem.ActiveItem active in DeliveryQuestItem.GetActiveItems())
            items.Append(' ').Append(active.Item.name).Append('=').Append(active.CurrentCount).Append('/').Append(active.MaxCount);
        return result + " | active:" + (items.Length == 0 ? " none" : items.ToString()) + " | " + SilksongGameplayHooks.DeliveryReport();
    }

    // QA: one lethal hit through the game's own damage path, so death rotation runs for real.
    string CommandDie(GameManager gm)
    {
        var hero = HeroController.instance;
        var pd = PlayerData.instance;
        if (hero == null || pd == null) return "error no HeroController";
        if (gm == null || gm.GameState != GlobalEnums.GameState.PLAYING) return "error not playing";
        FieldInfo health = typeof(PlayerData).GetField("health", BindingFlags.Public | BindingFlags.Instance);
        FieldInfo blue = typeof(PlayerData).GetField("healthBlue", BindingFlags.Public | BindingFlags.Instance);
        if (health == null) return "error no PlayerData.health";
        if (blue != null) blue.SetValue(pd, 0);
        health.SetValue(pd, 1);
        return DsQaReport.Invoke(hero, hero, "TakeDamage", new object[] { hero.gameObject }, "CollisionSide.top", "HazardType.NON_HAZARD")
               + " " + DsQaReport.Fields(pd, "health");
    }

    // QA hooks for what injected input cannot reach (InControl ignores adb key events).
    // hurt: non-lethal damage through the game's own TakeHealth, so the HUD follows.
    string CommandHurt(string[] a, GameManager gm)
    {
        int amount;
        if (a.Length < 2 || !int.TryParse(a[1], out amount) || amount <= 0) return "error hurt <amount>";
        if (gm == null) return "error no GameManager";
        var pd = PlayerData.instance;
        if (pd == null) return "error no PlayerData";
        pd.TakeHealth(Mathf.Min(amount, pd.health - 1), false, false);
        return "ok " + DsQaReport.Fields(pd, "health", "maxHealth");
    }

    // dash: the hero's own dash start, as the dash button would trigger it.
    string CommandDash(GameManager gm)
    {
        if (gm == null) return "error no GameManager";
        var hero = HeroController.instance;
        if (hero == null) return "error no HeroController";
        if (!hero.CanDash()) return "error hero cannot dash now";
        MethodInfo dash = typeof(HeroController).GetMethod("HeroDash", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(bool) }, null);
        if (dash == null) return "error no HeroController.HeroDash(bool)";
        dash.Invoke(hero, new object[] { false });
        return "ok " + DsQaReport.Fields(hero.cState, "dashing");
    }

    // travel: a normal scene transition through a named entrance.
    string CommandTravel(string[] a, GameManager gm)
    {
        if (a.Length < 3) return "error travel <scene> <gate>";
        if (gm == null) return "error no GameManager";
        if (gm.GameState != GlobalEnums.GameState.PLAYING || gm.IsInSceneTransition) return "error not playing";
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

    static DualSouls.Mods.TweakController ModsController()
    {
        var runtime = SilksongModsRuntime.Current;
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
}
#endif
