package dev.silksong.launcher.builtinmods

enum class BuiltInModControlKind {
    Choice,
    Command,
    Route,
}

data class BuiltInModDescriptor(
    val id: String,
    val contractId: String,
    val controlKind: BuiltInModControlKind,
    val group: String,
    val title: String,
    val description: String,
    val defaultValue: String,
    val values: List<String>,
    val isAvailable: Boolean,
    val unavailableReason: String = "",
) {
    // Compatibility until the launcher controller consumes isAvailable directly.
    val actionable: Boolean get() = isAvailable
}

/** Android mirror of the production typed adapters; Python contracts prevent drift. */
object BuiltInModCatalog {
    private const val HOLLOW_KNIGHT_MISSING =
        "No Hollow Knight adapter operation is connected for this required row yet."
    private const val SILKSONG_MISSING =
        "No Silksong adapter operation is connected for this required row yet."

    val hollowKnight = listOf(
        unavailable("skins", "skins", BuiltInModControlKind.Route, "GENERAL", "SKINS", "Open the installed skin library.", "open", listOf("open"), HOLLOW_KNIGHT_MISSING),
        available("companion_backdrop", "black_background", "GENERAL", "BLACK BACKGROUND", "Use a black lower-screen background instead of the dimmed scenery wash.", "dimmed", listOf("dimmed", "black")),

        available("run_speed", "run_speed", "WORLD", "RUN SPEED", "Choose the Knight's normal, +25%, or +50% movement pace.", "vanilla", listOf("vanilla", "plus_25", "plus_50")),
        unavailable("fast_transitions", "fast_transitions", BuiltInModControlKind.Choice, "WORLD", "FAST TRANSITIONS", "Shorten supported scene transitions.", "off", listOf("off", "on"), HOLLOW_KNIGHT_MISSING),
        unavailable("auto_map", "auto_map", BuiltInModControlKind.Choice, "WORLD", "AUTO MAP", "Reveal visited rooms on the map automatically.", "off", listOf("off", "on"), HOLLOW_KNIGHT_MISSING),
        unavailable("innate_compass", "innate_compass", BuiltInModControlKind.Choice, "WORLD", "INNATE COMPASS", "Show the Knight on the map without requiring Wayward Compass.", "off", listOf("off", "on"), HOLLOW_KNIGHT_MISSING),
        unavailable("bench_teleport", "bench_teleport", BuiltInModControlKind.Route, "WORLD", "BENCH TELEPORT", "Open the recorded-bench destination list.", "open", listOf("open"), HOLLOW_KNIGHT_MISSING),
        unavailable("secret_radar", "secret_radar", BuiltInModControlKind.Choice, "WORLD", "SECRET RADAR", "Signal nearby secrets without changing progression.", "off", listOf("off", "on"), HOLLOW_KNIGHT_MISSING),

        available("nail_damage", "nail_damage", "COMBAT", "NAIL DAMAGE", "Multiply nail damage while preserving smith upgrades.", "x1", listOf("x1", "x2", "x3", "x5")),
        available("damage_received", "damage_taken", "COMBAT", "DAMAGE TAKEN", "Choose normal damage, keep masks, or ignore damage entirely.", "vanilla", listOf("vanilla", "no_mask_loss", "invincible")),
        unavailable("damage_cap", "damage_cap", BuiltInModControlKind.Choice, "COMBAT", "DAMAGE CAP", "Limit damage received from a single hit.", "off", listOf("off", "on"), HOLLOW_KNIGHT_MISSING),
        available("one_hit_kills", "one_hit_kills", "COMBAT", "ONE-HIT KILLS", "Defeat regular enemies in one hit while excluding boss-scale targets.", "off", listOf("off", "on")),
        available("unlimited_soul", "unlimited_soul", "COMBAT", "UNLIMITED SOUL", "Keep Soul available through the game's normal refill path.", "off", listOf("off", "on")),

        unavailable("health_bars", "enemy_health_bars", BuiltInModControlKind.Choice, "ENCOUNTERS", "ENEMY HEALTH BARS", "Show health bars for eligible enemies and bosses.", "off", listOf("off", "on"), HOLLOW_KNIGHT_MISSING),
        unavailable("damage_numbers", "damage_numbers", BuiltInModControlKind.Choice, "ENCOUNTERS", "DAMAGE NUMBERS", "Show the damage dealt by supported attacks.", "off", listOf("off", "on"), HOLLOW_KNIGHT_MISSING),
        unavailable("boss_retry", "boss_retry", BuiltInModControlKind.Choice, "ENCOUNTERS", "BOSS RETRY", "Retry supported boss encounters from a safe checkpoint.", "off", listOf("off", "on"), HOLLOW_KNIGHT_MISSING),

        unavailable("equip_anywhere", "equip_anywhere", BuiltInModControlKind.Choice, "CHARMS", "EQUIP ANYWHERE", "Change charms away from benches through legal game actions.", "off", listOf("off", "on"), HOLLOW_KNIGHT_MISSING),
        unavailable("charm_costs", "charm_costs", BuiltInModControlKind.Choice, "CHARMS", "CHARM COSTS", "Remove charm notch costs while enabled.", "vanilla", listOf("vanilla", "free"), HOLLOW_KNIGHT_MISSING),
        unavailable("unlimited_notches", "unlimited_notches", BuiltInModControlKind.Choice, "CHARMS", "UNLIMITED NOTCHES", "Equip charms without the normal notch limit.", "off", listOf("off", "on"), HOLLOW_KNIGHT_MISSING),

        unavailable("state_slot", "state_slot", BuiltInModControlKind.Choice, "SAVE STATES", "SLOT", "Choose the save-state slot used by the commands below.", "1", listOf("1", "2", "3", "4", "5"), HOLLOW_KNIGHT_MISSING),
        unavailable("save_to_slot", "save_to_slot", BuiltInModControlKind.Command, "SAVE STATES", "SAVE TO SLOT", "Capture the current state in the selected slot.", "run", listOf("run"), HOLLOW_KNIGHT_MISSING),
        unavailable("load_from_slot", "load_from_slot", BuiltInModControlKind.Command, "SAVE STATES", "LOAD FROM SLOT", "Restore the state stored in the selected slot.", "run", listOf("run"), HOLLOW_KNIGHT_MISSING),
        unavailable("delete_slot", "delete_slot", BuiltInModControlKind.Command, "SAVE STATES", "DELETE SLOT", "Delete the state stored in the selected slot.", "run", listOf("run"), HOLLOW_KNIGHT_MISSING),

        unavailable("geo_magnet", "geo_magnet", BuiltInModControlKind.Choice, "ECONOMY", "GEO MAGNET", "Collect nearby Geo without requiring Gathering Swarm.", "off", listOf("off", "on"), HOLLOW_KNIGHT_MISSING),
        unavailable("keep_geo_on_death", "keep_geo_on_death", BuiltInModControlKind.Choice, "ECONOMY", "KEEP GEO ON DEATH", "Keep Geo through death without duplicate Shade awards.", "off", listOf("off", "on"), HOLLOW_KNIGHT_MISSING),
        unavailable("journal_one_kill", "journal_one_kill", BuiltInModControlKind.Choice, "ECONOMY", "JOURNAL IN ONE KILL", "Complete eligible Hunter's Journal entries after one kill.", "off", listOf("off", "on"), HOLLOW_KNIGHT_MISSING),
        unavailable("geo_multiplier", "geo_multiplier", BuiltInModControlKind.Choice, "ECONOMY", "GEO MULTIPLIER", "Multiply supported Geo awards.", "x1", listOf("x1", "x2", "x3", "x5"), HOLLOW_KNIGHT_MISSING),

        available("lifeblood_flash", "lifeblood_flash", "PRESENTATION", "LIFEBLOOD FLASH", "Use the original flash, a softened flash, or no flash.", "vanilla", listOf("vanilla", "soft", "off")),
    )

