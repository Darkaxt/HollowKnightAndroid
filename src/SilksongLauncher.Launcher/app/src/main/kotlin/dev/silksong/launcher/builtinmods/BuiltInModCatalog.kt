package dev.silksong.launcher.builtinmods

data class BuiltInModDescriptor(
    val id: String,
    val group: String,
    val title: String,
    val description: String,
    val defaultValue: String,
    val values: List<String>,
    val actionable: Boolean = true,
    val trackingId: String = "",
    val unavailableReason: String = "",
)

/** Android mirror of the production typed adapters; Python contracts prevent drift. */
object BuiltInModCatalog {
    val hollowKnight = listOf(
            row("companion_backdrop", "PRESENTATION", "COMPANION BACKDROP", "Choose the accepted dimmed scenery wash or a black lower-screen backdrop.", "dimmed", listOf("dimmed", "black")),
            row("lifeblood_flash", "PRESENTATION", "LIFEBLOOD FLASH", "Use the accepted softened flash, the original flash, or no flash.", "soft", listOf("soft", "vanilla", "off")),
            row("damage_received", "COMBAT", "DAMAGE RECEIVED", "Choose normal damage, keep masks, or ignore damage entirely.", "vanilla", listOf("vanilla", "no_mask_loss", "invincible")),
            row("nail_damage", "COMBAT", "NAIL DAMAGE", "Multiply nail damage while preserving smith upgrades.", "x1", listOf("x1", "x2", "x3", "x5")),
            row("one_hit_kills", "COMBAT", "ONE-HIT KILLS", "Defeat regular enemies in one hit while excluding boss-scale targets.", "off", listOf("off", "on")),
            row("run_speed", "PLAYER", "RUN SPEED", "Choose the Knight's normal, +25%, or +50% walking and running pace.", "vanilla", listOf("vanilla", "plus_25", "plus_50")),
            row("unlimited_soul", "PLAYER", "UNLIMITED SOUL", "Keep Soul available through the game's normal refill path.", "off", listOf("off", "on")),
        )

    val silksong = listOf(
            row("damage_received", "COMBAT", "DAMAGE RECEIVED", "Choose normal damage, prevent death, or full invincibility.", "vanilla", listOf("vanilla", "prevent_death", "invincible")),
            row("unlimited_silk", "COMBAT", "UNLIMITED SILK", "Keep Silk available using Silksong's own drain and refill paths.", "off", listOf("off", "on")),
            row("one_hit_kills", "COMBAT", "ONE-HIT KILLS", "Use Silksong's managed instant-kill damage state.", "off", listOf("off", "on")),
            row("equip_anywhere", "LOADOUT", "EQUIP ANYWHERE", "Allow tool and crest changes away from benches.", "off", listOf("off", "on")),
        )

    val deferred = listOf(
        BuiltInModDescriptor("charm_costs", "CHARMS", "CHARM COSTS", "Adjust charm costs while preserving the complete loadout.", "off", listOf("off"), false, "HKMOD-006", "All-cost snapshots and equipped/save lifecycle rollback are not proven."),
        BuiltInModDescriptor("unlimited_notches", "CHARMS", "UNLIMITED NOTCHES", "Equip charms without violating notch and overcharm rules.", "off", listOf("off"), false, "HKMOD-007", "Overcharm invariants are not proven."),
    )

    fun forGame(gameId: String): List<BuiltInModDescriptor> = when (gameId) {
        "hollow-knight" -> hollowKnight
        "silksong" -> silksong
        else -> emptyList()
    }

    fun deferred(gameId: String): List<BuiltInModDescriptor> =
        if (gameId == "hollow-knight") deferred else emptyList()

    private fun row(
        id: String,
        group: String,
        title: String,
        description: String,
        defaultValue: String,
        values: List<String>,
    ) = BuiltInModDescriptor(id, group, title, description, defaultValue, values)
}
