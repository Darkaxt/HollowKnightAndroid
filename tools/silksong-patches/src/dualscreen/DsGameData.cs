// DsGameData — the one place that decides whether the game's data may be
// touched at all, and the one place that touches it.
//
// This exists because of a bug that is worth stating in full, since it is the
// kind that looks impossible.
//
// The second screen showed two inventory items instead of a full inventory, AND
// the GAME'S OWN inventory became wrong. Nothing here writes anything, so a
// read-only screen corrupting the game's state made no sense -- until
// CollectableItemManager.GetCollectedItems() turned out not to be a read:
//
//   * it fills a STATIC cache (_collectedItemCache) that the game's own
//     inventory pane later reads back, and
//   * it calls IsInHiddenMode(), which consults
//     HeroController.instance.Config.ForceBareInventory and INCREMENTS a shared
//     Version whenever that answer changes.
//
// We were polling it on a timer, including on the main menu where there is no
// hero at all. So the answer flipped, the version moved, and the game's cache
// was rebuilt from our context -- with the bare-inventory list. Both symptoms,
// one cause.
//
// Two rules came out of it, and they are enforced here rather than remembered:
//
//   1. NOTHING is read unless the game is actually in play. On a menu the
//      managers are half-built and PlayerData is a default instance, so every
//      answer is wrong even when it is safe.
//   2. Prefer the master list plus PlayerData over a convenience accessor.
//      An accessor that caches is an accessor that can be perturbed by the
//      order and timing of OUR calls, which is not something a second screen
//      may ever influence.
//
// Audited at the time of writing:
//   ToolItemManager.GetAllTools()   -> returns instance.toolItems      PURE
//   ToolItemManager.GetAllCrests()  -> LINQ over crestList             PURE
//   EnemyJournalManager.GetAllEnemies() -> recordList.ToList()         PURE
//   QuestManager.GetAllQuests()     -> returns masterList              PURE
//   QuestManager.GetAcceptedQuests()-> static version-keyed cache      avoid
//   CollectableItemManager.GetCollectedItems() -> cache + Version bump UNSAFE

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

public static class DsGameData
{
    /// <summary>
    /// May the game's data be read right now?
    ///
    /// True only in a real gameplay scene with a real save loaded. Menus,
    /// loading screens and cutscenes all answer false, which is what keeps the
    /// second screen from reading (or perturbing) half-initialised state.
    /// </summary>
    public static bool InGame
    {
        get
        {
            try
            {
                if (!PlayerData.HasInstance) return false;
                var gm = GameManager.instance;
                if (gm == null) return false;
                if (!gm.IsGameplayScene()) return false;

                // A gameplay scene can still be mid-load; the hero existing is
                // the simplest signal that the save is actually live.
                return HeroController.instance != null;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>A short reason for the idle screen, when not in game.</summary>
    public static string IdleReason
    {
        get
        {
            try
            {
                if (GameManager.instance == null) return "Starting\u2026";
                if (GameManager.instance.IsMenuScene()) return "Main menu";
                if (!PlayerData.HasInstance) return "No save loaded";
                return "Loading\u2026";
            }
            catch { return "Waiting\u2026"; }
        }
    }
}
#endif
