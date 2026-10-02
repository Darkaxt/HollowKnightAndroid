// Exact-shaped scalar/native API boundary, isolated from published HK fixtures.
namespace SsShellContracts.GlobalEnums
{
    public enum GameState{PLAYING,PAUSED,EXITING_LEVEL,LOADING}
    public enum UIState{PLAYING,PAUSED,MAIN_MENU_HOME}
    public enum MapZone{NONE}
}
namespace SsShellContracts.TeamCherry.Localization
{
    public struct LocalisedString{public override string ToString()=>"native-text-boundary";}
    public static class Language{public static string Get(string key,string sheet)=>key;}
}
