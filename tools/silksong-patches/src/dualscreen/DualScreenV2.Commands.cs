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
        "help | report | state | pause | mods | skins | skin <list|row N|left N|right N|close> | tab <id> | mods-list | mod <id> <value> | pd <field>... | die (refused on slot 1) | sprites";

    static readonly string[] ReportPlayerFields =
        { "health", "maxHealth", "healthBlue", "silk", "silkMax", "geo", "ShellShards" };

    void PollCommands() { DsCommandFile.Poll(RunCommand); }

    string RunCommand(string line)
    {
        string[] a = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        GameManager gm = _gameManager;
        switch (a[0].ToLowerInvariant())
        {
            case "help": return CommandHelp;
            case "report": return CommandReport(gm);
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
            case "die": return CommandDie(gm);
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
        sb.Append(DsQaReport.Frames()).Append('\n');
        sb.Append(DsQaReport.Errors());
        return sb.ToString();
    }

    // QA: one lethal hit through the game's own damage path, so death rotation runs for real.
    string CommandDie(GameManager gm)
    {
        var hero = HeroController.instance;
        var pd = PlayerData.instance;
        if (hero == null || pd == null) return "error no HeroController";
        if (gm == null || gm.GameState != GlobalEnums.GameState.PLAYING) return "error not playing";
        if (gm.profileID == 1) return "error refused on slot 1 (player save); use a disposable slot";
        FieldInfo health = typeof(PlayerData).GetField("health", BindingFlags.Public | BindingFlags.Instance);
        FieldInfo blue = typeof(PlayerData).GetField("healthBlue", BindingFlags.Public | BindingFlags.Instance);
        if (health == null) return "error no PlayerData.health";
        if (blue != null) blue.SetValue(pd, 0);
        health.SetValue(pd, 1);
        return DsQaReport.Invoke(hero, hero, "TakeDamage", new object[] { hero.gameObject }, "CollisionSide.top", "HazardType.NON_HAZARD")
               + " " + DsQaReport.Fields(pd, "health");
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