    val silksong = listOf(
        unavailable("skins", "skins", BuiltInModControlKind.Route, "GENERAL", "SKINS", "Open the installed skin library.", "open", listOf("open"), SILKSONG_MISSING),
        unavailable("black_background", "black_background", BuiltInModControlKind.Choice, "GENERAL", "BLACK BACKGROUND", "Use a black lower-screen background instead of the dimmed scenery wash.", "off", listOf("off", "on"), SILKSONG_MISSING),

        unavailable("run_speed", "run_speed", BuiltInModControlKind.Choice, "WORLD", "RUN SPEED", "Choose Hornet's normal, +25%, or +50% movement pace.", "vanilla", listOf("vanilla", "plus_25", "plus_50"), SILKSONG_MISSING),
        unavailable("fast_transitions", "fast_transitions", BuiltInModControlKind.Choice, "WORLD", "FAST TRANSITIONS", "Shorten supported scene transitions.", "off", listOf("off", "on"), SILKSONG_MISSING),
        unavailable("auto_map", "auto_map", BuiltInModControlKind.Choice, "WORLD", "AUTO MAP", "Reveal visited rooms on the map automatically.", "off", listOf("off", "on"), SILKSONG_MISSING),
        unavailable("innate_compass", "innate_compass", BuiltInModControlKind.Choice, "WORLD", "INNATE COMPASS", "Show Hornet on the map without requiring a compass tool.", "off", listOf("off", "on"), SILKSONG_MISSING),
        unavailable("bench_teleport", "bench_teleport", BuiltInModControlKind.Route, "WORLD", "BENCH TELEPORT", "Open the recorded-bench destination list.", "open", listOf("open"), SILKSONG_MISSING),
        unavailable("secret_radar", "secret_radar", BuiltInModControlKind.Choice, "WORLD", "SECRET RADAR", "Signal nearby secrets without changing progression.", "off", listOf("off", "on"), SILKSONG_MISSING),

        unavailable("nail_damage", "nail_damage", BuiltInModControlKind.Choice, "COMBAT", "NEEDLE DAMAGE", "Multiply Needle damage while preserving upgrades.", "x1", listOf("x1", "x2", "x3", "x5"), SILKSONG_MISSING),
        available("damage_received", "damage_taken", "COMBAT", "DAMAGE TAKEN", "Choose normal damage, prevent death, or full invincibility.", "vanilla", listOf("vanilla", "prevent_death", "invincible")),
        unavailable("damage_cap", "damage_cap", BuiltInModControlKind.Choice, "COMBAT", "DAMAGE CAP", "Limit damage received from a single hit.", "off", listOf("off", "on"), SILKSONG_MISSING),
        available("one_hit_kills", "one_hit_kills", "COMBAT", "ONE-HIT KILLS", "Use Silksong's managed instant-kill damage state.", "off", listOf("off", "on")),
        available("unlimited_silk", "unlimited_soul", "COMBAT", "UNLIMITED SILK", "Keep Silk available using Silksong's own drain and refill paths.", "off", listOf("off", "on")),

        unavailable("health_bars", "enemy_health_bars", BuiltInModControlKind.Choice, "ENCOUNTERS", "ENEMY HEALTH BARS", "Show health bars for eligible enemies and bosses.", "off", listOf("off", "on"), SILKSONG_MISSING),
        unavailable("damage_numbers", "damage_numbers", BuiltInModControlKind.Choice, "ENCOUNTERS", "DAMAGE NUMBERS", "Show the damage dealt by supported attacks.", "off", listOf("off", "on"), SILKSONG_MISSING),
        unavailable("boss_retry", "boss_retry", BuiltInModControlKind.Choice, "ENCOUNTERS", "BOSS RETRY", "Retry supported boss encounters from a safe checkpoint.", "off", listOf("off", "on"), SILKSONG_MISSING),

        available("equip_anywhere", "equip_anywhere", "CRESTS & TOOLS", "EQUIP ANYWHERE", "Allow Crest and Tool changes away from benches.", "off", listOf("off", "on")),
        unavailable("charm_costs", "charm_costs", BuiltInModControlKind.Choice, "CRESTS & TOOLS", "TOOL COSTS", "Remove Tool slot costs while enabled.", "vanilla", listOf("vanilla", "free"), SILKSONG_MISSING),
        unavailable("unlimited_notches", "unlimited_notches", BuiltInModControlKind.Choice, "CRESTS & TOOLS", "UNLIMITED TOOL SLOTS", "Equip Tools without the normal Crest slot limit.", "off", listOf("off", "on"), SILKSONG_MISSING),

        unavailable("state_slot", "state_slot", BuiltInModControlKind.Choice, "SAVE STATES", "SLOT", "Choose the save-state slot used by the commands below.", "1", listOf("1", "2", "3", "4", "5"), SILKSONG_MISSING),
        unavailable("save_to_slot", "save_to_slot", BuiltInModControlKind.Command, "SAVE STATES", "SAVE TO SLOT", "Capture the current state in the selected slot.", "run", listOf("run"), SILKSONG_MISSING),
        unavailable("load_from_slot", "load_from_slot", BuiltInModControlKind.Command, "SAVE STATES", "LOAD FROM SLOT", "Restore the state stored in the selected slot.", "run", listOf("run"), SILKSONG_MISSING),
        unavailable("delete_slot", "delete_slot", BuiltInModControlKind.Command, "SAVE STATES", "DELETE SLOT", "Delete the state stored in the selected slot.", "run", listOf("run"), SILKSONG_MISSING),

        unavailable("geo_magnet", "geo_magnet", BuiltInModControlKind.Choice, "ECONOMY", "ROSARY MAGNET", "Collect nearby Rosaries without requiring a Tool.", "off", listOf("off", "on"), SILKSONG_MISSING),
        unavailable("keep_geo_on_death", "keep_geo_on_death", BuiltInModControlKind.Choice, "ECONOMY", "KEEP ROSARIES ON DEATH", "Keep Rosaries through death without duplicate recovery awards.", "off", listOf("off", "on"), SILKSONG_MISSING),
        unavailable("journal_one_kill", "journal_one_kill", BuiltInModControlKind.Choice, "ECONOMY", "JOURNAL IN ONE KILL", "Complete eligible Journal entries after one kill.", "off", listOf("off", "on"), SILKSONG_MISSING),
        unavailable("geo_multiplier", "geo_multiplier", BuiltInModControlKind.Choice, "ECONOMY", "ROSARY MULTIPLIER", "Multiply supported Rosary awards.", "x1", listOf("x1", "x2", "x3", "x5"), SILKSONG_MISSING),

        available("instant_dialogue", "instant_dialogue", "PRESENTATION", "INSTANT DIALOGUE", "Show dialogue text immediately instead of printing it over time.", "off", listOf("off", "on")),
        available("disable_world_rumble", "disable_world_rumble", "PRESENTATION", "DISABLE WORLD RUMBLE", "Prevent ambient world rumble effects.", "off", listOf("off", "on")),
        available("ignore_frost_slowdown", "ignore_frost_slowdown", "PLAYER", "IGNORE FROST SLOWDOWN", "Prevent frost buildup from slowing Hornet.", "off", listOf("off", "on")),
    )

    fun forGame(gameId: String): List<BuiltInModDescriptor> = when (gameId) {
        "hollow-knight" -> hollowKnight
        "silksong" -> silksong
        else -> emptyList()
    }

    private fun available(
        id: String,
        contractId: String,
        group: String,
        title: String,
        description: String,
        defaultValue: String,
        values: List<String>,
    ) = BuiltInModDescriptor(
        id, contractId, BuiltInModControlKind.Choice, group, title, description,
        defaultValue, values, isAvailable = true,
    )

    private fun unavailable(
        id: String,
        contractId: String,
        controlKind: BuiltInModControlKind,
        group: String,
        title: String,
        description: String,
        defaultValue: String,
        values: List<String>,
        unavailableReason: String,
    ) = BuiltInModDescriptor(
        id, contractId, controlKind, group, title, description,
        defaultValue, values, isAvailable = false, unavailableReason = unavailableReason,
    )
}
