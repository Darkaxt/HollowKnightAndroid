// Silksong's tool-type colours, kept out of the shared DsTheme because they
// name Silksong types (ToolItemType, GlobalSettings).

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

public static partial class DsTheme
{
    // Tool types have colours in the game's own HUD; matching them means a
    // red tool reads as a red tool here too.
    //
    // The game's answer is GlobalSettings.UI.GetToolTypeColor, which is where
    // InventoryItemTool.CursorColor gets it from, so that is what we ask --
    // but carefully, and never eagerly.
    //
    // GlobalSettingsBase.Get is NOT a getter. On a miss it cancels the game's
    // own delayed-loader coroutine, DESTROYS that loader's GameObject, starts
    // an Addressables load and blocks on WaitForCompletion. Called at the wrong
    // moment that is a frame hitch at best and a fight with the game's load
    // ordering at worst -- the same shape of trap as CollectableItemManager's
    // cache (see DsScreens). So it is only asked once, only during gameplay,
    // by which time the HUD has long since forced the settings resident and the
    // call is a field read. Anything unexpected keeps the authored values.
    static readonly Color[] _toolColors = new Color[4];
    static bool _toolColorsRead;

    static readonly Color[] _toolColorsFallback =
    {
        new Color(0.85f, 0.35f, 0.32f, 1f),   // Red
        new Color(0.42f, 0.62f, 0.88f, 1f),   // Blue
        new Color(0.92f, 0.80f, 0.38f, 1f),   // Yellow
        new Color(0.75f, 0.72f, 0.80f, 1f),   // Skill
    };

    public static Color ToolTypeColor(ToolItemType type)
    {
        int i = (int)type;
        if (i < 0 || i > 3) i = 3;

        if (!_toolColorsRead && DsGameData.InGame)
        {
            _toolColorsRead = true;
            try
            {
                for (int t = 0; t < 4; t++)
                    _toolColors[t] = GlobalSettings.UI.GetToolTypeColor((ToolItemType)t);
                Debug.Log("[DsTheme] tool colours from the game: " +
                          _toolColors[0] + " " + _toolColors[1] + " " +
                          _toolColors[2] + " " + _toolColors[3]);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[DsTheme] tool colours unavailable, using ours: " + e.Message);
                for (int t = 0; t < 4; t++) _toolColors[t] = default(Color);
            }
        }

        // A zero alpha means we never got a real one.
        return _toolColors[i].a > 0f ? _toolColors[i] : _toolColorsFallback[i];
    }
}
#endif
