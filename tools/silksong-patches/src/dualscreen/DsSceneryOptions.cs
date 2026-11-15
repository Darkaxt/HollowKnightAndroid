// DsSceneryOptions -- user-facing companion backdrop settings.
//
// The Mods menu "black background" choice is stored here. Its only previous
// reader was the retired DsPort overlay runtime, so the live DsShell does not
// consume it yet; wiring it into the companion backdrop belongs to the unified
// lower-screen rebuild.
public static class DsSceneryOptions
{
    public static bool BlackBackground { get; set; }
}
