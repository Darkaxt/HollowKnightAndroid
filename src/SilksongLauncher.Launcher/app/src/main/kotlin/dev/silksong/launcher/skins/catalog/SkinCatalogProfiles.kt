package dev.silksong.launcher.skins.catalog

import dev.silksong.launcher.profiles.GameProfile
import dev.silksong.launcher.profiles.HollowKnightProfile
import dev.silksong.launcher.profiles.SilksongProfile

/** Exact decoded atlas authority for one canonical target. */
data class SkinTextureDimensions(val width: Int, val height: Int) {
    init {
        require(width > 0 && height > 0)
    }
}

/** Closed, immutable per-game skin schema authority. */
class SkinCatalogProfile internal constructor(
    val profileId: String,
    val gameVersion: String,
    val assetName: String,
    val catalogId: String,
    val sha256: String,
    paths: List<String>,
    textureDimensions: Map<String, SkinTextureDimensions>,
    private val expectedPathCount: Int,
    val legacyMigrationAllowed: Boolean,
) {
    val paths: List<String> = paths.toList()
    val textureDimensions: Map<String, SkinTextureDimensions> = textureDimensions.toMap()
    val pathCount: Int get() = expectedPathCount

    init {
        require(profileId == "hollow-knight" || profileId == "silksong")
        require(expectedPathCount > 0 && (paths.isEmpty() || paths.size == expectedPathCount))
        require(paths.toSet().size == paths.size)
        require(paths.all(::safeCanonicalPath))
        require(textureDimensions.keys.all { it in paths })
        require(textureDimensions.isEmpty() || textureDimensions.keys == paths.toSet())
        require(sha256.matches(Regex("[0-9a-f]{64}")))
        require(legacyMigrationAllowed == (profileId == "hollow-knight"))
    }

    private fun safeCanonicalPath(path: String): Boolean =
        path.endsWith(".png") && !path.startsWith('/') && !path.contains('\\') &&
            path.split('/').all { it.isNotEmpty() && it != "." && it != ".." }
}

object SkinCatalogProfiles {
    private val silksongPaths = listOf(
        "Assets/Collections/Hornet Cln Data/atlas0.png",
        "Assets/Collections/Hornet Cloakless Cln Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Dagger Cln Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Drill Lance Cln Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Scythe Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Shaman Cln Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Warrior Cln Data/atlas0.png",
        "Assets/Collections/Hornet CrestWeapon Whip Cln Data/atlas0.png",
        "Assets/Collections/Hornet_Needolin_Windy Data/atlas0.png",
        "Assets/Collections/HUD Cln Data/atlas0.png",
        "Assets/Collections/HUD Extras Cln Data/atlas0.png",
    )

    private val silksongDimensions = listOf(
        2048 to 2048,
        2048 to 4096,
        2048 to 2048,
        2048 to 2048,
        2048 to 4096,
        2048 to 2048,
        2048 to 4096,
        1024 to 2048,
        1024 to 2048,
        4096 to 4096,
        512 to 1024,
    ).mapIndexed { index, (width, height) ->
        silksongPaths[index] to SkinTextureDimensions(width, height)
    }.toMap()

    val HollowKnight = SkinCatalogProfile(
        HollowKnightProfile.id,
        HollowKnightProfile.currentGameVersion,
        HollowKnightCatalogPaths.ASSET_NAME,
        HollowKnightCatalogPaths.CATALOG_ID,
        HollowKnightCatalogPaths.SHA256,
        emptyList(),
        emptyMap(),
        expectedPathCount = 205,
        legacyMigrationAllowed = true,
    )
    val Silksong = SkinCatalogProfile(
        SilksongProfile.id,
        SilksongProfile.currentGameVersion,
        SilksongCatalogPaths.ASSET_NAME,
        SilksongCatalogPaths.CATALOG_ID,
        SilksongCatalogPaths.SHA256,
        silksongPaths,
        silksongDimensions,
        expectedPathCount = 11,
        legacyMigrationAllowed = false,
    )

    fun require(profile: GameProfile): SkinCatalogProfile = when {
        profile === HollowKnightProfile -> HollowKnight
        profile === SilksongProfile -> Silksong
        else -> throw IllegalArgumentException("No skin catalog for unregistered profile ${profile.id}")
    }

    fun require(profileId: String): SkinCatalogProfile = when (profileId) {
        HollowKnightProfile.id -> HollowKnight
        SilksongProfile.id -> Silksong
        else -> throw IllegalArgumentException("No skin catalog for profile $profileId")
    }
}

object SilksongCatalogPaths {
    const val ASSET_NAME = "silksong-skin-catalog-v1.txt"
    const val CATALOG_ID = "silksong-1.0.29980-character-hud-11"
    const val SHA256 = "363ff1affbe9b95214667b4ebdc0d903aa8cc098aa3e956183519c9e88f4136e"
}
